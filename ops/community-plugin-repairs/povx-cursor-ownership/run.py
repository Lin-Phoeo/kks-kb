"""Explicit local POV cursor patch and actual-method synthetic verification. No installation."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = "b1fa80dc3147b071daf90a91ae2bff4ff1216919ab168bd56f30b976cbc1c789"
CANDIDATE = "e73fb5eebe2913817d89981e798953f1011deafb43e8bffa34e57d46db6d115b"


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--cecil", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    original, cecil, output = (p.resolve() for p in (args.original, args.cecil, args.output))
    repo = HERE.parents[2]
    if not original.is_file() or sha(original) != ORIGINAL:
        parser.error("Unsupported original hash")
    if output.exists() or output == repo or repo in output.parents:
        parser.error("Output must be new and outside the repository")
    if not cecil.is_file():
        parser.error("Cecil must exist")
    output.mkdir(parents=True)

    def invoke(command, label, code=0):
        result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
        text = result.stdout + result.stderr
        (output / (label + ".txt")).write_text(text, encoding="utf-8")
        if result.returncode != code:
            raise RuntimeError(f"{label} unexpected exit {result.returncode}")
        return text

    build = output / "build"
    for path in ("tool/Patcher.csproj", "tests/Probe.csproj"):
        invoke(["dotnet", "build", str(HERE / path), "-c", "Release", "--artifacts-path", str(build), f"-p:CecilPath={cecil}", "--nologo"], "build-" + Path(path).stem)
    tool = build / "bin/Patcher/release/Patcher.dll"
    fixture = build / "bin/Probe/release/Probe.dll"
    candidate = output / "candidate/KKS_PovX.dll"
    invoke(["dotnet", str(tool), "patch", str(original), str(candidate)], "patch")
    if sha(candidate) != CANDIDATE:
        raise RuntimeError("Candidate hash differs")
    for label, source, failures in (("original", original, 6), ("candidate", candidate, 0)):
        probe = output / (label + "-probe") / "Probe.dll"
        invoke(["dotnet", str(tool), "probe", str(source), str(fixture), str(probe)], "extract-" + label)
        shutil.copyfile(fixture.with_suffix(".runtimeconfig.json"), probe.with_suffix(".runtimeconfig.json"))
        text = invoke(["dotnet", str(probe)], label, 1 if failures else 0)
        if f"checks=18 failures={failures}" not in text or sum(line.startswith("FAIL ") for line in text.splitlines()) != failures:
            raise RuntimeError("Unexpected behavior count")
    bad = output / "unknown.dll"
    bad.write_bytes(original.read_bytes() + b"\0")
    guards = {}
    for name, source, target in (("unknown", bad, output / "refused.dll"), ("repatch", candidate, output / "repatch.dll"), ("inplace", original, original), ("existing", original, candidate)):
        before = sha(target) if target.exists() else None
        result = subprocess.run(["dotnet", str(tool), "patch", str(source), str(target)], capture_output=True)
        guards[name] = result.returncode != 0 and (sha(target) == before if before else not target.exists())
    if not all(guards.values()) or sha(original) != ORIGINAL or sha(candidate) != CANDIDATE:
        raise RuntimeError("Refusal or file protection failed")
    result = {"passed": True, "installed": False, "runtimeVerified": False, "originalSha256": ORIGINAL, "candidateSha256": CANDIDATE, "checks": 18, "originalFailures": 6, "candidateFailures": 0, "refusals": guards, "scope": "Actual Update_Cursor and ToggleCursorLock CIL with synthetic cursor/input collaborators; no Unity native execution."}
    (output / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
