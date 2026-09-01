# Hiện trạng dự án

Ảnh chụp tại thời điểm cập nhật gần nhất. Kế hoạch phía trước xem [`roadmap.md`](roadmap.md).

> Cập nhật lần cuối: 2026-09-01 · Tương ứng nhánh `main`

## Tóm tắt

| Hạng mục | Trạng thái |
|---|---|
| Kiến trúc Core / vỏ UI | ✅ Tốt — nền móng vững cho các giai đoạn sau |
| Lệnh nền tảng (Revit + AutoCAD) | ✅ 3 nhóm lệnh, chạy được từ Ribbon và HTTP |
| HTTP Bridge cho agent AI | ⚠️ Chạy được, chưa có xác thực |
| Batch runner | ⬜ Chưa bắt đầu |
| MEPF | ⬜ Chưa bắt đầu |
| Lớp AI | ⬜ Chưa bắt đầu |
| Kiểm thử tự động | ❌ Không có test nào |
| CI | ❌ Không có workflow nào |

Ước tính: hoàn thành khoảng **10%** phạm vi trong tài liệu nghiên cứu. Phần đã làm là phần nền
móng — tỷ lệ này sẽ tăng nhanh hơn ở các giai đoạn sau vì mỗi lệnh mới tái dùng được hạ tầng có sẵn.

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
(`ExecuteInCommandContextAsync`). Kèm client `scripts/dhcb_agent.py` không cần dependency ngoài.

### Điều kiện cho tự động hoá cấp 2–3
`SilentFailuresPreprocessor` đã có và được dùng đúng trong cả ba lệnh Revit. Đây là điều kiện
bắt buộc để chạy batch không người trực — nền cho batch runner đã sẵn sàng.

---

## Lỗi đã biết

Xếp theo mức độ. Nhóm "âm thầm" nguy hiểm nhất vì tool báo thành công trong khi kết quả sai.

### Nghiêm trọng — sai âm thầm

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
11. **Comment lệch code.** `Revit/App.cs` nói Bridge "lazy-get từ DocumentManager", thực tế
    `ExternalEvent` được tạo ngay trong constructor.

---

## Việc tiếp theo

Theo [`roadmap.md`](roadmap.md) — Giai đoạn 0 (trả nợ kỹ thuật) rồi Giai đoạn 1 (batch runner):

1. Thêm `DhcbTools.Core.Tests` — bắt luôn lỗi #1 và #5.
2. Sửa nhóm lỗi âm thầm #1–#4.
3. Thêm token cho Bridge (#8).
4. Sửa #6, #7.
5. Tách `DhcbTools.Shared` (#9) — làm **trước** khi thêm tính năng mới.
