param(
    [Parameter(Mandatory = $true)][string]$CecilPath,
    [Parameter(Mandatory = $true)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$CecilPath = (Resolve-Path -LiteralPath $CecilPath).Path
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (Test-Path -LiteralPath $OutputRoot) { throw 'OutputRoot must be a new external directory.' }
if ($OutputRoot.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputRoot must be outside the repository.' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
foreach ($target in @(
    @{Name='patcher'; Project='OtherRuntimeGuards.csproj'},
    @{Name='fixture'; Project='tests/fixture/Fixture.csproj'},
    @{Name='tester'; Project='tests/tester/Tester.csproj'}
)) {
    $destination = Join-Path $OutputRoot $target.Name
    $intermediate = Join-Path $OutputRoot ('obj/' + $target.Name + '/')
    dotnet build (Join-Path $PSScriptRoot $target.Project) -c Release "-p:CecilPath=$CecilPath" "-p:BaseIntermediateOutputPath=$intermediate" -o $destination
    if ($LASTEXITCODE -ne 0) { throw ('Build failed: ' + $target.Name) }
}
dotnet (Join-Path $OutputRoot 'tester/Tester.dll') (Join-Path $OutputRoot 'fixture/Fixture.dll') (Join-Path $OutputRoot 'results')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic regression failed.' }
