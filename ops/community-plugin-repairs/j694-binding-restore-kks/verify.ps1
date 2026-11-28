param([Parameter(Mandatory=$true)][string]$OriginalPath,[Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$ArtifactsPath)
$ErrorActionPreference='Stop'
$original=(Resolve-Path -LiteralPath $OriginalPath).Path;$game=(Resolve-Path -LiteralPath $GameRoot).Path;$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
. (Join-Path $PSScriptRoot 'artifact-path.ps1')
$repository=$PSScriptRoot;for($i=0;$i -lt 3;$i++){$repository=[IO.Directory]::GetParent($repository).FullName}
$artifacts=Assert-J694ArtifactPath $artifacts $repository $game
$originalHash='1187a494a9f9d467c07db5c41d014c72f7a0771a7b31f1eb9b2fe92de832a033';$candidateHash='2c10f57c6405e800a6a95d2fbf972f2e7b8891e14f49706dc3779ab04c6e6c20'
if((Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant() -ne $originalHash){throw 'Unreviewed original.'}
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must be new.'}
& (Join-Path $PSScriptRoot 'tests/artifact-path-tests.ps1')
$cecil=Join-Path $game 'BepInEx/core/Mono.Cecil.dll';if([Reflection.AssemblyName]::GetAssemblyName($cecil).Version.ToString() -ne '0.10.4.0'){throw 'Reviewed Cecil 0.10.4 required.'}
New-Item -ItemType Directory -Path $artifacts|Out-Null
& dotnet build (Join-Path $PSScriptRoot 'tool/Patcher.csproj') -c Release "-p:CecilPath=$cecil" --artifacts-path (Join-Path $artifacts 'tool-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Tool build failed'}
& dotnet build (Join-Path $PSScriptRoot 'tests/Probe.csproj') -c Release --artifacts-path (Join-Path $artifacts 'probe-build') --nologo
if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}
$tool=Join-Path $artifacts 'tool-build/bin/Patcher/release/Patcher.dll';$fixture=Join-Path $artifacts 'probe-build/bin/Probe/release/Probe.dll';$candidate=Join-Path $artifacts 'candidate/J694BindingRestore.dll';$api=Join-Path $game 'BepInEx/plugins/KKSAPI.dll'
& dotnet $tool patch $original $api $candidate
if($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $candidateHash){throw 'Candidate hash/generation failed'}
& (Join-Path $PSScriptRoot 'verify-contract.ps1') -OriginalPath $original -CandidatePath $candidate -GameRoot $game -ReportPath (Join-Path $artifacts 'contract.json')
$runs=@();foreach($mode in @('original','candidate')){
 $input=if($mode -eq 'original'){$original}else{$candidate};$probe=Join-Path $artifacts "$mode/Probe.dll"
 & dotnet $tool probe $input $fixture $probe;if($LASTEXITCODE -ne 0){throw 'CIL extraction failed'}
 Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($fixture,'runtimeconfig.json')) -Destination ([IO.Path]::ChangeExtension($probe,'runtimeconfig.json'))
 $log=@(& dotnet $probe 2>&1);$code=$LASTEXITCODE;$log|Set-Content -LiteralPath (Join-Path $artifacts "$mode.log") -Encoding utf8
 if($code -ne 0 -or $log[-1] -cne 'checks=45 failures=0'){throw "Actual-CIL $mode checks failed"};$runs+=@{mode=$mode;checks=45;failures=0}
}
$refusals=@();foreach($case in @('unknown-original','already-patched','same-path','existing-output','wrong-api','unknown-extraction')){
 $input=if($case -eq 'unknown-original'){$fixture}elseif($case -eq 'already-patched'){$candidate}else{$original}
 $output=if($case -eq 'same-path'){$original}elseif($case -eq 'existing-output'){$candidate}else{Join-Path $artifacts ($case+'.dll')}
 if($case -eq 'unknown-extraction'){& dotnet $tool probe $fixture $fixture $output *> (Join-Path $artifacts ($case+'.log'))}
 else{$apiInput=if($case -eq 'wrong-api'){$fixture}else{$api};& dotnet $tool patch $input $apiInput $output *> (Join-Path $artifacts ($case+'.log'))}
 if($LASTEXITCODE -eq 0){throw "Refusal missing: $case"};if($case -notin @('same-path','existing-output') -and (Test-Path -LiteralPath $output)){throw 'Refused operation wrote output'};$refusals+=@{case=$case;refused=$true}
}
if((Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant() -ne $originalHash -or (Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $candidateHash){throw 'Input/candidate changed'}
[ordered]@{passed=$true;originalSha256=$originalHash;candidateSha256=$candidateHash;runs=$runs;refusals=$refusals;artifactPathChecks=@{refused=10;allowed=2;filesystemMutation=$false};originalPreserved=$true;installed=$false;runtimeVerified=$false;scope='104 actual original/candidate CIL methods transplanted into synthetic game/Unity/KKPE collaborators; six startup/config/Harmony wiring methods explicitly excluded. Metadata audit checks actual four Harmony targets, not live installation.'}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $artifacts 'verification-summary.json') -Encoding utf8
Write-Output 'PASS original45/0 and candidate45/0; six refusals; only one AssemblyRef changed.'
$global:LASTEXITCODE=0
