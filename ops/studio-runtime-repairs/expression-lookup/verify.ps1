param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$UnityCorePath,
    [string]$V2InputPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath,
    [string]$FrameworkCompilerPath
)
$ErrorActionPreference='Stop'
$inputFull=(Resolve-Path -LiteralPath $InputPath).Path
$cecilFull=(Resolve-Path -LiteralPath $CecilPath).Path
$unityCoreFull=(Resolve-Path -LiteralPath $UnityCorePath).Path
$v2Full=if($V2InputPath){(Resolve-Path -LiteralPath $V2InputPath).Path}else{$null}
$artifactsFull=[IO.Path]::GetFullPath($ArtifactsPath)
$sourceFull=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(Test-Path -LiteralPath $artifactsFull){throw 'ArtifactsPath must not exist.'}
if($artifactsFull.StartsWith($sourceFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep artifacts outside the public source directory.'}
$originalHash='a3a8422471cfd71b44d4199caac932809b74d48c8abd809678c580d295e58893'
$candidateHash='55ec9023c84aaa775cb460d38e95d42348d652411b0431084c124eac8172f356'
if($v2Full -and (Get-FileHash -LiteralPath $v2Full).Hash.ToLowerInvariant() -ne '512b889ca694f973ceab440f49a88a74978fed988f8ae3e4f45012a4ba21f387'){throw 'Unsupported v2 control SHA-256.'}
if((Get-FileHash -LiteralPath $inputFull).Hash.ToLowerInvariant() -ne $originalHash){throw 'Unsupported original SHA-256.'}
if(!$FrameworkCompilerPath){$FrameworkCompilerPath=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'}
$compilerFull=(Resolve-Path -LiteralPath $FrameworkCompilerPath).Path
New-Item -ItemType Directory -Path $artifactsFull,"$artifactsFull/helper" | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/PoseEditorLookupRepair.cs" -Destination "$artifactsFull/helper/PoseEditorLookupRepair.cs"
Push-Location "$artifactsFull/helper"
try { & $compilerFull /nologo /target:library /optimize+ /langversion:5 "/reference:$unityCoreFull" /out:PoseEditorLookupRepair.dll PoseEditorLookupRepair.cs; if($LASTEXITCODE -ne 0){throw 'Framework helper compilation failed.'} }
finally { Pop-Location }
& dotnet build "$PSScriptRoot/tool/ExpressionLookupPatch.csproj" -c Release --nologo -v quiet "-p:CecilPath=$cecilFull" "-p:BaseIntermediateOutputPath=$artifactsFull/tool-obj/" -o "$artifactsFull/tool"
if($LASTEXITCODE -ne 0){throw 'Patcher build failed.'}
& dotnet build "$PSScriptRoot/tests/LookupProbe.csproj" -c Release --nologo -v quiet "-p:BaseIntermediateOutputPath=$artifactsFull/probe-obj/" -o "$artifactsFull/fixture"
if($LASTEXITCODE -ne 0){throw 'Probe build failed.'}
$tool="$artifactsFull/tool/ExpressionLookupPatch.dll"
$candidate="$artifactsFull/KKS_ExpressionStudio.dll"
& dotnet $tool $inputFull "$artifactsFull/helper/PoseEditorLookupRepair.dll" $candidate
if($LASTEXITCODE -ne 0){throw 'Patch generation failed.'}
if((Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $candidateHash){throw 'Candidate differs from reviewed output.'}
$modes=if($v2Full){@('control','candidate-v2','candidate-v3')}else{@('control','candidate-v3')}
foreach($mode in $modes){
    $source=if($mode -eq 'control'){$inputFull}elseif($mode -eq 'candidate-v2'){$v2Full}else{$candidate}
    New-Item -ItemType Directory -Path "$artifactsFull/$mode" | Out-Null
    & dotnet $tool --extract $source "$artifactsFull/fixture/LookupProbe.dll" "$artifactsFull/$mode/LookupProbe.dll"
    if($LASTEXITCODE -ne 0){throw "CIL extraction failed: $mode"}
    Copy-Item -LiteralPath "$artifactsFull/fixture/LookupProbe.runtimeconfig.json" -Destination "$artifactsFull/$mode/LookupProbe.runtimeconfig.json"
    & dotnet "$artifactsFull/$mode/LookupProbe.dll" $mode "$artifactsFull/$mode-results.json"
    if($LASTEXITCODE -ne 0){throw "CIL regression failed: $mode"}
}
$control=Get-Content -LiteralPath "$artifactsFull/control-results.json" -Raw | ConvertFrom-Json
$prior=if($v2Full){Get-Content -LiteralPath "$artifactsFull/candidate-v2-results.json" -Raw | ConvertFrom-Json}else{$null}
$repaired=Get-Content -LiteralPath "$artifactsFull/candidate-v3-results.json" -Raw | ConvertFrom-Json
if(!$control.passed -or !$repaired.passed -or $control.cases.Count -ne 25 -or $repaired.cases.Count -ne 25 -or ($prior -and (!$prior.passed -or $prior.cases.Count -ne 25))){throw 'Unexpected case results.'}
if((Get-FileHash -LiteralPath $inputFull).Hash.ToLowerInvariant() -ne $originalHash){throw 'Original changed.'}
$receipt=Get-Content -LiteralPath "$candidate.receipt.json" -Raw | ConvertFrom-Json
$summary=[ordered]@{patch=$receipt;control=$control;priorCandidate=$prior;candidate=$repaired;totalChecks=25*$modes.Count;originalPreserved=$true;scope='25 cases per supplied original/candidate build, using extracted primary-prefix, complete EditorsFor control flow, entire ScanUnity CIL and imported helper. Unity/fallback callees synthetic; no real game/Mono/editor operation acceptance.'}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$artifactsFull/verification-summary.json" -Encoding UTF8
Write-Output "PASS: frozen v3 candidate hash, $($summary.totalChecks) checks across $($modes.Count) builds, original preserved."
