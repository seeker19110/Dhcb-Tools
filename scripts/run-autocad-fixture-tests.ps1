<#
.SYNOPSIS
    Runs deterministic AutoCAD engineering write checks on a disposable sample copy.
.DESCRIPTION
    Uses an already built/installed core plugin, creates fixture entities with AutoLISP, runs the
    suite, then independently verifies exported attribute values and final text formatting.
    Does not save any DWG and does not build projects or require a user's project drawing.
#>
[CmdletBinding()]
param(
    [string]$PluginPath = "$env:APPDATA\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2026\DhcbTools.AutoCAD.Core.dll",
    [string]$AcadDirectory = 'C:\Program Files\Autodesk\AutoCAD 2026',
    [string]$OutputRoot = "$env:USERPROFILE\DHCB-test-results",
    [int]$TimeoutSeconds = 180
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$repo = Split-Path -Parent $PSScriptRoot
$console = Join-Path $AcadDirectory 'accoreconsole.exe'
$sample = Join-Path $AcadDirectory 'Sample\Mechanical Sample\Data Extraction and Multileaders Sample.dwg'
$fixture = Join-Path $repo 'tests\suites\fixtures\autocad-engineering.lsp'
$suite = Join-Path $repo 'tests\suites\autocad-engineering-write.json'
$lockedSuite = Join-Path $repo 'tests\suites\autocad-engineering-locked.json'
foreach ($path in @($console, $sample, $PluginPath, $fixture, $suite, $lockedSuite)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Required file missing: ' + $path) }
}
if ($TimeoutSeconds -lt 30 -or $TimeoutSeconds -gt 1200) { throw 'TimeoutSeconds must be between 30 and 1200.' }
$sampleHash = (Get-FileHash -LiteralPath $sample -Algorithm SHA256).Hash
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss-fff'
$out = Join-Path $OutputRoot ('autocad-engineering-write-' + $stamp)
New-Item -ItemType Directory -Path $out | Out-Null
$copy = Join-Path $out 'disposable-sample.dwg'
Copy-Item -LiteralPath $sample -Destination $copy
$step = Join-Path $out 'step.json'
$runLog = Join-Path $out 'run.jsonl'
$lockedStep = Join-Path $out 'step-locked.json'
$lockedOut = Join-Path $out 'locked'
New-Item -ItemType Directory -Path $lockedOut | Out-Null
$script = Join-Path $out 'run.scr'
$stdout = Join-Path $out 'console.log'
$stderr = Join-Path $out 'console-error.log'
[ordered]@{ command='RunTests'; config=[ordered]@{ suitePath=$suite; outputFolder=$out; allowWrites=$true } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $step -Encoding UTF8
[ordered]@{ command='RunTests'; config=[ordered]@{ suitePath=$lockedSuite; outputFolder=$lockedOut; allowWrites=$true } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lockedStep -Encoding UTF8
# AutoLISP strings use forward slashes so Windows paths do not become escape sequences.
function Lisp-Path([string]$Path) {
    if ($Path.Contains('"')) { throw 'A path contains quotes.' }
    return $Path.Replace('\','/')
}
$lines = @(
    ('(load "' + (Lisp-Path $fixture) + '")'),
    ('(dhcb-qa-create "' + (Lisp-Path $out) + '")'),
    '_.NETLOAD', ('"' + $PluginPath + '"'),
    'DHCB_RUN', $step, $runLog, $copy,
    '(dhcb-qa-lock)',
    'DHCB_RUN', $lockedStep, $runLog, $copy,
    ('(dhcb-qa-check-final "' + (Lisp-Path $out) + '")'),
    '_.QUIT', '_Y'
)
# The plugin remains subject to the existing AutoCAD trust policy; no security setting is changed.
$lines | Set-Content -LiteralPath $script -Encoding ASCII
$process = Start-Process -FilePath $console -ArgumentList ('/i "' + $copy + '" /s "' + $script + '" /l en-US') -PassThru -WorkingDirectory $out -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$null = $process.Handle # Retain the native handle so Windows PowerShell can read ExitCode after exit.
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw ('Fixture checks timed out. See ' + $out)
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw ('Core Console exit code ' + $process.ExitCode + '. See ' + $out) }
    if ((Get-FileHash -LiteralPath $sample -Algorithm SHA256).Hash -ne $sampleHash) { throw 'Source sample hash changed.' }
    if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $sampleHash) { throw 'Disposable DWG was unexpectedly saved.' }
    $entries = @(Get-Content -LiteralPath $runLog -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
    if ($entries.Count -ne 2) { throw ('Expected both fixture suites to run. See ' + $out) }
    foreach ($entry in $entries) {
        $partial = $entry.PSObject.Properties["partialSuccess"] -and $entry.partialSuccess
        $hasErrors = $entry.PSObject.Properties["errors"] -and @($entry.errors).Count -gt 0
        if (-not $entry.success -or $partial -or $hasErrors) { throw ('RunTests failed. See ' + $out) }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $out 'fixture-final-ok.txt'))) { throw 'Final DBText/MText assertion did not complete.' }
    $expected = @(Import-Csv -LiteralPath (Join-Path $out 'fixture-expected.csv') -Encoding UTF8)
    if ($expected.Count -ne 3) { throw 'Expected three fixture block references.' }
    $baseline = @(Import-Csv -LiteralPath (Join-Path $out 'attributes-baseline.csv') -Encoding UTF8)
    $restored = @(Import-Csv -LiteralPath (Join-Path $out 'attributes-restored.csv') -Encoding UTF8)
    $locked = @(Import-Csv -LiteralPath (Join-Path $lockedOut 'attributes-locked.csv') -Encoding UTF8)
    if ($locked.Count -ne 6) { throw 'Expected exactly six locked fixture attributes.' }
    function Read-Attribute([object[]]$Rows, [string]$Handle, [string]$Tag) {
        $matches = @($Rows | Where-Object { $_.Handle -eq $Handle -and $_.AttributeTag -eq $Tag })
        if ($matches.Count -ne 1) { throw ('Expected one attribute for fixture handle/tag: ' + $Handle + '/' + $Tag) }
        return [string]$matches[0].AttributeValue
    }
    foreach ($file in @('attributes-numbered.csv','attributes-incremented.csv','attributes-imported.csv','attributes-restored.csv')) {
        if (@(Import-Csv -LiteralPath (Join-Path $out $file) -Encoding UTF8).Count -ne 6) { throw ('Expected exactly six exported fixture attributes: ' + $file) }
    }
    $numbered = @(Import-Csv -LiteralPath (Join-Path $out 'attributes-numbered.csv') -Encoding UTF8)
    $incremented = @(Import-Csv -LiteralPath (Join-Path $out 'attributes-incremented.csv') -Encoding UTF8)
    $imported = @(Import-Csv -LiteralPath (Join-Path $out 'attributes-imported.csv') -Encoding UTF8)
    foreach ($item in $expected) {
        if ((Read-Attribute $numbered $item.Handle 'NUM') -ne $item.Number) { throw 'Numbering order/value differs from fixture geometry.' }
        $pattern = 'CH-' + ([int]$item.Number.Substring(3) + 10).ToString('000')
        if ((Read-Attribute $incremented $item.Handle 'NUM') -ne $pattern) { throw 'Increment pattern/value differs from fixture expectation.' }
        $note = if ($item.InitialNote -eq 'DHCB-QA-OLD') { 'DHCB-QA-CSV, "quoted"' } else { $item.InitialNote }
        if ((Read-Attribute $imported $item.Handle 'NOTE') -ne $note) { throw 'CSV quoting/value import differs from fixture expectation.' }
        foreach ($tag in @('NUM','NOTE')) {
            if ((Read-Attribute $restored $item.Handle $tag) -ne (Read-Attribute $baseline $item.Handle $tag)) { throw 'Restored attributes differ from baseline.' }
            if ((Read-Attribute $locked $item.Handle $tag) -ne (Read-Attribute $baseline $item.Handle $tag)) { throw 'Locked attributes changed despite layer lock.' }
        }
    }
    $layers = @(Import-Csv -LiteralPath (Join-Path $lockedOut 'layers-final.csv') -Encoding UTF8)
    $badProperties = @($layers | Where-Object { $_.Name -eq 'DHCB-QA-BADPROPERTIES' })
    $badLine = @($layers | Where-Object { $_.Name -eq 'DHCB-QA-BADLINE' })
    if ($badProperties.Count -ne 1 -or $badProperties[0].Color -eq '999' -or $badProperties[0].Lineweight -eq '999' -or $badProperties[0].IsPlottable -ne 'true' -or $badProperties[0].Linetype -ne 'Continuous') {
        throw 'Invalid new-layer properties were not reported with the expected safe fallbacks.'
    }
    if ($badLine.Count -ne 1 -or $badLine[0].Linetype -ne 'Continuous') { throw 'Unavailable linetype fallback differs from reported Continuous.' }
    if (@($layers | Where-Object { $_.Name -eq 'DHCB-QA-ATTRONLY' }).Count -ne 1) { throw 'Attribute-only source layer was incorrectly removed.' }
    'PASS: exact geometry numbering, increment pattern, quoted CSV import, restored and locked attributes, retained attribute-only layer, safe property fallbacks, unchanged entity layers, DBText/MText formatting, and unchanged DWG hashes.' | Set-Content -LiteralPath (Join-Path $out 'independent-assertions.txt') -Encoding UTF8
    foreach ($entry in $entries) { Write-Output ('PASS: ' + $entry.summary) }
    Write-Output ('PASS: independent attribute/text/layer/format assertions; source and copied DWGs unchanged. Evidence: ' + $out)
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
