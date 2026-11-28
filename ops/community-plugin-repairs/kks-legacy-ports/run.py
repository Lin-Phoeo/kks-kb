"""Generate an exact local KKS API port and check installed metadata. Never installs."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import shutil

HERE = Path(__file__).resolve().parent
CASES = {
    "36daa827fccce2165297a0944dd4087c59ebdc02a12fa077f8739bf61eea2775": ("TimelinePOVSwitchX.dll", "b96ca0ab518a98bd1039c1aeb1aace0628c937c9448aa9c2b00544bc5717f754", 279),
    "1283d17f92e24d8d4b735b4d2d95c5c550b25e4b0f93ab3b36b83ae20a611df0": ("KK_StudioVoice.dll", "bd61e6522ae5bce52797e34069c0d427bb4fc96b0a0f38a8ba2ff70cd05b16b7", 204),
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--game-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    original, game, output = (p.resolve() for p in (args.original, args.game_root, args.output))
    digest = sha(original)
    if digest not in CASES:
        parser.error("Unsupported original SHA-256")
    repo = HERE.parents[2]
    if output.exists() or output == repo or repo in output.parents:
        parser.error("Output must be new and outside the repository")
    if output == game or any(output == game / p or game / p in output.parents for p in ("BepInEx", "UserData", "mods", "abdata", "CharaStudio_Data")):
        parser.error("Output must not be a game runtime or content directory")
    output.mkdir(parents=True)
    name, target_hash, member_count = CASES[digest]

    def invoke(command, label, expected=0):
        r = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
        (output / (label + ".txt")).write_text(r.stdout + r.stderr, encoding="utf-8")
        if r.returncode != expected:
            raise RuntimeError(f"{label} exit {r.returncode}")
        return r.stdout + r.stderr

    build = output / "build"
    for project in ("tool/Patcher.csproj", "audit/Audit.csproj"):
        invoke(["dotnet", "build", str(HERE / project), "-c", "Release", "--artifacts-path", str(build), f"-p:CecilPath={game / 'BepInEx/core/Mono.Cecil.dll'}", "--nologo"], "build-" + Path(project).stem)
    tool = build / "bin/Patcher/release/Patcher.dll"
    audit = build / "bin/Audit/release/Audit.dll"
    candidate = output / "candidate" / name
    invoke(["dotnet", str(tool), str(original), str(game), str(candidate)], "patch")
    if sha(candidate) != target_hash:
        raise RuntimeError("Unexpected candidate bytes; dependency identity may have changed")
    invoke(["dotnet", str(audit), str(game), str(output / "api.json"), str(candidate)], "audit")
    result = json.loads((output / "api.json").read_text(encoding="utf-8"))[0]
    missing = [m for m in result["members"] if not m["resolved"]]
    if missing or len(result["members"]) != member_count:
        raise RuntimeError("Metadata references do not match reviewed target")
    if not all(row["valid"] for row in result["harmonyTargets"]):
        raise RuntimeError("Declared Harmony target or patch ABI does not resolve")
    mouth = None
    if name == "KK_StudioVoice.dll":
        invoke(["dotnet", "build", str(HERE / "mouth-tests/MouthTests.csproj"), "-c", "Release", "--artifacts-path", str(build), f"-p:GameRoot={game}", "--nologo"], "build-mouth")
        fixture = build / "bin/MouthTests/release/MouthTests.dll"
        for label, source, failures in (("original", original, 4), ("candidate", candidate, 0)):
            probe = output / (label + "-mouth-probe") / "MouthTests.dll"
            invoke(["dotnet", str(tool), "mouth-probe", str(source), str(game / "CharaStudio_Data/Managed/Assembly-CSharp.dll"), str(fixture), str(probe)], "extract-mouth-" + label)
            for dependency in ("0Harmony.dll", "Mono.Cecil.dll", "MonoMod.RuntimeDetour.dll", "MonoMod.Utils.dll", "MouthTests.runtimeconfig.json"):
                shutil.copyfile(fixture.parent / dependency, probe.parent / dependency)
            text = invoke(["dotnet", str(probe)], "mouth-" + label, 1 if failures else 0)
            if f"checks=11 failures={failures}" not in text or sum(line.startswith("FAIL ") for line in text.splitlines()) != failures:
                raise RuntimeError("Unexpected mouth target/actual-CIL baseline")
        mouth = {"checks": 11, "originalFailures": 4, "candidateFailures": 0, "scope": "Exact author mouth patch and actual KKS CalcBlend CIL with synthetic character/blend collaborators; direct prefix dispatch after reflected target/argument contract resolution. Not a Harmony engine installation test."}
    bad = output / "unknown.dll"
    bad.write_bytes(original.read_bytes() + b"\0")
    guards = {}
    for label, source, target in (("unknown", bad, output / "refused.dll"), ("repatch", candidate, output / "repatched.dll"), ("inplace", original, original), ("existing", original, candidate)):
        before = sha(target) if target.exists() else None
        r = subprocess.run(["dotnet", str(tool), str(source), str(game), str(target)], capture_output=True)
        guards[label] = r.returncode != 0 and (sha(target) == before if before else not target.exists())
    if mouth:
        wrong_game = output / "wrong-target-fixture"
        for relative in ("BepInEx/plugins/KKSAPI.dll", "CharaStudio_Data/Managed/UnityEngine.ImageConversionModule.dll"):
            target = wrong_game / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(game / relative, target)
        invoke(["dotnet", str(tool), "wrong-mouth-target", str(wrong_game / "CharaStudio_Data/Managed/Assembly-CSharp.dll")], "make-wrong-target")
        rejected = output / "wrong-target-candidate.dll"
        r = subprocess.run(["dotnet", str(tool), str(original), str(wrong_game), str(rejected)], capture_output=True, text=True, encoding="utf-8", errors="replace")
        (output / "wrong-target-refusal.txt").write_text(r.stdout + r.stderr, encoding="utf-8")
        guards["changedKksMouthTargetSignature"] = r.returncode != 0 and not rejected.exists() and "Unknown KKS mouth target" in r.stdout + r.stderr
    if not all(guards.values()) or sha(original) != digest or sha(candidate) != target_hash:
        raise RuntimeError("Refusal or source protection failed")
    summary = {"passed": True, "installed": False, "runtimeVerified": False, "originalSha256": digest, "candidateSha256": target_hash, "resolvedMembers": member_count, "unresolvedMembers": 0, "declaredHarmonyTargets": result["harmonyTargets"], "mouth": mouth, "guards": guards, "scope": "Metadata plus explicit synthetic actual-CIL mouth test when applicable. No Unity native or live plugin execution. Harmony owner registration, voice data coverage and Timeline camera behavior remain runtime gates."}
    (output / "verification.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
