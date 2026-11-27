"""Explicit local build/verification. Never installs or changes game configuration."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
EXPECTED = "81f517809512b1d5a0971befa40a4c4124ea5fb3e7f0e80339944b56f96f652f"


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def invoke(args: list[str], output: Path, label: str, expected: int = 0) -> None:
    result = subprocess.run(args, text=True, encoding="utf-8", errors="replace", capture_output=True)
    (output / (label + ".txt")).write_text(result.stdout + result.stderr, encoding="utf-8")
    if result.returncode != expected:
        raise RuntimeError(f"{label}: exit {result.returncode}, expected {expected}; read {label}.txt")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-root", required=True, type=Path, help="Explicit read-only game assembly dependencies")
    parser.add_argument("--original", required=True, type=Path, help="Author DLL with the exact supported SHA-256")
    parser.add_argument("--output", required=True, type=Path, help="New output directory outside the repository")
    parser.add_argument("--prior-helper", type=Path, help="Optional older helper for semantic IL comparison")
    args = parser.parse_args()
    game, original, output = (p.resolve() for p in (args.game_root, args.original, args.output))
    if not original.is_file() or sha(original) != EXPECTED:
        parser.error("Original file does not have the exact supported SHA-256; no output created")
    if output.exists() or output == REPO or REPO in output.parents:
        parser.error("Output must be new and outside the repository")
    if output == game or any(output == game / p or game / p in output.parents for p in ("BepInEx", "UserData", "mods", "abdata")):
        parser.error("Output must not be a game runtime or content directory")
    for relative in ("BepInEx/core/Mono.Cecil.dll", "BepInEx/core/BepInEx.dll", "CharaStudio_Data/Managed/Assembly-CSharp.dll"):
        if not (game / relative).is_file():
            parser.error(f"Missing explicitly requested dependency: {relative}")
    if args.prior_helper and not args.prior_helper.resolve().is_file():
        parser.error("Prior helper does not exist")
    output.mkdir(parents=True)
    artifacts = output / "build"
    projects = {"helper": ("helper/Helper.csproj", "Helper", "Codex.ManageableSkirtsState.dll"),
                "patch": ("tools/patch/Patcher.csproj", "Patcher", "Patcher.dll"),
                "managed": ("tools/verify-managed/ManagedVerifier.csproj", "ManagedVerifier", "ManagedVerifier.dll"),
                "audit": ("tools/audit/Audit.csproj", "Audit", "Audit.dll")}
    executables = {}
    for label, (project, name, dll) in projects.items():
        invoke(["dotnet", "build", str(HERE / project), "-c", "Release", "--artifacts-path", str(artifacts),
                f"-p:GameRoot={game}", "--nologo"], output, "build-" + label)
        executables[label] = artifacts / "bin" / name / "release" / dll
        if not executables[label].is_file():
            raise RuntimeError(f"Expected build artifact missing: {name}/{dll}")
    candidate = output / "candidate" / "ziglo_ManageableSkirts.dll"
    invoke(["dotnet", str(executables["patch"]), str(original), str(executables["helper"]), str(candidate)], output, "patch")
    invoke(["dotnet", str(executables["managed"]), str(original), str(output / "baseline.json")], output, "baseline", expected=1)
    invoke(["dotnet", str(executables["managed"]), str(candidate), str(output / "candidate.json")], output, "candidate")
    audit = ["dotnet", str(executables["audit"]), str(original), str(candidate),
             str(candidate.parent / "Codex.ManageableSkirtsState.dll"), str(game), str(output / "il-audit.json")]
    if args.prior_helper:
        audit.append(str(args.prior_helper.resolve()))
    invoke(audit, output, "il-audit")
    altered = output / "unknown.dll"
    altered.write_bytes(original.read_bytes() + b"\0")
    rejected = output / "unknown-output" / "ziglo_ManageableSkirts.dll"
    guard = subprocess.run(["dotnet", str(executables["patch"]), str(altered), str(executables["helper"]), str(rejected)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    overwrite = subprocess.run(["dotnet", str(executables["patch"]), str(original), str(executables["helper"]), str(original)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    gates = {"unknownShaRejected": guard.returncode != 0 and not rejected.exists(),
             "sourceOverwriteRejected": overwrite.returncode != 0 and sha(original) == EXPECTED}
    if not all(gates.values()):
        raise RuntimeError("Patcher guard failed")
    baseline = json.loads((output / "baseline.json").read_text(encoding="utf-8"))
    current = json.loads((output / "candidate.json").read_text(encoding="utf-8"))
    if baseline["checks"] != 26 or sum(row["passed"] for row in baseline["results"]) != 6:
        raise RuntimeError("Unexpected baseline result; do not call an assembly-loading failure a regression")
    result = {"schemaVersion": 2, "patchRevision": 2, "passed": True, "installed": False, "originalSha256": EXPECTED,
              "candidateSha256": sha(candidate), "helperSha256": sha(candidate.parent / "Codex.ManageableSkirtsState.dll"),
              "baseline": {"checks": baseline["checks"], "passed": sum(row["passed"] for row in baseline["results"]), "exitCode": 1},
              "candidate": {"checks": current["checks"], "passed": sum(row["passed"] for row in current["results"]), "exitCode": 0},
              "guards": gates, "limitation": "Managed synthetic dependency fixtures and installed metadata only; no Unity native/game execution or configuration writes."}
    (output / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
