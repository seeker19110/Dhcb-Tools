<#
.SYNOPSIS
  Build and test DHCB Tools on Windows, including the complete Revit/AutoCAD WPF interfaces.
.EXAMPLE
  .\scripts\check-build.ps1 -RevitVersion 2027 -AcadVersion 2026 -AcadRuntime net10
#>
[CmdletBinding()]
param(
    [ValidateSet(2027)] [int]$RevitVersion = 2027,
    [ValidateSet(2025, 2026, 2027)] [int]$AcadVersion = 2026,
    [ValidateSet('Auto', 'net10')] [string]$AcadRuntime = 'Auto',
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'DHCB Tools requires Windows.' }
if (@($env:PATHEXT -split ';') -notcontains '.EXE') { $env:PATHEXT = '.COM;.EXE;.BAT;.CMD;' + $env:PATHEXT }
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    if (-not $SkipTests) {
        foreach ($test in 'Shared.Logic', 'BatchRunner') {
            & dotnet test "tests/DhcbTools.$test.Tests/DhcbTools.$test.Tests.csproj" -c $Configuration -nologo
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
    & dotnet build src/DhcbTools.BatchRunner/DhcbTools.BatchRunner.csproj -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $common = @("-p:RevitVersion=$RevitVersion", "-p:AcadVersion=$AcadVersion", '-c', $Configuration, '-nologo')
    if ($AcadRuntime -ne 'Auto') { $common += "-p:AcadRuntime=$AcadRuntime" }
    foreach ($hostProject in 'Core', 'Core.AutoCAD', 'Revit', 'AutoCAD', 'AutoCAD.Core') {
        & dotnet build "src/DhcbTools.$hostProject/DhcbTools.$hostProject.csproj" @common
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    Write-Host 'All Windows builds passed.'
}
finally { Pop-Location }
