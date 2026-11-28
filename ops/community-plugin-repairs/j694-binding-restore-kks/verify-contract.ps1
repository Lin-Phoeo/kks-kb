param([Parameter(Mandatory=$true)][string]$OriginalPath,[Parameter(Mandatory=$true)][string]$CandidatePath,[Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$ReportPath)
$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'))
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
foreach($dir in @((Join-Path $GameRoot 'BepInEx/core'),(Join-Path $GameRoot 'CharaStudio_Data/Managed'),(Join-Path $GameRoot 'BepInEx/plugins'))+@(Get-ChildItem -LiteralPath (Join-Path $GameRoot 'BepInEx/plugins') -Directory -Recurse|ForEach-Object FullName)){$resolver.AddSearchDirectory($dir)}
$rp=[Mono.Cecil.ReaderParameters]::new();$rp.AssemblyResolver=$resolver
function Types($ts){foreach($t in $ts){$t;Types $t.NestedTypes}}
function Shape($m){
 if(!$m.HasBody){return 'no-body'}
 $parts=[Collections.Generic.List[string]]::new();$parts.Add("$($m.Attributes)|$($m.Body.InitLocals)|$(@($m.Body.Variables|ForEach-Object {$_.VariableType.FullName}) -join '|')")
 foreach($i in $m.Body.Instructions){$operand=$i.Operand;if($operand -is [Mono.Cecil.Cil.Instruction]){$operand='jump'+$m.Body.Instructions.IndexOf($operand)}elseif($operand -is [Mono.Cecil.Cil.Instruction[]]){$operand=@($operand|ForEach-Object {$m.Body.Instructions.IndexOf($_)}) -join ','}elseif($operand -is [Mono.Cecil.MemberReference]){$operand=$operand.FullName};$parts.Add("$($i.OpCode.Code) $operand")}
 foreach($e in $m.Body.ExceptionHandlers){$parts.Add("EH $($e.HandlerType) $($e.CatchType.FullName) $($m.Body.Instructions.IndexOf($e.TryStart)) $($m.Body.Instructions.IndexOf($e.TryEnd)) $($m.Body.Instructions.IndexOf($e.HandlerStart)) $($m.Body.Instructions.IndexOf($e.HandlerEnd))")}
 return $parts -join "`n"
}
function InheritedField($t,$name){while($t){$f=@($t.Fields|Where-Object Name -ceq $name);if($f.Count){return $f[0]};if(!$t.BaseType){return $null};$t=$t.BaseType.Resolve()}}
function RequiredMethod($t,$name,$signature){$m=@($t.Methods|Where-Object {$_.Name -ceq $name -and (@($_.Parameters|ForEach-Object {$_.ParameterType.FullName}) -join '|') -ceq ($signature -join '|')});if($m.Count -ne 1){throw "Method contract failed: $($t.FullName).$name"};return $m[0]}
function ResourceHashes($a){$rows=@();foreach($r in $a.MainModule.Resources){if($r -isnot [Mono.Cecil.EmbeddedResource]){throw 'Unexpected resource kind'};$sha=[Security.Cryptography.SHA256]::Create();try{$rows+=($r.Name+':'+[BitConverter]::ToString($sha.ComputeHash($r.GetResourceData())))}finally{$sha.Dispose()}};return $rows -join '|'}
$old=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($OriginalPath,$rp);$new=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($CandidatePath,$rp)
$pe=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/KKSPE.dll'),$rp);$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'CharaStudio_Data/Managed/Assembly-CSharp.dll'),$rp)
try{
 $before=@{};foreach($t in (Types $old.MainModule.Types)){foreach($m in $t.Methods){$before[$m.FullName]=Shape $m}}
 $changed=@();$after=@();foreach($t in (Types $new.MainModule.Types)){foreach($m in $t.Methods){$after+=$m.FullName;if(!$before.ContainsKey($m.FullName) -or $before[$m.FullName] -cne (Shape $m)){$changed+=$m.FullName}}}
 $missing=@($before.Keys|Where-Object {$_ -notin $after});$oldFields=@((Types $old.MainModule.Types)|ForEach-Object Fields|ForEach-Object {$_.FullName+':'+$_.Attributes});$newFields=@((Types $new.MainModule.Types)|ForEach-Object Fields|ForEach-Object {$_.FullName+':'+$_.Attributes})
 $oldRefs=@($old.MainModule.AssemblyReferences|Where-Object Name -ne KKAPI|ForEach-Object FullName);$newRefs=@($new.MainModule.AssemblyReferences|Where-Object Name -ne KKSAPI|ForEach-Object FullName)
 if($changed.Count -or $missing.Count -or ($oldFields -join '|') -cne ($newFields -join '|') -or ($oldRefs -join '|') -cne ($newRefs -join '|') -or (ResourceHashes $old) -cne (ResourceHashes $new)){throw 'Unexpected method/field/resource/reference scope.'}
 $sourceUnresolved=@();$candidateUnresolved=@();foreach($r in $old.MainModule.GetMemberReferences()){try{if(!$r.Resolve()){$sourceUnresolved+=$r.FullName}}catch{$sourceUnresolved+=$r.FullName}};foreach($r in $new.MainModule.GetMemberReferences()){try{if(!$r.Resolve()){$candidateUnresolved+=$r.FullName}}catch{$candidateUnresolved+=$r.FullName}}
 if($sourceUnresolved.Count -ne 14 -or $candidateUnresolved.Count){throw 'Unexpected original/candidate resolution.'}
 $specs=@(@{type='HSPE.PoseController';fields=@('_childObjects','_dynamicBonesEditor')},@{type='HSPE.CharaPoseController';fields=@('_boobsEditor')},@{type='HSPE.AMModules.CollidersEditor';fields=@('_loneColliders','_dirtyColliders','_addNewDynamicBonesAsDefault')},@{type='HSPE.AMModules.CollidersEditor/ColliderDataBase';fields=@('allDynamicBones')},@{type='HSPE.AMModules.DynamicBonesEditor';fields=@('_dynamicBones','_parent')},@{type='HSPE.AMModules.BoobsEditor';fields=@('_dynamicBones')})
 $fields=@();foreach($s in $specs){$t=(Types $pe.MainModule.Types)|Where-Object FullName -ceq $s.type;if(!$t){throw 'Missing KKPE type'};foreach($name in $s.fields){$f=InheritedField $t $name;if(!$f){throw "Missing KKPE field $name"};$fields+=$f.FullName}}
 $methods=@();$methods+=(RequiredMethod $pe.MainModule.GetType('HSPE.AMModules.CollidersEditor') 'SetIgnoreDynamicBone' @('DynamicBoneCollider','HSPE.PoseController','System.Object','System.Boolean')).FullName
 $methods+=(RequiredMethod $pe.MainModule.GetType('HSPE.AMModules.BoobsEditor') 'GetID' @('DynamicBone_Ver02')).FullName
 $refresh=RequiredMethod $pe.MainModule.GetType('HSPE.AMModules.DynamicBonesEditor') 'RefreshDynamicBoneList' @();$methods+=$refresh.FullName
 $edit=@();foreach($spec in @(@{name='ChangeChara';parameters=@('System.String')},@{name='LoadClothesFile';parameters=@('System.String')},@{name='SetCoordinateInfo';parameters=@('ChaFileDefine/CoordinateType','System.Boolean')})){$m=RequiredMethod $game.MainModule.GetType('Studio.OCIChar') $spec.name $spec.parameters;if($m.IsStatic -or $m.ReturnType.FullName -cne 'System.Void'){throw 'Unexpected edit method shape'};$edit+=$m.FullName}
 $plugin=$new.MainModule.GetType('J694BindingRestore.Plugin');$post=RequiredMethod $plugin 'AfterStudioEdit' @('Studio.OCIChar');$post2=RequiredMethod $plugin 'AfterKkpeRefresh' @('System.Object');if(!$post.IsStatic -or !$post2.IsStatic -or $refresh.IsStatic){throw 'Unexpected postfix/target binding'}
 $report=[ordered]@{passed=$true;originalMethods=$before.Count;unchangedMethods=$after.Count;changedMethods=$changed;removedMethods=$missing;fieldsUnchanged=$true;resourcesUnchanged=$true;onlyAssemblyReferenceChanged='KKAPI 1.43.0.0 -> KKSAPI 1.48.1.0';originalUnresolved=$sourceUnresolved;candidateUnresolved=$candidateUnresolved;candidateMemberRefs=@($new.MainModule.GetMemberReferences()).Count;kkpeFields=$fields;kkpeMethods=$methods;harmonyTargets=@($edit)+@($refresh.FullName);patchMethods=@($post.FullName,$post2.FullName);scope='Metadata only; does not prove live Harmony installation or actual character editing.'}
 $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $ReportPath -Encoding utf8
 Write-Output "PASS unchanged methods=$($after.Count); references original14 missing -> candidate0; KKPE fields=$($fields.Count); Harmony targets=4"
}finally{$old.Dispose();$new.Dispose();$pe.Dispose();$game.Dispose();$resolver.Dispose()}
