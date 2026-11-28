function Assert-J694ArtifactPath([string]$ArtifactsPath,[string]$RepositoryRoot,[string]$GameRoot){
 $path=[IO.Path]::GetFullPath($ArtifactsPath).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
 $repo=[IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
 $game=[IO.Path]::GetFullPath($GameRoot).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
 $compare=[StringComparison]::OrdinalIgnoreCase
 $separator=[IO.Path]::DirectorySeparatorChar
 if($path.Equals($repo,$compare) -or $path.StartsWith($repo+$separator,$compare)){throw 'Artifacts must be outside the repository.'}
 if($path.Equals($game,$compare)){throw 'Artifacts cannot be the game root.'}
 if($path.StartsWith($game+$separator,$compare)){
  $relative=$path.Substring($game.Length+1);$top=($relative -split '[\\/]')[0]
  if($top -in @('BepInEx','UserData','mods','abdata') -or $top -like '*_Data'){throw 'Artifacts cannot be in the game runtime or content tree.'}
 }
 return $path
}
