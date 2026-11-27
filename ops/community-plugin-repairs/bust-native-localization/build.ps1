param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference='Stop'
$game=(Resolve-Path -LiteralPath $GameRoot).Path
$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
$source=[IO.Path]::GetFullPath($PSScriptRoot)
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must be a new directory.'}
if($artifacts -eq $source -or $artifacts.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep generated files outside the source tree.'}
$bust=Join-Path $game 'BepInEx/plugins/KKS_StudioBustAdjuster.dll'
$api=Join-Path $game 'BepInEx/plugins/KKSAPI.dll'
$expectedBust='a07e33d8768b6a1540aee3b727cf3ad43cd62335fc3ecad04f6256e5ac53098f'
$expectedApi='866464809d1bd04450a35850c23f9d17a0d1e5a166ed9f5dc525c506fc04ef01'
if((Get-FileHash -LiteralPath $bust).Hash.ToLowerInvariant() -ne $expectedBust -or (Get-FileHash -LiteralPath $api).Hash.ToLowerInvariant() -ne $expectedApi){throw 'Unsupported BustAdjuster or KKAPI bytes.'}
$sdk=@(& dotnet --list-sdks | Where-Object {$_ -match '^10\.'})[-1]
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw '.NET SDK 10 is required.'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$references=@('mscorlib.dll','System.dll','System.Core.dll')|ForEach-Object {Join-Path $framework $_}
$references+=@('BepInEx/core/BepInEx.dll','BepInEx/core/0Harmony.dll','BepInEx/plugins/KKSAPI.dll','CharaStudio_Data/Managed/UnityEngine.dll','CharaStudio_Data/Managed/UnityEngine.CoreModule.dll','CharaStudio_Data/Managed/UnityEngine.UI.dll','CharaStudio_Data/Managed/UnityEngine.UIModule.dll','CharaStudio_Data/Managed/Unity.TextMeshPro.dll','CharaStudio_Data/Managed/Assembly-CSharp.dll','CharaStudio_Data/Managed/UniRx.dll')|ForEach-Object {Join-Path $game $_}
foreach($r in $references){if(!(Test-Path -LiteralPath $r)){throw "Missing reference: $r"}}
New-Item -ItemType Directory -Path $artifacts|Out-Null
$candidate=Join-Path $artifacts 'Codex.StudioBustNativeText.dll'
$rsp=Join-Path $artifacts 'compile.rsp'
$lines=@('/nologo','/target:library','/optimize+','/langversion:5','/deterministic+','/nostdlib+','/codepage:65001',('/out:"'+$candidate+'"'),('/pathmap:"'+$source+'=/src"'))
$lines+=@($references|ForEach-Object {'/reference:"'+$_+'"'})
$lines+=@('NativeTextPolicy.cs','StudioBustNativeText.cs')|ForEach-Object {'"'+(Join-Path $source $_)+'"'}
$lines|Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw 'Native text helper compilation failed.'}
if((Get-FileHash -LiteralPath $bust).Hash.ToLowerInvariant() -ne $expectedBust -or (Get-FileHash -LiteralPath $api).Hash.ToLowerInvariant() -ne $expectedApi){throw 'Input changed during generation.'}
$receipt=[ordered]@{candidateSha256=(Get-FileHash -LiteralPath $candidate).Hash.ToLowerInvariant();originalBustSha256=$expectedBust;kkapiSha256=$expectedApi;originalFilesChanged=$false;installed=$false;scope='Own creation-event display helper. Original category/node/track identities remain untouched.'}
$receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $artifacts 'build-receipt.json') -Encoding utf8
$receipt|ConvertTo-Json
