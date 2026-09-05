# Hiện trạng dự án

Ảnh chụp tại thời điểm cập nhật gần nhất. Kế hoạch phía trước xem [`roadmap.md`](roadmap.md).

> Cập nhật lần cuối: 2026-09-05 · sau khi mở **chặng thi công** của chuỗi thiết kế → hoàn công: **`SetoutExport`**
> (đề xuất A1 — toạ độ định vị cho máy toàn đạc, [`toa-do-dinh-vi.md`](toa-do-dinh-vi.md)) và **`ConstructionStatus`
> + `ProgressReport`** (đề xuất B1 — trạng thái thi công và báo cáo tiến độ, [`tien-do-thi-cong.md`](tien-do-thi-cong.md));
> trước đó là đêm batch đầu tiên trên **dự án thật** (§20) và vòng đóng vai kỹ sư dùng thử (§21).
>
> **Ba lệnh mới nhất chưa chạy thật:** cả ba có tầng thuần (50 + 41 ca test), lệnh Core, nút Ribbon và ca kiểm
> trong `tests/suites/`, nhưng **chưa chạy lần nào trong Revit** — mang nhãn *thử nghiệm* theo nguyên tắc 6.
> Con số "đã chạy thật" dưới đây vì thế là **49/49** (cập nhật 2026-09-05).
>
> **Đã kiểm trên Revit thật:** 43/43 lệnh Revit *của vòng đó* có ít nhất một ca kiểm chạy trong Revit, chia ba bộ theo
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
> **Nhật ký batch có chuỗi băm (11.5):** mỗi dòng `run-*.jsonl` mang `prevHash`/`hash`, kiểm lại bằng
> `BatchRunner --verify-log`. Bốn cách sửa log đều bị bắt, chỉ ra đúng số dòng — §23. Đã chạy thật trên
> log đêm batch **30 dòng / 351 KB** và log AutoCAD nối qua **4 tiến trình accoreconsole** — §24.
>
> **`snapshot` phía AutoCAD (10.1) đã có:** agent nhìn được bản vẽ — render off-screen sống (`live`) hoặc ảnh xem
> trước trong DWG (`thumbnail`), chạy thật trên AutoCAD 2026.1 — §25. Giai đoạn 10 không còn mục ⬜ nào.
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
| Trạng thái thi công và báo cáo tiến độ (đề xuất B1) | ✅ mã nguồn 🧪 chưa chạy thật — `ConstructionStatus` ghi trạng thái từ CSV hiện trường, `ProgressReport` ra HTML + CSV: % theo **số lượng và chiều dài**, gộp theo tầng/hệ/category, luỹ kế theo tuần — [`tien-do-thi-cong.md`](tien-do-thi-cong.md) |
| Toạ độ định vị ra máy toàn đạc (đề xuất A1) | ✅ mã nguồn 🧪 chưa chạy thật — `SetoutExport`: CSV theo thứ tự cột máy (`PNEZD`/`PENZD`…) + DXF điểm, hệ Survey tự kiểm chiều transform, giao trục, tên điểm ≤ 16 ký tự không trùng — [`toa-do-dinh-vi.md`](toa-do-dinh-vi.md) |
| Khởi tạo dự án | ✅ Grid/Level/Family/Project info + **file từ template, transfer standards, trục/level từ CSV (CAD/Excel), sheet hàng loạt** |
| MEPF nền tảng (sleeve, cao độ, hanger, chia ống, connector) | ✅ Core + Bridge + batch + Ribbon; đã chạy thật trên model HVAC và cấp thoát nước mẫu |
| MEPF routing A (theo line), B (rải thiết bị theo phòng) | ✅ Core + Ribbon + Bridge; chờ kiểm thử trên model mẫu |
| MEPF sizing (đề xuất → CSV → áp), màu/tên hệ, đánh số theo dòng chảy | ✅ |
| Batch runner chạy đêm (Revit + AutoCAD accoreconsole) | ✅ [`batch-runner.md`](batch-runner.md) |
| Chuỗi băm nhật ký batch (NĐ 207/2026, điều kiện ①) | ✅ `Shared.Logic/Evidence/HashChain` gắn ở `RunLog.Append`; kiểm bằng `BatchRunner --verify-log` — §23, và chạy thật cả hai đường Revit/AutoCAD ở §24 |
| `IUpdater` cao độ theo sự kiện | ✅ Mặc định tắt, tự tắt khi > 200 ms |
| Checker tham số/đặt tên, clash nội bộ | ✅ HTML + 3D view, `clash-accepted.json` |
| Lớp AI (offline) | ✅ [`ai-offline.md`](ai-offline.md) — heuristic mặc định, Ollama local tuỳ chọn |
| Routing C (A* 3D) | ✅ `PathFinder3D` (thuần) + lệnh Core `AutoRoute`: 2 điểm → né chướng ngại → model line → tuỳ chọn `RouteFromLines` dựng luôn |
| MCP server | ✅ `scripts/dhcb_mcp_server.py` |
| Giai đoạn 7 P1 — khoảng trống so với tool thị trường ([`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md)) | ✅ Mã nguồn (PR #11): SheetRename, RevisionOnSheets, StylePurge, ColorByParameter, FamilyAudit, WarningsExport, checkset ngưỡng; batch autodetect phiên bản Revit + PlotPdf; AI structured outputs + ≤ 8 ứng viên; MCP read-only/nhóm. ⬜ Phần AutoCAD (LayerTranslate, DrawingCompare, BlockQuantity, AttributeIncrement, purge text/dim/regapp) **chưa có mã nguồn** |
| Giai đoạn 7 P2 | ✅ Mã nguồn (PR #12): SlopePipes, PipeKick, SystemBom, AutoRoute, ScheduleExport, ViewportCopy; vỏ `DhcbTools.AutoCAD.Core` (chỉ AcDbMgd/AcCoreMgd) cho accoreconsole; map năm AutoCAD → package (2026.1+ là .NET 10) |
| Hướng dẫn cài đặt & kiểm thử thủ công | ✅ (PR #13) [`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md) — checklist R1–R48, C1–C17, B1–B12, M1–M4 |
| Kiểm thử tự động | ✅ Bộ test xUnit (`Shared.Logic` + `Shared.Hosting`), gồm bốn bộ đối chiếu mã nguồn với nhau: `RibbonCoverageTests` (vỏ Revit ↔ bảng lệnh), `CatalogFieldTests` (catalog ↔ property config thật), `SuiteCoverageTests` (49/49 lệnh có ca kiểm chạy trong Revit), `VietnameseMessageTests` (không còn thông báo tiếng Anh trong Core) |
| CI | ✅ `tests.yml` (test + check-build bằng API package, ubuntu) — xanh |
| CD | ✅ đóng gói Release thật (Revit 2023/2024/2025, **AutoCAD 2024/2025/2026**) + GitHub Release khi đẩy tag (`release.yml`, windows-latest). AutoCAD 2026 là nhánh .NET 10, installer đặt vào `DhcbTools.bundle\Contents6` |

Toàn bộ 64 lệnh đã có mã nguồn và biên dịch xanh với API package. **49/49 lệnh Revit đã chạy thật ít nhất
một lần trong Revit 2024.3** (ba lệnh chặng thi công là phần chưa) và **15/15 lệnh AutoCAD** đã có ca kiểm tự
động qua `accoreconsole` (§10).
Việc có giá trị nhất lúc này là **9.4 — đưa cho một nhóm kỹ sư dùng thật**; phản hồi của họ quyết định
giai đoạn 10/11 đi sâu vào đâu.

---

## Phần chưa có mã nguồn

Đối chiếu cây mã nguồn `main` với những gì PR #11/#12 mô tả. Ba nhóm dưới đây được commit message nhắc tới
nhưng file không có trong repo — CI bắt được vì bảng lệnh và catalog tham chiếu tới chúng.

| Nhóm | Tài liệu nói | Thực tế trong repo |
|---|---|---|
| Vỏ Revit (Ribbon) | 6 panel, đủ lệnh MEPF, đăng ký `ElevationUpdater`, hook batch `pending-job.json` | ✅ `App.cs` có **6 panel**, phủ đủ **49/49** lệnh (nút phẳng + nút xổ xuống), có `BatchStartupHook` và đăng ký `ElevationUpdater` (mặc định tắt). ⬜ AI chat WPF vẫn chưa có — lớp AI dùng qua Bridge `/chat` và `dhcb_agent.py` |

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
Hồ sơ & Style · Kiểm tra & AI** — phủ đủ 49 lệnh (nút phẳng + nút xổ xuống). `CommandRunner` đọc config JSON ở
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
| **`AutoRoute` — ngân sách A* theo cỡ bài toán, biên Z tách riêng** | 2026-09-05 | Trần cố định 400.000 thua ở bài có lời giải cần 459.115 node (0,3 s); nay tự chọn theo ô × 7 hướng, kẹp 400.000–2.000.000. Biên Z mặc định 1000 thay vì lấy chung 3000 (61 lớp → 20 triệu node, 1,8 GB chưa xong). Chín nút chỉnh A* lên form Ribbon. Snowdon HVAC bước 100 mm: **815 ms → 136 ms**, A* tự cạn hàng đợi thay vì chạm trần; bộ `mep` **26/26** — §35 |
| **`AutoRoute` — tuyến thật đầu tiên trên Snowdon HVAC** | 2026-09-05 | Điểm mẫu cũ z = 3000 nằm **giữa hai tầng** (L1 ≈ 1600, L2 ≈ 4950) nên bị bao kín là đúng. Lấy tim duct thật qua `SetoutExport` Internal/mm: **tuyến 2 đoạn 1 rẽ, 145 ms**, đủ 4 category kể cả tường, và cái nhảy 530 mm là bắt buộc (`allowVertical: false` → không nối thông). Lộ giới hạn thiết kế: hộp bao tường là rào kín kể cả chỗ duct thật xuyên qua. Riser L3 → L4: chỉ sàn thì lên được (có lỗ sàn), đủ 4 category thì kín — shaft có tường bao, cùng giới hạn hộp bao. Bộ mới `revit-autoroute` **8 ca**, đều chạy trong Revit; bấm tay form Ribbon đủ 9 ô, xem trước cả hai chiều — §36 |
| **Routing mức D — chui qua lỗ mở của tường/sàn** | 2026-09-05 | Insert của tường/sàn (shaft/opening/lỗ chờ; cửa tuỳ chọn) đục khỏi hộp bao bằng `BoxSubtract`, bộ tìm đường không đổi. Snowdon HVAC: qua lỗ mở tường thật 1118 × 2134 ở L4 **1 đoạn thẳng, 28 node**; mức C cùng điểm không nối thông. Riser và tuyến 12,6 m vẫn kín vì **model không vẽ lỗ chờ** (48 lỗ đọc được, không cái nào trên tường shaft). Bộ `revit-autoroute` **13/13**, Shared.Logic 1291 — §37 |
| **Bấm tay form `AutoRoute` mức D** | 2026-09-05 | Hai ô mới có mặt; xem trước qua lỗ mở tường thật **khớp từng số với batch** (1 đoạn, 28 node, 1 lỗ mở), bỏ tick thì mức C và *Chạy thật* tự xám. Lộ lỗi chỉ thấy khi bấm tay: `respectOpenings` thiếu trong `DefaultBool` nên form **bỏ tick mặc định** → mức D tắt ngầm và ghi `false` đè config; sửa + assert — §38 |
| **11.4 — kiểm IDS trên chính file IFC, khớp IfcTester 10/10** | 2026-09-05 | `IfcIdsElement` + `BatchRunner --verify-ifc … --verify-ids`, cùng `IdsEvaluator`/`IdsReport` với đường Revit. Snowdon IFC (91 MB, 3,5 s): 10 specification khớp IfcTester từng con số sau khi sửa 3 lỗi DHCB (boolean `.F.`→`FALSE`, thuộc tính riêng lớp, **số không đạt bị cắt ở 200** — lỗi cũ ảnh hưởng cả Revit) và ghi 1 lỗi IfcTester (`FALSE` hoa → True). Phủ 100 %, vá cổng CI đỏ sau #93. Smoke 39/40 — §41 |
| **`IdsValidate` cảnh báo IDS lệch chuẩn XSD** | 2026-09-05 | §39 để lại: IDS "gần đúng" DHCB chạy, IfcTester từ chối. Nay `IdsSchemaLint` (tầng thuần, quy tắc rút tay từ `ids.xsd` vì .NET không biên dịch được XMLSchema.xsd của W3C) **cảnh báo kèm số dòng, không chặn**: namespace, `ifcVersion`, thứ tự facet, `xs:restriction`, thẻ con bắt buộc. Fixture lệch chuẩn = bản trước §39, IfcTester xác nhận từ chối; smoke 39/40 (1 bỏ qua), ca mới ra đúng 3 cảnh báo, con số kiểm y hệt fixture chuẩn — §40 |
| **`IdsValidate` đối chiếu IfcTester** | 2026-09-05 | IfcTester 0.8.5 trên chính IFC xuất từ Snowdon: **42 tường không đạt là lỗi giả** — tường kính xuất ra `IfcCurtainWall`, không phải con của `IfcWall`; ánh xạ Walls → IfcWall gộp cả tường kính. Sửa `WallType.Kind == Curtain` → `IfcCurtainWall`; sau sửa khớp IfcTester cả 3 specification. Fixture IDS cũng sai chuẩn XSD (`restriction` phải thuộc `xs:`), IfcTester từ chối — sửa. Bộ `smoke` 38/38 — §39 |
| **Đêm batch thật đầu tiên — dự án thực tế A** | 2026-09-04 | **8/9 file `.rvt` thật** (00–03 kiến trúc, 05–08 MEP, 139–176 MB/file) chạy trọn 10 bước chỉ đọc, không đụng file gốc; lộ TaskDialog nâng cấp phiên bản treo batch 43 phút — sửa bằng `DialogBoxShowing`, chạy lại qua đúng chỗ đó trong 86 giây; file 04 lỗi mạng tới central model (máy chủ `<server-A>`), không phải lỗi mã nguồn; tạo bản sao Revit 2024 cho 8/9 file để lần sau mở tức thì — §20 |
| **Đóng vai kỹ sư dùng thử trên dự án thực tế A** | 2026-09-04 | `ApplySizing` 113/113, `HangerAuto` nhận đúng family thật của dự án, `RemoveUnusedViews` xem trước khớp thật tuyệt đối; lộ friction thật (`ElevationTag`/`HangerAuto` cần tên tham số/family riêng dự án — đúng thiết kế báo lỗi 9.2, không phải bug) và **một lỗi ngầm nguy hiểm**: bản sao mất trạng thái nạp link khiến `ClashDetection` báo sai **0** thay vì **479** va chạm — sửa bằng nạp lại link + thử file cạnh host, đo lại đúng 479 — §21 |
| **Ba nâng cấp tự động hoá — `DictionaryLearn`, `E-PRECOND`, `UsageReport`** | 2026-09-04 | **30/31 (smoke) + 20/20 (mep)** trên Revit 2024.3 thật. `DictionaryLearn` soi 332 tên tham số, tìm ra `Elevation at Bottom`/`Elevation at Top` (tên dựng sẵn không tồn tại trong model) và **từ chối đề xuất** cho `centreElevation` thay vì đoán bừa; `E-PRECOND` chặn đúng ca "0 va chạm giả" mà không chặn nhầm bốn lệnh đọc link (445 sleeve · 551 thiết bị · 7 va chạm với link · 516 vật cản từ link); `UsageReport` đọc log thật ra đúng 2 dòng cho 2 lượt chạy — cờ tắt ghi trong `RunTests` giữ 49 lệnh của bộ test khỏi lọt vào số liệu — §22 |
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
- **`ScheduleExport` trả `success: true` khi mất một phần đầu ra.** §24: thư mục đầu ra dài 218 ký tự làm một
  schedule vượt MAX_PATH (263 > 260) nên không ghi được. Lệnh báo **đúng và đủ** trong `errors` (tên schedule +
  nguyên nhân) và summary ghi "35/36", nhưng `Success` vẫn là true nên `report.html` hiện *OK* và mã thoát không
  phản ánh. Khác với ca "0 kết quả" mà `E-PRECOND`/`ElevationTag` đã chặn: đây là thành công **một phần** thật,
  đổi thành thất bại thì 35 file xuất được cũng bị gắn cờ đỏ. Để ngỏ tới khi có người dùng thật quyết định.
- `RvtFileInfo` nhận phiên bản bằng cách quét chuỗi trong 2 MB đầu file thay vì parse OLE — đủ cho batch, nhưng file mã hoá/
  bất thường sẽ rơi về `revitVersion` của job.
- `AcadScriptGen.PlotPdf` theo thứ tự prompt `-PLOT` của AutoCAD 2018+ tiếng Anh; bản địa hoá hoặc phiên bản khác có thể lệch
  prompt — kiểm B11 trước khi dùng thật.
- .NET 10: AutoCAD 2026.1 (net10) **đã build và chạy thật** qua accoreconsole (§24); Revit 2027 (gói API 2027.2.0)
  **đã build** cả bản WPF trên net10 và có CI, nhưng **chưa chạy thật** vì máy chỉ có Revit 2024.3 — nên
  `release.yml` chưa đóng gói 2026/2027. Xem "Nền tảng — .NET 10" trong [`roadmap.md`](roadmap.md).
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
4. ~~Gỡ ma sát từ điển tham số phát hiện ở §21~~ — xong: lệnh **`DictionaryLearn`** soi tên tham số thật
   của mô hình đang mở và đề xuất/ghi `dictionary.json` thay cho việc kỹ sư mở JSON trong `%APPDATA%`
   sửa tay mỗi lần vấp `E-PARAM-MISSING`. Tầng thuần `Ai/DictionarySuggester` có test (chỉ đề xuất tên
   có thật; tham số rỗng toàn dự án và sai kiểu bị hạ điểm; trộn không xoá thứ đã khai; file JSON hỏng
   thì dừng chứ không ghi đè). ⬜ Còn: chạy thật trên dự án A để xem đề xuất có khớp tên thật không.
5. ~~Chặn **lớp lỗi** của bug #14, không chỉ nguyên nhân của nó~~ — xong: mã lỗi **`E-PRECOND`** và
   lớp tiền đề `Shared.Logic/Checks/Precondition` (thuần, có test) + `Core/Checks/RevitPrecondition`.
   Chỗ vá cũ của #14 nằm trong `BatchJobRunner.Open()`, nhưng **đường Ribbon và Bridge không đi qua đó**
   — kỹ sư tự mở một bản sao có link chưa nạp rồi bấm `ClashDetection` vẫn nhận đúng con số 0 giả như cũ.
   Nay `ClashDetection`, `SleeveAuto`, `DevicePlacement`, `AutoRoute` dừng ngay trước mọi transaction khi
   **mọi** link đều chưa nạp (nạp một phần = cảnh báo, có thể là cố ý), và `ClashDetection` cũng dừng khi
   một trong hai nhóm category rỗng — "0 va chạm" khi không có gì để kiểm là câu nói về đầu vào chứ không
   về mô hình. Ca kiểm `revit-smoke` chốt đường chặn bằng nhóm category rỗng (đường link chưa nạp không
   dựng được bằng file JSON khai báo — kiểm tay theo §21).
6. ~~Thu số liệu 9.4 bằng máy thay vì chờ người điền form~~ — xong: **`UsageReport`** (công cụ nội bộ
   như `RunTests`, không lên Ribbon). Phát hiện khi làm: log **chỉ ghi khi lệnh ném exception**, lần chạy
   thành công không để lại dấu vết nào — nên câu hỏi quyết định giai đoạn 10/11 không có dữ liệu nào trả
   lời được. Nay mọi lần chạy đều ghi một dòng ở đúng chỗ hội tụ của cả bốn đường vào
   (`RevitCommandTable.Dispatch` / `AcadCommandTable.Dispatch`), và `UsageReport` đọc lại thành *lệnh nào
   dùng bao nhiêu **ngày**, lệnh nào bấm rồi bỏ (xem trước mà chưa bao giờ chạy thật), lệnh nào lỗi nhiều
   nhất, lệnh nào chưa ai bấm*. `RunTests` tắt cờ ghi trong lúc chạy bộ ca kiểm, nếu không chính bộ test
   bơm số liệu lên. Tầng thuần `Usage/UsageLog` có test, gồm vòng tròn `Format` → `Parse` (định dạng dòng
   log là hợp đồng giữa hai thời điểm cách nhau 30 ngày). ⬜ Số liệu chỉ bắt đầu tích từ bản cài kế tiếp.
7. Rồi tới **9.4 — đưa cho một nhóm kỹ sư dùng thật**; phản hồi của họ quyết định giai đoạn 10/11 đi sâu
   vào đâu. **Mẫu thu phản hồi đã có**: [`mau-phan-hoi-9-4.md`](mau-phan-hoi-9-4.md) — bảng tick
   *dùng hằng tuần / bấm rồi bỏ / chưa dùng* cho đủ 49 lệnh Revit + 15 lệnh AutoCAD, kèm bốn câu hỏi mở.
   `PhanHoiFormTests` đối chiếu danh sách lệnh trong mẫu với `CommandCatalog` hai chiều nên mẫu không trôi.
   Còn thiếu: phát hành v1.1 và chọn nhóm kỹ sư — cả hai đều là việc của người, không phải của mã.
8. ~~Chuỗi băm cho nhật ký batch (11.5)~~ — xong, **đã chạy thật cả hai đường (§24)**: đêm batch Revit
   3 model × 10 step (30 dòng, 351 KB, dòng dài nhất 123.357 ký tự) và đêm batch AutoCAD 4 bản vẽ × 3 step
   nối qua **4 tiến trình `accoreconsole` riêng** — chuỗi liền ở cả hai, sửa/xoá dòng đều bị bắt đúng số
   dòng, và log ghi trước khi có tính năng bị báo *chưa mang chuỗi băm* thay vì cho qua. `prevHash`/`hash`
   gắn ở `RunLog.Append`, kiểm bằng
   `BatchRunner --verify-log`. Làm được ngay mà không cần chờ 9.4 vì tầng thuần chiếm gần hết và nó đến
   từ **NĐ 207/2026** chứ không từ ý thích. Không làm thành lệnh Core như tên `EvidenceVerify` ban đầu
   gợi ý: kiểm log không cần `Document` nào, mà thêm lệnh Core thì vướng nguyên tắc 6 — đổi lại được thứ
   chạy trên CI. ⬜ Còn: chỉ số 30 ngày (theo định nghĩa phải chờ 30 ngày) và log của **dự án thật** như
   §20 — §24 mới chạy trên model mẫu Snowdon Towers.
9. **Ba lệnh chặng thi công — chạy thật trong Revit.** `SetoutExport` (A1), `ConstructionStatus` và
   `ProgressReport` (B1) đều đã có mã nguồn, tầng thuần có test, nút Ribbon và ca kiểm; việc còn lại là một
   vòng `run-in-revit-tests.ps1 -Suite smoke` rồi `-Suite mep`. ✅ **Đã chạy thật 2026-09-05** (§28, §31): `SetoutExport` 260 điểm trên model kiến trúc
   và 546 điểm trên model HVAC; `ProgressReport` chạy trên cả hai. Đường ghi của `ConstructionStatus` cũng
   **đã chạy thật** sau khi thêm `keyParameter` — CSV khớp theo **Mark** thay vì `ElementId`, nên fixture nằm
   được trong repo: ghi 3 cửa → ghi lại phải 0 → CSV lùi trạng thái bị chặn → `ProgressReport` ra **1,4 %**,
   lần đầu con số tiến độ nói về công trường chứ không nói về tham số. ⬜ Còn **một việc phải làm tay**:
   **đối chiếu một điểm định vị bằng Spot Coordinate** trên model có khai toạ độ chung thật. Và một ranh giới phải nói rõ: ca kiểm ghi thật dùng
   `Comments` làm tham số trạng thái vì model mẫu không có shared parameter nào cho việc này — đó là
   **fixture, không phải khuyến nghị**; dự án thật gắn shared parameter riêng, mà DHCB **chưa có lệnh
   tạo/gắn shared parameter**. Chi tiết: [`toa-do-dinh-vi.md`](toa-do-dinh-vi.md) ·
   [`tien-do-thi-cong.md`](tien-do-thi-cong.md).
   Lý do làm A1/B1 trước các đề xuất còn lại: chỉ đọc hoặc chỉ ghi tham số, tầng thuần chiếm phần lớn, và cả
   hai mở thêm **nhóm người dùng khác** (tổ trắc đạc, ban chỉ huy công trường) cho chính vòng 9.4.
10. ~~**B3 — BCF cho `ClashDetection`**~~ — xong 2026-09-05: khai `bcfPath` thì lệnh ghi thêm file **BCF 2.1**
   mở thẳng trong Navisworks/Solibri/BIMcollab, mỗi va chạm một topic có camera nhìn vào tâm. Làm được ngay,
   không chờ 9.4, vì toàn bộ .bcf là **zip + XML** — tầng thuần `Shared.Logic/Bcf` (21 ca test đọc lại chính
   file vừa ghi) chiếm gần hết, và nó chỉ **thêm đầu ra cho lệnh đã chạy thật** nên không vướng nguyên tắc 6
   như một lệnh Core mới. Điều đắt nhất khi làm không phải XML mà là **GUID topic phải ổn định**: sinh từ
   `key` va chạm (đúng khoá của `clash-accepted.json`), nếu không thì mỗi đêm chạy lại là tư vấn thấy toàn
   vấn đề mới và nhận xét họ đã ghi nằm lại ở vấn đề cũ. ✅ **Đã chạy thật 2026-09-05** trong `revit-mep`: `clash.bcf` 9.897 byte, **7 topic**, đọc lại bằng thư
   viện zip thấy đủ `bcf.version` + `project.bcfp` + mỗi topic một `markup.bcf`/`viewpoint.bcfv` (§28).
   ⬜ Còn: **mở thử trong Navisworks/Solibri thật** — việc của người, không phải của mã.
11. ~~**C4 — `ModelLinesFromCad`**~~ — xong 2026-09-05, khép nhóm "song song, rẻ". Từ nay DWG đã link/import
   dựng thẳng ra model line theo layer, `RouteFromLines` ăn tiếp — trước đó kỹ sư vẫn **vẽ tay lại tuyến**
   đè lên bản vẽ CAD, tức là mắt xích duy nhất còn đứt giữa `CadLayerMap` và `RouteFromLines`. Tầng thuần
   `Cad/CadCurveFilter` (23 ca test) giữ ba điều: **đường vẽ chồng hai lần không thành hai model line**
   (hai ống chồng nhau thì nhìn mặt bằng không thấy), bỏ đoạn rác trim/extend, và nối đoạn thẳng hàng
   **nhưng không nối xuyên ngã ba** — nối qua đó là xoá mất một nhánh tuyến. Chạy lại không sinh bản sao
   (so trùng bỏ qua tên layer, vì model line mang tên line style chứ không mang tên layer DWG).
   ✅ **Đã chạy thật 2026-09-05** (§29, §31): bộ ghi MEP tự dựng fixture bằng lệnh mới **`CadLink`**,
   chạy được cả **DXF văn bản** lẫn **DWG 2018 nhị phân**, và cả ba cách đặt (`origin`, `shared`, `centered`).
   Kết quả: **`Đã tạo 2 model line`** đúng con số fixture gài, lần hai 0 mới / 2 đã có. Hai lỗi thật lộ ra và
   đã sửa: `dwgNameContains` chưa bao giờ khớp một bản vẽ **link** (tên file nằm ở `CADLinkType` chứ không
   ở `ImportInstance.Name`), và `CadLink` so tên **cắt đuôi mở rộng rồi tìm chuỗi con** nên coi `.dwg` là
   `.dxf` cùng tên — báo "đã có" cho một bản vẽ chưa bao giờ vào mô hình, không lỗi nào nổi lên.
12. **Mục 11.1 — `IdsValidate`** ✅ 2026-09-05: đọc file **IDS 1.0** của chủ đầu tư/thẩm tra rồi kiểm thẳng
   trên mô hình Revit. Tầng thuần `Shared.Logic/Ids` (34 ca test, phủ 100% dòng), lệnh Core, nút Ribbon,
   2 ca kiểm smoke. Chạy thật: *1270 phần tử, 3 specification, 42 không đạt, 1 specification không có phần
   tử nào để kiểm*. Trước PR này, roadmap mô tả tầng `Shared.Logic/Ids` ở **thì hiện tại** trong khi thư mục
   đó chưa tồn tại — bốn bộ test đối chiếu mã nguồn không soi tới roadmap. ✅ Đối chiếu với **IfcTester** 2026-09-05 (§39): lộ 42 lỗi giả do ánh xạ tường kính và fixture sai
   chuẩn XSD, cả hai đã sửa; sau sửa khớp cả 3 specification. ✅ 11.4 quyết xong (§41): kiểm **trên chính file IFC** qua `--verify-ids`, 10/10 specification khớp IfcTester. ⬜ Solibri chưa có trên máy.
13. **`.NET 8` hết hỗ trợ 10/11/2026** — `BatchRunner` và bộ test đã lên **net10.0** (2026-09-05), và ba chỗ
   còn viết tay `net8.0` vào đường dẫn `bin` nay hỏi MSBuild (`-getProperty:TargetFramework`).
14. **Không mở P3** — giữ hướng chiều sâu theo [`roadmap.md`](roadmap.md).
