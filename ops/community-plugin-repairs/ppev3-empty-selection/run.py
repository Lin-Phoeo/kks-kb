"""Exact local PPEv3 empty-selection fix and actual-CIL checks; never install."""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = '864b641f5458b8099980f7ced378e1df7023fe60b70195f8de6272dee3d790d0'
CANDIDATE = '2fdf17fb9a2d2748c489bae3e80464d286adf237702183f405d51c81a42f1c14'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ('original', 'game-root', 'output'):
        p.add_argument('--' + name, type=Path, required=True)
    a = p.parse_args()
    original, game, out = (x.resolve() for x in (a.original, a.game_root, a.output))
    if not original.is_file() or sha(original) != ORIGINAL:
        p.error('Exact locally repaired input required')
    if out.exists():
        p.error('New output required')
    for parent in [out, *out.parents]:
        if (parent / '.git').exists() or parent.name.casefold() in ('bepinex', 'userdata', 'mods', 'abdata') or parent.name.casefold().endswith('_data'):
            p.error('Output outside repository/runtime/content required')
    if out == game or game in out.parents and out.relative_to(game).parts[0] != '.scratch':
        p.error('Only private game scratch output allowed')
    out.mkdir(parents=True)

    def run(cmd, label, expected=0):
        result = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
        (out / (label + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode != expected:
            raise RuntimeError(label + ': unexpected exit ' + str(result.returncode))
        return result.stdout

    build = out / 'build'
    for project in ('tool/Tool.csproj', 'tests/Probe.csproj'):
        run(['dotnet', 'build', str(HERE / project), '-c', 'Release', '--artifacts-path', str(build), f'-p:CecilPath={game / "BepInEx/core/Mono.Cecil.dll"}', '--nologo'], 'build-' + Path(project).stem)
    tool = build / 'bin/Tool/release/Tool.dll'
    fixture = build / 'bin/Probe/release/Probe.dll'
    candidate = out / 'candidate/KKS_PostProcessingEffect.dll'
    run(['dotnet', str(tool), 'patch', str(original), str(game), str(candidate)], 'patch')
    if sha(candidate) != CANDIDATE:
        raise RuntimeError('Unexpected candidate hash')
    checks = {}
    for label, source, failures in [('original', original, 2), ('candidate', candidate, 0)]:
        probe = out / label / 'Probe.dll'
        run(['dotnet', str(tool), 'probe', str(source), str(fixture), str(probe)], 'extract-' + label)
        shutil.copyfile(fixture.with_suffix('.runtimeconfig.json'), probe.with_suffix('.runtimeconfig.json'))
        result = json.loads(run(['dotnet', str(probe)], 'run-' + label, 1 if failures else 0))
        if result['checks'] != 10 or result['failed'] != failures:
            raise RuntimeError('Unexpected actual-CIL result')
        checks[label] = {k: result[k] for k in ('checks', 'passed', 'failed')}
    guards = []
    for name, mode, source, aux, target in [
        ('unknown', 'patch', fixture, game, out/'unknown.dll'), ('repatch', 'patch', candidate, game, out/'twice.dll'),
        ('inplace', 'patch', original, game, original), ('existing', 'patch', original, game, candidate),
        ('runtime-output', 'patch', original, game, game/'BepInEx/plugins/DO_NOT_CREATE_PPE_TEST.dll'),
        ('repository-output', 'patch', original, game, HERE/'DO_NOT_CREATE_PPE_TEST.dll'),
        ('unknown-probe', 'probe', fixture, fixture, out/'unknown-probe.dll')]:
        before = sha(target) if target.exists() else None
        result = subprocess.run(['dotnet',str(tool),mode,str(source),str(aux),str(target)],capture_output=True)
        if result.returncode == 0 or (sha(target) != before if before else target.exists()):
            raise RuntimeError('Refusal failed: ' + name)
        guards.append(name)
    if sha(original) != ORIGINAL:
        raise RuntimeError('Original input changed')
    receipt = json.loads(Path(str(candidate)+'.receipt.json').read_text())
    report = {**receipt, 'checks': checks, 'refusalChecks': guards, 'actualCilMethods': 1, 'runtimeVerified': False,
        'scope': 'Actual selection callback with synthetic game-mode, event, character and Transform collaborators; no live selection, scene, rendering or config operations.'}
    (out/'verification-summary.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':
    main()
