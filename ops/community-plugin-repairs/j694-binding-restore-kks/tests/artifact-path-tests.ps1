$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../artifact-path.ps1')
$base=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'j694-path-only-no-io'))
$repo=Join-Path $base 'repository';$game=Join-Path $base 'game'
$blocked=@($repo,(Join-Path $repo 'tests/new-output'),(Join-Path $repo 'ops/../new-output'),$game,(Join-Path $game 'BepInEx/plugins/new-output'),(Join-Path $game 'BepInEx/../mods/new-output'),(Join-Path $game 'UserData/new-output'),(Join-Path $game 'abdata/new-output'),(Join-Path $game 'CharaStudio_Data/Managed/new-output'),(Join-Path $game 'KoikatsuSunshine_Data/new-output'))
foreach($path in $blocked){$rejected=$false;try{[void](Assert-J694ArtifactPath $path $repo $game)}catch{$rejected=$true};if(!$rejected){throw "Forbidden path accepted: $path"}}
foreach($path in @((Join-Path $base 'artifacts/new-output'),(Join-Path $game '.scratch/new-output'))){[void](Assert-J694ArtifactPath $path $repo $game)}
Write-Output 'PASS artifact path boundaries: 10 refused, 2 allowed; no filesystem mutation.'
