# Hiện trạng dự án

Ảnh chụp tại thời điểm cập nhật gần nhất. Kế hoạch phía trước xem [`roadmap.md`](roadmap.md).

> Cập nhật lần cuối: 2026-09-03 · Nhánh `main`, sau vòng kiểm thử chạy **bên trong Revit 2024.3** cho
> **đủ 42/42 lệnh**.
>
> **Đã kiểm trên Revit thật:** 42/42 lệnh Revit có ít nhất một ca kiểm chạy trong Revit, chia ba bộ theo
> model mẫu (kiến trúc / HVAC / cấp thoát nước) — xem [Kiểm thử](#kiểm-thử) và
> [`bang-chung-test.md`](bang-chung-test.md) §8. Vòng này lộ ra **7 lỗi** mà 481 test thuần không bắt được,
> tất cả đã sửa kèm test chốt chặn.
>
> **Đã kiểm trên AutoCAD thật:** 15/15 lệnh AutoCAD cũng có ca kiểm tự động, chạy qua `accoreconsole`
> bằng cùng cơ chế và cùng tầng đánh giá với bên Revit — §10. Vòng đầu lộ ra 2 lỗi (cùng họ với lỗi
> `ParameterImport` đã sửa ở PR #29), đã sửa kèm ca song sinh chống test-xanh-suông.
>
> **Đường ghi thật:** 12 ca ghi thật trên bản chép của file mẫu, chuỗi tự chứng minh đã `Commit()` chứ
> không rollback, và tự khôi phục — §11.
>
> **Còn lại:** đường ghi mới phủ 4 lệnh có phép nghịch đảo; lệnh **tạo phần tử mới** chưa có ca ghi. Và
> cần một đêm batch trên **dự án thật** thay vì file mẫu.

## Tóm tắt

| Hạng mục | Trạng thái |
|---|---|
| Kiến trúc Core / vỏ UI | ✅ `Shared.Logic` (thuần) + `Shared.Hosting` (CommandResult, ICoreCommand, HTTP server) dùng chung hai nền tảng |
| Lệnh nền tảng (Revit + AutoCAD) | ✅ |
| HTTP Bridge cho agent AI | ✅ Token, khoá khi dò token, bind 127.0.0.1, timeout huỷ lệnh, `/tools`, `/chat` |
| Batch export + Health report | ✅ |
| Khởi tạo dự án | ✅ Grid/Level/Family/Project info + **file từ template, transfer standards, trục/level từ CSV (CAD/Excel), sheet hàng loạt** |
| MEPF nền tảng (sleeve, cao độ, hanger, chia ống, connector) | ✅ Core + Bridge + batch + Ribbon; đã chạy thật trên model HVAC và cấp thoát nước mẫu |
| MEPF routing A (theo line), B (rải thiết bị theo phòng) | ✅ Core + Ribbon + Bridge; chờ kiểm thử trên model mẫu |
| MEPF sizing (đề xuất → CSV → áp), màu/tên hệ, đánh số theo dòng chảy | ✅ |
| Batch runner chạy đêm (Revit + AutoCAD accoreconsole) | ✅ [`batch-runner.md`](batch-runner.md) |
| `IUpdater` cao độ theo sự kiện | ✅ Mặc định tắt, tự tắt khi > 200 ms |
| Checker tham số/đặt tên, clash nội bộ | ✅ HTML + 3D view, `clash-accepted.json` |
| Lớp AI (offline) | ✅ [`ai-offline.md`](ai-offline.md) — heuristic mặc định, Ollama local tuỳ chọn |
| Routing C (A* 3D) | ✅ `PathFinder3D` (thuần) + lệnh Core `AutoRoute`: 2 điểm → né chướng ngại → model line → tuỳ chọn `RouteFromLines` dựng luôn |
| MCP server | ✅ `scripts/dhcb_mcp_server.py` |
| Giai đoạn 7 P1 — khoảng trống so với tool thị trường ([`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md)) | ✅ Mã nguồn (PR #11): SheetRename, RevisionOnSheets, StylePurge, ColorByParameter, FamilyAudit, WarningsExport, checkset ngưỡng; batch autodetect phiên bản Revit + PlotPdf; AI structured outputs + ≤ 8 ứng viên; MCP read-only/nhóm. ⬜ Phần AutoCAD (LayerTranslate, DrawingCompare, BlockQuantity, AttributeIncrement, purge text/dim/regapp) **chưa có mã nguồn** |
| Giai đoạn 7 P2 | ✅ Mã nguồn (PR #12): SlopePipes, PipeKick, SystemBom, AutoRoute, ScheduleExport, ViewportCopy; vỏ `DhcbTools.AutoCAD.Core` (chỉ AcDbMgd/AcCoreMgd) cho accoreconsole; map năm AutoCAD → package (2026.1+ là .NET 10) |
| Hướng dẫn cài đặt & kiểm thử thủ công | ✅ (PR #13) [`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md) — checklist R1–R48, C1–C17, B1–B12, M1–M4 |
| Kiểm thử tự động | ✅ **489 test xUnit** (`Shared.Logic` + `Shared.Hosting`), gồm bốn bộ đối chiếu mã nguồn với nhau: `RibbonCoverageTests` (vỏ Revit ↔ bảng lệnh), `CatalogFieldTests` (catalog ↔ property config thật), `SuiteCoverageTests` (42/42 lệnh có ca kiểm chạy trong Revit), `VietnameseMessageTests` (không còn thông báo tiếng Anh trong Core) |
| CI | ✅ `tests.yml` (test + check-build bằng API package, ubuntu) — xanh |
| CD | ✅ đóng gói Release thật (Revit 2023/2024/2025, AutoCAD 2024/2025) + GitHub Release khi đẩy tag (`release.yml`, windows-latest) |

Toàn bộ 57 lệnh đã có mã nguồn và biên dịch xanh với API package. **42/42 lệnh Revit đã chạy thật ít nhất
một lần trong Revit 2024.3**; 15 lệnh AutoCAD mới chạy tay một phần. Việc có giá trị nhất lúc này là
**làm cho AutoCAD có bộ ca kiểm tự động tương đương** (accoreconsole đã chạy được nên không cần cơ chế mới),
rồi mới tới 9.4 — đưa cho một nhóm kỹ sư dùng thật.

---

## Phần chưa có mã nguồn

Đối chiếu cây mã nguồn `main` với những gì PR #11/#12 mô tả. Ba nhóm dưới đây được commit message nhắc tới
nhưng file không có trong repo — CI bắt được vì bảng lệnh và catalog tham chiếu tới chúng.

| Nhóm | Tài liệu nói | Thực tế trong repo |
|---|---|---|
| Vỏ Revit (Ribbon) | 6 panel, đủ lệnh MEPF, đăng ký `ElevationUpdater`, hook batch `pending-job.json` | ✅ `App.cs` có **6 panel**, phủ đủ **42/42** lệnh (nút phẳng + nút xổ xuống), có `BatchStartupHook` và đăng ký `ElevationUpdater` (mặc định tắt). ⬜ AI chat WPF vẫn chưa có — lớp AI dùng qua Bridge `/chat` và `dhcb_agent.py` |

### Lệnh AutoCAD — nay đã đủ 15 lệnh có mã nguồn

11 lệnh còn lại từng đánh dấu `.Pending()` trong `CommandCatalog` (`AttributeExport`, `AttributeImport`,
`TextReplace`, `LayerStandardCheck`, `GridExtract`, `XrefAudit`, `LayerTranslate`, `DrawingCompare`,
`BlockQuantity`, `AttributeIncrement`, `CadLayerMap`) **đã có mã nguồn** trong `Core.AutoCAD` (thư mục
`Attributes/`, `TextTools/`, `LayerTools/`, `Reporting/`), dây vào `AcadCommandTable` và có vỏ lệnh
`[CommandMethod]` tương ứng trong `DhcbTools.AutoCAD`; `.Pending()` đã được gỡ nên giờ chào ra `GET /tools`,
MCP và lớp ra lệnh tiếng Việt bình thường. **Chưa chạy thử trên AutoCAD thật** — chỉ mới đọc kỹ mã nguồn thủ
công (không có `dotnet` trong môi trường viết code để build/test), rủi ro lỗi biên dịch hoặc sai tên API vẫn còn.

Hai đơn giản hoá đáng chú ý so với đặc tả gốc:
- **`DrawingCompare`**: so sánh **mức layer** (đếm entity theo layer giữa hai file) thay vì so từng entity theo
  Handle như mô tả ban đầu — Handle của hai file DWG độc lập không đáng tin để đối chiếu 1-1.
- **`GridExtract`**: đặt tên trục là `AXIS-<số thứ tự>` thay vì dò tìm `DBText` gần đường Line để lấy tên thật,
  vì khớp text theo khoảng cách hình học dễ sai trên bản vẽ dày đặc.

Kéo theo: `jobs/autocad-nightly.sample.json` và các mục `C16`, `B12`, phần `DrawingCompare` trong hướng dẫn kiểm
thử thủ công nay có mã nguồn để chạy, nhưng vẫn **chưa được kiểm trên AutoCAD thật**.

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

**AutoCAD (15):** LayerExport, LayerImport, DrawingCleanup, AutoNumbering, AttributeExport, AttributeImport,
TextReplace, LayerStandardCheck, GridExtract, XrefAudit, LayerTranslate, DrawingCompare, BlockQuantity,
AttributeIncrement, CadLayerMap. Lệnh dòng lệnh: `DHCB_LAYER_EXPORT`, `DHCB_LAYER_IMPORT`, `DHCB_CLEANUP`,
`DHCB_AUTONUMBER`, `DHCB_ATTR_EXPORT`, `DHCB_ATTR_IMPORT`, `DHCB_TEXT_REPLACE`, `DHCB_LAYER_CHECK`,
`DHCB_GRID_EXTRACT`, `DHCB_XREF_AUDIT`, `DHCB_LAYER_TRANSLATE`, `DHCB_DRAWING_COMPARE`, `DHCB_BLOCK_QUANTITY`,
`DHCB_ATTR_INCREMENT`, `DHCB_LAYER_MAP`. Vỏ `DhcbTools.AutoCAD.Core` chỉ có `DHCB_RUN` — dùng cho accoreconsole.
Xem [Lệnh AutoCAD — nay đã đủ 15 lệnh có mã nguồn](#lệnh-autocad--nay-đã-đủ-15-lệnh-có-mã-nguồn) cho các đơn
giản hoá so với đặc tả gốc.

### Ribbon Revit
6 panel: Nền tảng · Xuất & Báo cáo · Khởi tạo dự án · MEPF · Hồ sơ & Style · Kiểm tra & AI, phủ đủ 42 lệnh.
`CommandRunner` đọc config JSON ở
`%APPDATA%\DHCB\configs\revit\<Lệnh>.json` → chạy xem trước (`dryRun`) → hỏi xác nhận → chạy thật; hiện mới dùng cho
bản build không WPF. Hai panel còn thiếu (Hồ sơ & Style, AI offline & Batch) và phần lớn lệnh giai đoạn 7 chưa có nút —
xem [Phần chưa có mã nguồn](#phần-chưa-có-mã-nguồn).

### Kiểm thử
`tests/DhcbTools.Shared.Logic.Tests`: CSV, số, đánh số, MEP layout, tên file, phiên bản xuất, HTML, token, **CleanupDecider,
JobTokens/BatchJob/RunLog/BatchReport/AcadScriptGen, GridClustering/GridNaming, RouteGraph, DevicePattern, Duct/PipeSizing,
SystemNaming, FlowNumbering, PathFinder3D, RuleChecker, ClashAcceptance, LayerMappingSuggester, SpecTextExtractor,
WarningAnalyzer, CommandIntentParser, CommandCatalog↔mã nguồn, NamePattern, PaletteGenerator, ThresholdRule, LayerMapTable,
DiffSummary, RvtFileInfo, PlotPdf, SlopeMath, BomAggregator, PolylineSimplifier**. `scripts/check-build.sh` biên dịch toàn bộ
Core/vỏ (kể cả vỏ core-only) trên Linux với API Revit 2025 + AutoCAD 2025; Core Revit cũng xanh với API 2023.

---

## Kết quả kiểm thử trên máy thật

| Vòng | Ngày | Kết quả |
|---|---|---|
| Revit 2024.3 — tay, qua Bridge (R1–R14) | 2026-09-02 | Xanh sau khi sửa 6 lỗi — [`bang-chung-test.md`](bang-chung-test.md) §6 |
| Revit — batch tự động, bộ smoke | 2026-09-03 | 11/12, lộ 3 lỗi chặn (journal, add-in không nạp) — §7 |
| **Revit — batch tự động, đủ 42/42 lệnh** | 2026-09-03 | **52 đạt / 0 trượt / 1 bỏ qua trên 53 ca**, ba model mẫu; lộ 7 lỗi runtime + 4 chỗ lệch tài liệu↔mã — §8 |
| **AutoCAD 2026.1 — batch qua accoreconsole** | 2026-09-03 | Chạy trọn lần đầu sau khi sửa lỗi `DHCB_RUN`; `LayerExport` + `DrawingCleanup` (purge sâu) trên 2 bản vẽ mẫu — §9 |
| **AutoCAD — bộ ca kiểm tự động, đủ 15/15 lệnh** | 2026-09-03 | **18 đạt / 0 trượt trên 18 ca**; lộ 2 lỗi ghi đè im lặng ở `LayerImport`/`AttributeImport` — §10 |
| **Đường ghi thật (Revit + AutoCAD)** | 2026-09-03 | **12 đạt / 0 trượt trên 12 ca**, chạy trên bản chép của file mẫu; chuỗi tự chứng minh đã commit thật và tự khôi phục — §11 |
| **`SleeveAuto` đọc model liên kết** | 2026-09-03 | **0 → 345 sleeve** trên Snowdon HVAC (tường nằm ở link kiến trúc); tối ưu hộp bao 49,8 s → **1,2 s**; bộ mep 17/17 — §14 |
| **Lệnh chạy nền + `/progress/<id>`** | 2026-09-03 | 202 → `running` → `done`, hỏi lại kết quả không mất; 404/401 đúng — §13 |
| **Đường ghi cho nhóm lệnh tạo phần tử mới** | 2026-09-03 | **11/11 (kiến trúc) + 4/4 (HVAC)**; `HangerAuto` 1120 → 0 sau khi bổ sung chống trùng; lộ lỗi chặn "batch treo ở hộp thoại cảnh báo lúc mở model" — §12 |

Quy trình và checklist tay: [`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md) §10;
cách viết ca kiểm chạy trong Revit: [`kiem-thu-trong-revit.md`](kiem-thu-trong-revit.md).

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
- **Chưa kiểm thử trên Revit/AutoCAD thật** cho toàn bộ lệnh — chỉ mới biên dịch với API package. Rủi ro cao nhất theo thứ tự:
  `RouteFromLines` và `PipeKick` (fitting/cút 45° phụ thuộc routing preference), `AutoRoute` (thời gian A* với bước 100 mm
  trên hộp lớn), `TransferStandards` (LineStyles/ObjectStyles không copy được qua API — đã ghi rõ trong Messages),
  `ProjectFromTemplate` (worksharing cần môi trường mạng), `StylePurge` (phân tích tham chiếu có thể thiếu trường hợp —
  luôn xem trước), `SlopePipes` trên ống đã nối fitting hai đầu (Revit có thể từ chối dịch điểm cuối).
- `RvtFileInfo` nhận phiên bản bằng cách quét chuỗi trong 2 MB đầu file thay vì parse OLE — đủ cho batch, nhưng file mã hoá/
  bất thường sẽ rơi về `revitVersion` của job.
- `AcadScriptGen.PlotPdf` theo thứ tự prompt `-PLOT` của AutoCAD 2018+ tiếng Anh; bản địa hoá hoặc phiên bản khác có thể lệch
  prompt — kiểm B11 trước khi dùng thật.
- AutoCAD 2026.1+ (package `AutoCAD.NET 25.1.x`) dùng .NET 10 — chưa build/kiểm; Revit 2027 cũng đang di trú .NET 10.
- `ParameterImport` vẫn đọc CSV theo dòng nên chưa đọc ô có xuống dòng bên trong nháy (nợ cũ).
- Batch Revit thoát bằng `Environment.Exit` sau khi ghi `batch-done.json` — đủ dùng cho Task Scheduler nhưng không "đẹp";
  Revit không có API thoát cho add-in.

## Việc tiếp theo

1. ~~Đường ghi cho lệnh tạo phần tử mới~~ — xong (§12): chốt bằng tính idempotent thay vì dọn lại.
   Còn `SleeveAuto`: đã tìm ra vì sao "0 giao cắt" — lệnh không đọc model liên kết (§14, đã sửa, 345
   sleeve trên model thật). Đường ghi của riêng nó vẫn là nợ vì bộ ghi chạy trên bản chép mà link
   Snowdon lưu đường dẫn tương đối nên không giải được từ đó.
2. Một đêm batch thật trên **dự án thật** (không phải file mẫu) để chốt Giai đoạn 1 đầu-cuối.
3. ~~Gom bảng mã lỗi vào một trang tài liệu~~ — xong: [`ma-loi.md`](ma-loi.md), có test đối chiếu với mã nguồn hai chiều.
4. Rồi tới **9.4 — đưa cho một nhóm kỹ sư dùng thật**; phản hồi của họ quyết định giai đoạn 10/11 đi sâu vào đâu.
5. **Không mở P3** — giữ hướng chiều sâu theo [`roadmap.md`](roadmap.md).
