; Installer DHCB Tools — Inno Setup 6.
;
; Trước đây release.yml chỉ ra zip và người dùng phải tự chép DLL vào đúng thư mục theo năm Revit;
; sai một bước là add-in không nạp mà không có thông báo gì. Installer này đặt đúng chỗ cho từng
; phiên bản, cài theo NGƯỜI DÙNG (không cần quyền admin, đúng cách kiểm thử thật trên Revit 2024).
;
; Dựng gói:
;   iscc /DVersion=1.0.0 /DStageDir=..\dist\stage installer\dhcb-tools.iss
;
; StageDir phải có cấu trúc (release.yml dựng sẵn):
;   revit-2023\  revit-2024\  revit-2025\   (DLL + .addin cho từng phiên bản)
;   autocad-2024\ autocad-2025\             (DLL vỏ đầy đủ + vỏ core-only)
;   batchrunner\                            (exe + jobs/ configs/ scripts/)

#ifndef Version
  #define Version "0.0.0-dev"
#endif
#ifndef StageDir
  #define StageDir "..\dist\stage"
#endif

[Setup]
AppId={{B4E7C2A9-3D51-4F86-9A2C-7E0D5B1F8C43}
AppName=DHCB Tools
AppVersion={#Version}
AppPublisher=DHCB
AppPublisherURL=https://github.com/seeker19110/Dhcb-Tools
DefaultDirName={localappdata}\Programs\DHCB Tools
DefaultGroupName=DHCB Tools
OutputDir=..\dist
OutputBaseFilename=DhcbTools-Setup-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Cài theo người dùng: không cần admin, và đúng thư mục %APPDATA% mà Revit/AutoCAD đọc add-in của user.
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName=DHCB Tools {#Version}

[Types]
Name: "full"; Description: "Cài tất cả"
Name: "custom"; Description: "Tự chọn"; Flags: iscustom

[Components]
Name: "revit2023";  Description: "Add-in Revit 2023";      Types: full
Name: "revit2024";  Description: "Add-in Revit 2024";      Types: full
Name: "revit2025";  Description: "Add-in Revit 2025";      Types: full
Name: "acad2024";   Description: "Plugin AutoCAD 2024";    Types: full
Name: "acad2025";   Description: "Plugin AutoCAD 2025";    Types: full
Name: "batch";      Description: "Batch runner chạy đêm";  Types: full

[Files]
; ── Revit: %APPDATA%\Autodesk\Revit\Addins\<năm>\ ────────────────────────────
Source: "{#StageDir}\revit-2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Excludes: "*.md"; \
  Components: revit2023; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; Excludes: "*.md"; \
  Components: revit2024; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Excludes: "*.md"; \
  Components: revit2025; Flags: ignoreversion recursesubdirs createallsubdirs

; ── AutoCAD: bundle tự nạp trong %APPDATA%\Autodesk\ApplicationPlugins\ ──────
Source: "{#StageDir}\autocad-2024\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2024"; Excludes: "*.md"; \
  Components: acad2024; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\autocad-2025\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2025"; Excludes: "*.md"; \
  Components: acad2025; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\PackageContents.xml"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle"; \
  Components: acad2024 or acad2025; Flags: ignoreversion

; ── Batch runner: thư mục chương trình bình thường ──────────────────────────
Source: "{#StageDir}\batchrunner\*"; DestDir: "{app}"; \
  Components: batch; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Tạo sẵn để shortcut log không trỏ vào thư mục chưa tồn tại (add-in tự tạo khi ghi dòng đầu tiên).
Name: "{userappdata}\DHCB\logs"

[Icons]
Name: "{group}\Thư mục batch runner"; Filename: "{app}"; Components: batch
Name: "{group}\Thư mục log DHCB";     Filename: "{userappdata}\DHCB\logs"
Name: "{group}\Gỡ cài đặt DHCB Tools"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}"; Description: "Mở thư mục batch runner"; \
  Flags: postinstall shellexec skipifsilent unchecked; Components: batch

[UninstallDelete]
; Bundle AutoCAD: xoá cả thư mục để AutoCAD không còn thấy plugin.
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Revit hỏi "Unsigned Add-In" ở lần mở đầu tiên — nói trước để kỹ sư không tưởng là lỗi.
    MsgBox('Đã cài xong.' + #13#10 + #13#10 +
           'Lần đầu mở Revit sẽ có hộp thoại "Unsigned Add-In" — chọn "Always Load".' + #13#10 +
           'AutoCAD: plugin tự nạp khi khởi động (không cần NETLOAD).' + #13#10 + #13#10 +
           'Log nằm ở %APPDATA%\DHCB\logs.', mbInformation, MB_OK);
  end;
end;
