param(
    [Parameter(Mandatory=$true)][string]$CloXrayPath,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference='Stop'
$original=(Resolve-Path -LiteralPath $CloXrayPath).Path
$cecil=(Resolve-Path -LiteralPath $CecilPath).Path
$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
$source=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must be a new directory.'}
if($artifacts -eq $source -or $artifacts.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build artifacts must stay outside this source directory.'}
$expectedOriginal='69c953c7ed5a421db28081f6db0b7029fa60c12aaf48433ecfd07da3cf554c98'
$expectedCandidate='74612e01c4d2278aca4b4d993dc4d944c223496afca63b87f6a23c1c1f68014d'
if((Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant() -ne $expectedOriginal){throw 'Unsupported official CloXray input identity.'}
if([Reflection.AssemblyName]::GetAssemblyName($cecil).Version.ToString() -ne '0.10.4.0'){throw 'The reviewed deterministic writer uses Mono.Cecil 0.10.4.0.'}
New-Item -ItemType Directory -Path $artifacts | Out-Null
& dotnet build (Join-Path $source 'tool/Patcher.csproj') -c Release "-p:CecilPath=$cecil" --artifacts-path (Join-Path $artifacts 'tool-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Patcher build failed.'}
& dotnet build (Join-Path $source 'tests/Probe.csproj') -c Release --artifacts-path (Join-Path $artifacts 'probe-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Synthetic fixture build failed.'}
$tool=Join-Path $artifacts 'tool-build/bin/Patcher/release/Patcher.dll'
$fixture=Join-Path $artifacts 'probe-build/bin/Probe/release/Probe.dll'
$candidate=Join-Path $artifacts 'LiquidWobbleMPB.dll'
& dotnet $tool patch $original $candidate
if($LASTEXITCODE -ne 0){throw 'Candidate generation failed.'}
if((Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $expectedCandidate){throw 'Candidate hash differs from the reviewed build.'}
& (Join-Path $source 'check-scope.ps1') -OriginalPath $original -CandidatePath $candidate -CecilPath $cecil -ReportPath (Join-Path $artifacts 'metadata-diff.json')
$reports=@()
foreach($mode in @('control','candidate')){
    $input=if($mode -eq 'control'){$original}else{$candidate}
    $probe=Join-Path $artifacts "$mode/Probe.dll"
    & dotnet $tool probe $input $fixture $probe
    if($LASTEXITCODE -ne 0){throw "Actual CIL extraction failed: $mode"}
    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($fixture,'runtimeconfig.json')) -Destination ([IO.Path]::ChangeExtension($probe,'runtimeconfig.json'))
    $lines=@(& dotnet $probe 2>&1)
    $code=$LASTEXITCODE
    $lines|Set-Content -LiteralPath (Join-Path $artifacts "$mode-actual-cil.log") -Encoding utf8
    $expectedFailures=if($mode -eq 'control'){13}else{0}
    $expectedExit=if($mode -eq 'control'){1}else{0}
    if($code -ne $expectedExit -or $lines[-1] -cne "checks=25 failures=$expectedFailures" -or @($lines|Where-Object {$_ -like 'FAIL *'}).Count -ne $expectedFailures){throw "Unexpected actual CIL outcome: $mode exit=$code"}
    $reports+=[pscustomobject]@{mode=$mode;checks=25;failures=$expectedFailures;exitCode=$code}
}
$rejects=@()
foreach($case in @('unknown-input','same-path','existing-output')){
    $input=if($case -eq 'unknown-input'){$fixture}else{$original}
    $output=if($case -eq 'same-path'){$original}elseif($case -eq 'existing-output'){$candidate}else{Join-Path $artifacts 'refused.dll'}
    & dotnet $tool patch $input $output *> (Join-Path $artifacts "$case.log")
    if($LASTEXITCODE -eq 0){throw "Expected refusal did not occur: $case"}
    if($case -eq 'unknown-input' -and (Test-Path -LiteralPath $output)){throw 'Refusal unexpectedly wrote a candidate.'}
    $rejects+=[pscustomobject]@{case=$case;refused=$true}
}
if((Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant() -ne $expectedOriginal -or (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $expectedCandidate){throw 'Reviewed input/output changed during verification.'}
$summary=[ordered]@{
    passed=$true;originalSha256=$expectedOriginal;candidateSha256=$expectedCandidate
    scenarios=$reports;refusalChecks=$rejects;inputsPreserved=$true;installed=$false
    scope='Actual OnSceneLoad, ToIntArray, RestoreWombState factory and full iterator CIL in synthetic Unity/KKAPI environment. Runtime shader, native Harmony, original scene round-trip and game behavior are not asserted.'
}
$summary|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $artifacts 'verification-summary.json') -Encoding utf8
Write-Output 'PASS: exact reviewed candidate; official control 25 checks / 13 expected failures; candidate 25 / 0; 3 refusal cases; input preserved.'
$global:LASTEXITCODE=0
