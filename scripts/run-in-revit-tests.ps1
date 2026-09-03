<#
.SYNOPSIS
    Cài add-in vừa build rồi chạy bộ kiểm thử BÊN TRONG Revit (giai đoạn 8.3/8.4).

.DESCRIPTION
    Một lệnh làm trọn vòng: build Release → copy vào thư mục add-in của người dùng → dựng file job
    → chạy DhcbTools.BatchRunner (tự mở Revit bằng journal, chạy bộ ca kiểm, đóng Revit) → in báo cáo.

    Revit khoá DLL khi đang chạy, nên script DỪNG NGAY nếu thấy Revit mở — đóng Revit rồi chạy lại.

.EXAMPLE
    .\scripts\run-in-revit-tests.ps1
    .\scripts\run-in-revit-tests.ps1 -Suite mep -RevitVersion 2024
#>
[CmdletBinding()]
param(
    # Bộ ca kiểm: "smoke" (model kiến trúc), "mep" (model HVAC) hoặc "plumbing" (model cấp thoát nước).
    [ValidateSet('smoke', 'mep', 'plumbing')]
    [string]$Suite = 'smoke',

    [int]$RevitVersion = 2024,

    # Model .rvt để chạy; không đặt thì lấy model mẫu kèm Revit theo bộ ca kiểm.
    [string]$Model,

    # Nơi ghi báo cáo và log.
    [string]$OutputRoot = "$env:USERPROFILE\DHCB-test-results",

    # Bỏ qua bước build (dùng lại bin/Release có sẵn).
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# Console Windows mặc định không phải UTF-8 nên tiếng Việt vỡ — đúng lỗi đã gặp ở dhcb_agent.py.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

function Stop-WithMessage([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 2
}

# ── 1. Revit phải đóng: DLL bị khoá khi Revit chạy ───────────────────────────
# Chờ trước khi bỏ cuộc: chạy ba bộ ca kiểm nối đuôi nhau thì tiến trình Revit của lượt trước còn
# vài chục giây mới biến mất (BatchRunner đã "kết thúc tiến trình", nhưng Windows dọn không tức thì).
# Bản trước bỏ cuộc ngay, nên lượt 2 và 3 chưa bao giờ chạy được trong cùng một lệnh.
$deadline = (Get-Date).AddSeconds(120)
$running = Get-Process Revit -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Revit còn chạy (PID $($running.Id -join ', ')) — chờ tối đa 120 s để nó đóng hẳn..."
}
while ($running -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $running = Get-Process Revit -ErrorAction SilentlyContinue
}
if ($running) {
    Stop-WithMessage ("Revit vẫn đang chạy (PID $($running.Id -join ', ')) sau 120 s. Đóng Revit rồi chạy lại — " +
                      "DLL add-in bị khoá khi Revit mở, và batch runner cần tự mở Revit của riêng nó.")
}

# ── 2. Model ─────────────────────────────────────────────────────────────────
if (-not $Model) {
    $samples = "C:\Program Files\Autodesk\Revit $RevitVersion\Samples"
    $Model = switch ($Suite) {
        'mep'      { Join-Path $samples 'Snowdon Towers Sample HVAC.rvt' }
        'plumbing' { Join-Path $samples 'Snowdon Towers Sample Plumbing.rvt' }
        default    { Join-Path $samples 'Snowdon Towers Sample Architectural.rvt' }
    }
}
if (-not (Test-Path $Model)) {
    Stop-WithMessage "Không tìm thấy model: $Model"
}

$suitePath = Join-Path $repo "tests\suites\revit-$Suite.json"
if (-not (Test-Path $suitePath)) {
    Stop-WithMessage "Không tìm thấy bộ ca kiểm: $suitePath"
}

Write-Host "Bộ ca kiểm : $suitePath"
Write-Host "Model      : $Model"

# ── 3. Build ─────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "`n== Build add-in Revit $RevitVersion (Release, có WPF)"
    dotnet build (Join-Path $repo 'src\DhcbTools.Revit\DhcbTools.Revit.csproj') `
        -c Release -p:RevitVersion=$RevitVersion -nologo -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "== Build BatchRunner"
    dotnet build (Join-Path $repo 'src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj') `
        -c Release -nologo -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# ── 4. Cài add-in vào thư mục của người dùng ─────────────────────────────────
$tfm = if ($RevitVersion -ge 2025) { 'net8.0-windows' } else { 'net48' }
$binDir = Join-Path $repo "src\DhcbTools.Revit\bin\Release\$tfm"
$addinDir = "$env:APPDATA\Autodesk\Revit\Addins\$RevitVersion"

Write-Host "`n== Cài vào $addinDir"
New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
Get-ChildItem $binDir -Include *.dll, *.addin -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike '*RevitAPI*' } |
    ForEach-Object { Copy-Item $_.FullName $addinDir -Force }
Write-Host ("   " + ((Get-ChildItem $addinDir -Filter *.dll).Name -join ', '))

# ── 5. Dựng file job ─────────────────────────────────────────────────────────
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$outDir = Join-Path $OutputRoot "$Suite-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$job = [ordered]@{
    name         = "DHCB - kiem thu trong Revit ($Suite)"
    app          = 'revit'
    revitVersion = $RevitVersion
    stopOnError  = $false
    saveMode     = 'None'          # Không bao giờ lưu đè lên model mẫu.
    outputFolder = $outDir -replace '\\', '/'
    files        = @(@{ path = $Model -replace '\\', '/'; detachFromCentral = $true })
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

# ── 6. Chạy ──────────────────────────────────────────────────────────────────
$runner = Join-Path $repo 'src\DhcbTools.BatchRunner\bin\Release\net8.0\DhcbTools.BatchRunner.exe'
if (-not (Test-Path $runner)) {
    Stop-WithMessage "Không tìm thấy BatchRunner: $runner (bỏ -SkipBuild để build)"
}

# Hộp thoại "Security - Unsigned Add-In" chặn việc nạp add-in, và journal không tắt được loại này.
# Revit nhớ lựa chọn "Always Load" theo chữ ký của DLL, nên MỖI LẦN BUILD LẠI là hỏi lại — tức batch
# không người trực không bao giờ chạy được nếu chưa xử lý. Theo dõi song song để báo ngay, thay vì để
# runner ngồi chờ hết giờ (vòng kiểm thử đầu tiên treo 10 phút rưỡi đúng vì chuyện này).
$watcher = Start-Job -ScriptBlock {
    for ($i = 0; $i -lt 360; $i++) {
        Start-Sleep -Seconds 5
        $dlg = Get-Process -Name Revit -ErrorAction SilentlyContinue |
               Where-Object { $_.MainWindowTitle -like "*Unsigned Add-In*" }
        if ($dlg) { return $dlg[0].MainWindowTitle }
    }
    return $null
}

Write-Host "`n== Chạy — Revit sẽ tự mở rồi tự đóng, đừng đụng vào máy trong lúc này`n"
& $runner --job $jobPath --log-dir $outDir --max-minutes 30
$exit = $LASTEXITCODE

$blocking = Receive-Job $watcher -ErrorAction SilentlyContinue
Stop-Job $watcher -ErrorAction SilentlyContinue
Remove-Job $watcher -Force -ErrorAction SilentlyContinue
if ($blocking) {
    Write-Host ""
    Write-Host "!! Revit dung o hop thoai: $blocking" -ForegroundColor Yellow
    Write-Host "   Add-in chua ky so nen Revit hoi truoc khi nap, va hoi LAI sau moi lan build." -ForegroundColor Yellow
    Write-Host "   Xu ly: mo Revit $RevitVersion bang tay, chon 'Always Load', dong Revit, chay lai." -ForegroundColor Yellow
    Write-Host "   Ben hon: ky so DhcbTools.Revit.dll - xem docs/kiem-thu-trong-revit.md." -ForegroundColor Yellow
}

# ── 7. Báo cáo ───────────────────────────────────────────────────────────────
$report = Join-Path $outDir 'in-revit-tests.md'
if (Test-Path $report) {
    Write-Host "`n=================== BÁO CÁO ==================="
    # File bao cao ghi UTF-8 khong BOM; Windows PowerShell 5.1 mac dinh doc cp1252 nen tieng Viet
    # vo het ("kiem thu" thanh "kiá»ƒm thá»­"). File tren dia van dung — chi khau hien thi sai.
    Get-Content $report -Encoding UTF8 | Write-Host
    Write-Host "===============================================`n"
    Write-Host "TRX: $(Join-Path $outDir 'in-revit-tests.trx')"
} else {
    Write-Warning "Không thấy báo cáo $report — xem $outDir\batch-error.txt và $env:APPDATA\DHCB\logs"
}

Write-Host "Thư mục kết quả: $outDir"
exit $exit
