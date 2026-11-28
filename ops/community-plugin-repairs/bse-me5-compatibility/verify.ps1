param(
    [Parameter(Mandatory=$true)][string]$Candidate,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference='Stop'
$candidatePath=(Resolve-Path -LiteralPath $Candidate).Path
$cecil=(Resolve-Path -LiteralPath $CecilPath).Path
$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must be new.'}
if($artifacts -eq $repo -or $artifacts.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep artifacts outside the repository.'}
$ancestor=[IO.DirectoryInfo]::new($artifacts)
while($null -ne $ancestor){
    if($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Artifacts cannot traverse a link or junction.'}
    if((Test-Path -LiteralPath (Join-Path $ancestor.FullName 'BepInEx')) -or (Test-Path -LiteralPath (Join-Path $ancestor.FullName 'CharaStudio.exe'))){throw 'Artifacts cannot be inside a game installation.'}
    $ancestor=$ancestor.Parent
}
if((Get-FileHash -LiteralPath $candidatePath).Hash.ToLowerInvariant() -ne 'cf2c8a6b08c5148e2253dd8d6f32563f2320f70a7184b81ae3e80ac1372239dd'){throw 'Candidate identity differs.'}
& dotnet build (Join-Path $PSScriptRoot 'tests/Probe.csproj') -c Release "-p:CecilPath=$cecil" --artifacts-path (Join-Path $artifacts 'build') --nologo
if($LASTEXITCODE -ne 0){throw 'Synthetic probe build failed.'}
$fixture=Join-Path $artifacts 'build/bin/Probe/release/Probe.dll'
$run=Join-Path $artifacts 'extracted'
New-Item -ItemType Directory -Path $run|Out-Null
$probe=Join-Path $run 'Probe.dll'
& dotnet $fixture extract $candidatePath $probe
if($LASTEXITCODE -ne 0){throw 'Exact candidate IL extraction failed.'}
Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($fixture,'runtimeconfig.json')) -Destination ([IO.Path]::ChangeExtension($probe,'runtimeconfig.json'))
Copy-Item -LiteralPath $cecil -Destination (Join-Path $run 'Mono.Cecil.dll')
& dotnet $probe run (Join-Path $artifacts 'result.json')
if($LASTEXITCODE -ne 0){throw 'Actual-IL synthetic regression failed.'}
Write-Output 'PASS: actual candidate bridge IL against synthetic renderer/window boundaries; no Unity or game execution.'
