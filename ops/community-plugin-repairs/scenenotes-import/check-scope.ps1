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
    $lines=[Collections.Generic.List[string]]::new()
    $lines.Add("$($m.Body.InitLocals):$((@($m.Body.Variables|ForEach-Object {$_.VariableType.FullName}) -join '|'))")
    foreach($i in $m.Body.Instructions){
        $arg=$i.Operand
        if($arg -is [Mono.Cecil.Cil.Instruction]){$arg='#'+$m.Body.Instructions.IndexOf($arg)}
        elseif($arg -is [Mono.Cecil.Cil.Instruction[]]){$arg=(@($arg|ForEach-Object {$m.Body.Instructions.IndexOf($_)}) -join ',')}
        elseif($arg -is [Mono.Cecil.MemberReference]){$arg=$arg.FullName}
        $lines.Add("$($i.OpCode.Code.ToString() -replace '_S$','') $arg")
    }
    foreach($h in $m.Body.ExceptionHandlers){$lines.Add("EH:$($h.HandlerType):$($h.CatchType.FullName):$($m.Body.Instructions.IndexOf($h.TryStart)):$($m.Body.Instructions.IndexOf($h.TryEnd)):$($m.Body.Instructions.IndexOf($h.HandlerStart)):$($m.Body.Instructions.IndexOf($h.HandlerEnd))")}
    $lines -join "`n"
}
try{
    $before=@{};foreach($t in (Types $old.MainModule.Types)){foreach($m in $t.Methods){$before[$m.FullName]=$m}}
    $changed=@();$added=@();$seen=[Collections.Generic.HashSet[string]]::new()
    foreach($t in (Types $new.MainModule.Types)){foreach($m in $t.Methods){if(!$before.ContainsKey($m.FullName)){$added+=$m.FullName}else{[void]$seen.Add($m.FullName);if((Shape $m) -cne (Shape $before[$m.FullName])){$changed+=$m.FullName}}}}
    $oldFields=@((Types $old.MainModule.Types)|ForEach-Object Fields|ForEach-Object FullName)
    $newFields=@((Types $new.MainModule.Types)|ForEach-Object Fields|ForEach-Object FullName)
    $oldRefs=@($old.MainModule.AssemblyReferences|ForEach-Object FullName);$newRefs=@($new.MainModule.AssemblyReferences|ForEach-Object FullName)
    $oldController=$old.MainModule.GetType('SceneNotes.SceneNotesSaveController');$newController=$new.MainModule.GetType('SceneNotes.SceneNotesSaveController')
    $helper=$newController.Methods|Where-Object Name -eq ApplyNotesForOperation
    $report=[ordered]@{originalMethods=$before.Count;changedMethods=$changed;addedMethods=$added;removedMethods=@($before.Keys|Where-Object {!$seen.Contains($_)});unchangedOriginalMethods=$before.Count-$changed.Count;saveMethodUnchanged=((Shape ($oldController.Methods|Where-Object Name -eq OnSceneSave)) -ceq (Shape ($newController.Methods|Where-Object Name -eq OnSceneSave)));fieldsUnchanged=(($oldFields -join '|') -ceq ($newFields -join '|'));assemblyReferencesUnchanged=(($oldRefs -join '|') -ceq ($newRefs -join '|'));candidateSha256=(Get-FileHash -LiteralPath $CandidatePath).Hash.ToLowerInvariant()}
    $expected='System.Void SceneNotes.SceneNotesSaveController::OnSceneLoad(KKAPI.Studio.SaveLoad.SceneOperationKind,KKAPI.Utilities.ReadOnlyDictionary`2<System.Int32,Studio.ObjectCtrlInfo>)'
    if($changed.Count -ne 1 -or $changed[0] -cne $expected -or $added.Count -ne 1 -or !$helper.IsPrivate -or !$helper.IsStatic -or $report.removedMethods.Count -ne 0 -or !$report.saveMethodUnchanged -or !$report.fieldsUnchanged -or !$report.assemblyReferencesUnchanged){throw 'Unexpected candidate scope.'}
    $report|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $ReportPath -Encoding utf8
    $report|ConvertTo-Json -Depth 6
}finally{$old.Dispose();$new.Dispose()}
