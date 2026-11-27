"""All installations/packages are synthetic temporary data; no game or network access."""
import base64
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

TOOL_PATH = Path(__file__).resolve().parents[1] / "dependency_tool.py"
spec = importlib.util.spec_from_file_location("dependency_tool", TOOL_PATH)
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)


class DependencyToolTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game, self.packages = self.root / "synthetic-game", self.root / "packages"
        self.plugin = self.game / "BepInEx/plugins/AssetImport"
        self.plugin.mkdir(parents=True)
        self.packages.mkdir()
        gate = self.plugin / "SyntheticImporter.dll"
        gate.write_bytes(b"synthetic identity")
        self.manifest = {"id": "synthetic-two-dependencies", "identityGates": [
            {"path": str(gate.relative_to(self.game)).replace("\\", "/"), "sha256": tool.digest(gate.read_bytes())}], "files": []}
        for index in range(2):
            payload = ("synthetic dependency " + str(index)).encode()
            row = {"target": f"BepInEx/plugins/AssetImport/Dependency{index}.dll", "member": f"lib/net47/Dependency{index}.dll",
                   "package": f"fixture{index}.nupkg", "sha256": tool.digest(payload), "bytes": len(payload),
                   "licenseMembers": ["LICENSE.TXT"], "url": f"https://api.nuget.org/v3-flatcontainer/synthetic/{index}.nupkg"}
            self.manifest["files"].append(row)
            self.write_package(row, payload)

    def write_package(self, row, payload, extra=None):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr(row["member"], payload)
            archive.writestr("LICENSE.TXT", b"synthetic license")
            if extra:
                archive.writestr(extra, b"not a dependency")
        data = stream.getvalue()
        row.update(packageSha256=tool.digest(data), packageSha512=base64.b64encode(hashlib.sha512(data).digest()).decode())
        (self.packages / row["package"]).write_bytes(data)

    def run_action(self, action="install", **kwargs):
        return tool.execute(action, self.game, self.manifest, packages=self.packages, running=lambda: False, **kwargs)

    def targets(self):
        return [self.game / row["target"] for row in self.manifest["files"]]

    def tree(self):
        return {str(p.relative_to(self.game)): p.read_bytes() for p in self.game.rglob("*") if p.is_file()}

    def test_default_doctor_and_install_preview_are_read_only(self):
        before = self.tree()
        with patch.object(tool.urllib.request, "urlopen", side_effect=AssertionError("network forbidden")):
            report = self.run_action("doctor")
            preview = self.run_action()
        self.assertTrue(all(row["state"] == "absent" for row in report["targets"]))
        self.assertTrue(preview["applyRequired"])
        self.assertEqual(before, self.tree())

    def test_install_readback_and_complete_repeatable_rollback(self):
        before = self.tree()
        result = self.run_action(apply=True)
        receipt = Path(result["receipt"])
        self.assertTrue(all(path.is_file() for path in self.targets()))
        self.assertEqual(len(list(receipt.parent.glob("*.licenses/LICENSE.TXT"))), 2)
        self.assertEqual(self.run_action(apply=True)["status"], "no_change")
        self.assertEqual(self.run_action("rollback", apply=True, receipt_path=receipt)["filesMoved"], 2)
        self.assertTrue(all(not path.exists() for path in self.targets()))
        self.assertEqual(self.run_action("rollback", apply=True, receipt_path=receipt)["filesMoved"], 0)
        self.assertTrue(all((self.game / path).read_bytes() == data for path, data in before.items()))

    def test_wrong_gate_refuses_before_any_target_write(self):
        (self.plugin / "SyntheticImporter.dll").write_bytes(b"unknown version")
        with self.assertRaisesRegex(ValueError, "source identity"):
            self.run_action(apply=True)
        self.assertTrue(all(not path.exists() for path in self.targets()))

    def test_existing_target_not_overwritten_even_when_other_absent(self):
        first = self.targets()[0]
        first.write_bytes(b"user file")
        with self.assertRaisesRegex(ValueError, "never overwritten"):
            self.run_action(apply=True)
        self.assertEqual(first.read_bytes(), b"user file")
        self.assertFalse(self.targets()[1].exists())

    def test_package_sha256_mismatch_rejected(self):
        (self.packages / self.manifest["files"][1]["package"]).write_bytes(b"changed package")
        with self.assertRaisesRegex(ValueError, "package hash"):
            self.run_action(apply=True)
        self.assertTrue(all(not path.exists() for path in self.targets()))

    def test_package_sha512_mismatch_rejected(self):
        self.manifest["files"][0]["packageSha512"] = "invalid official digest"
        with self.assertRaisesRegex(ValueError, "package hash"):
            self.run_action(apply=True)
        self.assertTrue(all(not path.exists() for path in self.targets()))

    def test_member_hash_mismatch_rejected(self):
        self.manifest["files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "member identity"):
            self.run_action(apply=True)
        self.assertTrue(all(not path.exists() for path in self.targets()))

    def test_traversal_and_absolute_paths_rejected(self):
        for unsafe in ("../escape", "/absolute", "C:/absolute", "..\\escape"):
            with self.subTest(path=unsafe):
                with self.assertRaises(ValueError):
                    tool.safe(self.game, unsafe)
        row = self.manifest["files"][0]
        self.write_package(row, b"synthetic dependency 0", "../escape.dll")
        with self.assertRaisesRegex(ValueError, "Unsafe relative"):
            self.run_action(apply=True)
        self.assertFalse((self.root / "escape.dll").exists())

    def test_duplicate_zip_member_rejected(self):
        row = self.manifest["files"][0]
        with self.assertWarns(UserWarning):
            self.write_package(row, b"synthetic dependency 0", row["member"])
        with self.assertRaisesRegex(ValueError, "Duplicate ZIP"):
            self.run_action(apply=True)

    def test_modified_current_refuses_entire_rollback(self):
        result = self.run_action(apply=True)
        self.targets()[1].write_bytes(b"subsequent user edit")
        with self.assertRaisesRegex(ValueError, "Current file changed"):
            self.run_action("rollback", apply=True, receipt_path=Path(result["receipt"]))
        self.assertTrue(self.targets()[0].exists())
        self.assertEqual(self.targets()[1].read_bytes(), b"subsequent user edit")

    def test_foreign_or_modified_receipt_rejected(self):
        result = self.run_action(apply=True)
        receipt = Path(result["receipt"])
        foreign = self.root / "receipt.json"
        foreign.write_bytes(receipt.read_bytes())
        with self.assertRaisesRegex(ValueError, "this game's"):
            self.run_action("rollback", apply=True, receipt_path=foreign)
        data = json.loads(receipt.read_text())
        data["files"][0]["path"] = "../outside.dll"
        receipt.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, "Receipt identity"):
            self.run_action("rollback", apply=True, receipt_path=receipt)
        self.assertTrue(all(path.exists() for path in self.targets()))

    def test_partial_write_failure_restores_prior_absence(self):
        real_open = Path.open
        second = self.targets()[1].resolve()
        def fail_second(path, mode="r", *args, **kwargs):
            if path == second and mode == "xb":
                raise OSError("synthetic disk failure")
            return real_open(path, mode, *args, **kwargs)
        with patch.object(Path, "open", fail_second):
            with self.assertRaisesRegex(OSError, "disk failure"):
                self.run_action(apply=True)
        self.assertTrue(all(not path.exists() for path in self.targets()))
        receipts = list((self.game / tool.STATE).glob("*/receipt.json"))
        self.assertEqual(json.loads(receipts[0].read_text())["status"], "failed_restored_absence")
        self.assertTrue((receipts[0].parent / "failed/Dependency0.dll").exists())

    def test_middle_write_failure_moves_both_complete_and_partial_files(self):
        real_open = Path.open
        second = self.targets()[1].resolve()
        class PartialWriter:
            def __init__(self, stream):
                self.stream = stream
            def __enter__(self):
                self.stream.__enter__()
                return self
            def __exit__(self, *args):
                return self.stream.__exit__(*args)
            def write(self, data):
                self.stream.write(data[:3])
                raise OSError("synthetic middle write failure")
        def fail_middle(path, mode="r", *args, **kwargs):
            stream = real_open(path, mode, *args, **kwargs)
            return PartialWriter(stream) if path == second and mode == "xb" else stream
        with patch.object(Path, "open", fail_middle):
            with self.assertRaisesRegex(OSError, "middle write failure"):
                self.run_action(apply=True)
        self.assertTrue(all(not path.exists() for path in self.targets()))
        receipt = next((self.game / tool.STATE).glob("*/receipt.json"))
        self.assertEqual(json.loads(receipt.read_text())["status"], "failed_restored_absence")
        self.assertEqual((receipt.parent / "failed/Dependency1.dll").read_bytes(), b"syn")
        self.assertTrue((receipt.parent / "failed/Dependency0.dll").exists())

    def test_failed_cleanup_move_records_recovery_and_continues_other_files(self):
        real_read, real_rename = Path.read_bytes, Path.rename
        first, second = (path.resolve() for path in self.targets())
        def fail_readback(path):
            if path == second:
                raise OSError("synthetic final readback failure")
            return real_read(path)
        def fail_first_move(path, target):
            if path == first:
                raise OSError("synthetic cleanup failure")
            return real_rename(path, target)
        with patch.object(Path, "read_bytes", fail_readback), patch.object(Path, "rename", fail_first_move):
            with self.assertRaisesRegex(OSError, "final readback failure"):
                self.run_action(apply=True)
        receipt = next((self.game / tool.STATE).glob("*/receipt.json"))
        data = json.loads(receipt.read_text())
        self.assertEqual(data["status"], "recovery_required")
        self.assertEqual(data["recoveryFailures"], ["Dependency0.dll"])
        self.assertTrue(first.exists())
        self.assertFalse(self.targets()[1].exists())
        self.assertTrue((receipt.parent / "failed/Dependency1.dll").exists())

    def test_interrupted_rollback_resumes_without_overwriting_backups(self):
        result = self.run_action(apply=True)
        receipt = Path(result["receipt"])
        real_rename = Path.rename
        second = self.targets()[1].resolve()
        def fail_second(path, target):
            if path == second:
                raise OSError("synthetic move failure")
            return real_rename(path, target)
        with patch.object(Path, "rename", fail_second):
            with self.assertRaisesRegex(OSError, "move failure"):
                self.run_action("rollback", apply=True, receipt_path=receipt)
        self.assertFalse(self.targets()[0].exists())
        self.assertTrue(self.targets()[1].exists())
        self.assertEqual(self.run_action("rollback", apply=True, receipt_path=receipt)["filesMoved"], 1)
        self.assertTrue(all(not path.exists() for path in self.targets()))

    def test_existing_mutation_lock_refuses_without_removing_it(self):
        state = self.game / tool.STATE
        state.mkdir(parents=True)
        lock = state / "mutation.lock"
        lock.write_text("another active installer")
        with self.assertRaises(FileExistsError):
            self.run_action(apply=True)
        self.assertEqual(lock.read_text(), "another active installer")
        self.assertTrue(all(not path.exists() for path in self.targets()))

    def test_running_game_refused_without_state_directory(self):
        with self.assertRaisesRegex(ValueError, "Close Studio"):
            tool.execute("install", self.game, self.manifest, self.packages, apply=True, running=lambda: True)
        self.assertFalse((self.game / tool.STATE).exists())

    def test_missing_package_never_downloads_without_opt_in(self):
        (self.packages / self.manifest["files"][0]["package"]).unlink()
        with patch.object(tool.urllib.request, "urlopen", side_effect=AssertionError("network forbidden")):
            with self.assertRaisesRegex(ValueError, "Package missing"):
                self.run_action(apply=True)

    def test_explicit_download_is_pinned_and_verified(self):
        row = self.manifest["files"][0]
        package = self.packages / row["package"]
        data = package.read_bytes()
        package.unlink()
        with patch.object(tool.urllib.request, "urlopen", return_value=io.BytesIO(data)) as request:
            result = self.run_action(apply=True, download=True)
        self.assertEqual(result["status"], "installed")
        request.assert_called_once_with(row["url"], timeout=45)
        self.assertEqual(package.read_bytes(), data)

    def test_process_inventory_and_unsupported_platform_fail_closed(self):
        with patch.object(tool.os, "name", "nt"), patch.object(tool.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, '"CharaStudio.exe","12"\n')):
            self.assertTrue(tool.game_running())
        with patch.object(tool.os, "name", "posix"):
            with self.assertRaises(RuntimeError):
                tool.game_running()

    def test_cli_requires_explicit_mutation_for_download(self):
        result = subprocess.run([sys.executable, "-B", str(TOOL_PATH), "--game-root", str(self.game), "--download-official"], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("requires explicit install", result.stderr)


if __name__ == "__main__":
    unittest.main()
