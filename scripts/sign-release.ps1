<#
.SYNOPSIS
    Ký Authenticode các DLL/EXE của DHCB trong một thư mục bằng chứng chỉ của tổ chức — dùng trong release.yml.

.DESCRIPTION
    Chứng chỉ đọc từ hai biến môi trường, do secret của repo cấp:
      DHCB_SIGN_PFX_BASE64    nội dung file .pfx mã base64
      DHCB_SIGN_PFX_PASSWORD  mật khẩu của .pfx
    Không có DHCB_SIGN_PFX_BASE64 → build dev in một dòng rồi thoát 0; -RequireSignature trả lỗi.
    Release từ tag luôn bật -RequireSignature và yêu cầu trạng thái chữ ký Valid.

    Chỉ ký file của DHCB (DhcbTools*.dll, DhcbTools*.exe) — Newtonsoft.Json.dll đã có chữ ký của nhà phát hành,
    ký đè là mất chữ ký gốc. Khác scripts/sign-addin.ps1 (ký bản cài trên máy dev, được tự tạo chứng chỉ): script
    này KHÔNG bao giờ tự tạo chứng chỉ, và file .pfx tạm bị xoá ngay sau khi ký.

.EXAMPLE
    ./scripts/sign-release.ps1 -Path dist
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'

if (-not $env:DHCB_SIGN_PFX_BASE64) {
    if ($RequireSignature) { throw "Ban phat hanh yeu cau chung chi ky; chua cau hinh DHCB_SIGN_PFX_BASE64." }
    Write-Host "Chua cau hinh chung chi ky (secret DHCB_SIGN_PFX_BASE64) - bo qua ky so, goi phat hanh KHONG ky."
    exit 0
}

$files = @(Get-ChildItem -Path $Path -Recurse -File -Include 'DhcbTools*.dll', 'DhcbTools*.exe')
if ($files.Count -eq 0) {
    throw "Khong co file DhcbTools*.dll/*.exe nao trong $Path - sai duong dan dong goi?"
}

$pfx = Join-Path ([IO.Path]::GetTempPath()) ("dhcb-sign-" + [guid]::NewGuid().ToString('N') + ".pfx")
try {
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:DHCB_SIGN_PFX_BASE64))
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx, $env:DHCB_SIGN_PFX_PASSWORD)
    if (-not $cert.HasPrivateKey) {
        throw "File .pfx khong chua khoa rieng - khong ky duoc."
    }

    foreach ($file in $files) {
        $result = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert -TimestampServer $TimestampServer -HashAlgorithm SHA256
        # Build dev có thể dùng chứng chỉ nội bộ; bản tag yêu cầu máy runner xác minh Valid.
        if (-not $result.SignerCertificate -or $result.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
            throw "Ky that bai: $($file.Name) - $($result.StatusMessage)"
        }
        if ($RequireSignature -and $result.Status -ne 'Valid') {
            throw "Chu ky khong duoc xac minh: $($file.Name) ($($result.Status))."
        }
        Write-Host "Da ky: $($file.Name) ($($result.Status))"
    }
}
finally {
    Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue
}
