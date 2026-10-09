<# Verify batch failure policies against AutoCAD 2026 on disposable drawings. #>
[CmdletBinding()]
param(
    [string]$AcadDirectory = 'C:\Program Files\Autodesk\AutoCAD 2026',
    [string]$OutputRoot = "$env:USERPROFILE\DHCB-test-results"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
. (Join-Path $PSScriptRoot 'HostPreflight.ps1')
$hostInputs = Get-AcceptanceHost $AcadDirectory $dotnet
$coreProject = Join-Path $repo 'src\DhcbTools.AutoCAD.Core\DhcbTools.AutoCAD.Core.csproj'
$runnerProject = Join-Path $repo 'src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj'
& $dotnet build $coreProject -c Release -p:AcadVersion=2026 -p:AcadRuntime=net10 -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Core plugin build failed.' }
& $dotnet build $runnerProject -c Release -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'BatchRunner build failed.' }
$plugin = Join-Path (Get-AcceptanceTargetDir $coreProject $dotnet) 'DhcbTools.AutoCAD.Core.dll'
$runner = Join-Path (Get-AcceptanceTargetDir $runnerProject $dotnet) 'DhcbTools.BatchRunner.exe'
Assert-AcceptanceOutput $plugin
Assert-AcceptanceOutput $runner
$out = Join-Path $OutputRoot ('batch-readiness-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$sampleHash = (Get-FileHash -LiteralPath $hostInputs.Sample -Algorithm SHA256).Hash
$copies = @()
foreach ($name in @('first.dwg','second.dwg')) {
    $path = Join-Path $out $name
    Copy-Item -LiteralPath $hostInputs.Sample -Destination $path
    $copies += $path
}

function Invoke-FixtureJob([string]$Name, [bool]$StopOnError, [object[]]$Steps, [string[]]$Drawings) {
    $dir = Join-Path $out $Name
    New-Item -ItemType Directory -Path $dir | Out-Null
    $job = [ordered]@{
        name=$Name; app='autocad'; saveMode='None'; stopOnError=$StopOnError
        outputFolder=$dir; files=@($Drawings | ForEach-Object { @{path=$_} }); steps=$Steps
    }
    $jobPath = Join-Path $dir 'job.json'
    $job | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jobPath -Encoding UTF8
    & $runner --job $jobPath --log-dir (Join-Path $dir 'logs') --plugin-dll $plugin --accoreconsole $hostInputs.Console --max-minutes 2 |
        Set-Content -LiteralPath (Join-Path $dir 'runner.log') -Encoding UTF8
    if ($LASTEXITCODE -ne 1) { throw "Expected failed job code 1: $Name ($LASTEXITCODE)" }
    $logs = @(Get-ChildItem -LiteralPath (Join-Path $dir 'logs') -Filter 'run-*.jsonl' -Recurse)
    if ($logs.Count -ne 1) { throw "Expected a separate run log: $Name" }
    & $runner --verify-log $logs[0].FullName | Set-Content -LiteralPath (Join-Path $dir 'verify.log') -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw "Broken hash chain: $Name" }
    if (-not (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($logs[0].FullName, '.html')))) { throw 'Missing report for this run.' }
    return @(Get-Content -LiteralPath $logs[0].FullName -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
}

function Export-Step([string]$Path, [bool]$Dependent) {
    return @{command='LayerExport'; config=@{outputPath=$Path}; skipIfPreviousFailed=$Dependent}
}
$skipped1 = Join-Path $out 'must-not-exist-1.csv'
$skipped2 = Join-Path $out 'must-not-exist-2.csv'
$recovery = Join-Path $out 'independent.csv'
$afterRecovery = Join-Path $out 'after-recovery.csv'
$steps = @(
    @{command='Invalid/Command'; config=@{}},
    (Export-Step $skipped1 $true), (Export-Step $skipped2 $true),
    (Export-Step $recovery $false), (Export-Step $afterRecovery $true)
)
$entries = @(Invoke-FixtureJob 'dependencies' $false $steps @($copies[0]))
if ($entries.Count -ne 5 -or $entries[0].success -or -not $entries[1].skipped -or -not $entries[2].skipped -or
    -not $entries[3].success -or -not $entries[4].success) { throw 'Dependency/recovery log differs from expected results.' }
if ((Test-Path -LiteralPath $skipped1) -or (Test-Path -LiteralPath $skipped2)) { throw 'A skipped export wrote output.' }
foreach ($path in @($recovery, $afterRecovery)) {
    if (-not (Test-Path -LiteralPath $path)) { throw 'Independent recovery export did not run.' }
}
$stop1 = Join-Path $out 'stop-must-not-exist-1.csv'
$stop2 = Join-Path $out 'stop-must-not-exist-2.csv'
$steps = @(@{command='Invalid/Command'; config=@{}}, (Export-Step $stop1 $false), (Export-Step $stop2 $false))
$entries = @(Invoke-FixtureJob 'stop-on-error' $true $steps $copies)
if ($entries.Count -ne 4 -or $entries[0].success -or @($entries | Where-Object skipped).Count -ne 3) {
    throw 'stopOnError did not skip both later commands and the second drawing.'
}
if ((Test-Path -LiteralPath $stop1) -or (Test-Path -LiteralPath $stop2)) { throw 'A stopped export wrote output.' }
foreach ($path in @($hostInputs.Sample) + $copies) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sampleHash) { throw "DWG changed: $path" }
}
'PASS: dependency chains, independent recovery, stopOnError, safe command filenames, separate reports, valid hash chains, unchanged source and copied DWGs.' |
    Set-Content -LiteralPath (Join-Path $out 'independent-assertions.txt') -Encoding UTF8
Write-Output $out
