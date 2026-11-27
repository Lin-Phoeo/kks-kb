param(
    [Parameter(Mandatory=$true)][string]$OriginalPath,
    [Parameter(Mandatory=$true)][string]$CandidatePath,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$ReportPath
)
$ErrorActionPreference='Stop'

[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $CecilPath).Path)
$old=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($OriginalPath)
$new=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($CandidatePath)
function Types($items){foreach($t in $items){$t;Types $t.NestedTypes}}
function Shape($m){
 if(!$m.HasBody){return 'none'}
 $lines=[Collections.Generic.List[string]]::new();$lines.Add("$($m.Body.InitLocals):$((@($m.Body.Variables|ForEach-Object {$_.VariableType.FullName}) -join '|'))")
 foreach($i in $m.Body.Instructions){$arg=$i.Operand;if($arg -is [Mono.Cecil.Cil.Instruction]){$arg='#'+$m.Body.Instructions.IndexOf($arg)}elseif($arg -is [Mono.Cecil.Cil.Instruction[]]){$arg=(@($arg|ForEach-Object {$m.Body.Instructions.IndexOf($_)}) -join ',')}elseif($arg -is [Mono.Cecil.MemberReference]){$arg=$arg.FullName};$lines.Add("$($i.OpCode.Code.ToString() -replace '_S$','') $arg")}
 foreach($h in $m.Body.ExceptionHandlers){$lines.Add("EH:$($h.HandlerType):$($h.CatchType.FullName):$($m.Body.Instructions.IndexOf($h.TryStart)):$($m.Body.Instructions.IndexOf($h.TryEnd)):$($m.Body.Instructions.IndexOf($h.HandlerStart)):$($m.Body.Instructions.IndexOf($h.HandlerEnd))")};$lines -join "`n"
}
$before=@{};$beforeFields=@{};foreach($t in (Types $old.MainModule.Types)){foreach($m in $t.Methods){$before[$m.FullName]=$m};foreach($f in $t.Fields){$beforeFields[$f.FullName]=$f}}
$changed=@();$added=@();$signatureChanged=@();$fields=@();$seen=[Collections.Generic.HashSet[string]]::new()
foreach($t in (Types $new.MainModule.Types)){
 foreach($m in $t.Methods){$key=$m.FullName;if($m.Name -eq 'RestoreWombState'){$previous=$old.MainModule.GetType($t.FullName).Methods|Where-Object Name -eq RestoreWombState;$key=$previous.FullName;if($key -ne $m.FullName){$signatureChanged+=@{before=$key;after=$m.FullName}}};if(!$before.ContainsKey($key)){$added+=$m.FullName}else{[void]$seen.Add($key);if((Shape $m) -cne (Shape $before[$key])){$changed+=$m.FullName}}}
 foreach($f in $t.Fields){if(!$beforeFields.ContainsKey($f.FullName)){$fields+=@{field=$f.FullName;static=$f.IsStatic}}}
}
$refsOld=@($old.MainModule.AssemblyReferences|ForEach-Object FullName);$refsNew=@($new.MainModule.AssemblyReferences|ForEach-Object FullName)
$r=@{originalMethods=$before.Count;changedMethods=$changed;unchangedOriginalMethods=$before.Count-$changed.Count;removedOriginalMethods=@($before.Keys|Where-Object {!$seen.Contains($_)});addedMethods=$added;signatureChanges=$signatureChanged;addedFields=$fields;referencesUnchanged=(($refsOld -join '|') -ceq ($refsNew -join '|'));saveMethodUnchanged=((Shape ($old.MainModule.GetType('LiquidWobbleMPB.WobbleSceneController').Methods|Where-Object Name -eq OnSceneSave)) -ceq (Shape ($new.MainModule.GetType('LiquidWobbleMPB.WobbleSceneController').Methods|Where-Object Name -eq OnSceneSave)));candidateSha256=(Get-FileHash $CandidatePath).Hash.ToLowerInvariant()}
$r|ConvertTo-Json -Depth 6|Set-Content $ReportPath -Encoding UTF8
$r|ConvertTo-Json -Depth 6
$expectedChanged=@(
 'System.Collections.IEnumerator LiquidWobbleMPB.WobbleSceneController::RestoreWombState(System.Int32[],System.Int32[],System.Collections.Generic.Dictionary`2<System.Int32,Studio.ObjectCtrlInfo>)',
 'System.Void LiquidWobbleMPB.WobbleSceneController::OnSceneLoad(KKAPI.Studio.SaveLoad.SceneOperationKind,KKAPI.Utilities.ReadOnlyDictionary`2<System.Int32,Studio.ObjectCtrlInfo>)',
 'System.Boolean LiquidWobbleMPB.WobbleSceneController/<RestoreWombState>d__10::MoveNext()'
)
$helper=$new.MainModule.GetType('LiquidWobbleMPB.WobbleSceneController').Methods|Where-Object Name -eq CaptureImportObjects
if($before.Count -ne 779 -or $changed.Count -ne 3 -or @(Compare-Object $expectedChanged $changed).Count -ne 0 -or $added.Count -ne 1 -or !$helper.IsPrivate -or !$helper.IsStatic -or $fields.Count -ne 1 -or $fields[0].static -or $fields[0].field -notlike '*<RestoreWombState>d__10::importObjects' -or !$r.referencesUnchanged -or !$r.saveMethodUnchanged -or $r.removedOriginalMethods.Count -ne 0){throw 'Unexpected scope diff'}
$old.Dispose();$new.Dispose()
