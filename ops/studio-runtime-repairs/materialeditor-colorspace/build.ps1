param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [string]$FrameworkReferenceDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
if (!$FrameworkReferenceDirectory) {
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $FrameworkReferenceDirectory = Join-Path $programFilesX86 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2'
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('kks-colorspace-build-' + [guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$sourceDirectory = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/')
if ($OutputDirectory.Equals($sourceDirectory, [StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory.StartsWith($sourceDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must be outside this source package.'
}
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$sdk = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot select the installed .NET SDK.' }
$sdkEntry = & $dotnet --list-sdks | Where-Object { $_.StartsWith($sdk + ' ') } | Select-Object -First 1
if (!$sdkEntry -or $sdkEntry -notmatch '\[(.+)\]') { throw 'Cannot locate the selected SDK.' }
$csc = Join-Path $Matches[1] "$sdk/Roslyn/bincore/csc.dll"
$references = @(
    (Join-Path $FrameworkReferenceDirectory 'mscorlib.dll'),
    (Join-Path $FrameworkReferenceDirectory 'System.dll'),
    (Join-Path $FrameworkReferenceDirectory 'System.Core.dll'),
    (Join-Path $FrameworkReferenceDirectory 'Facades/netstandard.dll'),
    (Join-Path $GameRoot 'BepInEx/core/BepInEx.dll'),
    (Join-Path $GameRoot 'BepInEx/core/0Harmony.dll'),
    (Join-Path $GameRoot 'CharaStudio_Data/Managed/UnityEngine.dll'),
    (Join-Path $GameRoot 'CharaStudio_Data/Managed/UnityEngine.CoreModule.dll'),
    (Join-Path $GameRoot 'CharaStudio_Data/Managed/UnityEngine.InputLegacyModule.dll'),
    (Join-Path $GameRoot 'CharaStudio_Data/Managed/UnityEngine.ImageConversionModule.dll')
)
foreach ($path in $references + $csc) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing local build dependency: $path" }
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = Join-Path $OutputDirectory 'CodexMEColorSpaceFix.dll'
if (Test-Path -LiteralPath $output) { throw 'Output DLL already exists; select a new output directory.' }
$compilerArgs = @('/noconfig', '/nostdlib+', '/target:library', '/langversion:latest', '/optimize+', '/deterministic+', "/pathmap:$PSScriptRoot=/_/materialeditor-colorspace", "/out:$output")
foreach ($reference in $references) { $compilerArgs += "/reference:$reference" }
foreach ($source in @('MEColorSpaceFixPlugin.cs', 'GpuCopyGate.cs', 'RuntimeStress.cs', 'SelfTestRenderTargetScope.cs', 'RoundTripTest.cs')) {
    $compilerArgs += Join-Path $PSScriptRoot $source
}
& $dotnet $csc @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-FileHash -LiteralPath $output -Algorithm SHA256
