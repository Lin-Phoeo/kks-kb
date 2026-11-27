param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$UnityCorePath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath,
    [string]$FrameworkCompilerPath
)
$ErrorActionPreference='Stop'
$inputFull=(Resolve-Path -LiteralPath $InputPath).Path
$cecilFull=(Resolve-Path -LiteralPath $CecilPath).Path
$unityFull=(Resolve-Path -LiteralPath $UnityCorePath).Path
$artifactsFull=[IO.Path]::GetFullPath($ArtifactsPath)
$sourceFull=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(Test-Path -LiteralPath $artifactsFull){throw 'ArtifactsPath must be new.'}
if($artifactsFull.StartsWith($sourceFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build outside the source directory.'}
$originalHash='be9a83e1619a7bf332f7c0a54c3f88a5b920135b3c0096ebd2326c3d33238736'
$candidateHash='530dc95e00cfb34486687a3de85cb995d4f6ae1cacc7169d58c07b97dacd15d7'
if((Get-FileHash -LiteralPath $inputFull -Algorithm SHA256).Hash.ToLowerInvariant() -ne $originalHash){throw 'Unknown original DLL hash.'}
if(!$FrameworkCompilerPath){$FrameworkCompilerPath=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'}
$compilerFull=(Resolve-Path -LiteralPath $FrameworkCompilerPath).Path
New-Item -ItemType Directory -Path "$artifactsFull/helper" -Force|Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/ExportSafety.cs" -Destination "$artifactsFull/helper/ExportSafety.cs"
Push-Location "$artifactsFull/helper"
try {
    & $compilerFull /nologo /target:library /optimize+ /langversion:5 "/reference:$unityFull" /out:ExportSafety.dll ExportSafety.cs
    if($LASTEXITCODE -ne 0){throw 'Helper compilation failed.'}
} finally { Pop-Location }
& dotnet build "$PSScriptRoot/tool/GreyExportPatch.csproj" -c Release --nologo "-p:CecilPath=$cecilFull" --artifacts-path "$artifactsFull/tool-build"
if($LASTEXITCODE -ne 0){throw 'Patcher compilation failed.'}
& dotnet build "$PSScriptRoot/tests/ExportProbe.csproj" -c Release --nologo --artifacts-path "$artifactsFull/probe-build"
if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed.'}
$tool="$artifactsFull/tool-build/bin/GreyExportPatch/release/GreyExportPatch.dll"
$fixture="$artifactsFull/probe-build/bin/ExportProbe/release/ExportProbe.dll"
$candidate="$artifactsFull/KKSMeshExporter.dll"
& dotnet $tool $inputFull "$artifactsFull/helper/ExportSafety.dll" $candidate
if($LASTEXITCODE -ne 0){throw 'Candidate generation failed.'}
if((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant() -ne $candidateHash){throw 'Candidate hash differs.'}
foreach($mode in @('control','candidate')) {
    $inputAssembly=if($mode -eq 'control'){$inputFull}else{$candidate}
    $folder="$artifactsFull/$mode"
    & dotnet $tool --extract $inputAssembly $fixture "$folder/ExportProbe.dll"
    if($LASTEXITCODE -ne 0){throw "CIL extraction failed: $mode"}
    Copy-Item -LiteralPath "$artifactsFull/probe-build/bin/ExportProbe/release/ExportProbe.runtimeconfig.json" -Destination "$folder/ExportProbe.runtimeconfig.json"
    & dotnet "$folder/ExportProbe.dll" $mode "$artifactsFull/$mode-results.json"
    if($LASTEXITCODE -ne 0){throw "CIL regression failed: $mode"}
}
$unknown="$artifactsFull/unknown-input.bin"
[IO.File]::WriteAllBytes($unknown,[Text.Encoding]::UTF8.GetBytes('synthetic unsupported input'))
$guards=@()
foreach($item in @(
    @{name='unknown-input';input=$unknown;output="$artifactsFull/unknown-output.dll"},
    @{name='already-patched';input=$candidate;output="$artifactsFull/patched-output.dll"},
    @{name='existing-output';input=$inputFull;output=$candidate}
)) {
    $before=if(Test-Path -LiteralPath $item.output){(Get-FileHash -LiteralPath $item.output).Hash}else{$null}
    & dotnet $tool $item.input "$artifactsFull/helper/ExportSafety.dll" $item.output 2> "$artifactsFull/$($item.name)-rejection.txt" | Out-Null
    $rejected=$LASTEXITCODE -ne 0
    $after=if(Test-Path -LiteralPath $item.output){(Get-FileHash -LiteralPath $item.output).Hash}else{$null}
    if(!$rejected -or $before -ne $after){throw "Rejection failed: $($item.name)"}
    $guards+=@{name=$item.name;rejected=$true;outputUnchanged=$true}
}
$control=Get-Content -LiteralPath "$artifactsFull/control-results.json" -Raw|ConvertFrom-Json
$repaired=Get-Content -LiteralPath "$artifactsFull/candidate-results.json" -Raw|ConvertFrom-Json
if(!$control.passed -or !$repaired.passed -or $control.checks -ne 62 -or $repaired.checks -ne 107){throw 'Unexpected case count or result.'}
if((Get-FileHash -LiteralPath $inputFull).Hash.ToLowerInvariant() -ne $originalHash){throw 'Original was modified.'}
$receipt=Get-Content -LiteralPath "$candidate.receipt.json" -Raw|ConvertFrom-Json
$report=[ordered]@{passed=$true;patch=$receipt;originalCases=62;candidateCases=107;rejectionChecks=$guards;totalObservedChecks=172;originalUnchanged=$true;installed=$false;scope='Actual extracted original/candidate IL including full skinned export; synthetic Unity/native FBX and isolated real System.IO. No GPU or real FBX validation.'}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$artifactsFull/verification-summary.json" -Encoding UTF8
Write-Output 'PASS: original 62, candidate 107, rejection 3; exact candidate hash and original preserved.'
$global:LASTEXITCODE=0
