"""Build an exact Hermes async-fade candidate and verify isolated actual IL."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys


ORIGINAL = "d044e20742b7cfdf6dcf9ecf1c10c66a7376dfe263d42c50a33cb8cdbece3743"
CANDIDATE = "4a038039a29d930bd34799cc8c177f76817526bbeba1123f2af833e92aa3b9ec"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def within(path: Path, root: Path) -> bool:
    return path == root or root in path.parents


def has_link(path: Path) -> bool:
    for part in (path, *path.parents):
        if part.exists() and (part.is_symlink() or getattr(part, "is_junction", lambda: False)()):
            return True
    return False


def execute(command: list[str], log: Path, *, refusal: bool = False) -> None:
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    log.write_bytes(result.stdout)
    if refusal == (result.returncode == 0):
        raise RuntimeError(f"Unexpected command result; see {log}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--game-root", type=Path, required=True)
    parser.add_argument("--cecil", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source = Path(__file__).resolve().parent
    repository = source.parents[2]
    original = args.original.resolve(strict=True)
    game = args.game_root.resolve(strict=True)
    cecil = args.cecil.resolve(strict=True)
    raw_output = args.output.absolute()
    output = args.output.resolve()
    if raw_output.exists() or output.exists():
        raise ValueError("Output must be a new directory.")
    if has_link(raw_output):
        raise ValueError("Output path cannot pass through symlinks or junctions.")
    if within(output, repository) or within(output, game):
        raise ValueError("Keep output outside the repository and game tree, including UserData.")
    if within(original, output) or within(cecil, output):
        raise ValueError("Output cannot contain an input.")
    if digest(original) != ORIGINAL:
        raise ValueError("Unsupported Hermes original SHA-256.")
    if not (game / "CharaStudio_Data/Managed/Assembly-CSharp.dll").is_file():
        raise ValueError("Explicit KKS game references are required.")
    original_hash, cecil_hash = digest(original), digest(cecil)
    output.mkdir(parents=True)
    execute(["dotnet", "build", str(source / "tool/Patcher.csproj"), "-c", "Release",
             f"-p:CecilPath={cecil}", "--artifacts-path", str(output / "tool-build"), "--nologo"], output / "build-tool.log")
    execute(["dotnet", "build", str(source / "tests/Probe.csproj"), "-c", "Release",
             f"-p:CecilPath={cecil}", "--artifacts-path", str(output / "probe-build"), "--nologo"], output / "build-probe.log")
    tool = output / "tool-build/bin/Patcher/release/Patcher.dll"
    probe_root = output / "probe-build/bin/Probe/release"
    probe = probe_root / "Probe.dll"
    candidate = output / "candidate/TheBirdOfHermes.KKS.dll"
    execute(["dotnet", str(tool), str(original), str(candidate), str(game)], output / "patch.log")
    if digest(candidate) != CANDIDATE:
        raise RuntimeError("Exact candidate hash mismatch.")
    reports = []
    for mode, binary in (("control", original), ("candidate", candidate)):
        isolated = output / f"{mode}-probe"
        isolated.mkdir()
        extracted = isolated / "Probe.dll"
        execute(["dotnet", str(probe), "extract", str(binary), str(extracted)], output / f"extract-{mode}.log")
        shutil.copy2(probe_root / "Probe.runtimeconfig.json", isolated)
        shutil.copy2(probe_root / "Mono.Cecil.dll", isolated)
        report = output / f"{mode}-results.json"
        execute(["dotnet", str(extracted), mode, str(report)], output / f"run-{mode}.log")
        data = json.loads(report.read_text(encoding="utf-8"))
        expected = (29, 5) if mode == "control" else (34, 0)
        if (data["passed"], data["failed"]) != expected:
            raise RuntimeError(f"Unexpected actual-IL result for {mode}.")
        expected_failures = {
            "V5 pending fade retained", "V5 fade restored after decode",
            "V6 pending fade retained", "V6 fade restored after decode",
            "decode completion clamps oversized trim",
        } if mode == "control" else set()
        if {check["name"] for check in data["checks"] if not check["passed"]} != expected_failures:
            raise RuntimeError(f"Unexpected failing assertions for {mode}.")
        reports.append(data)
    refusal_cases = (
        ("unknown-input", tool, output / "unknown.dll"),
        ("existing-output", original, candidate),
        ("same-input-output", original, original),
    )
    for name, input_path, destination in refusal_cases:
        execute(["dotnet", str(tool), str(input_path), str(destination), str(game)], output / f"refuse-{name}.log", refusal=True)
    execute(["dotnet", str(probe), "extract", str(tool), str(output / "unknown-probe/Probe.dll")], output / "refuse-unknown-extraction.log", refusal=True)
    if digest(original) != original_hash or digest(cecil) != cecil_hash or digest(candidate) != CANDIDATE:
        raise RuntimeError("An input or candidate changed during verification.")
    summary = {"passed": True, "originalSha256": ORIGINAL, "candidateSha256": CANDIDATE,
               "control": {"passed": 29, "failed": 5, "expectedFailures": True},
               "candidate": {"passed": 34, "failed": 0}, "refusals": 4,
               "exploratoryZeroDuration": [r["exploratoryZeroDuration"] for r in reports],
               "inputsUnchanged": True, "installed": False,
               "scope": "Actual LoadV4/V5/V6 and AudioTrack CIL using synthetic audio/decoder/Unity boundaries. No game process or user content accessed; invalid zero-duration observations excluded from pass counts."}
    (output / "verification.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print("PASS: original 29/5 expected, candidate 34/0, four refusals, exact candidate and preserved inputs.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, RuntimeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
