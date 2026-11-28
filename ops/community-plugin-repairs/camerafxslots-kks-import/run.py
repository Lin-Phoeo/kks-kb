"""Explicit opt-in CameraFXSlots KKS build and actual-CIL validation. Never installs."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = "432aa2935d257b11d7fff680a2fb357b0ac6cca08f72f9e24780083b28e7685b"
CONTROL = "0aa7bb2db66b86b4e03251d7506c734e4f3c046fccb9188c36cceddc27d834ef"
CANDIDATE = "5cf6b7b8f410055067f81c2c013eeca5f8bf47c5f7117e7ad2745564c0e4cfd8"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--game-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    original, game, output = (p.resolve() for p in (args.original, args.game_root, args.output))
    if not original.is_file() or sha(original) != ORIGINAL:
        parser.error("Unsupported original hash")
    repo = HERE.parents[2]
    if output.exists() or output == repo or repo in output.parents:
        parser.error("Output must be new and outside repository")
    if output == game or any(output == game / p or game / p in output.parents for p in ("BepInEx", "UserData", "mods", "abdata", "CharaStudio_Data")):
        parser.error("Output must not be a game runtime or content directory")
    output.mkdir(parents=True)

    def invoke(command, label, expected=0):
        r = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
        text = r.stdout + r.stderr
        (output / (label + ".txt")).write_text(text, encoding="utf-8")
        if r.returncode != expected:
            raise RuntimeError(f"{label}: unexpected exit {r.returncode}")
        return text

    build = output / "build"
    for project in ("tool/Tool.csproj", "tests/Probe.csproj", "audit/Audit.csproj", "helper/Helper.csproj"):
        invoke(["dotnet", "build", str(HERE / project), "-c", "Release", "--artifacts-path", str(build), f"-p:CecilPath={game / 'BepInEx/core/Mono.Cecil.dll'}", f"-p:GameRoot={game}", "--nologo"], "build-" + Path(project).stem)
    tool = build / "bin/Tool/release/Tool.dll"
    fixture = build / "bin/Probe/release/Probe.dll"
    candidate = output / "candidate/ziglo_CameraFXSlots.dll"
    control = output / "control/ziglo_CameraFXSlots.dll"
    invoke(["dotnet", str(tool), "patch", str(original), str(game), str(control)], "patch-import")
    if sha(control) != CONTROL:
        raise RuntimeError("Import control hash changed")
    helper = build / "bin/Helper/release/CameraFXSlots.LifecycleBuild.dll"
    invoke(["dotnet", str(tool), "lifecycle", str(control), str(helper), str(candidate)], "patch-lifecycle")
    if sha(candidate) != CANDIDATE:
        raise RuntimeError("Unexpected candidate hash; target dependency or writer changed")
    for name, source, failures in (("original", original, 26), ("control", control, 14), ("candidate", candidate, 0)):
        probe = output / (name + "-probe") / "Probe.dll"
        invoke(["dotnet", str(tool), "probe", str(source), str(fixture), str(probe)], "extract-" + name)
        shutil.copyfile(fixture.with_suffix(".runtimeconfig.json"), probe.with_suffix(".runtimeconfig.json"))
        result = invoke(["dotnet", str(probe), str(output / (name + "-results.json"))], name, 1 if failures else 0)
        if f"checks=41 failures={failures}" not in result or sum(line.startswith("FAIL ") for line in result.splitlines()) != failures:
            raise RuntimeError("Unexpected actual-CIL baseline/candidate counts")
    invoke(["dotnet", str(build / "bin/Audit/release/Audit.dll"), str(original), str(candidate), str(game), str(output / "il-audit.json")], "il-audit")
    unknown = output / "unknown.dll"
    unknown.write_bytes(original.read_bytes() + b"\0")
    receipt_target = output / "receipt-only/ziglo_CameraFXSlots.dll"
    receipt_target.parent.mkdir()
    receipt = Path(str(receipt_target) + ".receipt.json")
    receipt.write_text("sentinel receipt", encoding="utf-8")
    receipt_sha = sha(receipt)
    guards = {}
    for name, source, target in (("unknown", unknown, output / "refused.dll"), ("repatch", candidate, output / "repatched.dll"), ("existing", original, candidate), ("existingReceipt", original, receipt_target), ("inplace", original, original)):
        before = sha(target) if target.exists() else None
        r = subprocess.run(["dotnet", str(tool), "patch", str(source), str(game), str(target)], capture_output=True, text=True, encoding="utf-8", errors="replace")
        (output / ("refusal-" + name + ".txt")).write_text(r.stdout + r.stderr, encoding="utf-8")
        guards[name] = r.returncode != 0 and (sha(target) == before if before else not target.exists())
    guards["originalAndReceiptPreserved"] = sha(original) == ORIGINAL and sha(receipt) == receipt_sha
    wrong_game = output / "wrong-event-game"
    invoke(["dotnet", str(tool), "wrong-event-fixture", str(wrong_game / "BepInEx/plugins/KKS_BepisPlugins/KKS_ExtensibleSaveFormat.dll")], "make-wrong-event")
    refused = output / "wrong-event-output.dll"
    r = subprocess.run(["dotnet", str(tool), "patch", str(original), str(wrong_game), str(refused)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    (output / "refusal-event-signature.txt").write_text(r.stdout + r.stderr, encoding="utf-8")
    guards["changedTargetEventSignatureRejected"] = r.returncode != 0 and not refused.exists() and "Changed scene handler signature" in r.stdout + r.stderr
    for name, source, support, target in (("lifecycleUnknownSource", unknown, helper, output / "life-unknown.dll"), ("lifecycleWrongHelper", control, unknown, output / "life-helper.dll"), ("lifecycleAlreadyPatched", candidate, helper, output / "life-repeat.dll"), ("lifecycleExistingOutput", control, helper, candidate), ("lifecycleExistingReceipt", control, helper, receipt_target), ("lifecycleInplace", control, helper, control)):
        before = sha(target) if target.exists() else None
        r = subprocess.run(["dotnet", str(tool), "lifecycle", str(source), str(support), str(target)], capture_output=True)
        guards[name] = r.returncode != 0 and (sha(target) == before if before else not target.exists())
    if not all(guards.values()):
        raise RuntimeError("Refusal guard failed")
    summary = {"passed": True, "installed": False, "runtimeVerified": False, "originalSha256": ORIGINAL, "importControlSha256": CONTROL, "candidateSha256": CANDIDATE, "actualCil": {"checks": 41, "originalFailures": 26, "importControlFailures": 14, "candidateFailures": 0}, "guards": guards, "importPolicy": "Native import retains camera slots; preserve all current FX slots and do not import source camera FX.", "scope": "Actual managed event/data/UI creation methods with synthetic dependencies plus installed API metadata. Lifecycle helper and cloned-FX ownership marker embedded. No Unity/native/UI or real scene disk roundtrip."}
    (output / "verification.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
