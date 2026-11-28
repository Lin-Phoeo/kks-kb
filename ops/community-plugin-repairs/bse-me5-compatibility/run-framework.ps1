param([Parameter(Mandatory=$true)][string]$RequestPath)
$ErrorActionPreference='Stop'
$request=Get-Content -LiteralPath $RequestPath -Raw|ConvertFrom-Json
# Byte loading is confined to the locally compiled transformer and existing Cecil.
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($request.cecil))|Out-Null
$tool=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($request.tool))
try{
    $tool.GetType('PatchBseMe5').GetMethod('Main').Invoke($null,@(,[string[]]$request.args))|Out-Null
}catch{
    Write-Error $_.Exception.InnerException.ToString()
    exit 1
}
