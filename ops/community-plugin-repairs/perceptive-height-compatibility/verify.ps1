param(
 [Parameter(Mandatory=$true)][string]$InputPath,
 [Parameter(Mandatory=$true)][string]$CecilPath,
 [Parameter(Mandatory=$true)][string]$BepInExPath,
 [Parameter(Mandatory=$true)][string]$UnityFacadePath,
 [Parameter(Mandatory=$true)][string]$UnityCorePath,
 [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($PSScriptRoot)
$out=[IO.Path]::GetFullPath($ArtifactsPath)
if(Test-Path -LiteralPath $out){throw 'Use a new artifacts directory'}
if($out.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Use artifacts outside the source directory'}
$original=(Resolve-Path -LiteralPath $InputPath).Path
if((Get-FileHash $original).Hash.ToLowerInvariant() -ne 'a7de711fae624aa1a780eb6e041c4e9c0c4b066f987b024ff52c4c2f067de35b'){throw 'Unsupported input hash'}
New-Item -ItemType Directory -Path $out|Out-Null
$cecil=(Resolve-Path -LiteralPath $CecilPath).Path
$bep=(Resolve-Path -LiteralPath $BepInExPath).Path
$unity=(Resolve-Path -LiteralPath $UnityCorePath).Path
$facade=(Resolve-Path -LiteralPath $UnityFacadePath).Path
Push-Location $source
try {
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:library /optimize+ /langversion:5 "/out:$out/CaptureHook.dll" "/reference:$bep" "/reference:$unity" "/reference:$facade" CaptureHook.cs
 if($LASTEXITCODE -ne 0){throw 'Helper compilation failed'}
} finally { Pop-Location }
& dotnet build "$source/tool/Patcher.csproj" -c Release --nologo "-p:CecilPath=$cecil" --artifacts-path "$out/tool-build"
if($LASTEXITCODE -ne 0){throw 'Tool build failed'}
$tool="$out/tool-build/bin/Patcher/release/Patcher.dll"
$candidate="$out/PerceptiveHeightX.KKS.dll"
& dotnet $tool patch $original "$out/CaptureHook.dll" $candidate
if($LASTEXITCODE -ne 0){throw 'Candidate build failed'}
$matrix=@()
foreach($name in @('KKS_Screencap','KK_Screencap','Screencap','CaptureProbe')){
 & dotnet build "$source/tests/Probe.csproj" -c Release --nologo "-p:AssemblyName=$name" --artifacts-path "$out/$name/template-build"
 if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}
 $template="$out/$name/template-build/bin/Probe/release/$name.dll"
 foreach($mode in @('original','candidate')){
  $dll=if($mode -eq 'original'){$original}else{$candidate}
  $probe="$out/$name/$mode/$name.dll"
  & dotnet $tool probe $dll $template $probe
  if($LASTEXITCODE -ne 0){throw 'CIL extraction failed'}
  Copy-Item "$out/$name/template-build/bin/Probe/release/$name.runtimeconfig.json" "$out/$name/$mode/$name.runtimeconfig.json"
  & dotnet $probe "$out/$name/$mode/result.json" > "$out/$name/$mode/console.txt"
  $exit=$LASTEXITCODE;$report=Get-Content "$out/$name/$mode/result.json" -Raw|ConvertFrom-Json
  $expected=if($mode -eq 'candidate'){0}elseif($name -eq 'Screencap'){5}elseif($name -eq 'CaptureProbe'){6}else{7}
  if($report.checks -ne 12 -or $report.failures -ne $expected -or $exit -ne [int]($expected -gt 0)){throw "Unexpected probe result: $name/$mode"}
  $matrix+=@{assembly=$name;mode=$mode;checks=$report.checks;failures=$report.failures;expected=$true}
 }
}
$guards=@()
foreach($guard in @(@{name='unknown-input';input="$out/CaptureHook.dll";output="$out/unknown.dll"},@{name='patched-input';input=$candidate;output="$out/repatch.dll"},@{name='same-input-output';input=$original;output=$original},@{name='existing-output';input=$original;output=$candidate})){
 $before=if(Test-Path $guard.output){(Get-FileHash $guard.output).Hash}else{$null}
 & dotnet $tool patch $guard.input "$out/CaptureHook.dll" $guard.output 2> "$out/$($guard.name).txt"|Out-Null
 $after=if(Test-Path $guard.output){(Get-FileHash $guard.output).Hash}else{$null}
 if($LASTEXITCODE -eq 0 -or $before -ne $after){throw 'Refusal failed'}
 $guards+=@{name=$guard.name;rejected=$true;unchanged=$true}
}
$result=@{passed=$true;matrix=$matrix;candidateChecks=48;originalChecks=48;originalExpectedFailures=25;refusalChecks=$guards;candidateSha256=(Get-FileHash $candidate).Hash.ToLowerInvariant();originalUnchanged=((Get-FileHash $original).Hash.ToLowerInvariant() -eq 'a7de711fae624aa1a780eb6e041c4e9c0c4b066f987b024ff52c4c2f067de35b');installed=$false;scope='Actual extracted subscription/lifecycle/helper CIL. Synthetic registry/event sources; no Unity or screenshot capture.'}
$result|ConvertTo-Json -Depth 7|Set-Content "$out/verification-summary.json" -Encoding UTF8
Write-Output 'PASS: 48 candidate checks, 25 expected baseline failures in 48 checks, 4 refusals; exact candidate hash.'
$global:LASTEXITCODE=0
