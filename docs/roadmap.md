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

## Giai đoạn 6 — Tuỳ nhu cầu 🟡

- **Routing mức C:** `PathFinder3D` (A* lưới 3D, phạt rẽ, khoảng hở) ✅ phần thuần; ⬜ lệnh Core: chọn 2 điểm + hộp tìm
  kiếm → polyline → model line → `RouteFromLines`.
- **MCP server:** ✅ `scripts/dhcb_mcp_server.py`.

## Giai đoạn 7 — Khoảng trống so với tool thị trường ✅ P1 + P2 🧪

Khảo sát và kế hoạch: [`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md).
P1 (7.1–7.14) và P2 (7.15–7.21: ống dốc, kick, BOM spool, AutoRoute mức C → A, ScheduleExport, ViewportCopy, vỏ AutoCAD
core-only) đã có mã nguồn và test phần thuần. **Việc tiếp theo:** kiểm thử trên model thật theo `dac-ta-kiem-thu.md` §4.2,
đặc biệt PipeKick (phụ thuộc cút 45° trong routing preference) và AutoRoute (đo thời gian A* với bước 100 mm).

## Sau đó

- Test tích hợp chạy **bên trong** Revit (add-in test runner kích bằng batch runner) — hạ tầng đã có.
- WPF form cho các lệnh hay dùng (hiện config JSON + xem trước) khi có phản hồi người dùng.
- `ParameterImport` đọc CSV theo luồng ký tự (ô có xuống dòng).
