param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference='Stop'
$inputFull=(Resolve-Path -LiteralPath $InputPath).Path
$cecilFull=(Resolve-Path -LiteralPath $CecilPath).Path
$artifactsFull=[IO.Path]::GetFullPath($ArtifactsPath)
$sourceFull=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(Test-Path -LiteralPath $artifactsFull){throw 'ArtifactsPath must not exist.'}
if($artifactsFull.StartsWith($sourceFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep generated files outside the public source directory.'}
$originalHash='1be20737d73ae8d23db5c08d3a86e992bc7b12b171401686ecb264e05b439cf6'
$candidateHash='91874b52329d4fa4bb0b69a21e073d69ed423eacdb3485ee03ba985b0a7cefd0'
if((Get-FileHash -LiteralPath $inputFull -Algorithm SHA256).Hash.ToLowerInvariant() -ne $originalHash){throw 'Unsupported original SHA-256.'}
New-Item -ItemType Directory -Path $artifactsFull | Out-Null
& dotnet build "$PSScriptRoot/tool/ConsoleOwnershipPatch.csproj" -c Release --nologo -v quiet "-p:CecilPath=$cecilFull" "-p:BaseIntermediateOutputPath=$artifactsFull/tool-obj/" -o "$artifactsFull/tool"
if($LASTEXITCODE -ne 0){throw 'Tool build failed.'}
& dotnet build "$PSScriptRoot/tests/NativeOwnershipProbe.csproj" -c Release --nologo -v quiet "-p:BaseIntermediateOutputPath=$artifactsFull/probe-obj/" -o "$artifactsFull/fixture"
if($LASTEXITCODE -ne 0){throw 'Probe build failed.'}
$tool="$artifactsFull/tool/ConsoleOwnershipPatch.dll"
$candidate="$artifactsFull/Unity.Console.dll"
& dotnet $tool $inputFull $candidate
if($LASTEXITCODE -ne 0){throw 'Patch generation failed.'}
if((Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant() -ne $candidateHash){throw 'Candidate identity mismatch.'}
New-Item -ItemType Directory -Path "$artifactsFull/extracted" | Out-Null
& dotnet $tool --extract-probe $inputFull $candidate "$artifactsFull/fixture/NativeOwnershipProbe.dll" "$artifactsFull/extracted/NativeOwnershipProbe.dll"
if($LASTEXITCODE -ne 0){throw 'CIL extraction failed.'}
Copy-Item -LiteralPath "$artifactsFull/fixture/NativeOwnershipProbe.runtimeconfig.json" -Destination "$artifactsFull/extracted/NativeOwnershipProbe.runtimeconfig.json"
& dotnet "$artifactsFull/extracted/NativeOwnershipProbe.dll" --launch "$artifactsFull/results"
if($LASTEXITCODE -ne 0){throw 'Native regression failed.'}

function Assert-Rejected([string]$Case,[string]$Source,[string]$Destination){
    $oldOutput=if(Test-Path -LiteralPath $Destination -PathType Leaf){(Get-FileHash -LiteralPath $Destination).Hash}else{$null}
    $oldReceipt=if(Test-Path -LiteralPath "$Destination.receipt.json" -PathType Leaf){(Get-FileHash -LiteralPath "$Destination.receipt.json").Hash}else{$null}
    $rejectionOutput = & dotnet $tool $Source $Destination 2>&1
    if($LASTEXITCODE -eq 0){throw "Expected rejection: $Case"}
    if($oldOutput){if((Get-FileHash -LiteralPath $Destination).Hash -ne $oldOutput){throw "Output changed: $Case"}}elseif(Test-Path -LiteralPath $Destination){throw "Output created: $Case"}
    if($oldReceipt){if((Get-FileHash -LiteralPath "$Destination.receipt.json").Hash -ne $oldReceipt){throw "Receipt changed: $Case"}}elseif(Test-Path -LiteralPath "$Destination.receipt.json"){throw "Receipt created: $Case"}
    return [ordered]@{case=$Case;passed=$true;outputPreserved=$true}
}
$unknown="$artifactsFull/unknown.bin"
[IO.File]::WriteAllText($unknown,'synthetic unsupported input')
$receiptOnly="$artifactsFull/receipt-only.dll"
[IO.File]::WriteAllText("$receiptOnly.receipt.json",'synthetic existing receipt')
$guards=@(
    (Assert-Rejected 'unknown-input' $unknown "$artifactsFull/refused-unknown.dll"),
    (Assert-Rejected 'already-patched-input' $candidate "$artifactsFull/refused-patched.dll"),
    (Assert-Rejected 'existing-output' $inputFull $candidate),
    (Assert-Rejected 'existing-receipt' $inputFull $receiptOnly),
    (Assert-Rejected 'input-equals-output' $inputFull $inputFull)
)
if((Get-FileHash -LiteralPath $inputFull).Hash.ToLowerInvariant() -ne $originalHash){throw 'Original changed.'}
$cases=@(Get-ChildItem -LiteralPath "$artifactsFull/results" -Filter '*.json' | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json})
if($cases.Count -ne 5 -or @($cases | Where-Object {!$_.passed}).Count){throw 'Unexpected case results.'}
$receipt=Get-Content -LiteralPath "$candidate.receipt.json" -Raw | ConvertFrom-Json
$summary=[ordered]@{patch=$receipt;nativeCases=$cases;rejectionCases=$guards;originalPreserved=$true;scope='Isolated Win32 and extracted CIL only; no game/Mono/IronPython acceptance implied.'}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$artifactsFull/verification-summary.json" -Encoding UTF8
Write-Output 'PASS: identical reviewed candidate, five Win32 cases, five rejection cases, original preserved.'
exit 0
