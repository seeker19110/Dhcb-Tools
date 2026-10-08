<# Runs read-only query acceptance on a disposable drawing, using a separate in-memory database. #>
[CmdletBinding()]
param(
    [string]$AcadDirectory = 'C:\Program Files\Autodesk\AutoCAD 2026',
    [string]$OutputRoot = "$env:USERPROFILE\DHCB-test-results"
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $PSScriptRoot 'DhcbTools.AutoCAD.Acceptance.csproj'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
. (Join-Path $PSScriptRoot 'HostPreflight.ps1')
$hostInputs = Get-AcceptanceHost $AcadDirectory $dotnet
& $dotnet build $project -c Release -p:AcadVersion=2026 -p:AcadRuntime=net10 -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Acceptance plugin build failed.' }
$target = Get-AcceptanceTargetDir $project $dotnet
$plugin = Join-Path $target 'DhcbTools.AutoCAD.Acceptance.dll'
Assert-AcceptanceOutput $plugin
$sample = $hostInputs.Sample
$hash = (Get-FileHash -LiteralPath $sample -Algorithm SHA256).Hash
$out = Join-Path $OutputRoot ('query-readiness-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$copy = Join-Path $out 'disposable.dwg'
Copy-Item -LiteralPath $sample -Destination $copy
$report = Join-Path $out 'queries.json'
$scr = Join-Path $out 'query.scr'
@('FILEDIA 0', 'SECURELOAD 0', '_.NETLOAD', ('"' + $plugin + '"'), 'DHCB_ACCEPTANCE', $report, '_.QUIT', '_Y') |
    Set-Content -LiteralPath $scr -Encoding ASCII
$process = Start-Process -FilePath $hostInputs.Console -ArgumentList ('/i "' + $copy + '" /s "' + $scr + '" /l en-US') `
    -RedirectStandardOutput (Join-Path $out 'console.log') -RedirectStandardError (Join-Path $out 'error.log') -PassThru -WindowStyle Hidden
$null = $process.Handle
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'Acceptance timed out.' }
$process.Refresh()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw ('No successful report: ' + $out) }
if ((Get-FileHash -LiteralPath $sample -Algorithm SHA256).Hash -ne $hash -or (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $hash) {
    throw 'DWG hash changed.'
}
$results = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (-not $results.allPassed) { throw ('Acceptance failed: ' + $report) }
Write-Output $report
