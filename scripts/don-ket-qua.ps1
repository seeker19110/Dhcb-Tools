<#
.SYNOPSIS
  Dọn thư mục kết quả chạy thật (mặc định %USERPROFILE%\DHCB-test-results) theo chính sách giữ lại.

.DESCRIPTION
  Mỗi lượt `run-in-revit-tests.ps1` / `run-in-autocad-tests.ps1` tạo một thư mục `<bộ>-yyyy-MM-dd_HH-mm-ss`.
  Phần nặng là `ban-chep\` (bản chép model .rvt + các model liên kết, 270–400 MB một lượt); phần có giá trị
  bằng chứng (log jsonl có chuỗi băm, report.html, csv) chỉ vài trăm KB. Sau 41 lượt thư mục đã 1,8 GB
  (đánh giá 2026-09-06, §54).

  Chính sách:
    - Với mỗi bộ (phần tên trước dấu thời gian): `KeepCopies` lượt mới nhất giữ nguyên cả bản chép model.
    - Lượt sau đó: xoá riêng `ban-chep\` (và `ifc\` nếu -DropIfc) — log/report giữ lại để đối chiếu.
    - Ngoài `KeepPerSuite` lượt mới nhất, lượt cũ hơn `MaxAgeDays` ngày: xoá cả thư mục.
    - Thư mục KHÔNG khớp mẫu `<bộ>-yyyy-MM-dd_HH-mm-ss` (ban-giao-A, doi-chieu-setout, engineer-trial…)
      không bao giờ bị đụng — chỉ liệt kê.

  Mặc định chỉ XEM TRƯỚC (như `dryRun` của mọi lệnh DHCB). Thêm -Apply để xoá thật.

.EXAMPLE
  .\scripts\don-ket-qua.ps1                       # xem trước
  .\scripts\don-ket-qua.ps1 -Apply                # dọn thật: bản chép chỉ ở lượt mới nhất mỗi bộ, xoá lượt > 14 ngày
  .\scripts\don-ket-qua.ps1 -KeepPerSuite 1 -MaxAgeDays 7 -DropIfc -Apply
#>
[CmdletBinding()]
param(
    [string]$Root = "$env:USERPROFILE\DHCB-test-results",
    [int]$KeepCopies = 1,
    [int]$KeepPerSuite = 2,
    [int]$MaxAgeDays = 14,
    [switch]$DropIfc,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
# Console cp1252 trên Windows làm tiếng Việt thành '?' (bẫy §48 của check-coverage.py) — ép UTF-8.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
if (-not (Test-Path -LiteralPath $Root)) { Write-Host "Không có thư mục $Root"; exit 2 }

$pattern = '^(?<suite>.+?)-(?<ts>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})$'
$now = Get-Date
$runs = @(); $other = @()
foreach ($d in Get-ChildItem -LiteralPath $Root -Directory) {
    $m = [regex]::Match($d.Name, $pattern)
    if ($m.Success) {
        $ts = [datetime]::ParseExact($m.Groups['ts'].Value, 'yyyy-MM-dd_HH-mm-ss', $null)
        $runs += [pscustomobject]@{ Dir = $d; Suite = $m.Groups['suite'].Value; Time = $ts }
    } else { $other += $d }
}

function Size-MB($path) {
    if (-not (Test-Path -LiteralPath $path)) { return 0 }
    $b = (Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    [math]::Round(($b / 1MB), 1)
}

$plan = @()
foreach ($g in ($runs | Group-Object Suite)) {
    $sorted = $g.Group | Sort-Object Time -Descending
    $i = 0
    foreach ($r in $sorted) {
        $i++
        if ($i -le $KeepCopies) { $plan += [pscustomobject]@{ Action = 'giữ'; Path = $r.Dir.FullName; MB = 0 }; continue }
        $age = ($now - $r.Time).TotalDays
        if ($i -gt $KeepPerSuite -and $age -gt $MaxAgeDays) {
            $plan += [pscustomobject]@{ Action = 'xoá lượt'; Path = $r.Dir.FullName; MB = (Size-MB $r.Dir.FullName) }
            continue
        }
        foreach ($sub in @('ban-chep') + $(if ($DropIfc) { @('ifc') } else { @() })) {
            $p = Join-Path $r.Dir.FullName $sub
            if (Test-Path -LiteralPath $p) {
                $plan += [pscustomobject]@{ Action = "xoá $sub"; Path = $p; MB = (Size-MB $p) }
            }
        }
    }
}

$total = [double](($plan | Where-Object Action -ne 'giữ' | Measure-Object MB -Sum).Sum)
Write-Host ("{0} — {1} lượt trong {2} bộ, {3} thư mục khác không đụng tới." -f $Root, $runs.Count, ($runs | Group-Object Suite).Count, $other.Count)
$plan | Where-Object Action -ne 'giữ' | ForEach-Object { Write-Host ("  {0,-14} {1,8:n1} MB  {2}" -f $_.Action, $_.MB, $_.Path) }
Write-Host ("Sẽ giải phóng ≈ {0:n0} MB." -f $total)

if (-not $Apply) { Write-Host "[Xem trước] Thêm -Apply để xoá thật."; exit 0 }
foreach ($p in ($plan | Where-Object Action -ne 'giữ')) {
    Remove-Item -LiteralPath $p.Path -Recurse -Force
}
Write-Host ("Đã xoá {0} mục, ≈ {1:n0} MB." -f (@($plan | Where-Object Action -ne 'giữ').Count), $total)
exit 0
