<#
.SYNOPSIS
  Audit NuGet and Python dependencies on Windows; fail on vulnerable packages or native tool errors.
#>
[CmdletBinding()]
param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'DHCB Tools requires Windows.' }
if (@($env:PATHEXT -split ';') -notcontains '.EXE') { $env:PATHEXT = '.COM;.EXE;.BAT;.CMD;' + $env:PATHEXT }
$propertyNames = @('RevitVersion', 'AcadVersion', 'AcadRuntime', 'PYTHONUTF8')
$previous = @{}
foreach ($name in $propertyNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$status = 0
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    foreach ($hostProfile in '2022:net48', '2023:net48', '2024:net48', '2025:net8', '2025:net10', '2026:net8', '2026:net10', '2027:net10') {
        $year, $runtime = $hostProfile.Split(':')
        $env:RevitVersion = $year
        $env:AcadVersion = $year
        $env:AcadRuntime = $runtime
        Write-Host "== NuGet (Revit/AutoCAD $year; AutoCAD $runtime)"
        & dotnet restore Dhcb-Tools.sln -nologo -v:q
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $report = (& dotnet list Dhcb-Tools.sln package --no-restore --vulnerable --include-transitive --format json | Out-String)
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $data = $report | ConvertFrom-Json
        if ($data.errors) { throw ('NuGet audit reported errors: ' + ($data.errors | ConvertTo-Json -Compress)) }
        $hits = @()
        foreach ($project in $data.projects) {
            foreach ($framework in $project.frameworks) {
                foreach ($kind in 'topLevelPackages', 'transitivePackages') {
                    foreach ($package in $framework.$kind) {
                        $advisories = @($package.vulnerabilities | ForEach-Object { $_.advisoryurl }) -join ', '
                        $hits += "  $($project.path) [$($framework.framework)] $($package.id) $($package.resolvedVersion): $advisories"
                    }
                }
            }
        }
        if ($hits.Count -gt 0) { $hits | ForEach-Object { Write-Host $_ }; $status = 1 }
        else { Write-Host '  No known vulnerable packages.' }
    }
    Write-Host '== Python (pip-audit)'
    # requirements include Vietnamese UTF-8 comments; do not decode them as the Windows code page.
    $env:PYTHONUTF8 = '1'
    & $Python -m pip_audit -r requirements-dev.txt --progress-spinner off
    if ($LASTEXITCODE -ne 0) { $status = 1 }
}
finally {
    foreach ($name in $propertyNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    Pop-Location
}
exit $status
