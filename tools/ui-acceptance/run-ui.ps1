<# Test actual Windows WPF controls with a recording backend. AutoCAD database acceptance runs separately. #>
[CmdletBinding()]
param([string]$OutputRoot = "$env:USERPROFILE\DHCB-test-results")
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'acceptance\HostPreflight.ps1')
Initialize-AcceptanceNativeTools
$project = Join-Path $PSScriptRoot 'DhcbTools.AutoCAD.UI.Acceptance.csproj'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
& $dotnet build $project -c Release -p:AcadVersion=2026 -p:AcadRuntime=net10 -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Windows UI acceptance build failed.' }
$target = & $dotnet msbuild $project -nologo -p:Configuration=Release -p:AcadVersion=2026 -p:AcadRuntime=net10 -getProperty:TargetPath
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($target)) { throw 'Cannot resolve Windows UI acceptance output.' }
$out = Join-Path $OutputRoot ('ui-readiness-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
& $dotnet $target.Trim() $out
if ($LASTEXITCODE -ne 0) { throw ('Windows UI acceptance failed: ' + $out) }
Write-Output ('Windows UI acceptance report: ' + (Join-Path $out 'ui.json'))
