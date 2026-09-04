# Hiện trạng dự án

Ảnh chụp tại thời điểm cập nhật gần nhất. Kế hoạch phía trước xem [`roadmap.md`](roadmap.md).

> Cập nhật lần cuối: 2026-09-04 · Nhánh `main`, sau đêm batch đầu tiên trên **dự án thật** (§20) và vòng
> đóng vai kỹ sư dùng thử (§21).
>
> **Đã kiểm trên Revit thật:** 42/42 lệnh Revit có ít nhất một ca kiểm chạy trong Revit, chia ba bộ theo
> model mẫu (kiến trúc / HVAC / cấp thoát nước) — xem [Kiểm thử](#kiểm-thử) và
> [`bang-chung-test.md`](bang-chung-test.md) §8. Vòng này lộ ra **7 lỗi** mà bộ test thuần (481 ca *tại thời
> điểm đó*) không bắt được, tất cả đã sửa kèm test chốt chặn.
>
> **Đã kiểm trên AutoCAD thật:** 15/15 lệnh AutoCAD cũng có ca kiểm tự động, chạy qua `accoreconsole`
> bằng cùng cơ chế và cùng tầng đánh giá với bên Revit — §10. Vòng đầu lộ ra 2 lỗi (cùng họ với lỗi
> `ParameterImport` đã sửa ở PR #29), đã sửa kèm ca song sinh chống test-xanh-suông.
>
> **Đường ghi thật:** 12 ca ghi thật trên bản chép của file mẫu, chuỗi tự chứng minh đã `Commit()` chứ
> không rollback, và tự khôi phục — §11.
>
> **Đã chạy trên dự án thật:** một đêm batch 10 bước chỉ đọc trên 9 file `.rvt` của một dự án thật (8/9 chạy
> trọn; file còn lại lỗi mạng tới central model) — §20; và một vòng đóng vai kỹ sư dùng thử — §21.
>
> **Còn lại:** chưa có job nào chạy **tự động qua Task Scheduler** (hai lượt ở §20/§21 đều chạy tay); chất
> lượng tuyến `AutoRoute` vẫn chưa chứng minh được; và 9.4 — đưa cho một nhóm kỹ sư dùng thật.

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
| Kiểm thử tự động | ✅ Bộ test xUnit (`Shared.Logic` + `Shared.Hosting`), gồm bốn bộ đối chiếu mã nguồn với nhau: `RibbonCoverageTests` (vỏ Revit ↔ bảng lệnh), `CatalogFieldTests` (catalog ↔ property config thật), `SuiteCoverageTests` (42/42 lệnh có ca kiểm chạy trong Revit), `VietnameseMessageTests` (không còn thông báo tiếng Anh trong Core) |
| CI | ✅ `tests.yml` (test + check-build bằng API package, ubuntu) — xanh |
| CD | ✅ đóng gói Release thật (Revit 2023/2024/2025, AutoCAD 2024/2025) + GitHub Release khi đẩy tag (`release.yml`, windows-latest) |

Toàn bộ 57 lệnh đã có mã nguồn và biên dịch xanh với API package. **42/42 lệnh Revit đã chạy thật ít nhất
một lần trong Revit 2024.3** và **15/15 lệnh AutoCAD** đã có ca kiểm tự động qua `accoreconsole` (§10).
Việc có giá trị nhất lúc này là **9.4 — đưa cho một nhóm kỹ sư dùng thật**; phản hồi của họ quyết định
giai đoạn 10/11 đi sâu vào đâu.

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
MCP và lớp ra lệnh tiếng Việt bình thường. **Đã chạy trên AutoCAD thật**: cả 15/15 lệnh có ca kiểm tự động
qua `accoreconsole` (18/18 ca, §10) — vòng đó lộ 2 lỗi ghi đè im lặng ở `LayerImport`/`AttributeImport`,
đã sửa kèm ca song sinh.

Hai đơn giản hoá đáng chú ý so với đặc tả gốc:
- **`DrawingCompare`**: so sánh **mức layer** (đếm entity theo layer giữa hai file) thay vì so từng entity theo
  Handle như mô tả ban đầu — Handle của hai file DWG độc lập không đáng tin để đối chiếu 1-1.
- **`GridExtract`**: đặt tên trục là `AXIS-<số thứ tự>` thay vì dò tìm `DBText` gần đường Line để lấy tên thật,
  vì khớp text theo khoảng cách hình học dễ sai trên bản vẽ dày đặc.

Kéo theo: `jobs/autocad-nightly.sample.json` và các mục `C16`, `B12`, phần `DrawingCompare` trong hướng dẫn kiểm
thử thủ công nay đã chạy được qua `accoreconsole`; phần **kiểm tay trong giao diện AutoCAD** (C1–C17) thì vẫn còn.

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
6 panel, đúng tên trong `src/DhcbTools.Revit/App.cs`: **Nền tảng · Xuất & Báo cáo · Khởi tạo dự án · MEPF ·
Hồ sơ & Style · Kiểm tra & AI** — phủ đủ 42 lệnh (nút phẳng + nút xổ xuống). `CommandRunner` đọc config JSON ở
`%APPDATA%\DHCB\configs\revit\<Lệnh>.json` → chạy xem trước (`dryRun`) → hỏi xác nhận → chạy thật.
Còn thiếu: **AI chat dạng WPF** — lớp AI hiện dùng qua Bridge `/chat` và `dhcb_agent.py`.

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
| **Quét hồi quy sau 10 PR** | 2026-09-03 | **90 đạt / 0 trượt / 1 bỏ qua trên 91 ca**, cả 7 bộ (Revit smoke·mep·plumbing·write·write-mep, AutoCAD smoke·write) — §15 |
| **`SleeveAuto` đọc model liên kết** | 2026-09-03 | **0 → 345 sleeve** trên Snowdon HVAC (tường nằm ở link kiến trúc); tối ưu hộp bao 49,8 s → **1,2 s**; bộ mep 17/17 — §14 |
| **Bộ tìm đường `AutoRoute`** | 2026-09-03 | **4049 ms → 10 ms**, 58.720 → 5.783 ô mở rộng trên 550 vật cản (đo bản cũ cạnh bản mới); trên Snowdon HVAC **0,3 s → 82 ms** (bước 500 mm) và **17,9 s → 815 ms** (bước 100 mm); thất bại nay chứng minh được tuyến KHÔNG tồn tại thay vì chỉ báo hết giờ; bộ `mep` **20/20** — §19 |
| **Đêm batch thật đầu tiên — dự án thực tế A** | 2026-09-04 | **8/9 file `.rvt` thật** (00–03 kiến trúc, 05–08 MEP, 139–176 MB/file) chạy trọn 10 bước chỉ đọc, không đụng file gốc; lộ TaskDialog nâng cấp phiên bản treo batch 43 phút — sửa bằng `DialogBoxShowing`, chạy lại qua đúng chỗ đó trong 86 giây; file 04 lỗi mạng tới central model (máy chủ `<server-A>`), không phải lỗi mã nguồn; tạo bản sao Revit 2024 cho 8/9 file để lần sau mở tức thì — §20 |
| **Đóng vai kỹ sư dùng thử trên dự án thực tế A** | 2026-09-04 | `ApplySizing` 113/113, `HangerAuto` nhận đúng family thật của dự án, `RemoveUnusedViews` xem trước khớp thật tuyệt đối; lộ friction thật (`ElevationTag`/`HangerAuto` cần tên tham số/family riêng dự án — đúng thiết kế báo lỗi 9.2, không phải bug) và **một lỗi ngầm nguy hiểm**: bản sao mất trạng thái nạp link khiến `ClashDetection` báo sai **0** thay vì **479** va chạm — sửa bằng nạp lại link + thử file cạnh host, đo lại đúng 479 — §21 |
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
| 13 | Batch treo ở TaskDialog nâng cấp phiên bản (Revit tự bật lúc mở file cũ, ngoài mọi transaction) — 43 phút không xong một file | `UIApplication.DialogBoxShowing` đăng ký cho cả phiên batch, cạnh `FailuresProcessing` — `BatchStartupHook` — §20 |
| 14 | Bản sao (`SaveAs` sau `DetachFromCentral`) làm mất trạng thái nạp link — `ClashDetection` báo sai **0** va chạm thay vì 479 thật, im lặng vì summary "0 va chạm" trông y hệt kết quả sạch | `BatchJobRunner.Open()` gọi `LoadUnloadedLinks` — nạp lại link, thử tiếp file cùng tên cạnh file host nếu đường dẫn ghi sẵn hỏng — §21 |

### Còn mở

- **`AutoRoute` — chất lượng tuyến chưa chứng minh được.** Phần đọc model liên kết đã sửa (§18: vật cản
  30 → 546); bộ tìm đường cũng đã vá ba lỗi đo được (§19: chậm, heuristic mù hướng, thất bại câm —
  4049 ms → 10 ms trên 550 vật cản), và đã chạy lại trên Snowdon HVAC: bộ `mep` 19/19, `AutoRoute`
  0,3 s → 82 ms (bước 500 mm) và 17,9 s → 815 ms (bước 100 mm). Hai bước lưới cho **cùng một kết luận
  bằng hai con số độc lập**: hai điểm của ca kiểm không nối thông nhau (782/12.025 và 79.701/1.335.961 ô),
  tức bị sàn và tường của model liên kết bao kín — không phải giới hạn bộ tìm đường. **Việc còn lại không
  còn là hiệu năng** mà là chọn được hai điểm trong cùng khoang trần kỹ thuật, nên chất lượng tuyến vẫn là
  con số không có. Giữ nhãn *thử nghiệm* vì lý do đó, không còn vì chậm.
- **Mỗi lệnh mới chạy thật trên một vài tình huống**, chưa phủ hết biến thể của dự án thật. Rủi ro còn cao nhất theo
  thứ tự: `RouteFromLines` và `PipeKick` (fitting/cút 45° phụ thuộc routing preference), `AutoRoute` (chất lượng tuyến —
  xem mục trên), `TransferStandards` (LineStyles/ObjectStyles không copy được qua API — đã ghi rõ trong Messages),
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
   `SleeveAuto` cũng xong trong cùng ngày (§14): sửa lệnh để đọc model liên kết, sửa script để chép
   luôn model liên kết cạnh bản chép — chuỗi ghi thật **334 → 0** trên model thật.
2. ~~Một đêm batch thật trên **dự án thật**~~ — phần "chạy được, chạy đúng, không làm hỏng gì trên dữ
   liệu thật" xong (§20): 8/9 file của dự án thực tế A (~700 MB), lộ và sửa lỗi treo TaskDialog nâng cấp
   phiên bản (bug #13). File 04 lỗi mạng tới central model, việc của hạ tầng chứ không phải mã nguồn.
   Còn lại: **chưa có job nào thật sự chạy tự động qua `install-nightly-task.ps1`** — hai lượt ở §20 đều
   chạy tay. Đăng ký Task Scheduler khi có một job cần lặp lại định kỳ thật (ví dụ báo cáo đêm trên bản
   `_upgraded-2024/`), không đáng làm cho một lượt một-lần-cho-biết.
3. ~~Gom bảng mã lỗi vào một trang tài liệu~~ — xong: [`ma-loi.md`](ma-loi.md), có test đối chiếu với mã nguồn hai chiều.
4. Rồi tới **9.4 — đưa cho một nhóm kỹ sư dùng thật**; phản hồi của họ quyết định giai đoạn 10/11 đi sâu
   vào đâu. **Mẫu thu phản hồi đã có**: [`mau-phan-hoi-9-4.md`](mau-phan-hoi-9-4.md) — bảng tick
   *dùng hằng tuần / bấm rồi bỏ / chưa dùng* cho đủ 42 lệnh Revit + 15 lệnh AutoCAD, kèm bốn câu hỏi mở.
   `PhanHoiFormTests` đối chiếu danh sách lệnh trong mẫu với `CommandCatalog` hai chiều nên mẫu không trôi.
   Còn thiếu: phát hành v1.1 và chọn nhóm kỹ sư — cả hai đều là việc của người, không phải của mã.
5. **Không mở P3** — giữ hướng chiều sâu theo [`roadmap.md`](roadmap.md).
