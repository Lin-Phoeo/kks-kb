"""Exact CoordinateLoadOption status repair and isolated actual-CIL checks; never install."""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = 'ea95a650360cd7f54c1905276afc3afa4829e2e4bf01d7ffb0b2900924c42cae'
CANDIDATE = '30e35088ec27b94209459a4c63a0f1468f488496bf176147ef61a49a85817b25'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('original', 'game-root', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    original, game, output = (x.resolve() for x in (args.original, args.game_root, args.output))
    if not original.is_file() or sha(original) != ORIGINAL:
        parser.error('Unsupported original hash')
    if output.exists():
        parser.error('New output required')
    for parent in [output, *output.parents]:
        if (parent / '.git').exists() or parent.name.casefold() in ('bepinex', 'userdata', 'mods', 'abdata') or parent.name.casefold().endswith('_data'):
            parser.error('Output outside repository/runtime/content required')
    if output == game or game in output.parents and output.relative_to(game).parts[0] != '.scratch':
        parser.error('Game-relative output must be private scratch')
    output.mkdir(parents=True)

    def run(command, label, expected=0):
        result = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace')
        (output / (label + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode != expected:
            raise RuntimeError(label + ': unexpected exit code ' + str(result.returncode))
        return result.stdout

    build = output / 'build'
    for project in ('tool/Tool.csproj', 'tests/Probe.csproj'):
        run(['dotnet', 'build', str(HERE / project), '-c', 'Release', '--artifacts-path', str(build), f'-p:CecilPath={game / "BepInEx/core/Mono.Cecil.dll"}', '--nologo'], 'build-' + Path(project).stem)
    tool = build / 'bin/Tool/release/Tool.dll'
    fixture = build / 'bin/Probe/release/Probe.dll'
    candidate = output / 'candidate/KKS_CoordinateLoadOption.dll'
    run(['dotnet', str(tool), 'patch', str(original), str(game), str(candidate)], 'patch')
    if sha(candidate) != CANDIDATE:
        raise RuntimeError('Unexpected candidate hash')
    checks = {}
    for label, source, failed in [('original', original, 11), ('candidate', candidate, 0)]:
        target = output / label / 'Probe.dll'
        run(['dotnet', str(tool), 'probe', str(source), str(fixture), str(target)], 'extract-' + label)
        shutil.copyfile(fixture.with_suffix('.runtimeconfig.json'), target.with_suffix('.runtimeconfig.json'))
        result = json.loads(run(['dotnet', str(target)], 'run-' + label, 1 if failed else 0))
        if result['checks'] != 33 or result['failed'] != failed:
            raise RuntimeError('Unexpected actual-CIL result ' + label)
        checks[label] = dict(checks=result['checks'], passed=result['passed'], failed=result['failed'])
    guards = []
    for name, mode, source, aux, target in [
        ('unknown', 'patch', fixture, game, output/'unknown.dll'),
        ('repatch', 'patch', candidate, game, output/'twice.dll'),
        ('inplace', 'patch', original, game, original),
        ('existing', 'patch', original, game, candidate),
        ('runtime-output', 'patch', original, game, game/'BepInEx/plugins/DO_NOT_CREATE_COORDINATE_TEST.dll'),
        ('repository-output', 'patch', original, game, HERE/'DO_NOT_CREATE_COORDINATE_TEST.dll'),
        ('unknown-probe', 'probe', fixture, fixture, output/'unknown-probe.dll'),
    ]:
        before = sha(target) if target.exists() else None
        result = subprocess.run(['dotnet',str(tool),mode,str(source),str(aux),str(target)], capture_output=True)
        if result.returncode == 0 or (sha(target) != before if before else target.exists()):
            raise RuntimeError('Refusal check failed: ' + name)
        guards.append(name)
    if sha(original) != ORIGINAL:
        raise RuntimeError('Original input changed')
    receipt = json.loads(Path(str(candidate)+'.receipt.json').read_text())
    report = dict(originalSha256=ORIGINAL,candidateSha256=CANDIDATE,checks=checks,refusalChecks=guards,
        actualCilMethods=17,changedMethods=receipt['changedMethods'],totalMethods=receipt['totalMethods'],unchangedMethods=receipt['unchangedMethods'],
        installed=False,runtimeVerified=False,scope='Actual transfer loops, callbacks and author ForEach against synthetic material/controller/reflection boundaries. No cards, scenes, textures or live game.')
    (output/'verification-summary.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':
    main()
