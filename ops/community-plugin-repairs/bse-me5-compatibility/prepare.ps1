param(
    [Parameter(Mandatory=$true)][string]$InputBse,
    [Parameter(Mandatory=$true)][string]$MaterialEditorPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$HarmonyPath
)
$ErrorActionPreference='Stop'
$inputPath=(Resolve-Path -LiteralPath $InputBse).Path
$mePath=(Resolve-Path -LiteralPath $MaterialEditorPath).Path
$cecil=(Resolve-Path -LiteralPath $CecilPath).Path
$harmony=(Resolve-Path -LiteralPath $HarmonyPath).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(Test-Path -LiteralPath $output){throw 'Output directory must be new.'}
if($output.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $output -eq $repo){throw 'Output must be outside the repository.'}
$ancestor=[IO.DirectoryInfo]::new($output)
while($null -ne $ancestor){
    if($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Output cannot traverse a link or junction.'}
    if((Test-Path -LiteralPath (Join-Path $ancestor.FullName 'BepInEx')) -or (Test-Path -LiteralPath (Join-Path $ancestor.FullName 'CharaStudio.exe'))){throw 'Output cannot be inside a game installation.'}
    $ancestor=$ancestor.Parent
}
foreach($path in @($inputPath,$mePath)){
    $dir=[IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($path))
    while($null -ne $dir){
        if((Test-Path -LiteralPath (Join-Path $dir.FullName 'BepInEx')) -or (Test-Path -LiteralPath (Join-Path $dir.FullName 'CharaStudio.exe'))){
            if($output -eq $dir.FullName -or $output.StartsWith($dir.FullName+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output cannot be inside a game installation.'}
        }
        $dir=$dir.Parent
    }
}
if((Get-FileHash -LiteralPath $inputPath).Hash.ToLowerInvariant() -ne '2212e3d27114fc83a578ffb6dec313642ea31e72381efc3f6a2c5427e96fbdfa'){throw 'Unsupported BSE input hash.'}
if((Get-FileHash -LiteralPath $mePath).Hash.ToLowerInvariant() -ne '9e72d250ca262df8a3c3aa93922113e3cdc0103ca1b22ee6cf9c44b04981278b'){throw 'Unsupported Material Editor hash.'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$build=Join-Path ([IO.Path]::GetTempPath()) ('bse-me5-tool-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build|Out-Null
$dll=Join-Path $build 'BseMe5Transform.dll'
& $compiler /nologo /target:library "/out:$dll" "/r:$cecil" (Join-Path $PSScriptRoot 'PatchBseMe5.cs')
if($LASTEXITCODE -ne 0){throw 'Transform compiler failed.'}
$request=Join-Path $build 'request.json'
[IO.File]::WriteAllText($request,(@{tool=$dll;cecil=$cecil;args=@($inputPath,$mePath,$output,$harmony,$repo)}|ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
& (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe') -NoProfile -File (Join-Path $PSScriptRoot 'run-framework.ps1') -RequestPath $request
if($LASTEXITCODE -ne 0){throw 'Candidate preparation failed.'}
Write-Output ('Prepared external candidate only: '+$output)
