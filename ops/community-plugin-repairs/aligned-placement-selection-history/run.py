"""Build the exact local KKS1.0.1 selection-history repair. Never install or launch Studio."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess
from output_paths import output_path_error, path_guard_checks
HERE=Path(__file__).resolve().parent
ORIGINAL='0b9a21faf851f8aebcd8eda15d8fc519a6174bde8561c84d6a19959070f2c779'
CANDIDATE='1a844bd061599d80889b39e01687e695faf29e2a8727503eefa230e786abed32'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--original',type=Path,required=True);p.add_argument('--game-root',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();original,game,out=(v.resolve()for v in(a.original,a.game_root,a.output))
if not original.is_file()or sha(original)!=ORIGINAL:p.error('Unknown original')
if out.exists():p.error('Use new external output')
path_error=output_path_error(out,HERE.parents[2],game)
if path_error:p.error(path_error)
path_guards=path_guard_checks()
if not all(path_guards.values()):raise RuntimeError('Output path guard checks failed')
out.mkdir(parents=True)
def run(cmd,label,code=0):
 r=subprocess.run(cmd,capture_output=True,text=True,encoding='utf-8',errors='replace');t=r.stdout+r.stderr;(out/(label+'.txt')).write_text(t,encoding='utf-8')
 if r.returncode!=code:raise RuntimeError(f'{label}: {r.returncode}, expected {code}')
 return t
build=out/'build'
for item in ('tool/Tool.csproj','tests/Probe.csproj','audit/Audit.csproj'):run(['dotnet','build',str(HERE/item),'-c','Release','--artifacts-path',str(build),f'-p:CecilPath={game/"BepInEx/core/Mono.Cecil.dll"}','--nologo'],'build-'+Path(item).stem)
tool=build/'bin/Tool/release/Tool.dll';fixture=build/'bin/Probe/release/Probe.dll';candidate=out/'candidate/KKS_AlignedPlacement.dll'
run(['dotnet',str(tool),'patch',str(original),'unused',str(candidate)],'patch')
if sha(candidate)!=CANDIDATE:raise RuntimeError('Unexpected candidate bytes')
for name,source,failures in (('original',original,6),('candidate',candidate,0)):
 actual=out/name/'Probe.dll';run(['dotnet',str(tool),'probe',str(source),str(fixture),str(actual)],'extract-'+name);shutil.copyfile(fixture.with_suffix('.runtimeconfig.json'),actual.with_suffix('.runtimeconfig.json'));text=run(['dotnet',str(actual)],name,1 if failures else 0)
 if f'checks=10 failures={failures}' not in text or text.count('FAIL ')!=failures:raise RuntimeError('Unexpected control results')
run(['dotnet',str(build/'bin/Audit/release/Audit.dll'),str(original),str(candidate),str(out/'il-audit.json')],'audit')
unknown=out/'unknown.dll';unknown.write_bytes(original.read_bytes()+b'\0');guards={}
for label,source,target in (('unknown',unknown,out/'refuse.dll'),('patched',candidate,out/'repatch.dll'),('samePath',original,original),('existingOutput',original,candidate)):
 before=sha(target)if target.exists()else None;r=subprocess.run(['dotnet',str(tool),'patch',str(source),'unused',str(target)],capture_output=True);guards[label]=r.returncode!=0 and(sha(target)==before if before else not target.exists())
if not all(guards.values())or sha(original)!=ORIGINAL:raise RuntimeError('Input/output guard failed')
report=dict(passed=True,originalSha256=ORIGINAL,candidateSha256=CANDIDATE,checks=10,originalFailures=6,candidateFailures=0,guards=guards,pathGuards=path_guards,installed=False,runtimeVerified=False,scope='Actual ApplyLinearMove/Rotation/CircularPlacement and snapshot/command creation CIL, synthetic Unity/Studio collaborators; no character or live game operation.')
(out/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))
