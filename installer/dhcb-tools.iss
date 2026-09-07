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
;   revit-2023\  revit-2024\  revit-2025\  revit-2026\   (DLL + .addin cho từng phiên bản)
;   autocad-2024\ autocad-2025\ autocad-2026\  (DLL vỏ đầy đủ + vỏ core-only; 2026 là .NET 10)
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
Name: "revit2026";  Description: "Add-in Revit 2026";      Types: full
Name: "acad2024";   Description: "Plugin AutoCAD 2024";    Types: full
Name: "acad2025";   Description: "Plugin AutoCAD 2025";    Types: full
Name: "acad2026";   Description: "Plugin AutoCAD 2026 Update 1.2 trở lên (.NET 10)"; Types: full
Name: "batch";      Description: "Batch runner chạy đêm";  Types: full
Name: "scripts";    Description: "Script Python (agent client, MCP server, AI offline)"; Types: full

[Files]
; ── Revit: %APPDATA%\Autodesk\Revit\Addins\<năm>\ ────────────────────────────
Source: "{#StageDir}\revit-2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Excludes: "*.md"; \
  Components: revit2023; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; Excludes: "*.md"; \
  Components: revit2024; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Excludes: "*.md"; \
  Components: revit2025; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Excludes: "*.md"; \
  Components: revit2026; Flags: ignoreversion recursesubdirs createallsubdirs

; ── AutoCAD: bundle tự nạp trong %APPDATA%\Autodesk\ApplicationPlugins\ ──────
Source: "{#StageDir}\autocad-2024\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2024"; Excludes: "*.md"; \
  Components: acad2024; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\autocad-2025\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2025"; Excludes: "*.md"; \
  Components: acad2025; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\autocad-2026\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2026"; Excludes: "*.md"; \
  Components: acad2026; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\PackageContents.xml"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle"; \
  Components: acad2024 or acad2025 or acad2026; Flags: ignoreversion

; ── Batch runner: thư mục chương trình bình thường ──────────────────────────
Source: "{#StageDir}\batchrunner\*"; DestDir: "{app}"; \
  Components: batch; Flags: ignoreversion recursesubdirs createallsubdirs

; ── Script Python: dhcb_agent.py / dhcb_mcp_server.py / dhcb_ai.py ──────────
; release.yml đã chép scripts/*.py và *.ps1 vào gói batchrunner, nhưng người chỉ cài phần "scripts"
; (không cài batch runner) vẫn cần chúng: MCP server cho Claude Desktop và client dòng lệnh đều nằm ở đây.
; Chỉ cần Python 3.9+, không có dependency ngoài.
Source: "{#StageDir}\batchrunner\scripts\*"; DestDir: "{app}\scripts"; \
  Components: scripts; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Dirs]
; Tạo sẵn để shortcut log không trỏ vào thư mục chưa tồn tại (add-in tự tạo khi ghi dòng đầu tiên).
Name: "{userappdata}\DHCB\logs"

[Icons]
Name: "{group}\Thư mục batch runner"; Filename: "{app}"; Components: batch
Name: "{group}\Thư mục script Python"; Filename: "{app}\scripts"; Components: scripts
Name: "{group}\Thư mục log DHCB";     Filename: "{userappdata}\DHCB\logs"
Name: "{group}\Gỡ cài đặt DHCB Tools"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}"; Description: "Mở thư mục batch runner"; \
  Flags: postinstall shellexec skipifsilent unchecked; Components: batch

[UninstallDelete]
; Bundle AutoCAD: xoá cả thư mục để AutoCAD không còn thấy plugin.
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle"

[Code]
var
  Acad2026Page: TInputDirWizardPage;

procedure InitializeWizard();
begin
  Acad2026Page := CreateInputDirPage(wpSelectComponents,
    'AutoCAD 2026 Update 1.2 trở lên', 'Chọn thư mục AutoCAD cần cài plugin',
    'Gói này cần AutoCAD 2026 Update 1.2 trở lên dùng .NET 10. Bộ cài sẽ kiểm tra runtime của AutoCAD trong thư mục đã chọn.',
    False, '');
  Acad2026Page.Add('Thư mục AutoCAD 2026:');
  Acad2026Page.Values[0] := ExpandConstant('{autopf}\Autodesk\AutoCAD 2026');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = Acad2026Page.ID) and not WizardIsComponentSelected('acad2026');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RuntimeText: AnsiString;
  AcadFolder: String;
begin
  Result := '';
  if not WizardIsComponentSelected('acad2026') then Exit;
  AcadFolder := ExpandConstant('{param:ACAD2026DIR|}') ;
  if AcadFolder = '' then AcadFolder := Acad2026Page.Values[0];
  if not FileExists(AddBackslash(AcadFolder) + 'acad.exe') then
    Result := 'Không tìm thấy AutoCAD 2026. Chọn đúng thư mục hoặc bỏ thành phần AutoCAD 2026.'
  else if not LoadStringFromFile(AddBackslash(AcadFolder) + 'acdbmgd.runtimeconfig.json', RuntimeText) then
    Result := 'Không đọc được cấu hình runtime AutoCAD. Cần AutoCAD 2026 Update 1.2 trở lên.'
  else if Pos('"net10.0"', String(RuntimeText)) = 0 then
    Result := 'AutoCAD tại thư mục này chưa dùng .NET 10. Cập nhật AutoCAD 2026 lên Update 1.2 trở lên rồi cài lại.';
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // WizardSilent: /SILENT hay /VERYSILENT. MsgBox do [Code] gọi KHÔNG bị /SUPPRESSMSGBOXES tắt, nên bản
  // 1.1.0 cài im lặng xong vẫn treo một cửa sổ "Setup" trống chờ bấm OK — Task Scheduler hay script cài
  // hàng loạt sẽ đứng mãi (bang-chung-test §45/§46).
  if (CurStep = ssPostInstall) and not WizardSilent then
  begin
    // Revit hỏi "Unsigned Add-In" ở lần mở đầu tiên — nói trước để kỹ sư không tưởng là lỗi.
    MsgBox('Đã cài xong.' + #13#10 + #13#10 +
           'Lần đầu mở Revit sẽ có hộp thoại "Unsigned Add-In" — chọn "Always Load".' + #13#10 +
           'AutoCAD: plugin tự nạp khi khởi động (không cần NETLOAD).' + #13#10 +
           'Script Python (nếu đã chọn) nằm trong thư mục cài đặt, mục scripts\ — cần Python 3.9+.' + #13#10 + #13#10 +
           'Log nằm ở %APPDATA%\DHCB\logs.', mbInformation, MB_OK);
  end;
end;
