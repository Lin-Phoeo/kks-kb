param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('kks-colorspace-tests-' + [guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$packageDirectory = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot)).TrimEnd('\', '/')
if ($OutputDirectory.Equals($packageDirectory, [StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory.StartsWith($packageDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test output must be outside this source package.'
}
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$sdk = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^(\d+)\.') { throw 'Cannot select an installed .NET SDK.' }
$testFramework = 'net' + $Matches[1] + '.0'
$results = @()
foreach ($mode in @('baseline', 'candidate')) {
    $modeDirectory = Join-Path $OutputDirectory $mode
    New-Item -ItemType Directory -Force -Path $modeDirectory | Out-Null
    $intermediate = (Join-Path $modeDirectory 'obj') + [IO.Path]::DirectorySeparatorChar
    $binary = (Join-Path $modeDirectory 'bin') + [IO.Path]::DirectorySeparatorChar
    & $dotnet build (Join-Path $PSScriptRoot 'Regression.csproj') --configuration Release --property:Mode=$mode --property:TestTargetFramework=$testFramework --property:BaseIntermediateOutputPath=$intermediate --property:OutputPath=$binary --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --nologo
    if ($LASTEXITCODE -ne 0) { throw "$mode test build failed." }
    $output = & $dotnet (Join-Path $binary "$mode-regression.dll") 2>&1
    $exitCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $modeDirectory 'results.txt') -Encoding UTF8
    $output | Write-Output
    if ($mode -eq 'baseline') {
        if ($exitCode -ne 23 -or !($output -match 'EXPECTED_RED_SECOND_DECODE')) { throw 'Baseline did not fail the expected second-decode requirement.' }
    } elseif ($exitCode -ne 0 -or !($output -match 'Managed assertions passed: 13')) { throw 'Candidate managed regression failed.' }
    $results += [pscustomobject]@{ mode = $mode; exitCode = $exitCode; result = if ($mode -eq 'baseline') { 'expected_red' } else { 'passed_13' } }
}
$scopeDirectory = Join-Path $OutputDirectory 'render-target-scope'
$intermediate = (Join-Path $scopeDirectory 'obj') + [IO.Path]::DirectorySeparatorChar
$binary = (Join-Path $scopeDirectory 'bin') + [IO.Path]::DirectorySeparatorChar
& $dotnet build (Join-Path $PSScriptRoot 'RenderTargetScope.csproj') --configuration Release --property:TestTargetFramework=$testFramework --property:BaseIntermediateOutputPath=$intermediate --property:OutputPath=$binary --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Render target scope test build failed.' }
$scopeOutput = & $dotnet (Join-Path $binary 'render-target-scope.dll') 2>&1
$scopeExit = $LASTEXITCODE
$scopeOutput | Set-Content -LiteralPath (Join-Path $scopeDirectory 'results.txt') -Encoding UTF8
$scopeOutput | Write-Output
if ($scopeExit -ne 0 -or !($scopeOutput -match 'Render target scope assertions passed: 6')) { throw 'Render target scope regression failed.' }
$results += [pscustomobject]@{ mode = 'render-target-scope'; exitCode = $scopeExit; result = 'passed_6' }
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding UTF8
Write-Output "Managed regression complete: $OutputDirectory"
