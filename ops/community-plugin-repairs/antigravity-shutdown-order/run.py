"""Build an exact-input shutdown candidate; execute copied Dispose CIL with a synthetic Harmony boundary."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = '99b38b09709e09a65a4e3e78f4f11ba3745ced0e075f8f06476e66fed8a929ab'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original', type=Path, required=True)
    parser.add_argument('--game-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    source, game, out = (p.resolve() for p in (args.original, args.game_root, args.output))
    if not source.is_file() or sha(source) != ORIGINAL:
        parser.error('Unsupported exact original')
    if out.exists():
        parser.error('Output must be new')
    for parent in [out, *out.parents]:
        if (parent / '.git').exists() or parent.name.lower() in ('bepinex', 'userdata', 'mods', 'abdata') or parent.name.lower().endswith('_data'):
            parser.error('Output must be outside repository and runtime/content directories')
    if out == game or game in out.parents and out.relative_to(game).parts[0] != '.scratch':
        parser.error('Game-relative output must be private .scratch')
    out.mkdir(parents=True)

    def run(command, label, expected=0):
        result = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=120)
        (out / (label + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode != expected:
            raise RuntimeError(f'{label}: exit {result.returncode}, expected {expected}')
        return result.stdout

    build = out / 'build'
    for project in ('tool/Tool.csproj', 'tests/Probe.csproj', 'mono/MonoProbe.csproj', 'mono-host/MonoHost.csproj'):
        run(['dotnet', 'build', str(HERE / project), '-c', 'Release', '--artifacts-path', str(build), f'-p:CecilPath={game / "BepInEx/core/Mono.Cecil.dll"}', f'-p:GameRoot={game}', '--nologo'], 'build-' + Path(project).stem)
    tool = build / 'bin/Tool/release/Tool.dll'
    fixture = build / 'bin/Probe/release/Probe.dll'
    candidate = out / 'candidate/KKS_AntiGravity.dll'
    run(['dotnet', str(tool), 'patch', str(source), str(game), str(candidate)], 'patch')
    results = {}
    for label, path, code in [('original', source, 1), ('candidate', candidate, 0)]:
        probe = out / label / 'Probe.dll'
        run(['dotnet', str(tool), 'probe', str(path), str(fixture), str(probe)], 'extract-' + label)
        shutil.copyfile(fixture.with_suffix('.runtimeconfig.json'), probe.with_suffix('.runtimeconfig.json'))
        results[label] = json.loads(run(['dotnet', str(probe)], 'run-' + label, code))
    if results['original']['failed'] == 0 or results['candidate']['passed'] != 15 or results['candidate']['failed'] != 0:
        raise RuntimeError('Unexpected actual Dispose CIL coverage')
    mono_fixture = build / 'bin/MonoProbe/release/AntiGravityMonoProbe.dll'
    mono_host = build / 'bin/MonoHost/release/MonoHost.dll'
    native_results = {}
    for label, path, checks in [('original', source, 5), ('candidate', candidate, 9)]:
        probe = out / ('mono-' + label) / 'AntiGravityMonoProbe.dll'
        run(['dotnet', str(tool), 'probe', str(path), str(mono_fixture), str(probe)], 'extract-mono-' + label)
        text = run(['dotnet', str(mono_host), str(game), str(probe), 'unused', 'unused', label, 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'], 'mono-' + label)
        match = re.search(r'RESULT mode=' + label + r' passed=(\d+) failed=(\d+) actualDisposeCil=True gameMethodsPatched=False', text)
        if not match or tuple(map(int, match.groups())) != (checks, 0):
            raise RuntimeError('Unexpected genuine Mono/Harmony result: ' + label)
        if label == 'original' and 'Parameter __runOriginal does not contain a valid index' not in text:
            raise RuntimeError('Original Harmony failure not reproduced')
        if label == 'candidate' and 'DISPOSE_ERROR none' not in text:
            raise RuntimeError('Candidate actual Dispose failed')
        native_results[label] = dict(passed=checks, failed=0, expectedUnpatchFailure=label == 'original')
    unknown_game = out / 'unknown-harmony-game'
    unknown_core = unknown_game / 'BepInEx/core'
    unknown_core.mkdir(parents=True)
    shutil.copyfile(fixture, unknown_core / '0Harmony.dll')
    guards = []
    for name, command in [
        ('unknown-input', ['patch', str(fixture), str(game), str(out / 'unknown.dll')]),
        ('already-patched', ['patch', str(candidate), str(game), str(out / 'twice.dll')]),
        ('same-path', ['patch', str(source), str(game), str(source)]),
        ('existing-output', ['patch', str(source), str(game), str(candidate)]),
        ('runtime-output', ['patch', str(source), str(game), str(game / 'BepInEx/plugins/DO_NOT_CREATE_ANTIG_TEST.dll')]),
        ('repository-output', ['patch', str(source), str(game), str(HERE / 'DO_NOT_CREATE_ANTIG_TEST.dll')]),
        ('unknown-probe', ['probe', str(fixture), str(fixture), str(out / 'unknown-probe.dll')]),
        ('unknown-harmony', ['patch', str(source), str(unknown_game), str(out / 'unknown-harmony.dll')]),
    ]:
        result = subprocess.run(['dotnet', str(tool), *command], capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=30)
        (out / ('guard-' + name + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode == 0:
            raise RuntimeError('Refusal guard failed: ' + name)
        guards.append(name)
    if sha(source) != ORIGINAL:
        raise RuntimeError('Original input changed')
    receipt = json.loads(candidate.with_suffix('.dll.receipt.json').read_text(encoding='utf-8'))
    summary = dict(originalSha256=ORIGINAL, candidateSha256=sha(candidate), original=dict(passed=results['original']['passed'], failed=results['original']['failed']), candidate=dict(passed=results['candidate']['passed'], failed=results['candidate']['failed']), actualCilMethods=1, guardChecks=guards, changedMethods=receipt['changedMethods'], totalMethods=receipt['totalMethods'], unchangedMethods=receipt['unchangedMethods'], genuineHarmonyVerified=True, genuineHarmonyActualCil=native_results, installed=False, runtimeVerified=False)
    (out / 'verification-summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
