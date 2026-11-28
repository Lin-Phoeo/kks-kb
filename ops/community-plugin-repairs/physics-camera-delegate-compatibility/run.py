"""Build reviewed physics compatibility candidates and isolated actual-CIL checks; no install."""
from pathlib import Path
import argparse,hashlib,json,subprocess,shutil
HERE=Path(__file__).resolve().parent
SPY=HERE.parent/'softflesh-optional-stall-profiling'
ORIGINALS={'LiquidFluid.KKS.dll':'edefaaa0418663182b04bf74cbaa59132e1b610014ababb5a467d69c778b74f2','SoftFlesh.KKS.dll':'4ef0d800e979a0b597d95dfadee41d546fd17fef8936d9ec3b007bcddf337cf1'}
FINAL={'LiquidFluid.KKS.dll':'5df105e5b4e6d5db59473a1a99fa5d3b9c0c1a027fb48cc2f676b19f18a98ae8','SoftFlesh.KKS.dll':'716cef2c94ee9680ca39db8102e8c258dd735708980a801c22f4859921a7f7a6'}
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__)
 for n in ['liquid','soft','game-root','output']:p.add_argument('--'+n,type=Path,required=True)
 a=p.parse_args();liquid,soft,game,out=[x.resolve()for x in [a.liquid,a.soft,a.game_root,a.output]]
 inputs={'LiquidFluid.KKS.dll':liquid,'SoftFlesh.KKS.dll':soft}
 for name,path in inputs.items():
  if not path.is_file()or sha(path)!=ORIGINALS[name]:p.error('Exact supported original required: '+name)
 if out.exists():p.error('New output directory required')
 for d in [out,*out.parents]:
  if (d/'.git').exists()or d.name.casefold()in ['bepinex','userdata','mods','abdata']or d.name.casefold().endswith('_data'):p.error('Output outside repository/runtime/content required')
 if out==game or game in out.parents and out.relative_to(game).parts[0]!='.scratch':p.error('Only private scratch output under game permitted')
 out.mkdir(parents=True)
 def run(cmd,label,expected=0):
  r=subprocess.run(cmd,capture_output=True,text=True,encoding='utf-8-sig',errors='replace',cwd=out)
  (out/(label+'.log')).write_text(r.stdout+r.stderr,encoding='utf-8')
  if r.returncode!=expected:raise RuntimeError(label+': unexpected exit '+str(r.returncode))
  return r.stdout
 build=out/'build';spybuild=out/'spy-build'
 for project in ['tool/Tool.csproj','tests/Probe.csproj','tests/readiness/ReadinessProbe.csproj','helper/Readiness.csproj','binding/Binding.csproj','mono-host/MonoHost.csproj']:
  run(['dotnet','build',str(HERE/project),'-c','Release','--artifacts-path',str(build),'-p:CecilPath='+str(game/'BepInEx/core/Mono.Cecil.dll'),'-p:GameRoot='+str(game),'--nologo','-v','minimal'],'build-'+Path(project).stem)
 for project in ['tool/Tool.csproj','tests/Probe.csproj']:
  run(['dotnet','build',str(SPY/project),'-c','Release','--artifacts-path',str(spybuild),'-p:CecilPath='+str(game/'BepInEx/core/Mono.Cecil.dll'),'--nologo','-v','minimal'],'build-spy-'+Path(project).stem)
 tool=build/'bin/Tool/release/Tool.dll';spytool=spybuild/'bin/Tool/release/Tool.dll';helper=build/'bin/Readiness/release/Readiness.BuildOnly.dll'
 for name,source in inputs.items():run(['dotnet',str(tool),'patch',str(source),str(game),str(out/'camera'/name)],'patch-camera-'+name)
 spy=out/'camera-spy/SoftFlesh.KKS.dll';run(['dotnet',str(spytool),'patch',str(out/'camera/SoftFlesh.KKS.dll'),str(game),str(spy)],'patch-spy')
 for name,source in [('LiquidFluid.KKS.dll',out/'camera/LiquidFluid.KKS.dll'),('SoftFlesh.KKS.dll',spy)]:
  run(['dotnet',str(tool),'timeline',str(source),str(game),str(helper),str(out/'final'/name)],'patch-timeline-'+name)
  if sha(out/'final'/name)!=FINAL[name]:raise RuntimeError('Final candidate hash drift: '+name)
 checks={}
 fixture=build/'bin/Probe/release/Probe.dll'
 for label,left,right,failures in [('original',liquid,soft,13),('final',out/'final/LiquidFluid.KKS.dll',out/'final/SoftFlesh.KKS.dll',0)]:
  probe=out/(label+'-camera-probe')/'Probe.dll'
  run(['dotnet',str(tool),'probe',str(left),str(right),str(fixture),str(probe)],'extract-camera-'+label)
  shutil.copyfile(fixture.with_suffix('.runtimeconfig.json'),probe.with_suffix('.runtimeconfig.json'))
  result=json.loads(run(['dotnet',str(probe)],'actual-camera-'+label,1 if failures else 0))
  if result['checks']!=22 or result['failed']!=failures:raise RuntimeError('Actual camera result drift')
  checks['camera-'+label]={'checks':result['checks'],'failed':result['failed']}
 spyfixture=spybuild/'bin/Probe/release/Probe.dll'
 for label,source,count in [('original',soft,1),('final',out/'final/SoftFlesh.KKS.dll',0)]:
  probe=out/(label+'-spy-probe')/'Probe.dll'
  run(['dotnet',str(spytool),'probe',str(source),str(spyfixture),str(probe)],'extract-spy-'+label)
  shutil.copyfile(spyfixture.with_suffix('.runtimeconfig.json'),probe.with_suffix('.runtimeconfig.json'))
  result=json.loads(run(['dotnet',str(probe),str(count)],'actual-spy-'+label))
  if result['checks']!=8 or result['failed']:raise RuntimeError('Actual spy result drift')
  checks['spy-'+label]={'checks':8,'failed':0,'expectedApplyCount':count}
 readyfixture=build/'bin/ReadinessProbe/release/ReadinessProbe.dll';readyprobe=out/'readiness-probe/ReadinessProbe.dll'
 run(['dotnet',str(tool),'readiness-probe',str(liquid),str(soft),str(out/'final/LiquidFluid.KKS.dll'),str(out/'final/SoftFlesh.KKS.dll'),str(readyfixture),str(readyprobe)],'extract-readiness')
 shutil.copyfile(readyfixture.with_suffix('.runtimeconfig.json'),readyprobe.with_suffix('.runtimeconfig.json'))
 text=run(['dotnet',str(readyprobe)],'actual-readiness')
 if 'TOTAL checks=40 failed=0'not in text:raise RuntimeError('Actual readiness result drift')
 checks['timeline-readiness']={'checks':40,'failed':0,'scope':'Actual original and final EnsureReady plus final injected helper; UI/bridge collaborators synthetic'}
 for name in inputs:
  text=run(['dotnet',str(build/'bin/MonoHost/release/MonoHost.dll'),str(game),str(build/'bin/Binding/release/Physics.BindingProbe.dll'),str(out/'final'/name),'-','-','MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'],'actual-mono-'+name)
  if 'bindingChecks=4 failures=0'not in text:raise RuntimeError('Native Mono binding result drift')
  checks['mono-'+name]={'checks':4,'failed':0,'scope':'actual final helper and EnsureReady called with empty Chainloader registry; no plugin instantiated'}
 guards=[]
 for label,command,target in [
  ('unknown-input',['dotnet',str(tool),'patch',str(fixture),str(game),str(out/'unknown.dll')],out/'unknown.dll'),
  ('repatch',['dotnet',str(tool),'patch',str(out/'final/LiquidFluid.KKS.dll'),str(game),str(out/'twice.dll')],out/'twice.dll'),
  ('in-place',['dotnet',str(tool),'patch',str(liquid),str(game),str(liquid)],liquid),
  ('overwrite',['dotnet',str(tool),'patch',str(liquid),str(game),str(out/'camera/LiquidFluid.KKS.dll')],out/'camera/LiquidFluid.KKS.dll'),
  ('live-output',['dotnet',str(tool),'patch',str(liquid),str(game),str(game/'BepInEx/plugins/DO_NOT_CREATE_PHYSICS.dll')],game/'BepInEx/plugins/DO_NOT_CREATE_PHYSICS.dll'),
  ('repo-output',['dotnet',str(tool),'patch',str(liquid),str(game),str(HERE/'DO_NOT_CREATE_PHYSICS.dll')],HERE/'DO_NOT_CREATE_PHYSICS.dll'),
  ('timeline-wrong-stage',['dotnet',str(tool),'timeline',str(liquid),str(game),str(helper),str(out/'wrong-stage.dll')],out/'wrong-stage.dll'),
  ('spy-repatch',['dotnet',str(spytool),'patch',str(spy),str(game),str(out/'spy-twice.dll')],out/'spy-twice.dll')]:
  before=sha(target)if target.is_file()else None
  r=subprocess.run(command,capture_output=True)
  if r.returncode==0 or (sha(target)!=before if before else target.exists()):raise RuntimeError('Refusal failed: '+label)
  guards.append(label)
 receipts=[json.loads(f.read_text())for f in sorted(out.glob('*/*.receipt.json'))]
 report={'scope':'Private offline transformation + actual CIL synthetic tests + installed game Mono binding; not Unity functional/runtime UI validation','checks':checks,'refusal_checks':guards,'steps':receipts,'final_sha256':{n:sha(out/'final'/n)for n in inputs},'originals_unchanged':all(sha(p)==ORIGINALS[n]for n,p in inputs.items()),'installed':False,'unity_runtime_verified':False,'limitation':'Panel-hover camera blocking intentionally disabled for these two plugins; all other original functional methods preserved except exact diagnostic activation and Timeline readiness gate'}
 (out/'verification-summary.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print(json.dumps({'passed':True,'checks':checks,'guards':len(guards),'final':report['final_sha256']},ensure_ascii=False))
if __name__=='__main__':main()
