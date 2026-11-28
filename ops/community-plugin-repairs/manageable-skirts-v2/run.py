"""Build and verify V2 locally from explicit inputs. Does not install or launch anything."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
EXPECTED = "17ae25d3f4654687b6b180abc04ca7528790363ecf24b7f461081f30b2d5fc4d"


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def invoke(args: list[str], root: Path, label: str, expected: int = 0) -> None:
    result = subprocess.run(args, text=True, encoding="utf-8", errors="replace", capture_output=True)
    (root / (label + ".txt")).write_text(result.stdout + result.stderr, encoding="utf-8")
    if result.returncode != expected:
        raise RuntimeError(f"{label}: exit {result.returncode}, expected {expected}; inspect its text report")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-root", required=True, type=Path)
    parser.add_argument("--original", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    game, original, output = (p.resolve() for p in (args.game_root, args.original, args.output))
    if not original.is_file() or sha(original) != EXPECTED:
        parser.error("Unsupported original SHA-256; no output created")
    if output.exists() or output == REPO or REPO in output.parents:
        parser.error("Output must be new and outside the repository")
    if output == game or any(output == game / p or game / p in output.parents for p in ("BepInEx", "UserData", "mods", "abdata", "CharaStudio_Data")):
        parser.error("Output must not be a game runtime or content directory")
    for relative in ("BepInEx/core/Mono.Cecil.dll", "BepInEx/core/BepInEx.dll", "CharaStudio_Data/Managed/Assembly-CSharp.dll"):
        if not (game / relative).is_file():
            parser.error(f"Missing dependency: {relative}")
    output.mkdir(parents=True)
    artifacts = output / "build"
    projects = {
        "helper": ("helper/Helper.csproj", "Helper", "Codex.ManageableSkirtsV2State.dll"),
        "patch": ("tools/patch/Patcher.csproj", "Patcher", "Patcher.dll"),
        "managed": ("tools/verify-managed/ManagedVerifier.csproj", "ManagedVerifier", "ManagedVerifier.dll"),
        "audit": ("tools/audit/Audit.csproj", "Audit", "Audit.dll"),
    }
    bins = {}
    for label, (project, name, dll) in projects.items():
        invoke(["dotnet", "build", str(HERE / project), "-c", "Release", "--artifacts-path", str(artifacts), f"-p:GameRoot={game}", "--nologo"], output, "build-" + label)
        bins[label] = artifacts / "bin" / name / "release" / dll
        if not bins[label].is_file():
            raise RuntimeError(f"Missing artifact {name}/{dll}")
    candidate = output / "candidate" / "ziglo_ManageableSkirts_KKS.dll"
    helper = candidate.parent / "Codex.ManageableSkirtsV2State.dll"
    patch = ["dotnet", str(bins["patch"])]
    invoke(patch + [str(original), str(bins["helper"]), str(candidate)], output, "patch")
    invoke(["dotnet", str(bins["managed"]), str(original), str(output / "baseline.json")], output, "baseline", 1)
    invoke(["dotnet", str(bins["managed"]), str(candidate), str(output / "candidate.json")], output, "candidate")
    invoke(["dotnet", str(bins["audit"]), str(original), str(candidate), str(helper), str(game), str(output / "il-audit.json")], output, "audit")

    refusal = output / "refusal"
    refusal.mkdir()
    altered = refusal / "unknown.dll"
    altered.write_bytes(original.read_bytes() + b"\0")
    sentinels = {}
    existing = refusal / "existing-output" / candidate.name
    existing.parent.mkdir()
    existing.write_bytes(b"existing output must survive")
    sentinels[existing] = sha(existing)
    existing_helper = refusal / "existing-helper" / helper.name
    existing_helper.parent.mkdir()
    existing_helper.write_bytes(b"existing helper must survive")
    sentinels[existing_helper] = sha(existing_helper)
    existing_receipt = refusal / "existing-receipt" / (candidate.name + ".receipt.json")
    existing_receipt.parent.mkdir()
    existing_receipt.write_text("existing receipt must survive", encoding="utf-8")
    sentinels[existing_receipt] = sha(existing_receipt)
    cases = {
        "unknownShaRejected": (altered, refusal / "unknown" / candidate.name),
        "alreadyPatchedRejected": (candidate, refusal / "repatch" / candidate.name),
        "existingOutputRejected": (original, existing),
        "existingHelperRejectedBeforeOutput": (original, existing_helper.parent / candidate.name),
        "existingReceiptRejectedBeforeOutput": (original, existing_receipt.parent / candidate.name),
        "sourceOverwriteRejected": (original, original),
    }
    guards = {}
    for name, (source, target) in cases.items():
        before = sha(target) if target.exists() else None
        result = subprocess.run(patch + [str(source), str(bins["helper"]), str(target)], capture_output=True, text=True, encoding="utf-8", errors="replace")
        (refusal / (name + ".txt")).write_text(result.stdout + result.stderr, encoding="utf-8")
        unchanged = sha(target) == before if before else not target.exists()
        guards[name] = result.returncode != 0 and unchanged
    guards["allSentinelsPreserved"] = all(sha(p) == digest for p, digest in sentinels.items())
    guards["originalUnmodified"] = sha(original) == EXPECTED
    if not all(guards.values()):
        raise RuntimeError("Patcher refusal failed")
    baseline = json.loads((output / "baseline.json").read_text(encoding="utf-8"))
    current = json.loads((output / "candidate.json").read_text(encoding="utf-8"))
    if baseline["checks"] != 43 or sum(row["passed"] for row in baseline["results"]) != 9:
        raise RuntimeError("Unexpected baseline; do not count loading errors as expected failures")
    if current["checks"] != 43 or not all(row["passed"] for row in current["results"]):
        raise RuntimeError("Candidate behavior failed")
    result = {
        "schemaVersion": 1, "passed": True, "installed": False, "runtimeVerified": False,
        "originalSha256": EXPECTED, "candidateSha256": sha(candidate), "helperSha256": sha(helper),
        "baseline": {"checks": 43, "passed": 9, "failed": 34, "exitCode": 1},
        "candidate": {"checks": 43, "passed": 43, "failed": 0, "exitCode": 0},
        "guards": guards,
        "limitation": "Real DLL managed methods against synthetic dependencies and installed metadata. No Unity native execution, installation or configuration writes.",
    }
    (output / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
