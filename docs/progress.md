# Hiện trạng dự án

Ảnh chụp tại thời điểm cập nhật gần nhất. Kế hoạch phía trước xem [`roadmap.md`](roadmap.md).

> Cập nhật lần cuối: 2026-09-02 · Tương ứng nhánh `claude/hoan-thien-not-w4dq9r` (Giai đoạn 0 — trả nợ kỹ thuật)

## Tóm tắt

| Hạng mục | Trạng thái |
|---|---|
| Kiến trúc Core / vỏ UI | ✅ Tốt — nền móng vững cho các giai đoạn sau |
| Lệnh nền tảng (Revit + AutoCAD) | ✅ 3 nhóm lệnh, chạy được từ Ribbon và HTTP |
| HTTP Bridge cho agent AI | ✅ Có `/query`, xác thực bằng token Bearer, huỷ việc khi client timeout |
| Batch export (PDF/DWG/IFC/NWC) + Health report | ✅ Xong |
| Khởi tạo dự án (grid/level/family/project info) | ✅ Xong |
| MEPF — sleeve, tag cao độ, hanger, chia ống, connector checker | ✅ Xong phần nền tảng, cả 5 lệnh đều có nút Ribbon + Bridge |
| MEPF — routing mức A/B/C | ⬜ Chưa bắt đầu |
| Batch runner chạy đêm theo lịch | ⬜ Chưa bắt đầu |
| `IUpdater` theo sự kiện | ⬜ Chưa bắt đầu |
| Lớp AI | ⬜ Chưa bắt đầu |
| Kiểm thử tự động | ❌ Không có test nào |
| CI | ❌ Không có workflow nào |

Ước tính: hoàn thành khoảng **35%** phạm vi trong tài liệu nghiên cứu — nhảy nhanh hơn dự kiến
vì phần lớn Giai đoạn 5+0 và nền tảng MEPF được làm cùng lúc với hạ tầng ban đầu. Phần còn thiếu
lớn nhất là routing MEPF (khối lượng nhiều nhất) và batch runner (đích của dự án theo tài liệu
nghiên cứu — vẫn chưa có, dù các lệnh nó cần đã có đủ).

---

## Đã làm được

### Khung solution
Tách `Core` (logic thuần) khỏi vỏ Revit/AutoCAD. Mọi lệnh nhận `Document`/`Database` + config
và trả `CommandResult`, nhờ đó dùng lại được ở cả ba nơi: Ribbon, HTTP Bridge, và sau này là
batch runner. Multi-target `net48` (Revit ≤2024) và `net8.0-windows` (Revit 2025+).

### Ba nhóm lệnh nền tảng

| Chức năng | Revit | AutoCAD |
|---|---|---|
| Xuất dữ liệu ra CSV | `ParameterExport` | `LayerExport` |
| Nhập CSV ghi ngược vào model | `ParameterImport` | `LayerImport` |
| Dọn object thừa | `RemoveUnusedViews` | `DrawingCleanup` |
| Đánh số hàng loạt | `AutoNumbering` | `AutoNumbering` |

Cả bốn lệnh đều hỗ trợ `DryRun` (mặc định bật) và gói trong một transaction duy nhất.

### HTTP Bridge
Revit cổng 8765 (`HttpListener` + `ExternalEvent` để marshal về main thread), AutoCAD cổng 8766
(`ExecuteInCommandContextAsync`). Có thêm endpoint `GET /health` và `POST /query` (đọc ngữ cảnh
model, không ghi transaction) bên cạnh `POST /execute`. Kèm client `scripts/dhcb_agent.py` không
cần dependency ngoài.

### Giai đoạn 5+0 — Xuất bản & khởi tạo dự án
`Export/BatchExportCommand` xuất PDF/DWG/IFC/NWC hàng loạt; `Health/HealthReportCommand` xuất
báo cáo HTML (warning, view thừa, open connector, in-place family). `ProjectInit/*` dựng
Level, Grid, load family theo danh mục, gán project info — tất cả từ config JSON.

### MEPF — phần nền tảng
`MEPF/SleeveCommand` (giao cắt MEP × Tường/Sàn, lọc 2 lớp BoundingBox → IntersectsSolid),
`ElevationTagCommand` (cao độ đáy/đỉnh/tim), `HangerCommand` (đặt hanger theo khoảng cách đều
dọc `LocationCurve`), `PipeSplitterCommand` (`BreakCurve` cho Pipe/Duct), `ConnectorCheckerCommand`
(quét connector hở toàn mô hình, tuỳ chọn tạo 3D view khoanh vùng). Cả 5 lệnh đều có nút trong
panel MEPF của Ribbon và case dispatch trong Bridge — dùng được cả từ UI lẫn HTTP.

### Điều kiện cho tự động hoá cấp 2–3
`SilentFailuresPreprocessor` đã có và được dùng đúng trong các lệnh Revit. Đây là điều kiện bắt
buộc để chạy batch không người trực — nền cho batch runner đã sẵn sàng, nhưng **batch runner tự
nó (mở → xử lý → lưu → đóng theo danh sách file, hẹn giờ Task Scheduler) chưa tồn tại.**

---

## Kiểm thử và thư viện logic thuần (Giai đoạn 0, đã làm)

`src/DhcbTools.Shared.Logic` — thư viện netstandard2.0 KHÔNG tham chiếu Revit lẫn AutoCAD, chứa phần
thuật toán trước đây bị trộn trong lệnh và bị chép ở nhiều nơi: `CsvText` (đọc/ghi CSV, UTF-8 có
BOM), `NumericText` (số Invariant, đọc được cả dấu phẩy), `NumberingPlanner` (đánh số theo vị trí có
gom dải theo dung sai), `MepLayout` (vị trí hanger, điểm cắt ống, cao độ, giao bounding box),
`FileNaming`, `ExportVersionMap`, `HtmlText`, `BridgeAuth`.

`tests/DhcbTools.Shared.Logic.Tests` — xUnit, chạy trên CI Linux không cần cài Revit
(`.github/workflows/tests.yml`). Nhờ tách tầng này, các lỗi **#1 (round-trip số), #2 (nuốt cảnh
báo), #3 (bất đối xứng export/import), #4 (CSV không BOM), #5 (đánh số không dung sai)** đã được sửa
và mỗi lỗi có test tái hiện. Kế hoạch kiểm thử đầy đủ — gồm cả kịch bản thủ công cho phần cần Revit
thật — ở [`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md); đặc tả các tính năng còn lại ở
[`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md).

---

## Lỗi đã biết

### Đã sửa ở Giai đoạn 0

### #1–#5 (đã sửa ở `DhcbTools.Shared.Logic`, xem mục trên)

### #6–#11 (đã sửa trực tiếp trong Core/vỏ, chưa có test tự động)

| # | Lỗi | Cách sửa |
|---|---|---|
| 6 | DrawingCleanup xoá nhầm và làm hỏng transaction | Linetype của layer definition và `CELTYPE` được tính là đang dùng; loại trừ layer hiện hành `CLAYER`; mỗi `Erase()` bọc try/catch riêng và báo object nào không xoá được |
| 7 | Request timeout vẫn thực thi | Việc trong hàng đợi mang cờ huỷ; client hết 30s thì handler bỏ qua thay vì chạy khi không còn ai nhận kết quả |
| 8 | HTTP Bridge không xác thực | Dùng `BridgeAuth` (đã test ở `Shared.Logic`) để sinh/so khớp token; `/execute` và `/query` bắt buộc header `Authorization: Bearer <token>` đúng **và** `Content-Type: application/json`; sai token quá 5 lần/60s bị khoá tạm 5 phút; `GET /health` chỉ trả `{status, version}` |
| 11 | Hanger và PipeSplitter chưa gắn UI | Thêm `HangerAutoCommand`, `PipeSplitterAutoCommand` và hai nút trong panel MEPF (Bridge đã dispatch sẵn từ trước) |

Chi tiết thiết kế của #8 và #11 — xem [`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md) §0.1 và §0.3.

### Đã sửa thêm

| # | Việc | Cách sửa |
|---|---|---|
| 10 | Hiệu năng collector | `ParameterExportCommand` và `AutoNumberingCommand` lọc category bằng `ElementMulticategoryFilter` (native) thay vì `.Where(...)` LINQ trong bộ nhớ |

### Còn lại

9. **`DhcbTools.Shared.Hosting` chưa tồn tại** (xem `dac-ta-tinh-nang.md` §0.2): `CommandResult`
   (hai property khác tên — `AffectedElementCount` vs `AffectedCount`), `ICoreCommand<TConfig>`,
   `Polyfills`, và phần dùng chung ~90% của hai `DhcbHttpBridge.cs` (`HttpBridgeServer`) vẫn bị
   nhân đôi giữa Core Revit và Core AutoCAD. `DhcbTools.Shared.Logic` (phần logic thuần, không
   phụ thuộc HttpListener) đã tách xong và dùng chung — đây chỉ còn phần "vỏ" HTTP + kiểu dữ liệu.
12. **Test tự động mới phủ lớp logic thuần.** #6 (DrawingCleanup AutoCAD), #7/#8 (Bridge — cần
    `HttpListener` thật nên khó unit test, xem kịch bản thủ công ở `dac-ta-kiem-thu.md` §4.1), và
    #11 (Ribbon/Bridge wiring) vẫn chưa có test.
13. **Mọi thay đổi C# trong Giai đoạn 0 (cả đợt #1-#5 lẫn #6-#11) chưa được build trên máy có
    Revit/AutoCAD SDK** — môi trường CI hiện chỉ build/test được `DhcbTools.Shared.Logic` (không
    tham chiếu Revit/AutoCAD API). `dotnet build` toàn solution trên Windows là bước bắt buộc
    trước khi coi Giai đoạn 0 là xong.

---

## Việc tiếp theo

Theo [`roadmap.md`](roadmap.md) — Giai đoạn 0 (trả nợ kỹ thuật) rồi Giai đoạn 1 (batch runner):

1. Build thử toàn solution trên Windows (`dotnet build`/Visual Studio) — #6-#11 chưa qua compiler.
2. Tách `DhcbTools.Shared.Hosting` theo `dac-ta-tinh-nang.md` §0.2 — gộp nốt `CommandResult`,
   `ICoreCommand`, `Polyfills`, và phần chung của hai `DhcbHttpBridge.cs`.
3. Batch runner chạy đêm (Giai đoạn 1) — đích của dự án theo tài liệu nghiên cứu.
