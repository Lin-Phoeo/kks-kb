param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference='Stop'
$artifacts=[IO.Path]::GetFullPath($ArtifactsPath)
$source=[IO.Path]::GetFullPath($PSScriptRoot)
if(Test-Path -LiteralPath $artifacts){throw 'ArtifactsPath must be new.'}
if($artifacts -eq $source -or $artifacts.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build outside the source tree.'}
$candidateDir=Join-Path $artifacts 'candidate'
& (Join-Path $source 'build.ps1') -GameRoot $GameRoot -ArtifactsPath $candidateDir
& dotnet build (Join-Path $source 'tests/Probe.csproj') -c Release --artifacts-path (Join-Path $artifacts 'tests') --nologo
if($LASTEXITCODE -ne 0){throw 'Synthetic fixture build failed.'}
$log=@(& dotnet (Join-Path $artifacts 'tests/bin/Probe/release/Probe.dll') 2>&1)
if($LASTEXITCODE -ne 0 -or $log[-1] -cne 'PASS checks=68 synthetic creation callbacks; no Unity runtime.'){throw 'Synthetic callback checks failed.'}
$log|Set-Content -LiteralPath (Join-Path $artifacts 'synthetic-checks.log') -Encoding utf8
$candidate=Join-Path $candidateDir 'Codex.StudioBustNativeText.dll'
[void][Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'))
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
foreach($relative in @('BepInEx/core','BepInEx/plugins','CharaStudio_Data/Managed')){$resolver.AddSearchDirectory((Join-Path $GameRoot $relative))}
$parameters=[Mono.Cecil.ReaderParameters]::new();$parameters.AssemblyResolver=$resolver
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($candidate,$parameters)
try{
    $members=@($assembly.MainModule.GetMemberReferences())
    foreach($member in $members){if($null -eq $member.Resolve()){throw "Unresolved member: $($member.FullName)"}}
    $plugin=$assembly.MainModule.GetType('Codex.StudioBustNativeText.Plugin')
    if(@($plugin.Methods|Where-Object Name -in @('Update','LateUpdate','FixedUpdate')).Count){throw 'Unexpected per-frame method.'}
    $calls=@($plugin.Methods|Where-Object HasBody|ForEach-Object {$_.Body.Instructions}|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand})
    if(@($calls|Where-Object Name -in @('set_name','set_Name','set_CategoryName')).Count){throw 'UI identity setter introduced.'}
    if(@($calls|Where-Object {$_.Name -like 'FindObjects*'}).Count){throw 'Global scene scan introduced.'}
    $receipt=Get-Content (Join-Path $candidateDir 'build-receipt.json') -Raw|ConvertFrom-Json
    [ordered]@{passed=$true;candidateSha256=$receipt.candidateSha256;syntheticChecks=68;memberReferencesResolved=$members.Count;noPerFrameMethods=$true;noIdentitySetters=$true;noGlobalSceneScan=$true;originalFilesChanged=$false;installed=$false;scope='Own helper creation callbacks compiled with synthetic component/Harmony recorders plus actual dependency metadata resolution. No real Unity UI assertion.'}|ConvertTo-Json|Set-Content (Join-Path $artifacts 'verification-summary.json') -Encoding utf8
}finally{$assembly.Dispose();$resolver.Dispose()}
Write-Output 'PASS: 68 synthetic checks; actual member references resolve; no per-frame/global scan or identity setter.'
$global:LASTEXITCODE=0
