param(
    [Parameter(Mandatory=$true)][string]$SceneNotesPath,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference='Stop'
$original=(Resolve-Path -LiteralPath $SceneNotesPath).Path
$cecil=(Resolve-Path -LiteralPath $CecilPath).Path
$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
$source=[IO.Path]::GetFullPath($PSScriptRoot)
$expectedOriginal='23ead0fa3d7bc2ddeaa98163929b280f96f417f9d2aacde98cb0c09d0a44da8d'
$expectedCandidate='536181e0a3c8ca34d62f12f493bad5282075c065b4580b929b746676ba52d6fa'
if((Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant() -ne $expectedOriginal){throw 'Unsupported SceneNotes input.'}
if([Reflection.AssemblyName]::GetAssemblyName($cecil).Version.ToString() -ne '0.10.4.0'){throw 'Reviewed writer requires Mono.Cecil 0.10.4.0.'}
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must be new.'}
if($artifacts -eq $source -or $artifacts.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build outside the source directory.'}
New-Item -ItemType Directory -Path $artifacts|Out-Null
& dotnet build (Join-Path $source 'tool/Patcher.csproj') -c Release "-p:CecilPath=$cecil" --artifacts-path (Join-Path $artifacts 'tool-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Patcher build failed.'}
& dotnet build (Join-Path $source 'tests/Probe.csproj') -c Release --artifacts-path (Join-Path $artifacts 'probe-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Fixture build failed.'}
$tool=Join-Path $artifacts 'tool-build/bin/Patcher/release/Patcher.dll'
$fixture=Join-Path $artifacts 'probe-build/bin/Probe/release/Probe.dll'
$candidate=Join-Path $artifacts 'ziglo_SceneNotes_KKS.dll'
& dotnet $tool patch $original $candidate
if($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $expectedCandidate){throw 'Candidate generation/hash failed.'}
& (Join-Path $source 'check-scope.ps1') -OriginalPath $original -CandidatePath $candidate -CecilPath $cecil -ReportPath (Join-Path $artifacts 'scope.json')
$runs=@()
foreach($mode in @('control','candidate')){
    $input=if($mode -eq 'control'){$original}else{$candidate}
    $probe=Join-Path $artifacts "$mode/Probe.dll"
    & dotnet $tool probe $input $fixture $probe
    if($LASTEXITCODE -ne 0){throw "CIL extraction failed: $mode"}
    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($fixture,'runtimeconfig.json')) -Destination ([IO.Path]::ChangeExtension($probe,'runtimeconfig.json'))
    $log=@(& dotnet $probe 2>&1);$exitCode=$LASTEXITCODE
    $failures=if($mode -eq 'control'){27}else{0};$expectedExit=if($mode -eq 'control'){1}else{0}
    $log|Set-Content -LiteralPath (Join-Path $artifacts "$mode.log") -Encoding utf8
    if($exitCode -ne $expectedExit -or $log[-1] -cne "checks=50 failures=$failures" -or @($log|Where-Object {$_ -like 'FAIL *'}).Count -ne $failures){throw "Unexpected red/green result: $mode"}
    $runs+=@{mode=$mode;checks=50;failures=$failures;exitCode=$exitCode}
}
$refusals=@()
foreach($case in @('unknown-input','same-path','existing-output','unknown-extraction')){
    $input=if($case -eq 'unknown-input'){$fixture}else{$original}
    $output=if($case -eq 'same-path'){$original}elseif($case -eq 'existing-output'){$candidate}else{Join-Path $artifacts "$case.dll"}
    if($case -eq 'unknown-extraction'){& dotnet $tool probe $fixture $fixture $output *> (Join-Path $artifacts "$case.log")}
    else{& dotnet $tool patch $input $output *> (Join-Path $artifacts "$case.log")}
    if($LASTEXITCODE -eq 0){throw "Expected refusal missing: $case"}
    if($case.StartsWith('unknown') -and (Test-Path -LiteralPath $output)){throw 'Refusal wrote an output.'}
    $refusals+=@{case=$case;refused=$true}
}
if((Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant() -ne $expectedOriginal -or (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $expectedCandidate){throw 'Input or candidate changed during verification.'}
[ordered]@{passed=$true;originalSha256=$expectedOriginal;candidateSha256=$expectedCandidate;runs=$runs;refusals=$refusals;inputsPreserved=$true;installed=$false;scope='Actual OnSceneLoad, OnSceneSave and candidate helper CIL against synthetic save-controller/SetNotes boundaries. No real Unity UI or original user scene execution.'}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $artifacts 'verification-summary.json') -Encoding utf8
Write-Output 'PASS: official 50 checks / 27 expected failures; candidate 50 / 0; four refusal cases; save format unchanged.'
$global:LASTEXITCODE=0
