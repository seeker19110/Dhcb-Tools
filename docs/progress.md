# Hiện trạng dự án

Ảnh chụp tại thời điểm cập nhật gần nhất. Kế hoạch phía trước xem [`roadmap.md`](roadmap.md).

> Cập nhật lần cuối: 2026-09-01 · Nhánh `claude/autocad-revit-offline-ai-features` — hoàn tất Giai đoạn 0–5 và 6.1/6.2
> theo [`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md); phần cần Revit/AutoCAD thật đã biên dịch bằng API package NuGet,
> chờ kiểm thử thủ công theo [`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md) §4.

## Tóm tắt

| Hạng mục | Trạng thái |
|---|---|
| Kiến trúc Core / vỏ UI | ✅ `Shared.Logic` (thuần) + `Shared.Hosting` (CommandResult, ICoreCommand, HTTP server) dùng chung hai nền tảng |
| Lệnh nền tảng (Revit + AutoCAD) | ✅ |
| HTTP Bridge cho agent AI | ✅ Token, khoá khi dò token, bind 127.0.0.1, timeout huỷ lệnh, `/tools`, `/chat` |
| Batch export + Health report | ✅ |
| Khởi tạo dự án | ✅ Grid/Level/Family/Project info + **file từ template, transfer standards, trục/level từ CSV (CAD/Excel), sheet hàng loạt** |
| MEPF nền tảng (sleeve, cao độ, hanger, chia ống, connector) | ✅ Đã gắn đủ Ribbon + Bridge + batch |
| MEPF routing A (theo line), B (rải thiết bị theo phòng) | ✅ Core + Ribbon + Bridge; chờ kiểm thử trên model mẫu |
| MEPF sizing (đề xuất → CSV → áp), màu/tên hệ, đánh số theo dòng chảy | ✅ |
| Batch runner chạy đêm (Revit + AutoCAD accoreconsole) | ✅ [`batch-runner.md`](batch-runner.md) |
| `IUpdater` cao độ theo sự kiện | ✅ Mặc định tắt, tự tắt khi > 200 ms |
| Checker tham số/đặt tên, clash nội bộ | ✅ HTML + 3D view, `clash-accepted.json` |
| Lớp AI (offline) | ✅ [`ai-offline.md`](ai-offline.md) — heuristic mặc định, Ollama local tuỳ chọn |
| Routing C (A* 3D) | ✅ Phần thuần `PathFinder3D`, chưa có lệnh Core dựng trực tiếp (kết quả là polyline cho routing A) |
| MCP server | ✅ `scripts/dhcb_mcp_server.py` |
| Giai đoạn 7 P2 | ✅ Mã nguồn: SlopePipes, PipeKick, SystemBom, AutoRoute (mức C → mức A), ScheduleExport, ViewportCopy; vỏ `DhcbTools.AutoCAD.Core` cho accoreconsole |
| Giai đoạn 7 — khoảng trống so với tool thị trường ([`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md)) | ✅ P1 xong mã nguồn: SheetRename, RevisionOnSheets, StylePurge, ColorByParameter, FamilyAudit, WarningsExport, checkset ngưỡng; AutoCAD LayerTranslate, DrawingCompare, BlockQuantity, AttributeIncrement, purge text/dim/regapp; batch autodetect phiên bản + PlotPdf; AI structured outputs + ≤8 ứng viên; MCP read-only/nhóm |
| Kiểm thử tự động | ✅ 340 test xUnit, chạy trên CI Linux |
| CI | ✅ test + check-build toàn bộ Core/vỏ bằng API package (RevitVersion=2025) |

Ước tính: hoàn thành khoảng **90 %** phạm vi tài liệu nghiên cứu về mặt mã nguồn. Phần còn lại là **kiểm thử trên
Revit/AutoCAD thật** (routing A/B, sizing, transfer standards, clash) và tinh chỉnh theo phản hồi dự án.

---

## Đã làm được

### Khung solution
`Shared.Logic` (netstandard2.0, thuần) ← `Shared.Hosting` (CommandResult, ICoreCommand<TConfig,TDocument>,
HttpBridgeServer, BridgeTokenStore, AuthLockout, BridgeWorkItem) ← `Core` (Revit) / `Core.AutoCAD` ← vỏ Revit / AutoCAD.
`BatchRunner` (net8.0 console) chỉ tham chiếu `Shared.Logic`. Không còn class trùng tên giữa hai Core
(`git grep -c "class CommandResult"` = 1).

### Bảng lệnh và danh mục
`RevitCommandTable` / `AcadCommandTable` là điểm dispatch duy nhất (Bridge, batch, Ribbon config-driven, AI đều gọi vào).
`Shared.Logic/Ai/CommandCatalog` liệt kê mọi lệnh + bí danh + trường config; test đối chiếu với mã nguồn.

### Danh sách lệnh Core

**Revit (42):** ParameterExport, ParameterImport, RemoveUnusedViews, AutoNumbering, BatchExport, HealthReport,
ProjectInfo, LevelSetup, GridSetup, FamilyLoader, SleeveAuto, ElevationTag, HangerAuto, PipeSplitter, ConnectorChecker,
RouteFromLines, DevicePlacement, SizingProposal, ApplySizing, SystemColor, SystemName, FlowNumbering,
ProjectFromTemplate, TransferStandards, GridFromCsv, SheetBatchCreate, **SheetRename, RevisionOnSheets, StylePurge,
ColorByParameter, FamilyAudit, WarningsExport**, **SlopePipes, PipeKick, SystemBom, AutoRoute, ScheduleExport, ViewportCopy** (P2),
ParameterRuleCheck (+ thresholds), ClashDetection, CadLayerMap, SpecToConfig.

**AutoCAD (15):** LayerExport, LayerImport, DrawingCleanup (+ text/dim style, regapp), AutoNumbering, AttributeExport,
AttributeImport, TextReplace, LayerStandardCheck, GridExtract, XrefAudit, **LayerTranslate, DrawingCompare, BlockQuantity,
AttributeIncrement**, CadLayerMap. Lệnh dòng lệnh mới: `DHCB_LAYTRANS`, `DHCB_COMPARE`, `DHCB_BLOCKCOUNT`, `DHCB_ATTR_INC`. Lệnh dòng lệnh: `DHCB`, `DHCB_*`, `DHCB_EXEC`, `DHCB_CFG`,
`DHCB_AI`, `DHCB_RUN`.

### Ribbon Revit
6 panel: Nền tảng · Xuất & Kiểm tra · Dự án & Hồ sơ · Hồ sơ & Style · MEPF · AI offline & Batch. Nút mới đều theo khuôn
`CommandRunner`: đọc config JSON ở `%APPDATA%\DHCB\configs\revit\<Lệnh>.json` (tự tạo mẫu lần đầu) → chạy xem trước →
hỏi xác nhận → chạy thật.

### Kiểm thử
`tests/DhcbTools.Shared.Logic.Tests`: CSV, số, đánh số, MEP layout, tên file, phiên bản xuất, HTML, token, **CleanupDecider,
JobTokens/BatchJob/RunLog/BatchReport/AcadScriptGen, GridClustering/GridNaming, RouteGraph, DevicePattern, Duct/PipeSizing,
SystemNaming, FlowNumbering, PathFinder3D, RuleChecker, ClashAcceptance, LayerMappingSuggester, SpecTextExtractor,
WarningAnalyzer, CommandIntentParser, CommandCatalog↔mã nguồn**. `scripts/check-build.sh` biên dịch toàn bộ Core/vỏ trên Linux.

---

## Kết quả kiểm thử trên máy thật

Chưa có vòng nào. Quy trình và checklist: [`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md) §10.

## Lỗi đã biết

Các lỗi #1–#11 trong bản trước **đã sửa**:

| # | Lỗi | Sửa ở |
|---|---|---|
| 1–5 | Round-trip số, nuốt cảnh báo, bất đối xứng export/import, CSV không BOM, đánh số không dung sai | `Shared.Logic` (bản trước) |
| 6 | DrawingCleanup xoá nhầm / hỏng transaction | `CleanupDecider` + `DrawingCleanupCommand` (linetype của layer, CLAYER, xref, try/catch từng item) |
| 7 | Request timeout vẫn thực thi | `BridgeWorkItem.TryClaim()` — vỏ kiểm tra trước khi mở transaction |
| 8 | Bridge không xác thực | `BridgeTokenStore` + `BridgeAuth` + `AuthLockout`, bind 127.0.0.1 |
| 9 | Trùng lặp giữa hai Core | `Shared.Hosting` |
| 10 | Hiệu năng collector | `ElementMulticategoryFilter` / `ElementLevelFilter` trong ParameterExport, AutoNumbering (và mọi lệnh mới) |
| 11 | Hanger/PipeSplitter chưa gắn UI/Bridge | `CatalogCommands` + `RevitCommandTable` |
| 12 | Không có test MEPF/Export/ProjectInit | Phần thuần đã có test; phần Revit theo §4 kiểm thử thủ công |

### Còn mở
- **Chưa kiểm thử trên Revit/AutoCAD thật** cho toàn bộ lệnh mới — chỉ mới biên dịch với API package. Rủi ro cao nhất:
  `RouteFromLines` (fitting phụ thuộc routing preference), `TransferStandards` (LineStyles/ObjectStyles không copy được
  qua API — đã ghi rõ trong Messages), `ProjectFromTemplate` (worksharing cần môi trường mạng).
- `ParameterImport` vẫn đọc CSV theo dòng nên chưa đọc ô có xuống dòng bên trong nháy (nợ cũ).
- `PathFinder3D` chưa có lệnh Core gọi trực tiếp; dự kiến: chọn hai điểm → polyline → vẽ model line → `RouteFromLines`.
- Batch Revit thoát bằng `Environment.Exit` sau khi ghi `batch-done.json` — đủ dùng cho Task Scheduler nhưng không "đẹp";
  Revit không có API thoát cho add-in.
