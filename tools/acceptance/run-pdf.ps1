<# Generates a disposable PDF fixture, then checks numeric scale, named setup and paper layout in Core Console. #>
[CmdletBinding()]
param(
    [string]$AcadDirectory = 'C:\Program Files\Autodesk\AutoCAD 2026',
    [string]$OutputRoot = "$env:USERPROFILE\DHCB-test-results"
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
. (Join-Path $PSScriptRoot 'HostPreflight.ps1')
$hostInputs = Get-AcceptanceHost $AcadDirectory $dotnet
foreach ($project in @('tools\acceptance\DhcbTools.AutoCAD.Acceptance.csproj', 'src\DhcbTools.AutoCAD.Core\DhcbTools.AutoCAD.Core.csproj', 'src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj')) {
    & $dotnet build (Join-Path $repo $project) -c Release -p:AcadVersion=2026 -p:AcadRuntime=net10 -nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw ('Build failed: ' + $project) }
}
$acceptanceOut = Get-AcceptanceTargetDir (Join-Path $PSScriptRoot 'DhcbTools.AutoCAD.Acceptance.csproj') $dotnet
$coreOut = Get-AcceptanceTargetDir (Join-Path $repo 'src\DhcbTools.AutoCAD.Core\DhcbTools.AutoCAD.Core.csproj') $dotnet
$runnerOut = Get-AcceptanceTargetDir (Join-Path $repo 'src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj') $dotnet
$plugin = Join-Path $acceptanceOut 'DhcbTools.AutoCAD.Acceptance.dll'
$core = Join-Path $coreOut 'DhcbTools.AutoCAD.Core.dll'
$runner = Join-Path $runnerOut 'DhcbTools.BatchRunner.exe'
foreach ($path in @($plugin, $core, $runner)) { Assert-AcceptanceOutput $path }
$out = Join-Path $OutputRoot ('pdf-readiness-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$sample = $hostInputs.Sample
$hash = (Get-FileHash -LiteralPath $sample -Algorithm SHA256).Hash
$copy = Join-Path $out 'disposable.dwg'
Copy-Item -LiteralPath $sample -Destination $copy
$fixture = Join-Path $out 'fixture.dwg'
$script = Join-Path $out 'fixture.scr'
@(
    'FILEDIA 0', 'SECURELOAD 0', '(setvar "TILEMODE" 1)', '(setvar "CLAYER" "0")',
    '(command "_.-LAYER" "_UNLOCK" "*" "")', '(command "_.ERASE" "_ALL" "")', '(setvar "INSUNITS" 4)',
    '(entmake ''((0 . "LWPOLYLINE") (100 . "AcDbEntity") (100 . "AcDbPolyline") (90 . 4) (70 . 1) (10 0.0 0.0) (10 10000.0 0.0) (10 10000.0 5000.0) (10 0.0 5000.0)))',
    '(entmake ''((0 . "TEXT") (10 1000.0 2500.0 0.0) (40 . 250.0) (1 . "QA-SCALE-1-100") (7 . "Standard")))',
    '_.NETLOAD', ('"' + $plugin + '"'), 'DHCB_PDF_SETUP', '_.SAVEAS', '2018', ('"' + $fixture + '"'), '_.QUIT', '_Y'
) | Set-Content -LiteralPath $script -Encoding ASCII
$process = Start-Process -FilePath $hostInputs.Console -ArgumentList ('/i "' + $copy + '" /s "' + $script + '" /l en-US') `
    -RedirectStandardOutput (Join-Path $out 'fixture.log') -RedirectStandardError (Join-Path $out 'error.log') -PassThru -WindowStyle Hidden
$null = $process.Handle
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'Fixture timed out.' }
$process.Refresh()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $fixture)) { throw 'Fixture failed.' }
$fixtureHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
$cases = @(
    @{ name='scale'; config=@{ plotScale='1=100' } },
    @{ name='setup'; config=@{ pageSetupName='QA Model 1 100' } },
    @{ name='layout'; config=@{ layout='QA A3'; plotArea='Layout' } }
)
foreach ($case in $cases) {
    $pdf = Join-Path $out ($case.name + '.pdf')
    $case.config.outputPath = $pdf
    $job = @{ name=$case.name; app='autocad'; saveMode='None'; stopOnError=$true; outputFolder=$out;
        files=@(@{ path=$fixture }); steps=@(@{ command='PlotPdf'; config=$case.config }) }
    $jobPath = Join-Path $out ($case.name + '.json')
    $job | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jobPath -Encoding UTF8
    & $runner --job $jobPath --plugin-dll $core --accoreconsole $hostInputs.Console --log-dir (Join-Path $out ($case.name + '-logs')) --max-minutes 3
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $pdf)) { throw ('PDF failed: ' + $case.name) }
}
if ((Get-FileHash -LiteralPath $sample -Algorithm SHA256).Hash -ne $hash -or
    (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $hash -or
    (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ne $fixtureHash) { throw 'DWG changed during PDF checks.' }
Write-Output ('PASS: scale/setup/layout; DWGs preserved. Measure rendered PDFs before production: ' + $out)
