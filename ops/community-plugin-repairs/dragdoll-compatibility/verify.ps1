param(
    [Parameter(Mandatory=$true)][string]$DragDollPath,
    [Parameter(Mandatory=$true)][string]$BaseAxisPath,
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath,
    [string]$FrameworkCompilerPath
)
$ErrorActionPreference='Stop'
$drag=(Resolve-Path -LiteralPath $DragDollPath).Path
$axis=(Resolve-Path -LiteralPath $BaseAxisPath).Path
$game=(Resolve-Path -LiteralPath $GameRoot).Path
$cecil=(Resolve-Path -LiteralPath $CecilPath).Path
$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
$source=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must be new.'}
if($artifacts.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep build outputs outside the public source tree.'}
$expectedDrag='5938a51b4db8ede31e459bfe8bc8e0735bcf10957727a230af39ecf69a1e9f1b'
$expectedAxis='c75421f9f12e23b797883469b313c794568db8c2242c44e30a1541360170cc74'
$expectedCandidate='72a6c00dfe75a84d046fd7bcbfde50798b85e1f7e6b147666415666587bf8a3a'
if((Get-FileHash -LiteralPath $drag).Hash.ToLowerInvariant() -ne $expectedDrag -or (Get-FileHash -LiteralPath $axis).Hash.ToLowerInvariant() -ne $expectedAxis){throw 'Unsupported DragDoll or base Axis identity.'}
if(!$FrameworkCompilerPath){$FrameworkCompilerPath=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'}
New-Item -ItemType Directory -Path $artifacts,(Join-Path $artifacts 'helper')|Out-Null
$helper=Join-Path $artifacts 'helper/AxisBuildIdentity.dll'
& $FrameworkCompilerPath /nologo /target:library /optimize+ /langversion:5 "/out:$helper" (Join-Path $PSScriptRoot 'AxisBuildIdentity.cs')
if($LASTEXITCODE -ne 0){throw 'Identity helper compilation failed.'}
& dotnet build (Join-Path $PSScriptRoot 'tool/Patcher.csproj') -c Release "-p:CecilPath=$cecil" --artifacts-path (Join-Path $artifacts 'tool-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Patcher build failed.'}
& dotnet build (Join-Path $PSScriptRoot 'tests/Probe.csproj') -c Release --artifacts-path (Join-Path $artifacts 'probe-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Fixture build failed.'}
$tool=Join-Path $artifacts 'tool-build/bin/Patcher/release/Patcher.dll'
$fixture=Join-Path $artifacts 'probe-build/bin/Probe/release/Probe.dll'
$candidate=Join-Path $artifacts 'KKS_DragDoll.dll'
& dotnet $tool $drag $axis $helper $candidate $game
if($LASTEXITCODE -ne 0){throw 'Patch generation failed.'}
if((Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $expectedCandidate){throw 'Output identity mismatch.'}
$reports=@()
foreach($mode in @('control','candidate')){
    $input=if($mode -eq 'control'){$drag}else{$candidate}
    $probe=Join-Path $artifacts "$mode/Probe.dll"
    & dotnet $tool --extract $input $fixture $probe
    if($LASTEXITCODE -ne 0){throw "Actual CIL extraction failed: $mode"}
    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($fixture,'runtimeconfig.json')) -Destination ([IO.Path]::ChangeExtension($probe,'runtimeconfig.json'))
    foreach($scenario in @('base','plus','unknown','unknown-field','absent')){
        $report=Join-Path $artifacts "$mode-$scenario.json"
        & dotnet $probe $mode $scenario $report
        if($LASTEXITCODE -ne 0){throw "CIL verification failed: $mode $scenario"}
        $reports+=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
    }
}
$rejects=@()
foreach($case in @('unknown-drag','unknown-axis','existing-output','input-output-same')){
    $inputDrag=if($case -eq 'unknown-drag'){$helper}else{$drag}
    $inputAxis=if($case -eq 'unknown-axis'){$helper}else{$axis}
    $output=if($case -eq 'existing-output'){$candidate}elseif($case -eq 'input-output-same'){$drag}else{Join-Path $artifacts "$case.dll"}
    & dotnet $tool $inputDrag $inputAxis $helper $output $game *> (Join-Path $artifacts "$case.log")
    if($LASTEXITCODE -eq 0){throw "Expected refusal did not occur: $case"}
    if($case.StartsWith('unknown') -and (Test-Path -LiteralPath $output)){throw "Refusal wrote output: $case"}
    $rejects+=[pscustomobject]@{case=$case;refused=$true}
}
if((Get-FileHash -LiteralPath $drag).Hash.ToLowerInvariant() -ne $expectedDrag -or (Get-FileHash -LiteralPath $axis).Hash.ToLowerInvariant() -ne $expectedAxis -or (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $expectedCandidate){throw 'Input or candidate was changed during checks.'}
$receipt=Get-Content -Raw -LiteralPath ($candidate+'.receipt.json')|ConvertFrom-Json
$summary=[ordered]@{passed=$true;patch=$receipt;scenarioRuns=$reports.Count;checks=($reports|Measure-Object -Property checks -Sum).Sum;scenarios=$reports;rejectionChecks=$rejects;inputsPreserved=$true;installed=$false;scope='Actual Tick CIL and imported identity helper with substituted discovery/hash inputs and synthetic Harmony registrations. No Unity/native interaction or complete secondary installer execution.'}
[IO.File]::WriteAllText((Join-Path $artifacts 'verification-summary.json'),($summary|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Output 'PASS: exact candidate, 34 actual-CIL checks, four refusal cases, inputs unchanged.'
$global:LASTEXITCODE=0
