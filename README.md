# DHCB Tools — Revit & AutoCAD

Add-in **2-trong-1** (C#) tự động hoá các tác vụ lặp lại cho kỹ sư xây dựng, chạy trực tiếp trên **Revit desktop**
và **AutoCAD desktop**, có **batch chạy đêm**, **HTTP Bridge/MCP cho agent AI**, và **lớp AI offline** (heuristic +
Ollama local — không dữ liệu nào rời máy). Ngoại lệ duy nhất: **panel web AutoCAD** (`tools/autocad-mcp-server`) gọi
Hermes CLI nên prompt kèm nội dung bản vẽ **được gửi tới provider inference đang cấu hình** — xem
[`tools/autocad-mcp-server/README.md`](tools/autocad-mcp-server/README.md#dữ-liệu-đi-đâu). Nghiên cứu và lộ trình ở [`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md),
khoảng trống theo chặng công việc (thiết kế → BIM → shop → thi công → hoàn công) ở
[`docs/nghien-cuu-chuoi-den-hoan-cong.md`](docs/nghien-cuu-chuoi-den-hoan-cong.md),
hiện trạng ở [`docs/progress.md`](docs/progress.md).

## Bắt đầu nhanh (10 phút)

**Cần có:**

| Thành phần | Bản | Bắt buộc khi |
|---|---|---|
| Windows | 10/11 x64 | Chạy add-in (chỉ để build/test thuần thì Linux/macOS cũng được) |
| .NET SDK | 8.0.x **+ 10.0.x** | SDK 8 build net48/net8.0-windows; SDK 10 cho AutoCAD ≥ 2026 và Revit ≥ 2027 (net10.0-windows) |
| Revit | 2023–2025 (2026/2027 build được, chưa chạy thật) | Dùng add-in Revit |
| AutoCAD | 2024–2026 | Dùng plugin AutoCAD (2026.1 dùng .NET 10 — đã chạy thật qua accoreconsole) |
| Python | 3.9+ | Dùng `scripts/*.py` (client Bridge, MCP server, AI offline) |
| Node/npx | bất kỳ LTS | **Chỉ** khi đóng gói `.mcpb` bằng `scripts/pack-mcpb.ps1` |
| Hermes CLI | — | **Chỉ** cho panel web AutoCAD (`tools/autocad-mcp-server`) |
| Ollama | — | Tuỳ chọn: tinh chỉnh lớp AI offline (mặc định chạy heuristic, không cần) |

**Chạy test tại chỗ** (không cần cài Revit/AutoCAD — đúng hai việc CI chạy):

```bash
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -c Release
python3 -m pytest tools/autocad-mcp-server -q      # cần: pip install -r requirements-dev.txt
```

Cài để dùng thật: [Cài đặt](#cài-đặt) · quy trình kiểm thử tay:
[`docs/huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](docs/huong-dan-cai-dat-va-kiem-thu-thu-cong.md).

## Cấu trúc solution

```
Dhcb-Tools.sln
Directory.Build.props              # multi-target net48 (Revit/AutoCAD ≤2024) / net8.0-windows (2025+)
src/
├── DhcbTools.Shared.Logic/        # Logic thuần, KHÔNG Revit/AutoCAD — có test xUnit
│   ├── CsvText, NumericText, NumberingPlanner, MepLayout, FileNaming, HtmlText, BridgeAuth, CleanupDecider
│   ├── Batch/      JobTokens, BatchJob, RunLog (JSONL), BatchReport (HTML), AcadScriptGen (accoreconsole)
│   ├── Geometry/   GridClustering, GridNaming (trục từ bản CAD/Excel)
│   ├── Mep/        RouteGraph, DevicePattern, DuctSizing, PipeSizing, SystemNaming, FlowNumbering, PathFinder3D
│   ├── Checks/     RuleChecker, ClashAcceptance
│   ├── Setout/     SetoutPlanner, SetoutCsv, SetoutDxf (toạ độ định vị) · Geometry/GridIntersections
│   ├── Progress/   ConstructionStatusValue, StatusRoll, WeeklyProgress, ProgressCsv (tiến độ thi công)
│   └── Ai/         CommandCatalog, CommandIntentParser, LayerMappingSuggester, SpecTextExtractor, WarningAnalyzer, OllamaClient
├── DhcbTools.Shared.Hosting/      # CommandResult, ICoreCommand<TConfig,TDocument>, HttpBridgeServer (token, khoá, timeout)
├── DhcbTools.Core/                # Core Revit — logic thuần, KHÔNG TaskDialog/WPF
│   ├── RevitCommandTable.cs       # dispatch theo tên lệnh — dùng chung Bridge/batch/Ribbon/AI
│   ├── ParameterSync, ModelCleanup, AutoNumbering, Export (BatchExport, SetoutExport), Health, Query
│   ├── ProjectInit/               # Level, Grid, Family, ProjectInfo, ProjectFromTemplate, TransferStandards, GridFromCsv, SheetBatchCreate
│   ├── MEPF/                      # Sleeve, ElevationTag, Hanger, PipeSplitter, ConnectorChecker,
│   │                              #   RouteFromLines (A), DevicePlacement (B), Sizing, SystemColor/Name, FlowNumbering
│   ├── Checks/                    # ParameterRuleCheck, ClashDetection
│   ├── Progress/                  # ConstructionStatus (ghi trạng thái), ProgressReport (HTML + CSV)
│   ├── Updaters/                  # ElevationUpdater (IUpdater, tắt mặc định)
│   ├── Ai/                        # CadLayerMap, SpecToConfig, DictionaryLearn
│   └── Batch/                     # BatchJobRunner (mở → chạy step → lưu → đóng)
├── DhcbTools.Revit/               # Vỏ Revit: Ribbon 6 panel phủ đủ 46 lệnh, Bridge 8765, hook batch
│                                  #   (pending-job.json), ElevationUpdater, WPF AutoNumbering
├── DhcbTools.Core.AutoCAD/        # Core AutoCAD: AcadCommandTable, LayerSync, DrawingCleanup, AutoNumbering, Attributes,
│                                  #   Text (TextReplace), Standards (LayerStandardCheck, GridExtract, XrefAudit, CadLayerMap), Query
├── DhcbTools.AutoCAD/             # Vỏ AutoCAD: 16 lệnh CommandMethod DHCB_*, Bridge 8766
├── DhcbTools.AutoCAD.Core/        # Vỏ core-only (AcDbMgd/AcCoreMgd, không AcMgd): DHCB_RUN cho accoreconsole
└── DhcbTools.BatchRunner/         # Console chạy đêm (Revit qua add-in, AutoCAD qua accoreconsole), báo cáo, mã thoát
scripts/  dhcb_agent.py · dhcb_mcp_server.py · dhcb_ai.py           # client Bridge, MCP server, AI offline
          check-build.sh                                            # biên dịch Core + vỏ bằng API package (Linux/CI)
          run-in-revit-tests.ps1 · run-in-autocad-tests.ps1          # chạy bộ ca kiểm bên trong Revit / accoreconsole
          install-nightly-task.ps1                                   # đăng ký Task Scheduler chạy batch đêm
          sign-addin.ps1 · pack-mcpb.ps1                             # ký DLL · đóng gói .mcpb (cần Node/npx)
tools/    autocad-mcp-server/ (MCP + panel web cho Hermes) · mcpb/ (manifest gói Claude Desktop)
installer/ dhcb-tools.iss (Inno Setup) · PackageContents.xml
jobs/     nightly.sample.json · autocad-nightly.sample.json
configs/  parameter-rules · layer-rules · ai · settings · dictionary (mẫu)
tests/    DhcbTools.Shared.Logic.Tests (chạy trên CI Linux — số ca xem output CI) + suites/ (ca kiểm chạy trong Revit & AutoCAD)
```

## Lệnh

Mọi lệnh có cùng chữ ký `Document/Database + config → CommandResult`, `dryRun` mặc định bật, chạy được từ 4 chỗ:
Ribbon/dòng lệnh, HTTP Bridge, batch runner, lớp AI. Danh mục đầy đủ: `python scripts/dhcb_agent.py revit tools`.

| Nhóm | Revit | AutoCAD |
|---|---|---|
| Dữ liệu ↔ CSV | `ParameterExport` / `ParameterImport` | `LayerExport` / `LayerImport`, `AttributeExport` / `AttributeImport` |
| Dọn dẹp | `RemoveUnusedViews` | `DrawingCleanup` (an toàn: CLAYER, linetype của layer, xref) |
| Đánh số | `AutoNumbering` (theo vị trí), `FlowNumbering` (theo dòng chảy) | `AutoNumbering` (block attribute) |
| Xuất & báo cáo | `BatchExport` (PDF/DWG/IFC/NWC), `HealthReport`, `SetoutExport` (toạ độ định vị cho máy toàn đạc — *thử nghiệm*, [`docs/toa-do-dinh-vi.md`](docs/toa-do-dinh-vi.md)) | `XrefAudit` |
| Kiểm tra | `ParameterRuleCheck`, `ClashDetection` (+ `clash-accepted.json`), `ConnectorChecker` | `LayerStandardCheck`, `TextReplace` |
| Dự án & hồ sơ | `ProjectFromTemplate`, `TransferStandards`, `LevelSetup`, `GridSetup`, `GridFromCsv`, `FamilyLoader`, `ProjectInfo`, `SheetBatchCreate` | `GridExtract` (layer AXIS → CSV cho `GridFromCsv`) |
| MEPF | `SleeveAuto`, `ElevationTag`, `HangerAuto`, `PipeSplitter`, `RouteFromLines`, `DevicePlacement`, `SizingProposal` / `ApplySizing`, `SystemColor`, `SystemName` | — |
| Hồ sơ & style (giai đoạn 7) | `SheetRename`, `RevisionOnSheets`, `StylePurge`, `ColorByParameter`, `FamilyAudit`, `WarningsExport`, `ScheduleExport`, `ViewportCopy` | `LayerTranslate`, `DrawingCompare`, `BlockQuantity`, `AttributeIncrement` |
| MEPF nâng cao (P2) | `SlopePipes`, `PipeKick`, `SystemBom`, `AutoRoute` (mức C → mức A) | — |
| Thi công & hoàn công | `ConstructionStatus`, `ProgressReport` (tiến độ theo tầng/hệ, % theo số lượng và chiều dài — *thử nghiệm*, [`docs/tien-do-thi-cong.md`](docs/tien-do-thi-cong.md)) | — |
| AI offline | `CadLayerMap`, `SpecToConfig`, `DictionaryLearn`, nút *Ra lệnh tiếng Việt* | `CadLayerMap` (`DHCB_LAYER_MAP`); ra lệnh tiếng Việt qua Bridge `POST /chat` |

Lệnh AutoCAD trên dòng lệnh — đúng các `[CommandMethod]` có trong `src/DhcbTools.AutoCAD`:
`DHCB_LAYER_EXPORT`, `DHCB_LAYER_IMPORT`, `DHCB_CLEANUP`, `DHCB_AUTONUMBER`, `DHCB_ATTR_EXPORT`,
`DHCB_ATTR_IMPORT`, `DHCB_ATTR_INCREMENT`, `DHCB_TEXT_REPLACE`, `DHCB_LAYER_CHECK`, `DHCB_LAYER_MAP`,
`DHCB_LAYER_TRANSLATE`, `DHCB_GRID_EXTRACT`, `DHCB_XREF_AUDIT`, `DHCB_DRAWING_COMPARE`,
`DHCB_BLOCK_QUANTITY`, `DHCB_BRIDGE` (bật/tắt HTTP Bridge).
`DHCB_RUN` (batch qua accoreconsole) nằm ở vỏ **core-only** `DhcbTools.AutoCAD.Core`, không có trong vỏ đầy đủ.

Nút Ribbon Revit dùng chung **một form động** dựng từ `CommandCatalog`: mỗi trường config thành một ô nhập đúng kiểu
(checkbox, ô số, nút chọn file/thư mục, combo category/tham số/level/view/family lấy từ mô hình đang mở). Bấm
*Xem trước* chạy `dryRun` và hiện kết quả; nút *Chạy thật* chỉ mở sau khi xem trước thành công. Giá trị đã nhập được
lưu ở `%APPDATA%\DHCB\configs\revit\<Lệnh>.json` cho lần sau.

## HTTP Bridge, agent và MCP

Revit `http://127.0.0.1:8765`, AutoCAD `http://127.0.0.1:8766`. Token sinh lần đầu ở `%APPDATA%\DHCB\bridge-token.txt`
(header `Authorization: Bearer …`, sai 5 lần/60 s → khoá 5 phút). Endpoint: `GET /health`, `GET /tools`,
`POST /execute`, `POST /query`, `POST /chat` (đề xuất lệnh từ tiếng Việt, không chạy; trần riêng 60 giây),
`GET /progress/<id>`.

| Mã | Khi nào |
|---|---|
| `401` / `429` | Sai token · khoá 5 phút vì dò token, **hoặc** `/execute` async khi hàng đợi đã đủ 20 job |
| `413` | Body quá 4 MB |
| `415` | Sai `Content-Type` — trước đây lẫn vào `401` và bị tính nhầm vào bộ đếm dò token |
| `503` | Quá 8 request đang xử lý cùng lúc |
| `504` | Hết thời gian chờ của `/execute` đồng bộ |

**Về `504`:** chỉ khi Bridge giành được quyền huỷ **trước lúc lệnh bắt đầu** thì mới khẳng định lệnh *không chạy*.
Ngược lại `504` kèm `id` + `progressUrl` và nghĩa là **"có thể đã chạy — đừng gửi lại"**: hỏi `GET /progress/<id>`
để biết chắc. `/progress` có thêm trạng thái `abandoned` và cờ `started`; phản hồi `202` kèm `timeoutSeconds`.
Lỗi `500` không trả nội dung exception ra ngoài nữa (chi tiết nằm trong log).

**Truy vấn đọc (`POST /query`)** — Revit 17 loại, AutoCAD 12. Ngoài các truy vấn đếm/liệt kê cơ bản
(`document_info`, `levels`, `views`, `sheets`, `rooms`, `elements`, `families`, `warnings`, `links`, `stats`)
còn phần đủ để agent **nhìn, chỉ và kiểm** được kết quả: `parameters_of` (tham số của category, để dựng
config không phải đoán), `element_geometry` (hộp bao, đường tâm, connector kèm tình trạng nối — toạ độ mm),
`schedule_rows`, `snapshot` (ảnh PNG base64 của view), `selection` (đọc và **đặt** lựa chọn),
`show_elements` (zoom cho kỹ sư nhìn), `active_view`. Phía AutoCAD là bộ đối xứng — `entity_geometry`,
`attributes_of`, `selection`, `show_entities`, `active_layout` — định danh bằng **handle** hex.
Mọi `CommandResult` mang theo `changedIds` nên agent kiểm lại được đúng phần tử vừa đổi.
Chi tiết: [`docs/agent-khep-vong.md`](docs/agent-khep-vong.md).

**Lệnh chạy lâu**: gửi `POST /execute` kèm `"async": true` → nhận ngay `202 {id}`, rồi hỏi
`GET /progress/<id>` tới khi `status` là `done`. Kết quả nằm ở server theo id nên đứt kết nối giữa chừng
không làm mất kết quả của việc đã chạy xong (giữ 30 phút hoặc 50 lệnh gần nhất). Từ client:
`python scripts/dhcb_agent.py revit HangerAuto --background …`.

```bash
python scripts/dhcb_agent.py revit tools
python scripts/dhcb_agent.py revit chat "đánh số cửa tầng 3 tiền tố D- 3 chữ số"
python scripts/dhcb_agent.py revit exec HangerAuto --config '{"hangerFamilyName":"DHCB_Hanger","spacingMm":2500}'
python scripts/dhcb_agent.py autocad exec GridExtract --config '{"gridLayer":"AXIS","outputPath":"C:/tmp/grids.csv"}'
python scripts/dhcb_mcp_server.py revit        # MCP server stdio cho Claude Desktop / Claude Code
```

Chi tiết lớp AI offline (heuristic mặc định, Ollama local tuỳ chọn) **và cấu hình MCP cho Claude Desktop /
Claude Code**: [`docs/ai-offline.md`](docs/ai-offline.md) — cấu hình `mcpServers` chỉ chép ở một chỗ đó.

## Batch chạy đêm

```powershell
DhcbTools.BatchRunner.exe --job jobs\nightly.json --log-dir D:\DHCB\logs --max-minutes 480 --analyze
.\scripts\install-nightly-task.ps1 -Job D:\DHCB\jobs\nightly.json -RunnerExe D:\DHCB\bin\DhcbTools.BatchRunner.exe -Time 23:00
```

Ra `logs/{yyyy-MM-dd}/run-HHmmss.jsonl` (mỗi lượt chạy một file log), `report.html`, `warnings-summary.md`;
mã thoát 0/1/2 cho Task Scheduler. Job có thêm `saveOnError` (mặc định `false`) và `dwgVersion` (mặc định `"2018"`);
**bên AutoCAD `saveMode: "Save"` nay lưu đè file gốc thật**. Chi tiết:
[`docs/batch-runner.md`](docs/batch-runner.md).

## Build

```powershell
# Windows có Revit/AutoCAD (bản đầy đủ, kèm WPF)
dotnet build src/DhcbTools.Revit/DhcbTools.Revit.csproj      -p:RevitVersion=2024
dotnet build src/DhcbTools.AutoCAD/DhcbTools.AutoCAD.csproj  -p:RevitVersion=2024 -p:AcadVersion=2024
dotnet build src/DhcbTools.BatchRunner/DhcbTools.BatchRunner.csproj
```

```bash
# Mọi hệ điều hành (CI): test logic thuần + biên dịch toàn bộ Core/vỏ bằng API package NuGet, không cần cài phần mềm
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj
./scripts/check-build.sh
```

**Kiểm thử chạy bên trong Revit/AutoCAD** (phần chạm API thật, không test được trên CI):

```powershell
.\scripts\run-in-revit-tests.ps1 -Suite smoke|mep|plumbing   # tự build, cài add-in, mở/chạy/đóng Revit
.\scripts\run-in-autocad-tests.ps1 -Suite smoke              # tự build, chạy qua accoreconsole
```

Bộ ca kiểm JSON ở [`tests/suites/`](tests/suites/), báo cáo ra TRX + Markdown, mã thoát khác 0 khi có ca trượt.
Chi tiết: [`docs/kiem-thu-trong-revit.md`](docs/kiem-thu-trong-revit.md), bằng chứng đã chạy:
[`docs/bang-chung-test.md`](docs/bang-chung-test.md), [`docs/bang-chung-test-autocad-live.md`](docs/bang-chung-test-autocad-live.md).

Packages: Revit `Nice3point.Revit.Api.RevitAPI/RevitAPIUI`, AutoCAD `AutoCAD.NET` (vỏ đầy đủ) và `AutoCAD.NET.Core/.Model`
(Core + vỏ core-only). Revit 2021–2024 và AutoCAD ≤2024 dùng net48; Revit 2025–2026 và AutoCAD 2025 dùng net8.0-windows;
AutoCAD ≥ 2026 (package 25.1.x) và Revit ≥ 2027 dùng **net10.0-windows** — `Directory.Build.props` là nơi duy nhất quyết
định TFM theo `-p:RevitVersion` / `-p:AcadVersion`; `release.yml` hỏi lại MSBuild thay vì tự tính.

## CI/CD

- **CI** (`.github/workflows/tests.yml`, ubuntu-latest, mọi push/PR): test `Shared.Logic` + `dotnet build` toàn bộ
  Core/vỏ (kể cả vỏ core-only) bằng API package NuGet, `UseWPF=false` — bắt lỗi biên dịch không cần Windows.
- **CD** (`.github/workflows/release.yml`, windows-latest, khi đẩy tag `vX.Y.Z` hoặc chạy tay): build **Release thật**
  (đủ WPF) cho Revit 2023/2024/2025 và AutoCAD 2024/2025 + vỏ core-only, đóng gói zip kèm hướng dẫn cài đặt, và tạo
  GitHub Release đính kèm toàn bộ gói.

```powershell
git tag v1.0.0 && git push origin v1.0.0   # kích hoạt release.yml
```

## Gặp lỗi có mã `E-...`?

Tra ở [`docs/ma-loi.md`](docs/ma-loi.md) — mỗi mã kèm nghĩa, khi nào gặp và cách xử lý.

## Cài đặt và kiểm thử trên máy thật

Quy trình đầy đủ (build → cài → file mẫu → checklist Revit/AutoCAD/batch/MCP → ghi kết quả):
[`docs/huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](docs/huong-dan-cai-dat-va-kiem-thu-thu-cong.md).

## Cài đặt

**Cách thường dùng — installer.** Tải `DhcbTools-Setup-<phiên bản>.exe` ở
[Releases](https://github.com/seeker19110/Dhcb-Tools/releases), chọn phiên bản Revit/AutoCAD cần cài. Installer chạy
theo người dùng (không cần quyền admin): add-in Revit vào `%APPDATA%\Autodesk\Revit\Addins\<năm>\`, plugin AutoCAD
vào bundle `%APPDATA%\Autodesk\ApplicationPlugins\DhcbTools.bundle\` nên **tự nạp khi khởi động, không cần
`NETLOAD`**. Nguồn: [`installer/dhcb-tools.iss`](installer/dhcb-tools.iss).

**Chép tay (dev).**

- **Revit:** copy `DhcbTools.Revit.addin` + `DhcbTools.Revit.dll`, `DhcbTools.Core.dll`, `DhcbTools.Shared.*.dll`,
  `Newtonsoft.Json.dll` vào `%APPDATA%\Autodesk\Revit\Addins\<năm>\`.
- **AutoCAD:** `NETLOAD DhcbTools.AutoCAD.dll` (kèm `DhcbTools.Core.AutoCAD.dll`, `DhcbTools.Shared.*.dll`), hoặc đặt vào
  `%AppData%\Autodesk\ApplicationPlugins\`.
- **Tuỳ chọn:** `%APPDATA%\DHCB\settings.json` (bật `ElevationUpdater`), `%APPDATA%\DHCB\ai.json` (model local), `%APPDATA%\DHCB\dictionary.json` (tên tham số/family của dự án — xem *Từ điển tham số* bên dưới) — mẫu trong `configs/`.

## Từ điển tham số

Các tra cứu tham số **của lệnh MEPF và khởi tạo dự án** không còn gọi thẳng `LookupParameter("Level")`
mà đi qua `RevitCompat` + lớp từ điển (vẫn còn vài chỗ gọi trực tiếp, ví dụ tham số do chính kỹ sư đặt tên
trong config, hoặc `Level` của view trong `SheetCommands`). Mỗi khoá logic (`level`, `diameter`, `bottomElevation`…) có một
danh sách tên đồng nghĩa Anh–Việt; dự án khai thêm tên riêng trong `%APPDATA%\DHCB\dictionary.json`
(mẫu: [`configs/dictionary.sample.json`](configs/dictionary.sample.json)). Tên khai trong file đứng trước tên dựng sẵn
chứ không thay thế, nên dự án dùng thư viện chuẩn chạy được mà không cần file này.

Tra không ra thì lệnh **báo lỗi `E-PARAM-MISSING` kèm danh sách tên đã thử**, không im lặng bỏ qua rồi báo thành công.

## Phiên bản và log

- **Phiên bản** đi từ tag git vào DLL (`release.yml` truyền `-p:Version=`), nên `GET /health` trả đúng bản đang chạy.
  Build tại chỗ không truyền gì thì là `0.9.0-dev`.
- **Log**: `%APPDATA%\DHCB\logs\<Revit|AutoCAD>-<ngày>.log` — khởi động add-in, trạng thái Bridge, và stack trace đầy
  đủ của mọi lệnh lỗi (hộp thoại chỉ hiện một dòng tóm tắt). Giữ 30 ngày, tự dọn lúc khởi động.

## Trạng thái

Toàn bộ giai đoạn 0–6 của [`docs/dac-ta-tinh-nang.md`](docs/dac-ta-tinh-nang.md) và cả P1 lẫn P2 giai đoạn 7
([`docs/nghien-cuu-tool-thi-truong-va-ke-hoach.md`](docs/nghien-cuu-tool-thi-truong-va-ke-hoach.md) — khoảng trống so với
pyRevit, DiRoots, Ideate, Colour Splasher, LAYTRANS, Drawing Compare, RevitBatchProcessor) đã có mã nguồn và biên dịch xanh
với API Revit/AutoCAD 2023–2027 (ma trận CI, gồm cả đường .NET 10); số test thuần xem output CI (`tests.yml` → artifact `test-results`).

**Đã chạy trên phần mềm thật:** 43/43 lệnh Revit *của vòng 2026-09-04* có ít nhất một ca kiểm chạy bên trong Revit 2024.3
và 15/15 lệnh AutoCAD có ca kiểm qua `accoreconsole`, cộng một đêm batch trên **dự án thật** — bằng chứng và số liệu từng vòng:
[`docs/bang-chung-test.md`](docs/bang-chung-test.md), NETLOAD trên AutoCAD thật:
[`docs/bang-chung-test-autocad-live.md`](docs/bang-chung-test-autocad-live.md). Phần **chưa** khép: chất lượng tuyến của `AutoRoute` (còn nhãn
*thử nghiệm*), ba lệnh chặng thi công mới thêm 2026-09-05 (`SetoutExport` — [`docs/toa-do-dinh-vi.md`](docs/toa-do-dinh-vi.md);
`ConstructionStatus` và `ProgressReport` — [`docs/tien-do-thi-cong.md`](docs/tien-do-thi-cong.md); đều có ca kiểm, **chưa chạy thật**),
chạy thật trên Revit 2026/2027 (máy chỉ có 2024.3), và 9.4 — đưa cho một nhóm kỹ sư dùng thật. Chi tiết và lỗi còn mở:
[`docs/progress.md`](docs/progress.md) · lộ trình: [`docs/roadmap.md`](docs/roadmap.md).
