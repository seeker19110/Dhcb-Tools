# Shared preflight for the AutoCAD 2026 .NET 10 acceptance fixtures.
function Initialize-AcceptanceNativeTools {
    # Some terminal environments retain only .CPL. Restore executable recognition for
    # this process; do not edit user/machine environment variables.
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT -and
        @($env:PATHEXT -split ';') -notcontains '.EXE') {
        $env:PATHEXT = '.COM;.EXE;.BAT;.CMD;' + $env:PATHEXT
    }
}

function Get-AcceptanceHost([string]$AcadDirectory, [string]$DotNet) {
    Initialize-AcceptanceNativeTools
    $console = Join-Path $AcadDirectory 'accoreconsole.exe'
    $sample = Join-Path $AcadDirectory 'Sample\Mechanical Sample\Data Extraction and Multileaders Sample.dwg'
    $runtimePath = Join-Path $AcadDirectory 'acdbmgd.runtimeconfig.json'
    $apiPath = Join-Path $AcadDirectory 'AcDbMgd.dll'
    foreach ($path in @($console, $sample, $runtimePath, $apiPath, $DotNet)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required acceptance input missing: $path" }
    }
    $runtime = Get-Content -LiteralPath $runtimePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($runtime.runtimeOptions.tfm -ne 'net10.0') { throw 'Acceptance fixtures require AutoCAD 2026.1.2+ using .NET 10.' }
    $api = [Reflection.AssemblyName]::GetAssemblyName($apiPath).Version
    if ($api.Major -ne 25 -or $api.Minor -ne 1) { throw 'Acceptance fixtures target AutoCAD 2026 (API 25.1) only.' }
    return @{ Console = $console; Sample = $sample }
}

function Get-AcceptanceTargetDir([string]$Project, [string]$DotNet) {
    Initialize-AcceptanceNativeTools
    $target = & $DotNet msbuild $Project -nologo -p:Configuration=Release -p:AcadVersion=2026 -p:AcadRuntime=net10 -getProperty:TargetDir
    if ($LASTEXITCODE -ne 0 -or -not $target) { throw "MSBuild failed to evaluate TargetDir: $Project" }
    $target = ([string]($target -join "`n")).Trim()
    if (-not $target -or -not (Test-Path -LiteralPath $target -PathType Container)) { throw "MSBuild returned an invalid TargetDir: $Project ($target)" }
    return $target
}

function Assert-AcceptanceOutput([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Acceptance build output missing: $Path" }
}
