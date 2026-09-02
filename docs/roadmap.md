# Lộ trình phát triển DHCB Tools

Tài liệu này mô tả **kế hoạch phía trước**. Hiện trạng thực tế nằm ở [`progress.md`](progress.md). Đặc tả chi tiết ở
[`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md), kế hoạch kiểm thử ở [`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md), cơ sở kỹ
thuật ở [`nghien-cuu-dhcb-revit-tools.md`](nghien-cuu-dhcb-revit-tools.md).

Ký hiệu: ✅ xong · 🟡 làm dở · ⬜ chưa bắt đầu · 🧪 code xong, chờ kiểm thử trên phần mềm thật.

## Nguyên tắc xuyên suốt

1. **Core không biết UI.** `Document`/`Database` + config → `CommandResult`; một lệnh chạy được từ Ribbon, Bridge, batch, AI.
2. **`DryRun` mặc định bật.** Ribbon luôn chạy xem trước rồi hỏi; Bridge/MCP ép `dryRun:true` trừ khi xác nhận.
3. **Một lệnh = một transaction**, `SilentFailuresPreprocessor`.
4. **AI chỉ sinh đề xuất**, offline, whitelist lệnh — xem [`ai-offline.md`](ai-offline.md).
5. **Phần tính được thì test được**: thuật toán xuống `Shared.Logic`, CI xanh trước khi lên máy Revit.

---

## Giai đoạn 0 — Trả nợ kỹ thuật ✅

Token Bridge · `Shared.Hosting` · Hanger/PipeSplitter gắn Ribbon + Bridge · DrawingCleanup an toàn · timeout huỷ lệnh ·
collector hiệu năng. Xem bảng lỗi #1–#12 trong `progress.md`.

## Giai đoạn 1 — Batch runner chạy đêm ✅ 🧪

`DhcbTools.BatchRunner` + hook add-in Revit + `DHCB_RUN` cho accoreconsole + Task Scheduler. Chi tiết
[`batch-runner.md`](batch-runner.md). Cần một đêm chạy thật trên máy có license để chốt.

## Giai đoạn 2 — Khởi tạo dự án & hồ sơ ✅ 🧪

`ProjectFromTemplate`, `TransferStandards`, `GridFromCsv` (CSV từ Excel hoặc từ AutoCAD `GridExtract`),
`SheetBatchCreate`. Hạn chế API: LineStyles/ObjectStyles không copy được qua `CopyElements` — tool báo rõ, làm tay bằng
Transfer Project Standards.

## Giai đoạn 3 — MEPF ✅ 🧪

| Bước | Trạng thái |
|---|---|
| Sleeve, tag cao độ, hanger, chia ống, connector hở | ✅ |
| Routing mức A — theo line vẽ tay | 🧪 `RouteFromLines` (RouteGraph có test) |
| Routing mức B — rải thiết bị theo phòng | 🧪 `DevicePlacement` (DevicePattern có test) |
| Sizing (đề xuất → CSV → áp) | 🧪 `SizingProposal` / `ApplySizing` (Duct/PipeSizing có test theo ASHRAE/SCH40) |
| Màu/filter theo hệ, System Name | 🧪 `SystemColor` / `SystemName` |
| Đánh số theo dòng chảy | 🧪 `FlowNumbering` |

**Việc tiếp theo cho routing:** kiểm thử trên model mẫu chữ U + nhánh T (DoD §3.1), đo tỉ lệ fitting dựng được với 3 bộ
family khác nhau; bổ sung fallback dời điểm khi đoạn ngắn hơn fitting.

## Giai đoạn 4 — Tự động hoá cấp 2 ✅ 🧪

`ElevationUpdater` (tắt mặc định, tự tắt khi > 200 ms/lần), `ParameterRuleCheck`, `ClashDetection` + `clash-accepted.json`.
**Việc tiếp theo:** đo hiệu năng updater trên dự án thật rồi mới bật mặc định.

## Giai đoạn 5 — Lớp AI ✅ (offline)

Map layer → type, thuyết minh → config, phân tích cảnh báo, ra lệnh tiếng Việt. Heuristic mặc định, Ollama local tuỳ chọn.
**Việc tiếp theo:** bổ sung từ điển đồng nghĩa layer theo chuẩn từng công ty (file JSON ngoài repo), thêm mẫu regex cho
thuyết minh thực tế.

## Giai đoạn 6 — Tuỳ nhu cầu ✅ 🧪

- **Routing mức C:** `PathFinder3D` (A* lưới 3D, phạt rẽ, khoảng hở) ✅ phần thuần; ✅ lệnh Core `AutoRoute` (2 điểm + hộp
  tìm kiếm → polyline rút gọn → model line → tuỳ chọn `RouteFromLines`) — 🧪 cần đo thời gian trên model thật.
- **MCP server:** ✅ `scripts/dhcb_mcp_server.py`, có `--read-only` và `--group` (mục 7.14).

## Giai đoạn 7 — Khoảng trống so với tool thị trường ✅ P1 + P2 🧪

Khảo sát và kế hoạch: [`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md).

| Đợt | Nội dung | Trạng thái |
|---|---|---|
| P1 (7.1–7.14) | SheetRename, RevisionOnSheets, StylePurge, ColorByParameter, FamilyAudit, WarningsExport, checkset ngưỡng; LayerTranslate, DrawingCompare, BlockQuantity, AttributeIncrement, purge sâu; batch autodetect + PlotPdf; AI structured outputs, MCP read-only/nhóm | ✅ merge #11 · 🧪 |
| P2 (7.15–7.21) | SlopePipes, PipeKick, SystemBom, AutoRoute, ScheduleExport, ViewportCopy; vỏ `DhcbTools.AutoCAD.Core` cho accoreconsole | ✅ merge #12 · 🧪 |
| P3 (chưa chốt) | Chỉ mở sau vòng kiểm thử 1: BOM ra bản vẽ spool (sheet + tag tự động), sizing có tổn thất áp suất theo fitting (MagiCAD), từ điển layer theo chuẩn công ty, copy view thường qua Duplicate + đặt lại (pyRevit), Layer Director cho AutoCAD | ⬜ |

**Việc tiếp theo (ưu tiên cao nhất toàn dự án):** vòng kiểm thử 1 theo
[`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md) — đặc biệt PipeKick (cút 45° trong
routing preference), AutoRoute (thời gian A*), SlopePipes (ống đã nối hai đầu), PlotPdf (thứ tự prompt theo phiên bản).

## Nền tảng — .NET 10 ⬜

Microsoft ngừng hỗ trợ .NET 8 ngày 10/11/2026; Autodesk đang preview di trú Revit 2025/2026 lên .NET 10, AutoCAD 2026.1
(package `AutoCAD.NET 25.1.x`) đã ở .NET 10. Việc cần làm khi SDK và phần mềm sẵn: thêm nhánh TFM `net10.0-windows` trong
`Directory.Build.props` (điều kiện `RevitVersion >= 2027` / `AcadVersion >= 2026`), chạy `check-build.sh` với tham số mới,
kiểm `Shared.*` (netstandard2.0) nạp được. Không đổi logic.

## Sau đó

- Test tích hợp chạy **bên trong** Revit (add-in test runner kích bằng batch runner) — hạ tầng đã có.
- WPF form cho các lệnh hay dùng (hiện config JSON + xem trước) khi có phản hồi người dùng.
- `ParameterImport` đọc CSV theo luồng ký tự (ô có xuống dòng).
