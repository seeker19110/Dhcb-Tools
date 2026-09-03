<#
.SYNOPSIS
    Ký số các DLL của add-in để Revit không hỏi "Security - Unsigned Add-In" nữa.

.DESCRIPTION
    Vì sao cần: Revit hỏi trước khi nạp add-in chưa ký, và nhớ lựa chọn "Always Load" theo CHỮ KÝ của
    file — nên mỗi lần build lại là hỏi lại. Hộp thoại đó chặn hẳn việc nạp add-in, journal không tắt
    được, nên batch chạy đêm KHÔNG BAO GIỜ chạy được nếu DLL chưa ký. Vòng kiểm thử thật đầu tiên
    (2026-09-03) treo 10 phút rưỡi đúng vì chuyện này.

    Mặc định script dùng chứng chỉ TỰ KÝ:
      - Đủ để Revit trên MÁY NÀY coi add-in là đã ký và tin cậy (chứng chỉ được cài vào kho của
        người dùng hiện tại, không cần admin).
      - KHÔNG đủ để phát hành cho máy khác — máy đó sẽ lại thấy add-in chưa tin cậy. Muốn phát hành
        thật thì cần chứng chỉ code-signing thương mại (OV/EV) rồi truyền vào bằng -PfxPath.

.EXAMPLE
    .\scripts\sign-addin.ps1
    .\scripts\sign-addin.ps1 -RevitVersion 2024
    .\scripts\sign-addin.ps1 -PfxPath C:\certs\dhcb.pfx -PfxPassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [int]$RevitVersion = 2024,

    # Thư mục chứa DLL cần ký; mặc định là thư mục add-in đã cài của người dùng.
    [string]$Path,

    # Chứng chỉ thương mại (.pfx). Bỏ trống = dùng/ tạo chứng chỉ tự ký.
    [string]$PfxPath,
    [System.Security.SecureString]$PfxPassword,

    # Tên chủ thể của chứng chỉ tự ký.
    [string]$CertSubject = 'CN=DHCB Tools Code Signing',

    # Máy chủ timestamp; chữ ký còn hiệu lực sau khi chứng chỉ hết hạn. Bỏ qua nếu không có mạng.
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

if (-not $Path) {
    $Path = "$env:APPDATA\Autodesk\Revit\Addins\$RevitVersion"
}
if (-not (Test-Path $Path)) {
    Write-Host "Khong tim thay thu muc: $Path" -ForegroundColor Red
    Write-Host "Cai add-in truoc (scripts\run-in-revit-tests.ps1 hoac installer)."
    exit 2
}

# ── 1. Lấy chứng chỉ ────────────────────────────────────────────────────────
if ($PfxPath) {
    if (-not (Test-Path $PfxPath)) { Write-Host "Khong thay .pfx: $PfxPath" -ForegroundColor Red; exit 2 }
    $cert = Get-PfxCertificate -FilePath $PfxPath -Password $PfxPassword
    Write-Host "Dung chung chi tu file: $($cert.Subject)"
}
else {
    # Tìm lại chứng chỉ tự ký cũ để chữ ký ổn định giữa các lần build — nếu tạo mới mỗi lần thì
    # Revit lại coi là nhà phát hành khác và hỏi lại, đúng cái đang muốn tránh.
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -EA SilentlyContinue |
            Where-Object { $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
            Sort-Object NotAfter -Descending | Select-Object -First 1

    if ($cert) {
        Write-Host "Dung lai chung chi tu ky co san (het han $($cert.NotAfter.ToString('yyyy-MM-dd')))"
    }
    else {
        Write-Host "Tao chung chi tu ky moi: $CertSubject"
        $cert = New-SelfSignedCertificate `
            -Subject $CertSubject `
            -Type CodeSigningCert `
            -KeyUsage DigitalSignature `
            -KeyAlgorithm RSA -KeyLength 3072 `
            -CertStoreLocation Cert:\CurrentUser\My `
            -NotAfter (Get-Date).AddYears(5)
    }

    # Chung chi tu ky phai nam trong kho Root + TrustedPublisher thi WinVerifyTrust moi coi la hop le.
    #
    # PHAI dung kho LocalMachine, khong dung CurrentUser: tu Windows 8, root do NGUOI DUNG tu them vao
    # CurrentUser\Root khong duoc trust provider chap nhan cho Authenticode. Da thu: chu ky van gan duoc
    # nhung Get-AuthenticodeSignature bao "A certificate chain processed, but terminated in a root
    # certificate which is not trusted by the trust provider". Cai vao LocalMachine can quyen admin.
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdmin) {
        Write-Host "Can quyen Administrator de cai chung chi tu ky vao kho LocalMachine." -ForegroundColor Red
        Write-Host "Mo PowerShell bang 'Run as administrator' roi chay lai script nay."
        exit 2
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) "dhcb-cert-$($cert.Thumbprint).cer"
    [IO.File]::WriteAllBytes($tmp, $cert.RawData)
    try {
        foreach ($store in 'Root', 'TrustedPublisher') {
            $already = Get-ChildItem "Cert:\LocalMachine\$store" -EA SilentlyContinue |
                       Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
            if ($already) {
                Write-Host "   Chung chi da co trong LocalMachine\$store"
            }
            else {
                Import-Certificate -FilePath $tmp -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
                Write-Host "   Da cai chung chi vao LocalMachine\$store"
            }
        }
    }
    finally {
        Remove-Item $tmp -Force -EA SilentlyContinue
    }
}

# ── 2. Ký ───────────────────────────────────────────────────────────────────
$targets = Get-ChildItem $Path -Filter 'DhcbTools*.dll' -File
if ($targets.Count -eq 0) {
    Write-Host "Khong co DhcbTools*.dll nao trong $Path" -ForegroundColor Red
    exit 2
}

Write-Host "`n== Ky $($targets.Count) file trong $Path"
$failed = 0
foreach ($file in $targets) {
    $args = @{ FilePath = $file.FullName; Certificate = $cert; HashAlgorithm = 'SHA256' }
    if ($TimestampServer) { $args.TimestampServer = $TimestampServer }

    try {
        $result = Set-AuthenticodeSignature @args
    }
    catch {
        # Khong co mang thi timestamp that bai; van ky duoc, chi la chu ky het hieu luc khi chung chi het han.
        Write-Host "   (timestamp that bai, ky khong timestamp) $($_.Exception.Message)" -ForegroundColor DarkYellow
        $args.Remove('TimestampServer')
        $result = Set-AuthenticodeSignature @args
    }

    if ($result.Status -eq 'Valid') {
        Write-Host "   OK    $($file.Name)"
    }
    else {
        Write-Host "   LOI   $($file.Name): $($result.Status) - $($result.StatusMessage)" -ForegroundColor Red
        $failed++
    }
}

# ── 3. Kiểm lại ─────────────────────────────────────────────────────────────
Write-Host "`n== Kiem lai"
foreach ($file in $targets) {
    $sig = Get-AuthenticodeSignature $file.FullName
    Write-Host ("   {0,-32} {1}" -f $file.Name, $sig.Status)
    if ($sig.Status -ne 'Valid') { $failed++ }
}

if ($failed -gt 0) {
    Write-Host "`nCo $failed file chua ky duoc." -ForegroundColor Red
    exit 1
}

Write-Host "`nXong. Mo Revit $RevitVersion mot lan de xac nhan khong con hop thoai 'Unsigned Add-In'." -ForegroundColor Green
if (-not $PfxPath) {
    Write-Host "Luu y: chung chi TU KY chi duoc tin tren may nay. Phat hanh cho may khac can chung chi thuong mai (-PfxPath)." -ForegroundColor Yellow
}
