"""Build exact local DragDoll PLUS history candidate. Does not install or run the game."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = "2d5ce76b35347e64f5fa6b5df65ab8ad90c343f043407160fc6b76dad4cec065"
CANDIDATE = "fe52ab2b851d301232d51803dc72be70400262b9b4a5906bd92096f0c2c64a4f"


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--original", required=True, type=Path)
    p.add_argument("--game-root", required=True, type=Path)
    p.add_argument("--output", required=True, type=Path)
    a = p.parse_args()
    original, game, output = (x.resolve() for x in (a.original, a.game_root, a.output))
    if not original.is_file() or sha(original) != ORIGINAL:
        p.error("Unsupported exact original")
    repo = HERE.parents[2]
    if output.exists() or output == repo or repo in output.parents:
        p.error("Output must be new and outside repository")
    if output == game or any(output == game / x or game / x in output.parents for x in ("BepInEx", "UserData", "mods", "abdata", "CharaStudio_Data")):
        p.error("Output must not be a game runtime or content directory")
    output.mkdir(parents=True)

    def run(command, label, expected=0):
        r = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
        text = r.stdout + r.stderr
        (output / (label + ".txt")).write_text(text, encoding="utf-8")
        if r.returncode != expected:
            raise RuntimeError(f"{label}: exit {r.returncode}, expected {expected}")
        return text

    build = output / "build"
    for project in ("helper/Helper.csproj", "tool/Tool.csproj", "tests/Probe.csproj", "audit/Audit.csproj", "binding/Binding.csproj", "mono-host/MonoHost.csproj"):
        run(["dotnet", "build", str(HERE / project), "-c", "Release", "--artifacts-path", str(build), f"-p:GameRoot={game}", f"-p:CecilPath={game / 'BepInEx/core/Mono.Cecil.dll'}", "--nologo"], "build-" + Path(project).stem)
    tool = build / "bin/Tool/release/Tool.dll"
    helper = build / "bin/Helper/release/Codex.DragDollPlusHistory.dll"
    fixture = build / "bin/Probe/release/Probe.dll"
    candidate = output / "candidate/KKS_DragDollPLUS.dll"
    run(["dotnet", str(tool), "patch", str(original), str(helper), str(game), str(candidate)], "patch")
    if sha(candidate) != CANDIDATE:
        raise RuntimeError("Unexpected candidate bytes")
    for name, source, failures in (("original", original, 20), ("candidate", candidate, 0)):
        probe = output / (name + "-probe") / "Probe.dll"
        run(["dotnet", str(tool), "probe", str(source), str(fixture), str(probe), str(helper)], "extract-" + name)
        shutil.copyfile(fixture.with_suffix(".runtimeconfig.json"), probe.with_suffix(".runtimeconfig.json"))
        text = run(["dotnet", str(probe)], name, 1 if failures else 0)
        if f"checks=33 failures={failures}" not in text or sum(line.startswith("FAIL ") for line in text.splitlines()) != failures:
            raise RuntimeError("Unexpected actual-CIL control result")
    run(["dotnet", str(build / "bin/Audit/release/Audit.dll"), str(original), str(candidate), str(helper), str(game), str(output / "il-audit.json")], "audit")
    audit = json.loads((output / "il-audit.json").read_text(encoding="utf-8"))
    binding_control = output / "binding-control/KKS_DragDollPLUS.dll"
    run(["dotnet", str(tool), "binding-control", str(candidate), str(game), str(binding_control)], "binding-control")
    mono_relative = "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll"
    for label, source, failures in (("binding-control", binding_control, 3), ("binding-candidate", candidate, 0)):
        text = run(["dotnet", str(build / "bin/MonoHost/release/MonoHost.dll"), str(game), str(build / "bin/Binding/release/DragDoll.BindingProbe.dll"), str(source), str(helper), ",".join(audit["helperCallTokens"]), mono_relative], label + "-mono", 1 if failures else 0)
        if f"bindingChecks=4 failures={failures}" not in text or text.count("BINDING_FAIL ") != failures:
            raise RuntimeError("Unexpected independent Mono binding result")
    invalid = output / "unknown.dll"
    invalid.write_bytes(original.read_bytes() + b"\0")
    rejected = output / "refused.dll"
    guards = {}
    for label, source, target in (("unknown", invalid, rejected), ("patched", candidate, output / "repatch.dll"), ("samePath", original, original), ("existing", original, candidate)):
        before = sha(target) if target.exists() else None
        r = subprocess.run(["dotnet", str(tool), "patch", str(source), str(helper), str(game), str(target)], capture_output=True)
        guards[label] = r.returncode != 0 and (sha(target) == before if before else not target.exists())
    receipt_output = output / "existing-receipt/KKS_DragDollPLUS.dll"
    receipt_output.parent.mkdir()
    receipt = Path(str(receipt_output) + ".receipt.json")
    receipt.write_text("receipt sentinel", encoding="utf-8")
    receipt_hash = sha(receipt)
    helper_output = output / "existing-helper/KKS_DragDollPLUS.dll"
    helper_output.parent.mkdir()
    helper_sentinel = helper_output.parent / helper.name
    helper_sentinel.write_bytes(b"helper sentinel")
    helper_hash = sha(helper_sentinel)
    for label, target in (("existingReceipt", receipt_output), ("existingHelper", helper_output)):
        r = subprocess.run(["dotnet", str(tool), "patch", str(original), str(helper), str(game), str(target)], capture_output=True)
        guards[label] = r.returncode != 0 and not target.exists()
    r = subprocess.run(["dotnet", str(tool), "patch", str(original), str(invalid), str(game), str(output / "wrong-helper.dll")], capture_output=True)
    guards["wrongHelper"] = r.returncode != 0 and not (output / "wrong-helper.dll").exists()
    guards["sentinelsPreserved"] = sha(receipt) == receipt_hash and sha(helper_sentinel) == helper_hash
    if not all(guards.values()) or sha(original) != ORIGINAL:
        raise RuntimeError("Original protection failed")
    result = {"passed": True, "installed": False, "runtimeVerified": False, "originalSha256": ORIGINAL, "candidateSha256": sha(candidate), "helperSha256": sha(helper), "checks": 33, "originalFailures": 20, "candidateFailures": 0, "monoBindingChecks": 4, "monoControlFailures": 3, "monoCandidateFailures": 0, "monoRuntimeSha256": sha(game / mono_relative), "guards": guards, "scope": "Actual history Apply/Undo/Redo/Do and Commit with synthetic character/geometry collaborators. Separate process uses installed Mono to resolve actual candidate MemberRefs and invoke external helper with null sentinels; no character, scene, game process or visual test."}
    (output / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
