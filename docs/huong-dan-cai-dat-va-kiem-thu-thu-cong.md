# Hướng dẫn cài đặt và kiểm thử thủ công trên máy thật

Tài liệu này dành cho người **cài DHCB Tools lên máy có Revit/AutoCAD** và **chạy kiểm thử thủ công** lần đầu. Code
biên dịch xanh với API package NuGet và có bộ test thuần chạy trên CI (số ca xem output CI). Các lệnh **đã chạy thật**
qua bộ ca kiểm tự động bên trong Revit 2024.3 và AutoCAD (`accoreconsole`) — xem
[`bang-chung-test.md`](bang-chung-test.md); phần checklist tay dưới đây là lớp kiểm còn lại, đi qua đúng giao diện mà
kỹ sư dùng. Kịch bản chi tiết từng lệnh ở
[`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md) §4; tài liệu này nối các bước lại thành một quy trình đi từ đầu đến cuối và
kèm mẫu ghi kết quả.

Ký hiệu: ✅ đạt · ❌ lỗi (ghi lại thông báo) · ⏭ bỏ qua (ghi lý do).

---

## 1. Yêu cầu máy

| Thành phần | Yêu cầu | Ghi chú |
|---|---|---|
| Windows | 10/11 x64 | Add-in chỉ chạy trên Windows |
| Revit | 2023, 2024 (net48) hoặc 2025 (net8) | Một máy có thể cài nhiều bản; build riêng cho từng bản |
| AutoCAD | 2024 (net48) hoặc 2025 (net8) | Bản 2026.1+ dùng .NET 10, chưa kiểm — xem §9 |
| .NET SDK | 8.0.x | `dotnet --version` ≥ 8.0; build net48 dùng cùng SDK (không cần VS) |
| Python | 3.9+ | Cho `scripts/dhcb_agent.py`, `dhcb_mcp_server.py`, `dhcb_ai.py` — không cần thư viện ngoài |
| Ollama (tuỳ chọn) | bản mới, model `qwen3:8b` | Chỉ cho phần AI có model; mọi tính năng AI đều có đường heuristic không cần model |
| Quyền | Ghi vào `%ProgramData%\Autodesk\Revit\Addins\<năm>` và `%APPDATA%\DHCB` | Không cần quyền admin nếu dùng thư mục Addins của user (§3.2) |

Mở PowerShell **không** phải admin trừ khi ghi vào `%ProgramData%`.

---

## 2. Lấy mã nguồn và build

```powershell
git clone https://github.com/seeker19110/Dhcb-Tools.git D:\DHCB\src
cd D:\DHCB\src
```

### 2.1 Test thuần (xác nhận môi trường .NET đúng)

```powershell
dotnet test tests\DhcbTools.Shared.Logic.Tests\DhcbTools.Shared.Logic.Tests.csproj
```

Kỳ vọng: `Failed: 0` (số ca đang tăng theo từng PR — con số chuẩn là output của CI, đừng so với một số cứng).
Nếu lỗi restore NuGet → kiểm tra proxy/kết nối, đây là bước duy nhất cần internet.

### 2.2 Build add-in Revit (mỗi bản Revit một lần)

```powershell
# Revit 2024 (net48)
dotnet build src\DhcbTools.Revit\DhcbTools.Revit.csproj -c Release -p:RevitVersion=2024
# Revit 2025 (net8.0-windows)
dotnet build src\DhcbTools.Revit\DhcbTools.Revit.csproj -c Release -p:RevitVersion=2025
```

`RevitVersion` mặc định là **2024** (net48) khi không truyền gì, nên `dotnet build Dhcb-Tools.sln -c Release`
trần cũng chạy được; truyền tham số khi cần bản khác.

Kết quả nằm ở `src\DhcbTools.Revit\bin\Release\net48\` (2024) hoặc `...\net8.0-windows\` (2025). Phải có đủ:
`DhcbTools.Revit.dll`, `DhcbTools.Core.dll`, `DhcbTools.Shared.Logic.dll`, `DhcbTools.Shared.Hosting.dll`,
`Newtonsoft.Json.dll`, `DhcbTools.Revit.addin`.

### 2.3 Build plugin AutoCAD

```powershell
# Vỏ đầy đủ (lệnh tương tác DHCB_*) — AutoCAD 2024
dotnet build src\DhcbTools.AutoCAD\DhcbTools.AutoCAD.csproj -c Release -p:RevitVersion=2024 -p:AcadVersion=2024
# Vỏ core-only cho accoreconsole (chỉ DHCB_RUN)
dotnet build src\DhcbTools.AutoCAD.Core\DhcbTools.AutoCAD.Core.csproj -c Release -p:RevitVersion=2024 -p:AcadVersion=2024
```

`-p:RevitVersion` ở đây chỉ để chọn TargetFramework (2024 → net48, 2025 → net8), đặt **cùng năm với AcadVersion**.

### 2.4 Build batch runner

```powershell
dotnet build src\DhcbTools.BatchRunner\DhcbTools.BatchRunner.csproj -c Release
```

Ra `src\DhcbTools.BatchRunner\bin\Release\net10.0\DhcbTools.BatchRunner.exe`. Copy toàn bộ thư mục này sang
`D:\DHCB\bin\` và **copy thêm** `DhcbTools.AutoCAD.Core.dll` (+ `DhcbTools.Core.AutoCAD.dll`, `DhcbTools.Shared.*.dll`)
vào cùng thư mục để runner tự tìm plugin cho accoreconsole.

Ghi kết quả §2: build 2024 ☐ · build 2025 ☐ · AutoCAD ☐ · AutoCAD.Core ☐ · BatchRunner ☐

---

## 3. Cài đặt

### 3.1 Revit

Copy vào **một trong hai** thư mục Addins (user không cần admin):

```
%APPDATA%\Autodesk\Revit\Addins\2024\          (chỉ user hiện tại)
%ProgramData%\Autodesk\Revit\Addins\2024\      (mọi user, cần admin)
```

Nội dung: `DhcbTools.Revit.addin` + tất cả DLL ở §2.2. File `.addin` trỏ `Assembly` = `DhcbTools.Revit.dll` (đường dẫn
tương đối, cùng thư mục) nên không phải sửa gì.

Mở Revit → lần đầu Revit hỏi "Load add-in?" → **Always Load**. Ribbon xuất hiện tab **DHCB Tools** với 6 panel
(đúng tên trong `src/DhcbTools.Revit/App.cs`):
*Nền tảng · Xuất & Báo cáo · Khởi tạo dự án · MEPF · Hồ sơ & Style · Kiểm tra & AI*.

Nếu tab không hiện: xem `%APPDATA%\Autodesk\Revit\Autodesk Revit 2024\Journals\journal.*.txt` dòng cuối có
`DhcbTools`; lỗi thường gặp là thiếu `Newtonsoft.Json.dll` hoặc DLL bị Windows chặn (chuột phải → Properties → Unblock).

### 3.2 AutoCAD

Cách nhanh: trong AutoCAD gõ `NETLOAD`, chọn `DhcbTools.AutoCAD.dll` (kèm DLL phụ cùng thư mục). Gõ `DHCB` → in danh sách lệnh.

Cách tự load: tạo `%APPDATA%\Autodesk\ApplicationPlugins\DhcbTools.bundle\PackageContents.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" Name="DHCB Tools" AppVersion="1.0" ProductCode="{7B3D2C1E-DHCB-4A2B-8C3D-000000000001}">
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="R24.3" SeriesMax="R25.0" />
    <ComponentEntry AppName="DhcbTools" ModuleName="./Contents/DhcbTools.AutoCAD.dll" LoadOnAutoCADStartup="True" />
  </Components>
</ApplicationPackage>
```

và copy DLL vào `Contents\`. Nếu AutoCAD hỏi SECURELOAD → chọn *Always Load*, hoặc thêm thư mục vào
`TRUSTEDPATHS`.

### 3.3 Thư mục làm việc `%APPDATA%\DHCB`

Add-in tự tạo khi chạy lần đầu. Cấu trúc sau khi dùng:

```
%APPDATA%\DHCB\
├── bridge-token.txt          token HTTP Bridge (sinh lần đầu Bridge khởi động)
├── settings.json             công tắc updater (copy từ configs\settings.sample.json nếu muốn bật)
├── ai.json                   cấu hình Ollama (copy từ configs\ai.sample.json, đặt enabled:true)
├── configs\revit\<Lệnh>.json  config từng nút Ribbon (tự tạo mẫu lần đầu bấm)
├── configs\autocad\<Lệnh>.json
├── pending-job.json / batch-done.json   do BatchRunner ghi khi chạy đêm
└── clash-accepted.json       cặp va chạm đã chấp nhận
```

Chép sẵn các file mẫu. **Hai thư mục khác nhau, đừng lẫn:**

- `%APPDATA%\DHCB\` — nơi add-in **tự đọc** (token, `settings.json`, `ai.json`, config từng nút Ribbon). Đường dẫn
  cố định, không đổi được.
- Thư mục quy tắc của dự án (ví dụ `D:\DHCB\configs\`) — file `parameter-rules.json`, `layer-rules.json`,
  `layer-map.csv` được **truyền đường dẫn vào config của lệnh** (`rulesPath`, `layerMapPath`…), nên đặt ở đâu cũng
  được; để ngoài `%APPDATA%` cho cả nhóm dùng chung qua ổ mạng là chuyện thường.

```powershell
mkdir $env:APPDATA\DHCB -Force
mkdir D:\DHCB\configs -Force
copy configs\parameter-rules.sample.json D:\DHCB\configs\parameter-rules.json
copy configs\layer-rules.sample.json     D:\DHCB\configs\layer-rules.json
copy configs\layer-map.sample.csv        D:\DHCB\configs\layer-map.csv
copy configs\ai.sample.json              $env:APPDATA\DHCB\ai.json        # tuỳ chọn
```

---

## 4. File mẫu để kiểm thử

Chuẩn bị **một lần**, dùng lại cho mọi vòng hồi quy. Không dùng file dự án thật cho lần đầu.

### 4.1 `test-model.rvt` (Revit, template mét)

| Cần có | Vì sao |
|---|---|
| ≥ 2 Level, 1 Grid dọc + ngang | ProjectInit, SheetRename token `{Level}` |
| 20 cửa, trong đó 3–4 cửa cùng hàng lệch 2–5 mm | AutoNumbering (lỗi #5), ColorByParameter |
| 10 đoạn ống Sanitary DN100 nằm ngang, dài 1–13 m, vài đoạn nối nhau | HangerAuto, PipeSplitter, SlopePipes, PipeKick, SystemBom |
| 3 đoạn duct Supply Air + 1 dầm cắt ngang tuyến | SleeveAuto, ClashDetection, AutoRoute |
| 1 tường bị ống xuyên | SleeveAuto |
| 5 sheet A-101…A-105, 1 sheet có legend + schedule, vài view chưa đặt sheet | SheetRename, RevisionOnSheets, ViewportCopy, RemoveUnusedViews |
| 2 revision trong Sheet Issues/Revisions | RevisionOnSheets |
| 3 view template, 2 filter, 2 text type không dùng | StylePurge |
| 1 family in-place, 1 family không có instance | FamilyAudit, checkset |
| Vài warning cố ý (2 tường trùng nhau) | HealthReport, WarningsExport |
| Model line style `DHCB-Route` vẽ hình chữ U + 1 nhánh T | RouteFromLines |

Lưu thêm bản copy `test-model-2023.rvt` bằng Revit 2023 (nếu có) để thử autodetect phiên bản của batch.

### 4.2 `test-drawing.dwg` (AutoCAD)

Layer `WALL`, `TUONG-200`, `DOOR`, `AXIS` (có 6 đoạn thẳng trục), 1 layer rỗng, 1 linetype chỉ layer dùng, 1 text style
không dùng, block `DOOR` có attribute `MARK` và `SIZE` × 10 chèn theo hàng, 1 xref thiếu file, 2 layout. Lưu thêm bản
`test-drawing-v2.dwg` sau khi dời 2 block và đổi layer 1 đường (cho DrawingCompare).

---

## 5. Kiểm thử Revit — theo thứ tự

Nguyên tắc chung cho **mọi nút Ribbon**: bấm lần 1 → tool tạo file config mẫu ở `%APPDATA%\DHCB\configs\revit\<Lệnh>.json`
và mở thông báo; sửa file; bấm lần 2 → chạy **xem trước** (dryRun) hiện kết quả; bấm **Yes** → chạy thật. Kiểm tra sau mỗi
lệnh thật: **Ctrl+Z hoàn tác được trọn một bước** (một lệnh = một transaction).

### 5.1 Khởi động và Bridge (5 phút)

| # | Việc | Kỳ vọng | Kết quả |
|---|---|---|---|
| R1 | Mở Revit, mở `test-model.rvt` | Tab DHCB Tools, 6 panel (*Nền tảng · Xuất & Báo cáo · Khởi tạo dự án · MEPF · Hồ sơ & Style · Kiểm tra & AI*), không hộp thoại lỗi | ☐ |
| R2 | `type %APPDATA%\DHCB\bridge-token.txt` | Có chuỗi ~43 ký tự | ☐ |
| R3 | `python scripts\dhcb_agent.py revit tools` | Liệt kê 42 lệnh Revit | ☐ |
| R4 | `curl http://127.0.0.1:8765/health` | 200, chỉ có status/version, không lộ tên file | ☐ |
| R5 | `curl -X POST http://127.0.0.1:8765/execute -d "{}"` (không token) | 401 `{"error":"unauthorized"}` | ☐ |
| R6 | Gửi sai token 5 lần liên tiếp rồi lần 6 đúng token | Lần 6 vẫn 401/429 trong 5 phút | ☐ |
| R7 | `python scripts\dhcb_agent.py revit query document_info` | JSON có title, số element | ☐ |
| R8 | Từ máy khác trong LAN gọi `http://<ip>:8765/health` | Không kết nối được | ☐ |

### 5.2 Lệnh nền tảng (20 phút)

| # | Lệnh | Cách chạy | Kỳ vọng | Kết quả |
|---|---|---|---|---|
| R9 | ParameterExport | Ribbon, categories Doors, params Mark/Level/Width | CSV mở Excel tiếng Việt đúng dấu; số dùng dấu chấm | ☐ |
| R10 | ParameterImport | Sửa 3 ô Mark trong CSV, chạy xem trước rồi thật | Đúng 3 ô đổi; ô bỏ qua có lý do trong Messages | ☐ |
| R11 | AutoNumbering | Doors, param Mark, prefix D-, pad 3 | Cửa cùng hàng lệch vài mm vẫn đánh trái→phải | ☐ |
| R12 | RemoveUnusedViews | Xem trước rồi thật | Danh sách xem trước khớp view bị xoá; Ctrl+Z hoàn tác | ☐ |
| R13 | BatchExport | PDF + DWG, mẫu `{SheetNumber}-{SheetName}` | Đủ file; 2 sheet trùng tên không ghi đè | ☐ |
| R14 | HealthReport | Ribbon | HTML mở được; tên view có `<` `&` không vỡ | ☐ |

### 5.3 Hồ sơ & Style — giai đoạn 7 P1 (25 phút)

| # | Lệnh | Config gợi ý | Kỳ vọng | Kết quả |
|---|---|---|---|---|
| R15 | SheetRename | `numberPattern:"A-{Level}-{n:00}"`, `orderBy:"Level"` | Xem trước liệt kê 5 sheet; chạy thật không lỗi trùng; đổi chéo A↔B được | ☐ |
| R16 | SheetRename | `find:"^A-", replace:"AR-"` | Chỉ đổi phần khớp regex | ☐ |
| R17 | RevisionOnSheets | `revisionSequence:2, sheetNumberContains:"A-1"` | Đúng sheet; chạy lần 2 báo 0 sheet cần đổi | ☐ |
| R18 | StylePurge | mặc định, `keepNameContains:["DHCB"]` | Chỉ xoá template/filter/text type **không** được tham chiếu; `<Solid fill>` không bị đụng; view đang dùng template không đổi | ☐ |
| R19 | ColorByParameter | Doors, `parameterName:"Width"` | Mỗi giá trị một màu khác xa; CSV chú giải đúng số lượng; chạy `reset:true` trả về bình thường | ☐ |
| R20 | FamilyAudit | `outputPath`, sau đó `renamePattern:"DHCB_{Category:upper}_{Name}"`, `filterContains:"Door"` | CSV đủ cột; in-place không bị đổi tên | ☐ |
| R21 | WarningsExport | Ribbon | CSV có ElementId; số dòng = số warning trong Manage → Warnings | ☐ |
| R22 | ParameterRuleCheck | `rulesPath` = parameter-rules.json (có thresholds) | HTML có phần vi phạm tham số **và** dòng Model/warnings khi vượt ngưỡng | ☐ |
| R23 | ScheduleExport | `nameContains:"Door"` | CSV đủ header + body, đúng cột đang hiển thị | ☐ |
| R24 | ViewportCopy | source A-101 (có legend + schedule), `targetSheetContains:"A-10"` | Legend/schedule sang mọi sheet đích cùng toạ độ, ghim; view plan báo bỏ qua | ☐ |

### 5.4 MEPF (40 phút)

| # | Lệnh | Config gợi ý | Kỳ vọng | Kết quả |
|---|---|---|---|---|
| R25 | ConnectorChecker | `create3dView:true` | Đúng số connector hở; view khoanh vùng mở được | ☐ |
| R26 | ElevationTag | mặc định | Tham số cao độ ghi `3200.0` (dấu chấm) trên máy tiếng Việt | ☐ |
| R27 | HangerAuto | family hanger có trong model, spacing 2000 | Đoạn 10 m: hanger cách đều; đoạn 1 m: đúng **một** hanger | ☐ |
| R28 | PipeSplitter | `maxSegmentMm:6000` | Đoạn 13 m → 6+6+1; đoạn 6,005 m không bị cắt mẩu 5 mm | ☐ |
| R29 | SleeveAuto | family sleeve có trong model, clearance 50 | Sleeve đúng vị trí/kích thước tại ống xuyên tường; ống chạm mép không sinh sleeve | ☐ |
| R30 | RouteFromLines | `lineStyleName:"DHCB-Route"`, Duct, Supply Air, 400×200 | Duct liền mạch, elbow + tee đúng type, không warning "not connected"; ghi số fitting hỏng nếu có | ☐ |
| R31 | SlopePipes | `systemContains:"Sanitary"`, `checkOnly:true` rồi chạy thật | Báo đúng số ống chưa đạt; sau khi chạy dốc 1 %/DN100, đầu cuối hạ 60 mm/6 m; ống nối cả hai đầu báo lỗi rõ, không hỏng transaction | ☐ |
| R32 | PipeKick | chọn 1 ống thẳng 3 m → nút Kick, `offsetMm:300, elbowAngleDeg:45` | 3 đoạn + 2 cút nối kín; nếu routing preference không có cút 45° → báo, ống vẫn 3 đoạn (không rollback) | ☐ |
| R33 | SystemBom | `spoolParameter:"Comments"` | CSV: tổng mét đúng ±1 %, số cây = ceil(m × 1,05 / 6) | ☐ |
| R34 | AutoRoute | 2 điểm cách 12 m hai bên dầm, `searchMarginMm:3000`, dryRun | Tuyến né dầm, ≤ 4 lần rẽ; ghi thời gian chạy (mục tiêu < 10 s) | ☐ |
| R35 | AutoRoute | `dryRun:false, buildRoute:true` + routeConfig Duct | Model line vẽ ra + duct dựng liền mạch | ☐ |
| R36 | ClashDetection | Ducts × Structural Framing, `acceptedPath` | HTML đúng số va chạm; thêm 1 cặp vào clash-accepted.json → chạy lại không báo cặp đó | ☐ |
| R37 | SystemColor / SystemName / FlowNumbering / DevicePlacement / Sizing | theo `dac-ta-kiem-thu.md` §4.2 | — | ☐ |

### 5.5 Dự án & hồ sơ (15 phút)

| # | Lệnh | Kỳ vọng | Kết quả |
|---|---|---|---|
| R38 | ProjectInit (Level/Grid/ProjectInfo) từ config JSON | Đúng số level/grid; chạy lần hai không nhân đôi | ☐ |
| R39 | ProjectFromTemplate | File mới từ .rte, worksharing bật, workset đúng; file đã tồn tại → Fail rõ | ☐ |
| R40 | TransferStandards | View template/filter/material sang file đích; LineStyles báo "không copy được" đúng như tài liệu | ☐ |
| R41 | GridFromCsv với CSV từ `DHCB_GRID_EXTRACT` (§6) | Trục đúng vị trí và tên 1,2,3 / A,B,C | ☐ |
| R42 | SheetBatchCreate từ CSV | Sheet + view đặt đúng; sheet đã có bị bỏ qua | ☐ |

### 5.6 AI offline (10 phút)

| # | Việc | Kỳ vọng | Kết quả |
|---|---|---|---|
| R43 | Nút *Ra lệnh tiếng Việt*: "đánh số cửa tầng 2 tiền tố D- 3 chữ số" | Đề xuất AutoNumbering, config có prefix D-, pad 3, dryRun true; phải bấm xác nhận mới chạy | ☐ |
| R44 | `python scripts\dhcb_agent.py revit chat "xoá view template thừa"` | Trả StylePurge, requiresConfirmation true | ☐ |
| R45 | CadLayerMap (heuristic, chưa bật Ollama) | CSV map có confidence; type không có trong model bị loại | ☐ |
| R46 | Bật Ollama: `ollama pull qwen3:8b`, copy `ai.sample.json` → `%APPDATA%\DHCB\ai.json` với `enabled:true`, `python scripts\dhcb_ai.py ollama-check` | Báo kết nối OK, model có | ☐ |
| R47 | CadLayerMap lại | Kết quả khác heuristic ở dòng khó; **không** có type bịa (đã lọc) | ☐ |
| R48 | Tắt mạng (rút cáp/Wi-Fi) rồi chạy lại R43–R47 | Mọi thứ vẫn chạy — offline đúng nghĩa | ☐ |

### 5.7 IUpdater cao độ (tùy chọn, 10 phút)

Copy `configs\settings.sample.json` → `%APPDATA%\DHCB\settings.json`, đặt `"updaters": {"elevation": true}`, mở lại Revit.
Kéo một ống lên 100 mm → tham số cao độ đổi ngay. Chọn 1 000 ống và Move → thời gian < 200 ms/lần, nếu chậm updater
**tự tắt** và báo một lần. Ghi thời gian đo: ______ ms.

---

## 6. Kiểm thử AutoCAD (30 phút)

Mở `test-drawing.dwg`. Mọi lệnh ghi đều hỏi `[Xemtrước/Thật]`, mặc định Xemtrước.

| # | Lệnh | Kỳ vọng | Kết quả |
|---|---|---|---|
| C1 | `DHCB` | In danh sách 15 lệnh + hướng dẫn | ☐ |
| C2 | `DHCB_LAYER_EXPORT` → sửa CSV → `DHCB_LAYER_IMPORT` | Round-trip tiếng Việt không mất dấu; layer trùng tên cập nhật, không nhân đôi | ☐ |
| C3 | `DHCB_CLEANUP` (Thật) | Không xoá CLAYER, không xoá linetype của layer, `0`/`Defpoints` giữ; transaction không hỏng | ☐ |
| C4 | `DHCB_EXEC DrawingCleanup` với `purgeUnusedTextStyles:true, purgeUnusedDimStyles:true` | Text style không dùng bị xoá, `Standard` giữ | ☐ |
| C5 | `DHCB_AUTONUMBER` block DOOR, tag MARK | Đánh số theo hàng trái→phải | ☐ |
| C6 | `DHCB_ATTR_INC` block DOOR, MARK, mẫu `P-{n:000}` | `P-001…` theo vị trí | ☐ |
| C7 | `DHCB_ATTR_EXPORT` / `DHCB_ATTR_IMPORT` | Round-trip đúng | ☐ |
| C8 | `DHCB_TEXT_REPLACE` | Đúng số text thay; regex hoạt động | ☐ |
| C9 | `DHCB_LAYER_CHECK` với layer-rules.json | HTML báo vi phạm đúng | ☐ |
| C10 | `DHCB_LAYTRANS` với layer-map.csv (Thật) | `WALL`, `TUONG-200` → `A-WALL` kể cả entity trong block; layer nguồn rỗng bị xoá; CLAYER giữ | ☐ |
| C11 | `DHCB_COMPARE` với `test-drawing-v2.dwg`, output .html | Đúng 2 Moved + 1 LayerChanged; HTML mở được | ☐ |
| C12 | `DHCB_BLOCKCOUNT` nhóm theo SIZE | CSV đúng số block theo SIZE | ☐ |
| C13 | `DHCB_XREF_AUDIT` | Xref thiếu file được liệt kê | ☐ |
| C14 | `DHCB_GRID_EXTRACT` layer AXIS | CSV 6 trục, tên 1,2,3/A,B,C — dùng cho R41 | ☐ |
| C15 | `DHCB_LAYERMAP` | CSV gợi ý map layer → Revit type | ☐ |
| C16 | `DHCB_AI` "đổi layer theo chuẩn" | Đề xuất LayerTranslate, không tự chạy | ☐ |
| C17 | Bridge 8766: `python scripts\dhcb_agent.py autocad tools` và `/health` không token | Như R3–R5 | ☐ |

---

## 7. Kiểm thử batch chạy đêm (45 phút, có thể chạy ban ngày)

### 7.1 Revit

1. Copy `jobs\nightly.sample.json` → `D:\DHCB\jobs\test.json`; sửa `files` trỏ tới `test-model.rvt` (và
   `test-model-2023.rvt` nếu có), `outputFolder`, đường dẫn `configs`. Giữ `saveMode:"SaveAs"`.
2. Đóng mọi Revit đang mở. Chạy:

```powershell
D:\DHCB\bin\DhcbTools.BatchRunner.exe --job D:\DHCB\jobs\test.json --log-dir D:\DHCB\logs --max-minutes 30 --analyze
```

| # | Kỳ vọng | Kết quả |
|---|---|---|
| B1 | Console in "Phiên bản Revit theo file: 2024"; nếu có file 2023 → cảnh báo mở bằng 2024 | ☐ |
| B2 | Revit tự mở, **không** hộp thoại nào cần bấm, tự đóng khi xong | ☐ |
| B3 | `logs\<ngày>\run-<HHmmss>.jsonl` mỗi step một dòng (mỗi lượt chạy một file); `report.html` bảng file × step xanh/đỏ; `warnings-summary.md` | ☐ |
| B4 | Bản gốc `test-model.rvt` **không đổi** (kiểm mtime); bản SaveAs nằm trong outputFolder | ☐ |
| B5 | Mã thoát: `echo $LASTEXITCODE` = 0 (hoặc 1 nếu có step cố ý lỗi) | ☐ |
| B6 | Chạy lại cùng job → kết quả như nhau (idempotent với step dryRun/chỉ đọc) | ☐ |
| B7 | `--dry-run` → mọi step ép dryRun, không file SaveAs | ☐ |
| B8 | Đặt `--max-minutes 1` với job 2 file → file 2 ghi `skipped` trong log, Revit vẫn đóng sạch | ☐ |

### 7.2 AutoCAD (accoreconsole)

```powershell
D:\DHCB\bin\DhcbTools.BatchRunner.exe --job D:\DHCB\jobs\test-acad.json --accoreconsole "C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe"
```

| # | Kỳ vọng | Kết quả |
|---|---|---|
| B9 | Console in đường dẫn plugin = `DhcbTools.AutoCAD.Core.dll` (ưu tiên core-only) | ☐ |
| B10 | Không lỗi "assembly references AcMgd" khi NETLOAD; `run-<HHmmss>.jsonl` có dòng cho từng step; thư mục làm việc là `acad-steps-<HHmmss>` | ☐ |
| B11 | Step `PlotPdf` sinh file PDF trong `outputFolder\pdf\` (mở được) | ☐ |
| B12 | `LayerTranslate` dryRun trong batch chỉ báo, không đổi file gốc | ☐ |

### 7.3 Hẹn giờ

```powershell
.\scripts\install-nightly-task.ps1 -Job D:\DHCB\jobs\test.json -RunnerExe D:\DHCB\bin\DhcbTools.BatchRunner.exe -LogDir D:\DHCB\logs -Time 23:00 -Analyze
```

Kiểm trong Task Scheduler: task "DHCB Tools - Batch đêm" chạy dưới tài khoản đang đăng nhập; bấm *Run* thủ công một lần →
*Last Run Result* = 0x0. ☐

---

## 8. MCP với Claude Desktop / Claude Code (15 phút)

Cấu hình theo [`ai-offline.md`](ai-offline.md) mục MCP, trỏ `command` tới `python`, `args` tới
`scripts\dhcb_mcp_server.py revit`. Đặt biến `DHCB_BRIDGE_TOKEN` hoặc để server tự đọc `%APPDATA%\DHCB\bridge-token.txt`.

| # | Việc | Kỳ vọng | Kết quả |
|---|---|---|---|
| M1 | Trong Claude: "liệt kê các level trong model" | Gọi tool `query` levels, trả đúng | ☐ |
| M2 | "xoá view thừa" | Tool chạy dryRun trước, hỏi xác nhận; chỉ khi `confirm:true` mới xoá | ☐ |
| M3 | Chạy server với `--read-only` rồi thử lệnh ghi | Trả "bị chặn", model không đổi | ☐ |
| M4 | `--group sheets` | `tools/list` chỉ còn nhóm sheet (≤ 8 tool) | ☐ |

---

## 9. Lỗi thường gặp

| Triệu chứng | Nguyên nhân | Xử lý |
|---|---|---|
| Tab DHCB Tools không hiện | DLL bị chặn (Zone.Identifier) hoặc thiếu Newtonsoft | Unblock file; kiểm journal Revit |
| Nút Ribbon báo "Không khởi động được HTTP Bridge" | Cổng 8765 bị chiếm (Revit khác đang mở) | Đóng phiên cũ, hoặc chỉ dùng Ribbon — lệnh vẫn chạy |
| `401 unauthorized` dù đúng token | Content-Type không phải `application/json`, hoặc đang bị khoá 5 phút | Dùng `dhcb_agent.py` (đặt header đúng); chờ 5 phút |
| Lệnh MEPF báo "Không tìm thấy family" | Family hanger/sleeve chưa load vào model | Load family rồi ghi đúng tên vào config |
| RouteFromLines/PipeKick báo fitting không dựng được | Routing preference của type thiếu elbow góc tương ứng | Sửa Routing Preferences của Pipe/Duct Type |
| Batch: Revit mở nhưng không chạy job | Add-in cài cho bản Revit khác với bản được mở | Cài add-in đúng năm; xem `%APPDATA%\DHCB\batch-error.txt` |
| accoreconsole: "Cannot load assembly" | Dùng vỏ đầy đủ (AcMgd) thay core-only | Copy `DhcbTools.AutoCAD.Core.dll` cạnh runner hoặc `--plugin-dll` |
| AutoCAD 2026.1+ | Package 25.1.x dùng .NET 10; chưa build/kiểm | Build với `-p:AcadVersion=2026` khi có SDK .NET 10 và AutoCAD 2026.1; ghi kết quả vào tài liệu này |
| Ollama không phản hồi | Endpoint không phải loopback hoặc model chưa pull | `dhcb_ai.py ollama-check`; giữ `endpoint` = `http://127.0.0.1:11434` |

---

## 10. Ghi kết quả và báo lỗi

Sau mỗi vòng, điền bảng trên (✅/❌/⏭) và ghi vào `docs/progress.md` mục "Kết quả kiểm thử trên máy thật" theo mẫu:

```
### Vòng <ngày> — Revit <năm>, AutoCAD <năm>, Windows <bản>
- Đạt: R1–R14, R15–R24, C1–C17, B1–B8
- Lỗi: R32 PipeKick — "…thông báo…" (model: test-model.rvt, ống Id 12345)
- Bỏ qua: R37 (chưa có family thiết bị), B9–B12 (chưa có accoreconsole)
```

Với mỗi ❌: kèm ảnh chụp thông báo, file config JSON đã dùng, và (nếu được) file mẫu thu nhỏ tái hiện lỗi. Mở issue trên
GitHub theo mẫu đó; sửa lỗi đi kèm một test thuần tái hiện khi phần logic tách được (quy tắc §3 của
[`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md)).

**Định nghĩa "xong vòng 1":** mọi hàng §5.1–§5.3, §6, §7.1 đạt; §5.4 đạt trừ những hàng bị chặn bởi family; §7.2 đạt trên
ít nhất một bản AutoCAD.
