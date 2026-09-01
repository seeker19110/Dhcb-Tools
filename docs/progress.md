# Hiện trạng dự án

Ảnh chụp tại thời điểm cập nhật gần nhất. Kế hoạch phía trước xem [`roadmap.md`](roadmap.md).

> Cập nhật lần cuối: 2026-09-02 · Tương ứng nhánh `main` (sau khi merge Phase 1+2+3: Batch Export, Health Report, Project Init, MEPF)

## Tóm tắt

| Hạng mục | Trạng thái |
|---|---|
| Kiến trúc Core / vỏ UI | ✅ Tốt — nền móng vững cho các giai đoạn sau |
| Lệnh nền tảng (Revit + AutoCAD) | ✅ 3 nhóm lệnh, chạy được từ Ribbon và HTTP |
| HTTP Bridge cho agent AI | ⚠️ Chạy được, có endpoint `/query`, chưa có xác thực |
| Batch export (PDF/DWG/IFC/NWC) + Health report | ✅ Xong |
| Khởi tạo dự án (grid/level/family/project info) | ✅ Xong |
| MEPF — sleeve, tag cao độ, hanger, chia ống, connector checker | ✅ Xong phần nền tảng |
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
(quét connector hở toàn mô hình, tuỳ chọn tạo 3D view khoanh vùng). Ribbon hiện có nút cho
Sleeve, ElevationTag, ConnectorChecker; **Hanger và PipeSplitter đã có Core command nhưng chưa
gắn nút Ribbon lẫn dispatch trong Bridge** — chỉ gọi được bằng cách sửa code, chưa dùng được
từ UI hay HTTP.

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

Xếp theo mức độ. Nhóm "âm thầm" nguy hiểm nhất vì tool báo thành công trong khi kết quả sai.

### Nghiêm trọng — sai âm thầm (#1–#5 đã sửa, xem mục trên)

1. **Round-trip số thực hỏng trên máy locale Việt Nam.** Export ghi bằng `InvariantCulture`
   (dấu chấm), import đọc theo culture hệ thống (dấu phẩy). Trên Windows tiếng Việt, mọi giá trị
   Double xuất ra không import ngược được và bị bỏ qua không báo lỗi.
   `ParameterExportCommand.cs` ↔ `ParameterImportCommand.cs`

2. **Cảnh báo bị nuốt trong AutoNumbering.** Các message "Bỏ qua phần tử X" được gom vào một
   `CommandResult` nhưng dòng `return` cuối tạo object mới, làm mất toàn bộ. Kỹ sư thấy
   "Đã đánh số 40/120" mà không biết 80 phần tử kia hỏng vì lý do gì.
   `Core/AutoNumbering/AutoNumberingCommand.cs`

3. **Bất đối xứng Export/Import tham số.** Export có fallback đọc tham số ở Type, import chỉ tra
   ở instance. Tham số Type xuất ra được, sửa xong không nhập lại được, không báo gì.

4. **CSV không có BOM.** Excel trên Windows hiển thị sai tên tiếng Việt. Cần `new UTF8Encoding(true)`.

### Nghiêm trọng — hành vi sai

5. **Đánh số theo hàng không có dung sai.** Sắp `OrderByDescending(Y).ThenBy(X)`; hai cửa cùng hàng
   lệch 1mm rơi vào hai "hàng" khác nhau nên `ThenBy(X)` gần như vô tác dụng — kết quả thực tế là
   sắp thuần theo Y. Cần gom Y theo dung sai (ví dụ 300mm) rồi mới sắp X trong nhóm.

6. **DrawingCleanup có thể xoá nhầm và làm hỏng transaction.** `CollectUsedLinetypeIds` chỉ duyệt
   entity, bỏ sót linetype mà layer definition đang dùng. Layer hiện hành (`CLAYER`) không được
   loại trừ; `Erase()` nó sẽ ném lỗi, và vì không có try/catch từng item nên hỏng cả transaction.
   `Core.AutoCAD/DrawingCleanup/DrawingCleanupCommand.cs`

7. **Request timeout vẫn thực thi.** Bridge báo timeout sau 30s nhưng item vẫn nằm trong queue;
   khi Revit rảnh nó vẫn chạy dù client đã bỏ đi. Với `dryRun:false` là thay đổi mô hình ngoài ý muốn.

### Bảo mật

8. **HTTP Bridge không xác thực.** Không kiểm tra token, `Origin` hay `Content-Type`. Bất kỳ tiến
   trình nào trên máy đều gửi được lệnh xoá view/sheet với `dryRun:false`. Cần token sinh ngẫu
   nhiên lúc khởi động, lưu ở `%APPDATA%`, bắt buộc qua header `Authorization`.

### Chất lượng

9. **Trùng lặp ~40%** giữa `Core` và `Core.AutoCAD`: `ICoreCommand`, `CommandResult`, `Polyfills`,
   và gần như toàn bộ phần HTTP của hai Bridge. Chi phí sẽ nhân lên theo mỗi tính năng mới.
10. **Hiệu năng collector.** `ParameterExport` và `AutoNumbering` dùng `FilteredElementCollector`
    rồi lọc bằng LINQ trong bộ nhớ. Nên dùng `ElementMulticategoryFilter` để Revit lọc ở tầng dưới.
11. **Hanger và PipeSplitter chưa gắn UI/Bridge.** Core command đã viết xong nhưng không có nút
    Ribbon, không có case trong `DispatchCommand` của Bridge — hiện chỉ chạy được nếu tự viết
    code gọi. Cần bổ sung cả hai chỗ trước khi coi nhóm MEPF nền tảng là "dùng được".
12. **Không còn test nào cho MEPF/Export/ProjectInit.** Cùng nợ kỹ thuật với các lệnh cũ, nhưng
    khối lượng logic hình học (giao cắt, khoảng cách hanger) rủi ro sai cao hơn CSV/đánh số.

---

## Việc tiếp theo

Theo [`roadmap.md`](roadmap.md) — Giai đoạn 0 (trả nợ kỹ thuật) rồi Giai đoạn 1 (batch runner):

1. Thêm `DhcbTools.Core.Tests` — bắt luôn lỗi #1 và #5.
2. Sửa nhóm lỗi âm thầm #1–#4.
3. Gắn Hanger/PipeSplitter vào Ribbon + Bridge (#11).
4. Thêm token cho Bridge (#8).
5. Sửa #6, #7.
6. Tách `DhcbTools.Shared` (#9) — làm **trước** khi thêm routing MEPF (khối lượng lớn nhất còn lại).
