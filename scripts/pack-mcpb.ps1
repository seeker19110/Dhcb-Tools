<#
.SYNOPSIS
    Đóng gói MCP server thành file .mcpb cài được vào Claude Desktop bằng một cú mở file (giai đoạn 10.4).

.DESCRIPTION
    Chép server + client HTTP vào thư mục dựng tạm cùng manifest.json rồi gọi @anthropic-ai/mcpb.
    Không đóng gói add-in Revit: add-in là DLL .NET phải nằm trong thư mục add-in của Revit và chỉ nạp
    lúc Revit khởi động — nó đi theo installer riêng (installer/dhcb-tools.iss).

.EXAMPLE
    .\scripts\pack-mcpb.ps1
    .\scripts\pack-mcpb.ps1 -Version 1.0.0 -App autocad
#>
[CmdletBinding()]
param(
    [string]$Version = "0.9.0",

    [ValidateSet('revit', 'autocad')]
    [string]$App = 'revit',

    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'dist' }

$stage = Join-Path ([System.IO.Path]::GetTempPath()) "dhcb-mcpb-$App-$([guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'scripts') | Out-Null

# ── manifest: thay phiên bản và app cho khớp lần đóng gói này ────────────────
$manifest = Get-Content (Join-Path $repo 'tools\mcpb\manifest.json') -Raw | ConvertFrom-Json
$manifest.version = $Version
$manifest.name = "dhcb-$App"
$manifest.display_name = "DHCB Tools — $(if ($App -eq 'revit') { 'Revit' } else { 'AutoCAD' })"
$manifest.server.mcp_config.args = @("`${__dirname}/scripts/dhcb_mcp_server.py", $App)

# Bỏ khoá chú thích "_comment" — hợp lệ trong repo nhưng không thuộc schema manifest.
$manifest.PSObject.Properties.Remove('_comment')

# Set-Content -Encoding UTF8 của Windows PowerShell 5.1 ghi kèm BOM, mà trình đóng gói .mcpb từ chối
# JSON có BOM ("Unexpected token '\ufeff'"). Ghi bằng .NET với UTF8Encoding($false) để chắc chắn không BOM.
[System.IO.File]::WriteAllText(
    (Join-Path $stage 'manifest.json'),
    ($manifest | ConvertTo-Json -Depth 10),
    (New-Object System.Text.UTF8Encoding $false))

# ── mã nguồn server: chỉ hai file, không dependency ngoài ───────────────────
Copy-Item (Join-Path $repo 'scripts\dhcb_mcp_server.py') (Join-Path $stage 'scripts') -Force
Copy-Item (Join-Path $repo 'scripts\dhcb_agent.py')      (Join-Path $stage 'scripts') -Force
Copy-Item (Join-Path $repo 'tools\mcpb\README.md')       $stage -Force

Write-Host "Thư mục dựng: $stage"
Get-ChildItem $stage -Recurse -File | ForEach-Object { Write-Host "   $($_.FullName.Substring($stage.Length + 1))" }

# ── đóng gói ────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$output = Join-Path $OutputDir "dhcb-$App-$Version.mcpb"

Write-Host "`n== npx @anthropic-ai/mcpb pack"
npx --yes @anthropic-ai/mcpb pack $stage $output
if ($LASTEXITCODE -ne 0) {
    Write-Host "Đóng gói thất bại. Cần Node.js để chạy npx; cài Node rồi chạy lại." -ForegroundColor Red
    exit $LASTEXITCODE
}

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nXong: $output" -ForegroundColor Green
Write-Host "Cài: mở file này bằng Claude Desktop. Nhớ cài add-in trước (installer) để Bridge có mà nối."
