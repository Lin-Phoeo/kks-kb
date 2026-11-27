"""Narrow, explicit two-file AssetImport dependency install/rollback tool."""
from __future__ import annotations
import argparse
import base64
import csv
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import sys
import urllib.request
import uuid
import zipfile

MANIFEST = Path(__file__).with_name("manifest.json")
STATE = ".community-plugin-repairs/assetimport-dependencies"


def safe(root, relative):
    value = PurePosixPath(relative)
    if value.is_absolute() or ".." in value.parts or "\\" in relative or ":" in relative:
        raise ValueError("Unsafe relative path")
    target = (root / relative).resolve()
    if not target.is_relative_to(root.resolve()) or target == root.resolve():
        raise ValueError("Path escapes its root")
    return target


def digest(data):
    return hashlib.sha256(data).hexdigest()


def game_running():
    if os.name != "nt":
        raise RuntimeError("Mutation requires Windows process verification")
    result = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], check=True, capture_output=True, text=True)
    names = {row[0].lower() for row in csv.reader(io.StringIO(result.stdout)) if row}
    return bool(names & {"charastudio.exe", "koikatsusunshine.exe", "koikatsusunshine_vr.exe"})


def doctor(game, manifest):
    gates = [{"path": row["path"], "matches": safe(game, row["path"]).is_file() and
              digest(safe(game, row["path"]).read_bytes()) == row["sha256"]} for row in manifest["identityGates"]]
    targets = []
    for row in manifest["files"]:
        path = safe(game, row["target"])
        state = "absent" if not path.exists() else "matching" if path.is_file() and digest(path.read_bytes()) == row["sha256"] else "conflict"
        targets.append({"path": row["target"], "state": state})
    return {"identityGates": gates, "targets": targets, "mutation": False}


def package_bytes(packages, row, download):
    path = safe(packages, row["package"])
    if path.exists() and not path.is_file():
        raise ValueError("Package path is not a regular file")
    if path.is_file():
        data = path.read_bytes()
    elif download:
        if not row["url"].startswith("https://api.nuget.org/v3-flatcontainer/"):
            raise ValueError("Only pinned official NuGet URLs are allowed")
        with urllib.request.urlopen(row["url"], timeout=45) as response:
            data = response.read(5_000_001)
    else:
        raise ValueError("Package missing; supply it or explicitly allow official download")
    if digest(data) != row["packageSha256"] or base64.b64encode(hashlib.sha512(data).digest()).decode() != row["packageSha512"]:
        raise ValueError("Official package hash mismatch")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate ZIP member")
        for name in names:
            safe(packages, name.rstrip("/"))
        payload = archive.read(row["member"])
        if len(payload) != row["bytes"] or digest(payload) != row["sha256"]:
            raise ValueError("DLL member identity mismatch")
        licenses = {name: archive.read(name) for name in row["licenseMembers"]}
    if not path.exists():
        packages.mkdir(parents=True, exist_ok=True)
        with path.open("xb") as stream:
            stream.write(data)
    return payload, licenses


def save(path, receipt):
    temporary = path.with_suffix(".new")
    temporary.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def execute(action, game, manifest, packages=None, receipt_path=None, apply=False, download=False, running=game_running):
    game = game.resolve()
    if action == "doctor":
        return doctor(game, manifest)
    if not apply:
        return {"action": action, "applyRequired": True, "doctor": doctor(game, manifest)}
    if running():
        raise ValueError("Close Studio and Koikatsu Sunshine before changing dependencies")
    state = safe(game, STATE)
    state.mkdir(parents=True, exist_ok=True)
    lock = state / "mutation.lock"
    with lock.open("x", encoding="utf-8") as stream:
        stream.write(str(os.getpid()))
    try:
        if action == "rollback":
            path = receipt_path.resolve()
            if not path.is_relative_to(state) or path.name != "receipt.json":
                raise ValueError("Receipt must belong to this game's local tool directory")
            receipt = json.loads(path.read_text(encoding="utf-8"))
            expected = [{"path": row["target"], "sha256": row["sha256"], "previous": "absent"} for row in manifest["files"]]
            if receipt.get("id") != manifest["id"] or receipt.get("files") != expected or receipt.get("game") != str(game):
                raise ValueError("Receipt identity differs")
            moves = []
            for row in expected:
                target, backup = safe(game, row["path"]), safe(path.parent, "removed/" + Path(row["path"]).name)
                if target.exists():
                    if not target.is_file() or digest(target.read_bytes()) != row["sha256"] or backup.exists():
                        raise ValueError("Current file changed or rollback backup conflicts; nothing moved")
                    moves.append((target, backup))
                elif not backup.is_file() or digest(backup.read_bytes()) != row["sha256"]:
                    raise ValueError("Installed file and rollback copy are both missing or changed")
            save(path, dict(receipt, status="rolling_back"))
            for target, backup in moves:
                backup.parent.mkdir(exist_ok=True)
                target.rename(backup)
            save(path, dict(receipt, status="rolled_back"))
            return {"status": "rolled_back", "receipt": str(path), "filesMoved": len(moves)}
        plan = doctor(game, manifest)
        if not all(row["matches"] for row in plan["identityGates"]):
            raise ValueError("AssetImport source identity differs")
        states = [row["state"] for row in plan["targets"]]
        if all(value == "matching" for value in states):
            return {"status": "no_change", "receiptCreated": False}
        if any(value != "absent" for value in states):
            raise ValueError("Only a complete two-file addition is supported; existing targets are never overwritten")
        payloads = [package_bytes(packages.resolve(), row, download) for row in manifest["files"]]
        folder = state / uuid.uuid4().hex
        folder.mkdir()
        path = folder / "receipt.json"
        receipt = {"schemaVersion": 1, "id": manifest["id"], "game": str(game), "status": "installing",
                   "files": [{"path": row["target"], "sha256": row["sha256"], "previous": "absent"} for row in manifest["files"]]}
        save(path, receipt)
        created = []
        try:
            for row, (payload, licenses) in zip(manifest["files"], payloads):
                for name, content in licenses.items():
                    license_path = safe(folder, row["package"] + ".licenses/" + name)
                    license_path.parent.mkdir(parents=True, exist_ok=True)
                    license_path.write_bytes(content)
                target = safe(game, row["target"])
                with target.open("xb") as stream:
                    created.append(target)
                    stream.write(payload)
                if digest(target.read_bytes()) != row["sha256"]:
                    raise ValueError("Installed hash readback differs")
        except Exception:
            failed = folder / "failed"
            failed.mkdir(exist_ok=True)
            recovery_failures = []
            for target in created:
                try:
                    if target.exists():
                        target.rename(failed / target.name)
                except OSError:
                    recovery_failures.append(target.name)
            save(path, dict(receipt, status="recovery_required" if recovery_failures else "failed_restored_absence", recoveryFailures=recovery_failures))
            raise
        save(path, dict(receipt, status="installed", runtimeVerified=False))
        return {"status": "installed", "receipt": str(path), "runtimeVerified": False}
    finally:
        lock.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", nargs="?", default="doctor", choices=("doctor", "install", "rollback"))
    parser.add_argument("--game-root", required=True, type=Path)
    parser.add_argument("--package-dir", type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--download-official", action="store_true")
    args = parser.parse_args()
    if args.action == "install" and args.apply and args.package_dir is None:
        parser.error("Install requires --package-dir for supplied or explicitly downloaded packages")
    if args.action == "rollback" and args.apply and args.receipt is None:
        parser.error("Rollback requires its original --receipt")
    if args.download_official and not (args.action == "install" and args.apply):
        parser.error("Official download requires explicit install --apply")
    print(json.dumps(execute(args.action, args.game_root, json.loads(MANIFEST.read_text(encoding="utf-8")),
                             args.package_dir, args.receipt, args.apply, args.download_official), indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, RuntimeError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print("Refused: " + str(error), file=sys.stderr)
        raise SystemExit(1)
