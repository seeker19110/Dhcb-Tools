# DHCB Tools — Revit & AutoCAD

Add-in **2-trong-1** (C#) tự động hoá các tác vụ lặp lại cho kỹ sư xây dựng, chạy trực tiếp trên **Revit desktop**
và **AutoCAD desktop**, có **batch chạy đêm**, **HTTP Bridge/MCP cho agent AI**, và **lớp AI offline** (không dữ liệu
nào rời máy). Nghiên cứu và lộ trình ở [`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md),
hiện trạng ở [`docs/progress.md`](docs/progress.md).

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
│   └── Ai/         CommandCatalog, CommandIntentParser, LayerMappingSuggester, SpecTextExtractor, WarningAnalyzer, OllamaClient
├── DhcbTools.Shared.Hosting/      # CommandResult, ICoreCommand<TConfig,TDocument>, HttpBridgeServer (token, khoá, timeout)
├── DhcbTools.Core/                # Core Revit — logic thuần, KHÔNG TaskDialog/WPF
│   ├── RevitCommandTable.cs       # dispatch theo tên lệnh — dùng chung Bridge/batch/Ribbon/AI
│   ├── ParameterSync, ModelCleanup, AutoNumbering, Export, Health, Query
│   ├── ProjectInit/               # Level, Grid, Family, ProjectInfo, ProjectFromTemplate, TransferStandards, GridFromCsv, SheetBatchCreate
│   ├── MEPF/                      # Sleeve, ElevationTag, Hanger, PipeSplitter, ConnectorChecker,
│   │                              #   RouteFromLines (A), DevicePlacement (B), Sizing, SystemColor/Name, FlowNumbering
│   ├── Checks/                    # ParameterRuleCheck, ClashDetection
│   ├── Updaters/                  # ElevationUpdater (IUpdater, tắt mặc định)
│   ├── Ai/                        # CadLayerMap, SpecToConfig
│   └── Batch/                     # BatchJobRunner (mở → chạy step → lưu → đóng)
├── DhcbTools.Revit/               # Vỏ Revit: Ribbon 5 panel, Bridge 8765, hook batch, WPF (AutoNumbering, AI chat)
├── DhcbTools.Core.AutoCAD/        # Core AutoCAD: AcadCommandTable, LayerSync, DrawingCleanup, AutoNumbering, Attributes,
│                                  #   Text (TextReplace), Standards (LayerStandardCheck, GridExtract, XrefAudit, CadLayerMap), Query
├── DhcbTools.AutoCAD/             # Vỏ AutoCAD: CommandMethod DHCB_*, Bridge 8766, DHCB_RUN cho batch, DHCB_AI
└── DhcbTools.BatchRunner/         # Console chạy đêm (Revit qua add-in, AutoCAD qua accoreconsole), báo cáo, mã thoát
scripts/  dhcb_agent.py · dhcb_mcp_server.py · dhcb_ai.py · install-nightly-task.ps1 · check-build.sh
jobs/     nightly.sample.json · autocad-nightly.sample.json
configs/  parameter-rules · layer-rules · ai · settings (mẫu)
tests/    DhcbTools.Shared.Logic.Tests (295 test, chạy trên CI Linux)
```

## Lệnh

Mọi lệnh có cùng chữ ký `Document/Database + config → CommandResult`, `dryRun` mặc định bật, chạy được từ 4 chỗ:
Ribbon/dòng lệnh, HTTP Bridge, batch runner, lớp AI. Danh mục đầy đủ: `python scripts/dhcb_agent.py revit tools`.

| Nhóm | Revit | AutoCAD |
|---|---|---|
| Dữ liệu ↔ CSV | `ParameterExport` / `ParameterImport` | `LayerExport` / `LayerImport`, `AttributeExport` / `AttributeImport` |
| Dọn dẹp | `RemoveUnusedViews` | `DrawingCleanup` (an toàn: CLAYER, linetype của layer, xref) |
| Đánh số | `AutoNumbering` (theo vị trí), `FlowNumbering` (theo dòng chảy) | `AutoNumbering` (block attribute) |
| Xuất & báo cáo | `BatchExport` (PDF/DWG/IFC/NWC), `HealthReport` | `XrefAudit` |
| Kiểm tra | `ParameterRuleCheck`, `ClashDetection` (+ `clash-accepted.json`), `ConnectorChecker` | `LayerStandardCheck`, `TextReplace` |
| Dự án & hồ sơ | `ProjectFromTemplate`, `TransferStandards`, `LevelSetup`, `GridSetup`, `GridFromCsv`, `FamilyLoader`, `ProjectInfo`, `SheetBatchCreate` | `GridExtract` (layer AXIS → CSV cho `GridFromCsv`) |
| MEPF | `SleeveAuto`, `ElevationTag`, `HangerAuto`, `PipeSplitter`, `RouteFromLines`, `DevicePlacement`, `SizingProposal` / `ApplySizing`, `SystemColor`, `SystemName` | — |
| AI offline | `CadLayerMap`, `SpecToConfig`, nút *Ra lệnh tiếng Việt* | `CadLayerMap`, `DHCB_AI` |

Lệnh AutoCAD trên dòng lệnh: `DHCB` (trợ giúp), `DHCB_LAYER_EXPORT/IMPORT`, `DHCB_CLEANUP`, `DHCB_AUTONUMBER`,
`DHCB_ATTR_EXPORT/IMPORT`, `DHCB_TEXT_REPLACE`, `DHCB_XREF_AUDIT`, `DHCB_GRID_EXTRACT`, `DHCB_LAYER_CHECK`,
`DHCB_LAYERMAP`, `DHCB_EXEC <Lệnh>` (config JSON), `DHCB_CFG <Lệnh>` (tạo config mẫu), `DHCB_AI`, `DHCB_RUN` (batch).

Nút Ribbon Revit mới dùng chung một khuôn: đọc config ở `%APPDATA%\DHCB\configs\revit\<Lệnh>.json` (tự tạo mẫu lần
đầu) → chạy **xem trước** → hỏi xác nhận → chạy thật.

## HTTP Bridge, agent và MCP

Revit `http://127.0.0.1:8765`, AutoCAD `http://127.0.0.1:8766`. Token sinh lần đầu ở `%APPDATA%\DHCB\bridge-token.txt`
(header `Authorization: Bearer …`, sai 5 lần/60 s → khoá 5 phút). Endpoint: `GET /health`, `GET /tools`,
`POST /execute`, `POST /query`, `POST /chat` (đề xuất lệnh từ tiếng Việt, không chạy). Lệnh client bỏ đi vì timeout
**không** được chạy.

```bash
python scripts/dhcb_agent.py revit tools
python scripts/dhcb_agent.py revit chat "đánh số cửa tầng 3 tiền tố D- 3 chữ số"
python scripts/dhcb_agent.py revit exec HangerAuto --config '{"hangerFamilyName":"DHCB_Hanger","spacingMm":2500}'
python scripts/dhcb_agent.py autocad exec GridExtract --config '{"gridLayer":"AXIS","outputPath":"C:/tmp/grids.csv"}'
python scripts/dhcb_mcp_server.py revit        # MCP server stdio cho Claude Desktop / Claude Code
```

Chi tiết lớp AI offline (heuristic mặc định, Ollama local tuỳ chọn): [`docs/ai-offline.md`](docs/ai-offline.md).

## Batch chạy đêm

```powershell
DhcbTools.BatchRunner.exe --job jobs\nightly.json --log-dir D:\DHCB\logs --max-minutes 480 --analyze
.\scripts\install-nightly-task.ps1 -Job D:\DHCB\jobs\nightly.json -RunnerExe D:\DHCB\bin\DhcbTools.BatchRunner.exe -Time 23:00
```

Ra `run.jsonl`, `report.html`, `warnings-summary.md`; mã thoát 0/1/2 cho Task Scheduler. Chi tiết:
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

Packages: Revit `Nice3point.Revit.Api.RevitAPI/RevitAPIUI`, AutoCAD `AutoCAD.NET`. Revit 2021–2024 và AutoCAD ≤2024 dùng
net48, 2025+ dùng net8.0-windows (chọn bằng `-p:RevitVersion`).

## Triển khai (dev)

- **Revit:** copy `DhcbTools.Revit.addin` + `DhcbTools.Revit.dll`, `DhcbTools.Core.dll`, `DhcbTools.Shared.*.dll`,
  `Newtonsoft.Json.dll` vào `%ProgramData%\Autodesk\Revit\Addins\<version>\`.
- **AutoCAD:** `NETLOAD DhcbTools.AutoCAD.dll` (kèm `DhcbTools.Core.AutoCAD.dll`, `DhcbTools.Shared.*.dll`), hoặc đặt vào
  `%AppData%\Autodesk\ApplicationPlugins\`.
- **Tuỳ chọn:** `%APPDATA%\DHCB\settings.json` (bật `ElevationUpdater`), `%APPDATA%\DHCB\ai.json` (model local) — mẫu trong `configs/`.

## Trạng thái

Toàn bộ giai đoạn 0–5 và 6.1/6.2 của [`docs/dac-ta-tinh-nang.md`](docs/dac-ta-tinh-nang.md) đã có mã nguồn, biên dịch
xanh với API Revit 2023/2024/2025 và AutoCAD 2024/2025, 295 test thuần xanh. **Chưa kiểm thử trên Revit/AutoCAD thật** cho
các lệnh mới — kịch bản ở [`docs/dac-ta-kiem-thu.md`](docs/dac-ta-kiem-thu.md) §4. Chi tiết và lỗi còn mở:
[`docs/progress.md`](docs/progress.md) · lộ trình: [`docs/roadmap.md`](docs/roadmap.md).
