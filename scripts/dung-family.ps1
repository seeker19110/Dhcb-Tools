<#
.SYNOPSIS
    Dựng và nâng cấp thư viện family cho TỪNG phiên bản Revit (§63).

.DESCRIPTION
    File .rfa gắn chặt phiên bản và nâng cấp MỘT CHIỀU: Revit 2024 không mở nổi family lưu từ 2026, và
    không API nào hạ cấp được. Vì vậy muốn có thư viện dùng được cho nhiều phiên bản thì phải để CHÍNH
    phiên bản đó ghi ra file — script này làm đúng việc ấy, một lượt cho mỗi phiên bản:

      build add-in theo -p:RevitVersion → cài vào %APPDATA%\Autodesk\Revit\Addins\<năm>
      → BatchRunner tự mở Revit bằng journal → chạy FamilyStarter (dựng mới) và/hoặc FamilyUpgrade
      (nâng cấp .rfa có sẵn) → đóng Revit → gom kết quả vào <OutputRoot>\<năm>\.

    Bản gốc trong -SourceFolder KHÔNG bao giờ bị đụng: FamilyUpgrade từ chối chạy nếu thư mục đích nằm
    trong thư mục nguồn.

.EXAMPLE
    .\scripts\dung-family.ps1 -RevitVersions 2024,2026

.EXAMPLE
    .\scripts\dung-family.ps1 -RevitVersions 2026 -Mode Upgrade -SourceFolder D:\ThuVienFamily
#>
[CmdletBinding()]
param(
    # Các phiên bản Revit cần dựng. Máy phải có đúng bản đó và add-in build được cho nó.
    [int[]]$RevitVersions = @(2024),

    # Starter = dựng family mẫu DHCB_Sleeve/DHCB_Hanger từ template kèm Revit (cách 1).
    # Upgrade = mở .rfa trong -SourceFolder rồi lưu lại theo định dạng phiên bản đang chạy (cách 2).
    # Both    = cả hai; phần Upgrade lấy nguồn là -SourceFolder nếu có, không thì lấy family vừa dựng.
    [ValidateSet('Starter', 'Upgrade', 'Both')]
    [string]$Mode = 'Starter',

    # Thư mục .rfa nguồn cho phần Upgrade. Chỉ đọc.
    [string]$SourceFolder,

    # Nơi ghi kết quả; mỗi phiên bản một thư mục con.
    [string]$OutputRoot = "$PSScriptRoot\..\out\families",

    # Model .rvt để mở (batch Revit luôn cần một file); mặc định model mẫu kiến trúc kèm Revit.
    # Không bao giờ bị lưu: job đặt saveMode None.
    [string]$Model,

    # Ghi đè .rfa đã có trong thư mục đích.
    [switch]$Overwrite,

    # Chỉ xem trước: liệt kê sẽ dựng/nâng cấp gì, không ghi file nào.
    [switch]$DryRun,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

function Stop-WithMessage([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 2
}

if ($Mode -eq 'Upgrade' -and -not $SourceFolder) {
    Stop-WithMessage "-Mode Upgrade cần -SourceFolder (thư mục chứa .rfa nguồn)."
}
if ($SourceFolder) {
    if (-not (Test-Path $SourceFolder)) {
        Stop-WithMessage "Không có thư mục nguồn: $SourceFolder"
    }
    # Phải là đường dẫn tuyệt đối: config đi vào job rồi được đọc BÊN TRONG Revit, mà thư mục làm việc
    # của Revit không phải thư mục gọi script — đường tương đối ở đó thành "không có thư mục nguồn".
    $SourceFolder = (Resolve-Path $SourceFolder).Path
}

# Revit khoá DLL add-in khi đang chạy, và BatchRunner cần tự mở Revit của riêng nó.
$running = Get-Process Revit -ErrorAction SilentlyContinue
if ($running) {
    Stop-WithMessage "Revit đang chạy (PID $($running.Id -join ', ')) — đóng rồi chạy lại."
}

$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

if (-not $SkipBuild) {
    Write-Host "== Build BatchRunner"
    dotnet build (Join-Path $repo 'src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj') -c Release -nologo -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# TFM của BatchRunner hỏi MSBuild chứ không viết tay — đường dẫn cứng hỏng im lặng khi project đổi khung.
$runnerProj = Join-Path $repo 'src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj'
$runnerTfm = (& dotnet build $runnerProj -getProperty:TargetFramework).Trim()
$runner = Join-Path $repo "src\DhcbTools.BatchRunner\bin\Release\$runnerTfm\DhcbTools.BatchRunner.exe"
if (-not (Test-Path $runner)) { Stop-WithMessage "Không tìm thấy BatchRunner: $runner (bỏ -SkipBuild để build)" }

$failures = @()
foreach ($version in $RevitVersions) {
    Write-Host "`n########## Revit $version ##########" -ForegroundColor Cyan

    $revitExe = "C:\Program Files\Autodesk\Revit $version\Revit.exe"
    if (-not (Test-Path $revitExe)) {
        Write-Warning "Máy không có Revit $version ($revitExe) — bỏ qua."
        $failures += "${version}: không cài"
        continue
    }

    $modelForVersion = if ($Model) { $Model } else { "C:\Program Files\Autodesk\Revit $version\Samples\Snowdon Towers Sample Architectural.rvt" }
    if (-not (Test-Path $modelForVersion)) {
        Write-Warning "Không thấy model để mở: $modelForVersion — bỏ qua Revit $version."
        $failures += "${version}: thiếu model"
        continue
    }

    # ── Build + cài add-in cho ĐÚNG phiên bản ────────────────────────────────
    if (-not $SkipBuild) {
        Write-Host "== Build add-in cho Revit $version"
        dotnet build (Join-Path $repo 'src\DhcbTools.Revit\DhcbTools.Revit.csproj') -c Release -p:RevitVersion=$version -nologo -v:q -clp:ErrorsOnly
        if ($LASTEXITCODE -ne 0) { $failures += "${version}: build lỗi"; continue }
    }

    $tfm = if ($version -ge 2027) { 'net10.0-windows' } elseif ($version -ge 2025) { 'net8.0-windows' } else { 'net48' }
    $binDir = Join-Path $repo "src\DhcbTools.Revit\bin\Release\$tfm"
    if (-not (Test-Path $binDir)) { Stop-WithMessage "Không thấy bin add-in: $binDir" }
    $addinDir = "$env:APPDATA\Autodesk\Revit\Addins\$version"
    New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
    Get-ChildItem $binDir -Include *.dll, *.addin -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike '*RevitAPI*' } |
        ForEach-Object { Copy-Item $_.FullName $addinDir -Force }
    Write-Host "== Đã cài add-in vào $addinDir"

    # ── Thư mục kết quả của phiên bản này ────────────────────────────────────
    $outDir = Join-Path $OutputRoot "$version"
    $starterDir = Join-Path $outDir 'dung-moi'
    $upgradeDir = Join-Path $outDir 'nang-cap'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    # ── Dựng các bước job ────────────────────────────────────────────────────
    $steps = @()
    if ($Mode -ne 'Upgrade') {
        $steps += @{
            command = 'FamilyStarter'
            config  = [ordered]@{
                outputFolder = $starterDir -replace '\\', '/'
                load         = $false      # Chỉ cần file .rfa; không đụng model đang mở.
                overwrite    = [bool]$Overwrite
                dryRun       = [bool]$DryRun
            }
        }
    }
    if ($Mode -ne 'Starter') {
        # Không có -SourceFolder thì nâng cấp chính family vừa dựng: nội dung vô nghĩa nhưng chứng minh
        # được đường chạy — chỉ xảy ra khi người dùng chọn Both mà không đưa nguồn.
        $src = if ($SourceFolder) { $SourceFolder } else { $starterDir }
        $steps += @{
            command = 'FamilyUpgrade'
            config  = [ordered]@{
                sourceFolder = $src -replace '\\', '/'
                outputFolder = $upgradeDir -replace '\\', '/'
                recursive    = $true
                overwrite    = [bool]$Overwrite
                dryRun       = [bool]$DryRun
            }
            skipIfPreviousFailed = $false
        }
    }

    $job = [ordered]@{
        name         = "DHCB - dung family cho Revit $version"
        app          = 'revit'
        revitVersion = $version
        stopOnError  = $false
        saveMode     = 'None'      # Không lưu model mẫu — lệnh chỉ ghi .rfa ra ngoài.
        outputFolder = $outDir -replace '\\', '/'
        files        = @(@{ path = $modelForVersion -replace '\\', '/'; detachFromCentral = $true })
        steps        = $steps
    }

    $jobPath = Join-Path $outDir 'job.json'
    $job | ConvertTo-Json -Depth 8 | Set-Content $jobPath -Encoding UTF8
    Write-Host "== Job: $jobPath"

    Write-Host "== Chạy — Revit $version tự mở rồi tự đóng, đừng đụng vào máy lúc này`n"
    & $runner --job $jobPath --log-dir $outDir --max-minutes 30
    if ($LASTEXITCODE -ne 0) { $failures += "${version}: BatchRunner trả mã $LASTEXITCODE" }

    $rfa = @(Get-ChildItem $outDir -Filter *.rfa -Recurse -ErrorAction SilentlyContinue)
    Write-Host ("== Revit {0}: {1} file .rfa trong {2}" -f $version, $rfa.Count, $outDir)
    foreach ($f in $rfa) { Write-Host ("   " + $f.FullName.Substring($outDir.Length + 1)) }
    if ($rfa.Count -eq 0 -and -not $DryRun) { $failures += "${version}: không ra file .rfa nào" }
}

Write-Host "`n=================== TỔNG KẾT ==================="
Write-Host "Thư mục kết quả: $OutputRoot"
if ($failures.Count -gt 0) {
    Write-Host ("Có vấn đề: " + ($failures -join ' | ')) -ForegroundColor Yellow
    exit 1
}
Write-Host "Tất cả phiên bản chạy xong." -ForegroundColor Green
exit 0
