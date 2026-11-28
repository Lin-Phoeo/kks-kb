"""Build exact-input DBDE repair and execute real CIL against synthetic collaborators; never install."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = 'baa0fcc5c5658182385eb59694e34f5264fd68d87a2885b1ec91f15ee1d98f19'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original',type=Path,required=True)
    parser.add_argument('--game-root',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    a=parser.parse_args()
    source,game,out=(p.resolve() for p in (a.original,a.game_root,a.output))
    if not source.is_file() or sha(source)!=ORIGINAL:parser.error('Unsupported exact original')
    if out.exists():parser.error('Output must be new')
    for parent in [out,*out.parents]:
        if (parent/'.git').exists() or parent.name.lower() in ('bepinex','userdata','mods','abdata') or parent.name.lower().endswith('_data'):parser.error('Output must be outside repository and runtime/content directories')
    if out==game or game in out.parents and out.relative_to(game).parts[0]!='.scratch':parser.error('Game-relative output must be private .scratch')
    out.mkdir(parents=True)
    def run(command,label,expected=0):
        r=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',errors='replace')
        (out/(label+'.log')).write_text(r.stdout+r.stderr,encoding='utf-8')
        if r.returncode!=expected:raise RuntimeError(f'{label}: exit {r.returncode}, expected {expected}')
        return r.stdout
    build=out/'build'
    for project in ('tool/Tool.csproj','tests/Probe.csproj'):
        run(['dotnet','build',str(HERE/project),'-c','Release','--artifacts-path',str(build),f'-p:CecilPath={game/"BepInEx/core/Mono.Cecil.dll"}','--nologo'],'build-'+Path(project).stem)
    tool=build/'bin/Tool/release/Tool.dll';fixture=build/'bin/Probe/release/Probe.dll'
    candidate=out/'candidate/KKS_DynamicBoneDistributionEditor.dll'
    run(['dotnet',str(tool),'patch',str(source),str(game),str(candidate)],'patch')
    results={}
    for label,path,code in [('original',source,1),('candidate',candidate,0)]:
        probe=out/label/'Probe.dll'
        run(['dotnet',str(tool),'probe',str(path),str(fixture),str(probe)],'extract-'+label)
        shutil.copyfile(fixture.with_suffix('.runtimeconfig.json'),probe.with_suffix('.runtimeconfig.json'))
        text=run(['dotnet',str(probe)],'run-'+label,code)
        results[label]=json.loads(text)
    if results['original']['failed']!=16 or results['candidate']['passed']!=22 or results['candidate']['failed']!=0:raise RuntimeError('Unexpected real-CIL coverage')
    guards=[]
    for name,command in [
        ('unknown-input',['patch',str(fixture),str(game),str(out/'unknown.dll')]),
        ('already-patched',['patch',str(candidate),str(game),str(out/'twice.dll')]),
        ('same-path',['patch',str(source),str(game),str(source)]),
        ('existing-output',['patch',str(source),str(game),str(candidate)]),
        ('runtime-output',['patch',str(source),str(game),str(game/'BepInEx/plugins/DO_NOT_CREATE_DBDE_TEST.dll')]),
        ('repository-output',['patch',str(source),str(game),str(HERE/'DO_NOT_CREATE_DBDE_TEST.dll')]),
        ('unknown-probe',['probe',str(fixture),str(fixture),str(out/'unknown-probe.dll')])]:
        p=subprocess.run(['dotnet',str(tool),*command],capture_output=True,text=True,encoding='utf-8',errors='replace')
        (out/('guard-'+name+'.log')).write_text(p.stdout+p.stderr,encoding='utf-8')
        if p.returncode==0:raise RuntimeError('Refusal guard failed: '+name)
        guards.append(name)
    if sha(source)!=ORIGINAL:raise RuntimeError('Original input changed')
    result=dict(originalSha256=ORIGINAL,candidateSha256=sha(candidate),original=dict(passed=results['original']['passed'],failed=results['original']['failed']),candidate=dict(passed=results['candidate']['passed'],failed=results['candidate']['failed']),guardChecks=guards,actualCilMethods=3,installed=False,runtimeVerified=False)
    (out/'verification-summary.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,indent=2))

if __name__=='__main__':main()
