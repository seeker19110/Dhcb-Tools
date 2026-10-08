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
;   revit-2022\ ... revit-2027\ (DLL + .addin cho từng phiên bản)
;   autocad-2022\ ... autocad-2027\ + autocad-2025-net10\ + autocad-2026-net8\
;   batchrunner\                            (exe + jobs/ configs/ scripts/)
; Nâng cấp theo phạm vi: /PRESERVEUNSELECTED=1 giữ thành phần đang cài nhưng không chọn trong lượt này.
; Mặc định vẫn gỡ thành phần bị bỏ chọn; cờ này không tự chọn hoặc cài thêm host.

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
LicenseFile=..\LICENSE
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
Name: "custom"; Description: "Tự chọn"; Flags: iscustom
Name: "full"; Description: "Cài tất cả"

[Components]
Name: "revit2022"; Description: "Add-in Revit 2022"; Types: full
Name: "revit2023"; Description: "Add-in Revit 2023"; Types: full
Name: "revit2024"; Description: "Add-in Revit 2024"; Types: full
Name: "revit2025"; Description: "Add-in Revit 2025"; Types: full
Name: "revit2026"; Description: "Add-in Revit 2026"; Types: full
Name: "revit2027"; Description: "Add-in Revit 2027"; Types: full
Name: "acad2022"; Description: "Plugin AutoCAD 2022"; Types: full
Name: "acad2023"; Description: "Plugin AutoCAD 2023"; Types: full
Name: "acad2024"; Description: "Plugin AutoCAD 2024"; Types: full
Name: "acad2025"; Description: "Plugin AutoCAD 2025"; Types: full
Name: "acad2026"; Description: "Plugin AutoCAD 2026"; Types: full
Name: "acad2027"; Description: "Plugin AutoCAD 2027"; Types: full
Name: "batch"; Description: "Batch runner chạy đêm"; Types: full
Name: "scripts"; Description: "Script Python (agent client, MCP server, AI offline)"; Types: full

[Files]
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
; Revit: add-in theo đúng năm; thư mục AutoCAD chứa một profile runtime duy nhất.
Source: "{#StageDir}\revit-2022\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2022"; Excludes: "*.md"; \
  Components: revit2022; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Excludes: "*.md"; \
  Components: revit2023; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; Excludes: "*.md"; \
  Components: revit2024; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Excludes: "*.md"; \
  Components: revit2025; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Excludes: "*.md"; \
  Components: revit2026; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\revit-2027\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027"; Excludes: "*.md"; \
  Components: revit2027; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\autocad-2022\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2022"; Excludes: "*.md"; \
  Components: acad2022; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\autocad-2023\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2023"; Excludes: "*.md"; \
  Components: acad2023; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\autocad-2024\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2024"; Excludes: "*.md"; \
  Components: acad2024; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\autocad-2025\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2025"; Excludes: "*.md"; \
  Components: acad2025; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsAcadRuntime(2025, 'net8')
Source: "{#StageDir}\autocad-2025-net10\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2025"; Excludes: "*.md"; \
  Components: acad2025; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsAcadRuntime(2025, 'net10')
Source: "{#StageDir}\autocad-2026\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2026"; Excludes: "*.md"; \
  Components: acad2026; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsAcadRuntime(2026, 'net10')
Source: "{#StageDir}\autocad-2026-net8\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2026"; Excludes: "*.md"; \
  Components: acad2026; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsAcadRuntime(2026, 'net8')
Source: "{#StageDir}\autocad-2027\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2027"; Excludes: "*.md"; \
  Components: acad2027; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\PackageContents.xml"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle"; \
  Components: acad2022 or acad2023 or acad2024 or acad2025 or acad2026 or acad2027; Flags: ignoreversion

; ── Batch runner: thư mục chương trình bình thường ──────────────────────────
Source: "{#StageDir}\batchrunner\*"; DestDir: "{app}"; \
  Excludes: "jobs, jobs\*, configs, configs\*, scripts, scripts\*"; Components: batch; Flags: ignoreversion recursesubdirs createallsubdirs
; Jobs/configs có thể được chỉnh sửa: nâng cấp không ghi đè nội dung người dùng đang dùng.
Source: "{#StageDir}\batchrunner\jobs\*"; DestDir: "{app}\jobs"; \
  Components: batch; Flags: onlyifdoesntexist recursesubdirs createallsubdirs
Source: "{#StageDir}\batchrunner\configs\*"; DestDir: "{app}\configs"; \
  Components: batch; Flags: onlyifdoesntexist recursesubdirs createallsubdirs

; ── Script Python: dhcb_agent.py / dhcb_mcp_server.py / dhcb_ai.py ──────────
; release.yml chép các script trong installer/batchrunner-scripts.txt vào gói batchrunner, nhưng người chỉ cài phần "scripts"
; (không cài batch runner) vẫn cần chúng: MCP server cho Claude Desktop và client dòng lệnh đều nằm ở đây.
; Chỉ cần Python 3.9+, không có dependency ngoài.
Source: "{#StageDir}\batchrunner\scripts\*"; DestDir: "{app}\scripts"; \
  Components: scripts; Flags: ignoreversion recursesubdirs createallsubdirs

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

[InstallDelete]
; Bỏ chọn chỉ xóa file thuộc DHCB; giữ manifest/DLL của add-in khác và dữ liệu người dùng.
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\DhcbTools.Revit.addin"; Components: not revit2022; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\DhcbTools.Revit.dll"; Components: not revit2022; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\DhcbTools.Core.dll"; Components: not revit2022; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\DhcbTools.Shared.Logic.dll"; Components: not revit2022; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\DhcbTools.Shared.Hosting.dll"; Components: not revit2022; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\dhcb-host-profile.json"; Components: not revit2022; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\DhcbTools.Revit.addin"; Components: not revit2023; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\DhcbTools.Revit.dll"; Components: not revit2023; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\DhcbTools.Core.dll"; Components: not revit2023; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\DhcbTools.Shared.Logic.dll"; Components: not revit2023; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\DhcbTools.Shared.Hosting.dll"; Components: not revit2023; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\dhcb-host-profile.json"; Components: not revit2023; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\DhcbTools.Revit.addin"; Components: not revit2024; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\DhcbTools.Revit.dll"; Components: not revit2024; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\DhcbTools.Core.dll"; Components: not revit2024; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\DhcbTools.Shared.Logic.dll"; Components: not revit2024; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\DhcbTools.Shared.Hosting.dll"; Components: not revit2024; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\dhcb-host-profile.json"; Components: not revit2024; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\DhcbTools.Revit.addin"; Components: not revit2025; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\DhcbTools.Revit.dll"; Components: not revit2025; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\DhcbTools.Core.dll"; Components: not revit2025; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\DhcbTools.Shared.Logic.dll"; Components: not revit2025; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\DhcbTools.Shared.Hosting.dll"; Components: not revit2025; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\dhcb-host-profile.json"; Components: not revit2025; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\DhcbTools.Revit.addin"; Components: not revit2026; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\DhcbTools.Revit.dll"; Components: not revit2026; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\DhcbTools.Core.dll"; Components: not revit2026; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\DhcbTools.Shared.Logic.dll"; Components: not revit2026; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\DhcbTools.Shared.Hosting.dll"; Components: not revit2026; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\dhcb-host-profile.json"; Components: not revit2026; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\DhcbTools.Revit.addin"; Components: not revit2027; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\DhcbTools.Revit.dll"; Components: not revit2027; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\DhcbTools.Core.dll"; Components: not revit2027; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\DhcbTools.Shared.Logic.dll"; Components: not revit2027; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\DhcbTools.Shared.Hosting.dll"; Components: not revit2027; Check: RemoveUnselectedComponents
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\dhcb-host-profile.json"; Components: not revit2027; Check: RemoveUnselectedComponents
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2022"; Components: not acad2022; Check: RemoveUnselectedComponents
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2023"; Components: not acad2023; Check: RemoveUnselectedComponents
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2024"; Components: not acad2024; Check: RemoveUnselectedComponents
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2025"; Components: not acad2025; Check: RemoveUnselectedComponents
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2026"; Components: not acad2026; Check: RemoveUnselectedComponents
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\2027"; Components: not acad2027; Check: RemoveUnselectedComponents
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle"; Components: not acad2022 and not acad2023 and not acad2024 and not acad2025 and not acad2026 and not acad2027; Check: RemoveUnselectedComponents

[Code]
var
  AcadRuntimePage: TInputDirWizardPage;
  ExistingAcad: array[2022..2027] of AnsiString;
  AcadRuntime: array[2025..2026] of String;
  ExistingAcadManifestError: String;

function RemoveUnselectedComponents(): Boolean;
begin
  Result := ExpandConstant('{param:PRESERVEUNSELECTED|0}') <> '1';
end;

function ExistingAcadBlock(const Year: String; const XmlDocument: Variant): AnsiString;
var
  Nodes, Component, Entries: Variant;
begin
  Result := '';
  if not FileExists(ExpandConstant('{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\Contents\') +
    Year + '\DhcbTools.AutoCAD.dll') then Exit;
  Nodes := XmlDocument.selectNodes('/ApplicationPackage/Components/ComponentEntry[@ModuleName="./Contents/' + Year + '/DhcbTools.AutoCAD.dll"]');
  if Nodes.length <> 1 then
  begin
    ExistingAcadManifestError := 'Không xác định được duy nhất thành phần AutoCAD ' + Year + ' trong manifest hiện có; sửa manifest trước khi nâng cấp theo phạm vi.';
    Exit;
  end;
  Component := Nodes.item(0).parentNode;
  Entries := Component.selectNodes('ComponentEntry');
  if Entries.length <> 1 then
  begin
    ExistingAcadManifestError := 'Thành phần AutoCAD ' + Year + ' có nhiều ModuleName; tách riêng từng năm trong manifest trước khi nâng cấp theo phạm vi.';
    Exit;
  end;
  // DOM ignores commented-out modules and normalizes quotes/whitespace. Encode its Unicode XML explicitly.
  Result := UTF8Encode(Component.xml);
end;

procedure RememberExistingAcadComponents();
var
  XmlContent: AnsiString;
  BundlePath: String;
  XmlDocument: Variant;
  Year: Integer;
begin
  if RemoveUnselectedComponents() then Exit;
  BundlePath := ExpandConstant('{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle');
  if not DirExists(BundlePath) then Exit;
  if not LoadStringFromFile(BundlePath + '\PackageContents.xml', XmlContent) then
  begin
    ExistingAcadManifestError := 'Không đọc được manifest AutoCAD hiện có; sửa manifest hoặc bỏ /PRESERVEUNSELECTED=1 trước khi cài.';
    Exit;
  end;
  try
    XmlDocument := CreateOleObject('Msxml2.DOMDocument.6.0');
    XmlDocument.resolveExternals := False;
    XmlDocument.validateOnParse := False;
    XmlDocument.setProperty('ProhibitDTD', True);
    if not XmlDocument.load(BundlePath + '\PackageContents.xml') then
      RaiseException('XML không hợp lệ.');
    if XmlDocument.documentElement.nodeName <> 'ApplicationPackage' then
      RaiseException('Không phải ApplicationPackage.');
  except
    ExistingAcadManifestError := 'Manifest AutoCAD hiện có không hợp lệ; sửa manifest trước khi nâng cấp theo phạm vi.';
    Exit;
  end;
  for Year := 2022 to 2027 do
    ExistingAcad[Year] := ExistingAcadBlock(IntToStr(Year), XmlDocument);
end;

procedure InitializeWizard();
var
  Year: Integer;
  Selected: String;
begin
  RememberExistingAcadComponents();
  // Lần cài đầu tự chọn host đã có; giữ nguyên lựa chọn nâng cấp và tham số dòng lệnh.
  if (ExpandConstant('{param:TYPE|}') = '') and
     (ExpandConstant('{param:COMPONENTS|}') = '') and
     (GetPreviousData('DhcbInstallerVersion', '') = '') and
     (WizardSelectedComponents(False) = '') and
     not FileExists(ExpandConstant('{localappdata}\Programs\DHCB Tools\unins000.exe')) then
  begin
    Selected := 'batch,scripts';
    for Year := 2022 to 2027 do
    begin
      if FileExists(ExpandConstant('{autopf}\Autodesk\Revit ') + IntToStr(Year) + '\Revit.exe') then
        Selected := Selected + ',revit' + IntToStr(Year);
      if FileExists(ExpandConstant('{autopf}\Autodesk\AutoCAD ') + IntToStr(Year) + '\acad.exe') then
        Selected := Selected + ',acad' + IntToStr(Year);
    end;
    WizardSelectComponents(Selected);
  end;
  AcadRuntimePage := CreateInputDirPage(wpSelectComponents,
    'AutoCAD 2025/2026', 'Chọn thư mục AutoCAD cần cài plugin',
    'Bộ cài kiểm tra runtime thực tế của AutoCAD và chọn bản plugin .NET 8 hoặc .NET 10 phù hợp.', False, '');
  AcadRuntimePage.Add('Thư mục AutoCAD 2025 (nếu đã chọn):');
  AcadRuntimePage.Add('Thư mục AutoCAD 2026 (nếu đã chọn):');
  AcadRuntimePage.Values[0] := ExpandConstant('{autopf}\Autodesk\AutoCAD 2025');
  AcadRuntimePage.Values[1] := ExpandConstant('{autopf}\Autodesk\AutoCAD 2026');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = AcadRuntimePage.ID) and
    not (WizardIsComponentSelected('acad2025') or WizardIsComponentSelected('acad2026'));
end;

// Parse runtimeconfig JSON before installation. Only runtimeOptions.tfm at the root is authoritative.
procedure SkipJsonWhitespace(const Text: String; var Index: Integer);
begin
  while Index <= Length(Text) do
  begin
    if Pos(Text[Index], ' ' + #9 + #10 + #13) = 0 then Exit;
    Index := Index + 1;
  end;
end;

function ReadJsonString(const Text: String; var Index: Integer; var Value: String): Boolean;
var
  C: String;
  Hex, CodePoint, Count: Integer;
begin
  Result := False;
  Value := '';
  if (Index > Length(Text)) or (Text[Index] <> '"') then Exit;
  Index := Index + 1;
  while Index <= Length(Text) do
  begin
    C := Text[Index];
    Index := Index + 1;
    if C = '"' then
    begin
      Result := True;
      Exit;
    end;
    if C = '\' then
    begin
      if Index > Length(Text) then Exit;
      C := Text[Index];
      Index := Index + 1;
      if C = 'u' then
      begin
        CodePoint := 0;
        for Count := 1 to 4 do
        begin
          if Index > Length(Text) then Exit;
          Hex := Pos(Lowercase(Text[Index]), '0123456789abcdef') - 1;
          if Hex < 0 then Exit;
          CodePoint := CodePoint * 16 + Hex;
          Index := Index + 1;
        end;
        C := Chr(CodePoint);
      end
      else if C = 'b' then C := #8
      else if C = 'f' then C := #12
      else if C = 'n' then C := #10
      else if C = 'r' then C := #13
      else if C = 't' then C := #9
      else if (C <> '"') and (C <> '\') and (C <> '/') then Exit;
    end
    else if Ord(C[1]) < 32 then Exit;
    Value := Value + C;
  end;
end;

function ReadJsonNumber(const Text: String; var Index: Integer): Boolean;
var
  Start: Integer;
begin
  Result := False;
  if (Index <= Length(Text)) and (Text[Index] = '-') then Index := Index + 1;
  if Index > Length(Text) then Exit;
  if Text[Index] = '0' then Index := Index + 1
  else
  begin
    if Pos(Text[Index], '123456789') = 0 then Exit;
    while (Index <= Length(Text)) and (Pos(Text[Index], '0123456789') > 0) do Index := Index + 1;
  end;
  if (Index <= Length(Text)) and (Text[Index] = '.') then
  begin
    Index := Index + 1;
    Start := Index;
    while (Index <= Length(Text)) and (Pos(Text[Index], '0123456789') > 0) do Index := Index + 1;
    if Index = Start then Exit;
  end;
  if (Index <= Length(Text)) and (Pos(Text[Index], 'eE') > 0) then
  begin
    Index := Index + 1;
    if (Index <= Length(Text)) and (Pos(Text[Index], '+-') > 0) then Index := Index + 1;
    Start := Index;
    while (Index <= Length(Text)) and (Pos(Text[Index], '0123456789') > 0) do Index := Index + 1;
    if Index = Start then Exit;
  end;
  Result := True;
end;

function ParseJsonValue(const Text: String; var Index: Integer; Depth: Integer;
  const Path: String; var Tfm: String): Boolean;
var
  Key, ChildPath, Value, Close: String;
  Keys: TStringList;
  IsObject: Boolean;
begin
  Result := False;
  if Depth > 32 then Exit;
  SkipJsonWhitespace(Text, Index);
  if Index > Length(Text) then Exit;
  if (Depth = 2) and (Path = 'runtimeOptions.tfm') and (Text[Index] <> '"') then Exit;
  if Text[Index] = '"' then
  begin
    Result := ReadJsonString(Text, Index, Value);
    if Result and (Depth = 2) and (Path = 'runtimeOptions.tfm') then
    begin
      if Tfm <> '' then Result := False
      else Tfm := Value;
    end;
    Exit;
  end;
  if (Text[Index] = '{') or (Text[Index] = '[') then
  begin
    IsObject := Text[Index] = '{';
    if IsObject then Close := '}' else Close := ']';
    Index := Index + 1;
    SkipJsonWhitespace(Text, Index);
    if (Index <= Length(Text)) and (Text[Index] = Close) then
    begin
      Index := Index + 1;
      Result := True;
      Exit;
    end;
    Keys := TStringList.Create;
    try
      while Index <= Length(Text) do
      begin
        ChildPath := Path + '[]';
        if IsObject then
        begin
          if not ReadJsonString(Text, Index, Key) then Exit;
          if Keys.IndexOf(Key) >= 0 then Exit;
          Keys.Add(Key);
          SkipJsonWhitespace(Text, Index);
          if (Index > Length(Text)) or (Text[Index] <> ':') then Exit;
          Index := Index + 1;
          ChildPath := Key;
          if Path <> '' then ChildPath := Path + '.' + Key;
        end;
        if not ParseJsonValue(Text, Index, Depth + 1, ChildPath, Tfm) then Exit;
        SkipJsonWhitespace(Text, Index);
        if Index > Length(Text) then Exit;
        if Text[Index] = Close then
        begin
          Index := Index + 1;
          Result := True;
          Exit;
        end;
        if Text[Index] <> ',' then Exit;
        Index := Index + 1;
        SkipJsonWhitespace(Text, Index);
      end;
    finally
      Keys.Free;
    end;
    Exit;
  end;
  if Copy(Text, Index, 4) = 'true' then Index := Index + 4
  else if Copy(Text, Index, 5) = 'false' then Index := Index + 5
  else if Copy(Text, Index, 4) = 'null' then Index := Index + 4
  else
  begin
    Result := ReadJsonNumber(Text, Index);
    Exit;
  end;
  Result := True;
end;

function ReadAcadRuntime(const Folder: String): String;
var
  RuntimeText: AnsiString;
  Text, Tfm: String;
  Index: Integer;
begin
  Result := '';
  if not FileExists(AddBackslash(Folder) + 'acad.exe') then Exit;
  if not LoadStringFromFile(AddBackslash(Folder) + 'acdbmgd.runtimeconfig.json', RuntimeText) then Exit;
  if Length(RuntimeText) > 1048576 then Exit;
  // .NET generates UTF-8 JSON; strip an optional UTF-8 BOM before parsing ASCII property names.
  if Copy(RuntimeText, 1, 3) = #239#187#191 then Delete(RuntimeText, 1, 3);
  Text := String(RuntimeText);
  Index := 1;
  Tfm := '';
  SkipJsonWhitespace(Text, Index);
  if (Index > Length(Text)) or (Text[Index] <> '{') then Exit;
  if not ParseJsonValue(Text, Index, 0, '', Tfm) then Exit;
  SkipJsonWhitespace(Text, Index);
  if Index <= Length(Text) then Exit;
  if Tfm = 'net8.0' then Result := 'net8';
  if Tfm = 'net10.0' then Result := 'net10';
end;

function IsAcadRuntime(Year: Integer; const Runtime: String): Boolean;
begin
  Result := AcadRuntime[Year] = Runtime;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  AcadFolder: String;
  Year: Integer;
begin
  Result := ExistingAcadManifestError;
  if Result <> '' then Exit;
  for Year := 2025 to 2026 do
    if WizardIsComponentSelected('acad' + IntToStr(Year)) then
    begin
      AcadFolder := ExpandConstant('{param:ACAD' + IntToStr(Year) + 'DIR|}');
      if AcadFolder = '' then AcadFolder := AcadRuntimePage.Values[Year - 2025];
      AcadRuntime[Year] := ReadAcadRuntime(AcadFolder);
      if AcadRuntime[Year] = '' then
      begin
        Result := 'Không xác định được runtime AutoCAD ' + IntToStr(Year) +
          '. Chọn thư mục có acad.exe và acdbmgd.runtimeconfig.json hợp lệ (.NET 8 hoặc .NET 10).';
        Exit;
      end;
    end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'DhcbInstallerVersion', '{#Version}');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure ReplaceXmlBlock(var S: AnsiString; const StartMarker, Replacement: AnsiString);
var
  P1, P2: Integer;
  SubStr: AnsiString;
  EndMarker: AnsiString;
begin
  EndMarker := '</Components>';
  P1 := Pos(StartMarker, S);
  if P1 > 0 then
  begin
    SubStr := Copy(S, P1, Length(S) - P1 + 1);
    P2 := Pos(EndMarker, SubStr);
    if P2 > 0 then
    begin
      S := Copy(S, 1, P1 - 1) + Replacement + Copy(S, P1 + P2 + Length(EndMarker) - 1, Length(S));
    end;
  end;
end;

procedure UpdatePackageContents();
var
  XmlPath: String;
  XmlContent: AnsiString;
  Year: Integer;
  HasSelectedAcad: Boolean;
begin
  HasSelectedAcad := False;
  for Year := 2022 to 2027 do
    if WizardIsComponentSelected('acad' + IntToStr(Year)) then HasSelectedAcad := True;
  if not HasSelectedAcad then Exit;
  XmlPath := ExpandConstant('{userappdata}\Autodesk\ApplicationPlugins\DhcbTools.bundle\PackageContents.xml');
  if not LoadStringFromFile(XmlPath, XmlContent) then
    RaiseException('Không đọc được manifest AutoCAD: ' + XmlPath);
  // Sửa từ cuối để comment trong node được giữ không trở thành marker của năm sau.
  for Year := 2027 downto 2022 do
    if not WizardIsComponentSelected('acad' + IntToStr(Year)) then
      ReplaceXmlBlock(XmlContent, UTF8Encode('<Components Description="AutoCAD ' + IntToStr(Year) + '">'), ExistingAcad[Year]);
  if not SaveStringToFile(XmlPath, XmlContent, False) then
    RaiseException('Không ghi được manifest AutoCAD: ' + XmlPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    UpdatePackageContents();
    if not WizardSilent then
    begin
      // Revit hỏi "Unsigned Add-In" ở lần mở đầu tiên — nói trước để kỹ sư không tưởng là lỗi.
      MsgBox('Đã cài xong.' + #13#10 + #13#10 +
             'Lần đầu mở Revit sẽ có hộp thoại "Unsigned Add-In" — chọn "Always Load".' + #13#10 +
             'AutoCAD: plugin tự nạp khi khởi động (không cần NETLOAD).' + #13#10 +
             'Script Python (nếu đã chọn) nằm trong thư mục cài đặt, mục scripts\ — cần Python 3.9+.' + #13#10 + #13#10 +
             'Log nằm ở %APPDATA%\DHCB\logs.', mbInformation, MB_OK);
    end;
  end;
end;
