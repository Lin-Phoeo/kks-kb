param(
    [Parameter(Mandatory=$true)][string]$DressUpPath,
    [Parameter(Mandatory=$true)][string]$MakeUpPath,
    [Parameter(Mandatory=$true)][string]$PseudoMakerPath,
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath,
    [string]$FrameworkCompilerPath
)
$ErrorActionPreference='Stop'
$dress=(Resolve-Path -LiteralPath $DressUpPath).Path
$makeup=(Resolve-Path -LiteralPath $MakeUpPath).Path
$pseudo=(Resolve-Path -LiteralPath $PseudoMakerPath).Path
$game=(Resolve-Path -LiteralPath $GameRoot).Path
$cecil=(Resolve-Path -LiteralPath $CecilPath).Path
$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must not already exist.'}
if($artifacts.StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep local artifacts outside public source.'}
$expected=@{
    Dress='69b355bf1018ab22e55e4ddfd146421b97c66542ad89ed9037c94f8eaa0ee19b'
    Makeup='0c05fc9066d39c039358bd79283510efff400ae8ac5b3519398ba34504347d6a'
    Pseudo='72e1c3a3dcb2120225f2d2823729cea81b2a2dffc8243de63ac6a4c901061882'
    DressPatched='2dc5cb77fff929814e912d42b5a38de85ad9e5cfd99ebd1a1103967b9ef1e24f'
    MakeupPatched='6f4271ccb0d92b6745f1cdce91c415d87da224d5a81e758395a43d1538fdc222'
}
if((Get-FileHash -LiteralPath $dress).Hash.ToLowerInvariant() -ne $expected.Dress -or (Get-FileHash -LiteralPath $makeup).Hash.ToLowerInvariant() -ne $expected.Makeup -or (Get-FileHash -LiteralPath $pseudo).Hash.ToLowerInvariant() -ne $expected.Pseudo){throw 'Unsupported input identity.'}
New-Item -ItemType Directory -Path $artifacts,(Join-Path $artifacts 'helper')|Out-Null
if(!$FrameworkCompilerPath){$FrameworkCompilerPath=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'}
$helper=Join-Path $artifacts 'helper/PseudoMakerIdentity.dll'
& $FrameworkCompilerPath /nologo /target:library /optimize+ /langversion:5 "/out:$helper" (Join-Path $PSScriptRoot 'PseudoMakerIdentity.cs')
if($LASTEXITCODE -ne 0){throw 'Helper build failed.'}
& dotnet build (Join-Path $PSScriptRoot 'tool/Patcher.csproj') -c Release "-p:CecilPath=$cecil" --artifacts-path (Join-Path $artifacts 'tool-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Patcher build failed.'}
& dotnet build (Join-Path $PSScriptRoot 'tests/Probe.csproj') -c Release --artifacts-path (Join-Path $artifacts 'probe-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Fixture build failed.'}
$tool=Join-Path $artifacts 'tool-build/bin/Patcher/release/Patcher.dll'
$fixture=Join-Path $artifacts 'probe-build/bin/Probe/release/KKS_StudioPseudoMaker.dll'
$items=@(@{label='dressup';input=$dress;file='KKS_F604DressUp.dll';sha=$expected.DressPatched},@{label='makeup';input=$makeup;file='KKS_F604MakeUp.dll';sha=$expected.MakeupPatched})
$reports=@();$receipts=@()
foreach($item in $items){
    $candidate=Join-Path $artifacts $item.file
    & dotnet $tool $item.input $pseudo $helper $candidate $game
    if($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $item.sha){throw 'Candidate generation or identity failed.'}
    $receipts+=Get-Content -LiteralPath ($candidate+'.receipt.json') -Raw|ConvertFrom-Json
    foreach($mode in @('control','candidate')){
        $input=if($mode -eq 'control'){$item.input}else{$candidate}
        $probe=Join-Path $artifacts ($item.label+'-'+$mode+'/KKS_StudioPseudoMaker.dll')
        & dotnet $tool --extract $input $pseudo $fixture $probe
        if($LASTEXITCODE -ne 0){throw 'Actual IL extraction failed.'}
        Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($fixture,'runtimeconfig.json')) -Destination ([IO.Path]::ChangeExtension($probe,'runtimeconfig.json'))
        foreach($scenario in @('current','legacy','unknown','wrong-guid')){
            $report=Join-Path $artifacts ($item.label+'-'+$mode+'-'+$scenario+'.json')
            & dotnet $probe $mode $scenario $report $item.label
            if($LASTEXITCODE -ne 0){throw 'Actual IL regression failed.'}
            $reports+=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
        }
    }
}
$refusals=@()
foreach($case in @('unknown-vanity','unknown-pseudomaker','existing-output','input-output-same')){
    $input=if($case -eq 'unknown-vanity'){$helper}else{$dress}
    $targetPseudo=if($case -eq 'unknown-pseudomaker'){$helper}else{$pseudo}
    $output=if($case -eq 'existing-output'){Join-Path $artifacts 'KKS_F604DressUp.dll'}elseif($case -eq 'input-output-same'){$dress}else{Join-Path $artifacts ($case+'.dll')}
    & dotnet $tool $input $targetPseudo $helper $output $game *> (Join-Path $artifacts ($case+'.log'))
    if($LASTEXITCODE -eq 0){throw "Expected refusal missing: $case"}
    if($case.StartsWith('unknown') -and (Test-Path -LiteralPath $output)){throw 'Refusal created output.'}
    $refusals+=[pscustomobject]@{case=$case;refused=$true}
}
if((Get-FileHash -LiteralPath $dress).Hash.ToLowerInvariant() -ne $expected.Dress -or (Get-FileHash -LiteralPath $makeup).Hash.ToLowerInvariant() -ne $expected.Makeup -or (Get-FileHash -LiteralPath $pseudo).Hash.ToLowerInvariant() -ne $expected.Pseudo){throw 'Source file changed.'}
Add-Type -Path $cecil
$metadata=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dress)
try{$cleanupAuthorityAbsent=$null -eq $metadata.MainModule.GetType('Freelancer604.Wardrobe.WardrobeCleanup')}finally{$metadata.Dispose()}
if(!$cleanupAuthorityAbsent){throw 'Unexpected cleanup authority in reviewed Free host.'}
$summary=[ordered]@{passed=$true;receipts=$receipts;scenarioRuns=$reports.Count;checks=($reports|Measure-Object -Property checks -Sum).Sum;results=$reports;refusals=$refusals;reviewedFreeHostCleanupAuthorityAbsent=$cleanupAuthorityAbsent;inputsUnchanged=$true;installed=$false;scope='Actual Vanity gate/guard and installed PseudoMaker UI/trim CIL using synthetic discovery/hash/native UI/reservation storage. No real Unity, saved content or cleanup moves.'}
[IO.File]::WriteAllText((Join-Path $artifacts 'verification-summary.json'),($summary|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Output 'PASS: two exact candidates, 178 actual-IL checks, four refusals, absent cleanup authority and preserved inputs.'
$global:LASTEXITCODE=0
