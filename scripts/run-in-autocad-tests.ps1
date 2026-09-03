<#
.SYNOPSIS
    Chạy bộ kiểm thử BÊN TRONG AutoCAD bằng accoreconsole (đối xứng với run-in-revit-tests.ps1).

.DESCRIPTION
    Một lệnh làm trọn vòng: build vỏ core-only + BatchRunner → dựng file job → chạy
    DhcbTools.BatchRunner (tự mở accoreconsole, NETLOAD plugin, chạy lệnh RunTests trên bản vẽ mẫu,
    đóng lại) → in báo cáo.

    Không cần đóng AutoCAD trước: accoreconsole là tiến trình riêng, không dùng chung DLL đang mở
    trong giao diện AutoCAD.

.EXAMPLE
    .\scripts\run-in-autocad-tests.ps1
    .\scripts\run-in-autocad-tests.ps1 -AcadVersion 2026 -Drawing "P:\du-an\KT-01.dwg"
#>
[CmdletBinding()]
param(
    # Phiên bản AutoCAD dùng để build và chạy. 2026.1+ là .NET 10.
    [int]$AcadVersion = 2026,

    # Bản vẽ .dwg để chạy; không đặt thì lấy bản vẽ mẫu kèm AutoCAD.
    [string]$Drawing,

    # Nơi ghi báo cáo và log.
    [string]$OutputRoot = "$env:USERPROFILE\DHCB-test-results",

    # Bỏ qua bước build (dùng lại bin/Release có sẵn).
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# Console Windows mặc định không phải UTF-8 nên tiếng Việt vỡ khi hiện báo cáo.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

function Stop-WithMessage([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 2
}

# ── 1. accoreconsole ─────────────────────────────────────────────────────────
$acadDir = "C:\Program Files\Autodesk\AutoCAD $AcadVersion"
$console = Join-Path $acadDir 'accoreconsole.exe'
if (-not (Test-Path $console)) {
    Stop-WithMessage "Không tìm thấy accoreconsole: $console"
}

# ── 2. Bản vẽ ────────────────────────────────────────────────────────────────
if (-not $Drawing) {
    $Drawing = Join-Path $acadDir 'Sample\Mechanical Sample\Data Extraction and Multileaders Sample.dwg'
}
if (-not (Test-Path $Drawing)) {
    Stop-WithMessage "Không tìm thấy bản vẽ: $Drawing"
}

$suitePath = Join-Path $repo 'tests\suites\autocad-smoke.json'
if (-not (Test-Path $suitePath)) {
    Stop-WithMessage "Không tìm thấy bộ ca kiểm: $suitePath"
}

Write-Host "Bộ ca kiểm : $suitePath"
Write-Host "Bản vẽ     : $Drawing"

# ── 3. Build ─────────────────────────────────────────────────────────────────
# Vỏ core-only (chỉ AcDbMgd/AcCoreMgd) là bản duy nhất NETLOAD chắc chắn được trong Core Console.
if (-not $SkipBuild) {
    Write-Host "`n== Build vỏ core-only AutoCAD $AcadVersion (Release)"
    dotnet build (Join-Path $repo 'src\DhcbTools.AutoCAD.Core\DhcbTools.AutoCAD.Core.csproj') `
        -c Release -p:AcadVersion=$AcadVersion -nologo -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "== Build BatchRunner"
    dotnet build (Join-Path $repo 'src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj') `
        -c Release -nologo -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# TFM theo bảng map trong Directory.Build.props: AutoCAD 2026.1+ (package 25.1.x) đã ở .NET 10.
$pluginDir = Join-Path $repo 'src\DhcbTools.AutoCAD.Core\bin\Release'
$plugin = Get-ChildItem $pluginDir -Recurse -Filter 'DhcbTools.AutoCAD.Core.dll' -ErrorAction SilentlyContinue |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $plugin) {
    Stop-WithMessage "Không tìm thấy DhcbTools.AutoCAD.Core.dll trong $pluginDir (bỏ -SkipBuild để build)"
}

# ── 4. Dựng file job ─────────────────────────────────────────────────────────
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$outDir = Join-Path $OutputRoot "autocad-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$job = [ordered]@{
    name         = 'DHCB - kiem thu trong AutoCAD'
    app          = 'autocad'
    stopOnError  = $false
    saveMode     = 'None'          # Không bao giờ lưu đè lên bản vẽ mẫu.
    outputFolder = $outDir -replace '\\', '/'
    files        = @(@{ path = $Drawing -replace '\\', '/' })
    steps        = @(@{
        command = 'RunTests'
        config  = [ordered]@{
            suitePath    = $suitePath -replace '\\', '/'
            outputFolder = '{outputFolder}'
        }
    })
}

$jobPath = Join-Path $outDir 'job.json'
$job | ConvertTo-Json -Depth 8 | Set-Content $jobPath -Encoding UTF8
Write-Host "== Job: $jobPath"

# ── 5. Chạy ──────────────────────────────────────────────────────────────────
$runner = Join-Path $repo 'src\DhcbTools.BatchRunner\bin\Release\net8.0\DhcbTools.BatchRunner.exe'
if (-not (Test-Path $runner)) {
    Stop-WithMessage "Không tìm thấy BatchRunner: $runner (bỏ -SkipBuild để build)"
}

Write-Host "`n== Chạy — accoreconsole mở bản vẽ, chạy bộ ca kiểm, đóng lại`n"
& $runner --job $jobPath --log-dir $outDir --plugin-dll $plugin.FullName --accoreconsole $console --max-minutes 20
$exit = $LASTEXITCODE

# ── 6. Báo cáo ───────────────────────────────────────────────────────────────
$report = Join-Path $outDir 'in-autocad-tests.md'
if (Test-Path $report) {
    Write-Host "`n=================== BÁO CÁO ==================="
    # File ghi UTF-8 không BOM; Windows PowerShell 5.1 mặc định đọc cp1252 nên tiếng Việt vỡ khi hiện ra.
    Get-Content $report -Encoding UTF8 | Write-Host
    Write-Host "===============================================`n"
    Write-Host "TRX: $(Join-Path $outDir 'in-autocad-tests.trx')"
} else {
    Write-Warning "Không thấy báo cáo $report — xem $outDir\*\acad-steps\*.log và $env:APPDATA\DHCB\logs"
}

Write-Host "Thư mục kết quả: $outDir"
exit $exit
