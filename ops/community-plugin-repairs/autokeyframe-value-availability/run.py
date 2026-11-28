"""Build and verify an exact AutoKeyframe current-value availability repair; never install."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ORIGINAL = '0ee625fec99a884aff322bb0e87b33d7105f531ccd88f99b4818711873edc0c7'
CANDIDATE = '703c6d58acf73d000ea0566dc78fd352e5f30d95b19980331b58d861e4700df5'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('original', 'game-root', 'output'):
        parser.add_argument('--'+name, type=Path, required=True)
    args = parser.parse_args()
    original, game, output = (p.resolve() for p in (args.original, args.game_root, args.output))
    repo = HERE.parents[2]
    if not original.is_file() or sha(original) != ORIGINAL:
        parser.error('Unsupported input hash')
    if output.exists() or output == repo or repo in output.parents or output == game:
        parser.error('Use a new directory outside the repository and game root')
    if game in output.parents:
        top = output.relative_to(game).parts[0]
        if top.lower() in ('bepinex', 'userdata', 'mods', 'abdata') or top.lower().endswith('_data'):
            parser.error('Output must not be a game runtime/content directory')
    output.mkdir(parents=True)
    def invoke(cmd, label, expected=0):
        r = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
        text = r.stdout+r.stderr
        (output/(label+'.txt')).write_text(text, encoding='utf-8')
        if r.returncode != expected:
            raise RuntimeError(label+' failed; see private output')
        return text
    build=output/'build'
    for project in ('tool/Patcher.csproj','helper/Helper.csproj','tests/Probe.csproj'):
        invoke(['dotnet','build',str(HERE/project),'-c','Release','--artifacts-path',str(build),f'-p:GameRoot={game}',f'-p:CecilPath={game / "BepInEx/core/Mono.Cecil.dll"}','--nologo'],'build-'+Path(project).stem)
    tool=build/'bin/Patcher/release/Patcher.dll'
    helper=build/'bin/Helper/release/AutoKeyframe.ValueAvailabilityBuild.dll'
    fixture=build/'bin/Probe/release/Probe.dll'
    candidate=output/'candidate/AutoKeyframe.KKS.dll'
    invoke(['dotnet',str(tool),'patch',str(original),str(helper),str(candidate)],'patch')
    assert sha(candidate)==CANDIDATE
    for label,source,failures in (('original',original,7),('candidate',candidate,0)):
        probe=output/(label+'-probe/Probe.dll')
        invoke(['dotnet',str(tool),'probe',str(source),str(fixture),str(probe)],'extract-'+label)
        shutil.copy2(fixture.with_suffix('.runtimeconfig.json'),probe.with_suffix('.runtimeconfig.json'))
        text=invoke(['dotnet',str(probe),str(output/(label+'-results.json'))],label,1 if failures else 0)
        assert f'checks=11 failures={failures}' in text
    audit=json.loads(invoke(['dotnet',str(tool),'audit',str(original),str(candidate),str(game)],'audit'))
    unknown=output/'unknown.dll';unknown.write_bytes(original.read_bytes()+b'\0')
    bad_helper=output/'wrong-helper.dll';data=helper.read_bytes();assert data.count(b'AutoKeyframeRepair')==1;bad_helper.write_bytes(data.replace(b'AutoKeyframeRepair',b'WrongKeyframeStubX'))
    receipt_only=output/'receipt-only.dll';receipt=Path(str(receipt_only)+'.receipt.json');receipt.write_text('sentinel',encoding='utf-8');receipt_hash=sha(receipt)
    guards={}
    for name,source,dependency,target in [('unknown',unknown,helper,output/'unknown-output.dll'),('repatch',candidate,helper,output/'repatch-output.dll'),('existing',original,helper,candidate),('existing-receipt',original,helper,receipt_only),('inplace',original,helper,original),('wrong-helper',original,bad_helper,output/'bad-helper-output.dll')]:
        before=sha(target) if target.exists() else None
        r=subprocess.run(['dotnet',str(tool),'patch',str(source),str(dependency),str(target)],capture_output=True)
        guards[name]=r.returncode!=0 and (sha(target)==before if before else not target.exists())
    assert all(guards.values()) and sha(original)==ORIGINAL and sha(receipt)==receipt_hash
    result={'passed':True,'originalSha256':ORIGINAL,'candidateSha256':CANDIDATE,'actualCil':{'checks':11,'originalFailures':7,'candidateFailures':0},'guards':guards,'audit':audit,'installed':False,'runtimeVerified':False,'scope':'Actual update-loop CIL plus embedded helper with synthetic track callbacks and Unity logger; no game or user scene execution.'}
    (output/'verification.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,indent=2))

if __name__=='__main__':
    main()
