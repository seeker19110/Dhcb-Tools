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

## Lỗi đã biết

### Đã sửa ở Giai đoạn 0

| # | Lỗi | Cách sửa |
|---|---|---|
| 1 | Round-trip số thực hỏng trên máy locale Việt Nam | Import đọc số bằng `InvariantCulture` đúng như lúc xuất, có fallback sang culture hệ thống cho file người dùng tự gõ (`ParameterImportCommand.TryParseDouble/TryParseInt`) |
| 2 | Cảnh báo bị nuốt ở dòng `return` cuối | Thêm `CommandResult.With(summary, affected)` giữ nguyên `Messages`/`Errors`; áp dụng cho AutoNumbering (Revit + AutoCAD) và ParameterImport |
| 3 | Bất đối xứng Export/Import tham số | Import tra tham số ở instance rồi fallback sang Type (`ResolveParameter`), đối xứng với export; tham số chỉ đọc/ghi hỏng đều được báo ra |
| 4 | CSV không có BOM | `ParameterExportCommand` và `LayerExportCommand` ghi bằng `new UTF8Encoding(true)` |
| 5 | Đánh số theo hàng không có dung sai | Thêm `AutoNumberingConfig.RowToleranceMm` (mặc định 300mm), gom toạ độ về "rổ" trước khi sắp nên tiêu chí phụ mới có tác dụng |
| 6 | DrawingCleanup xoá nhầm và làm hỏng transaction | Linetype của layer definition và `CELTYPE` được tính là đang dùng; loại trừ layer hiện hành `CLAYER`; mỗi `Erase()` bọc try/catch riêng và báo object nào không xoá được |
| 7 | Request timeout vẫn thực thi | Việc trong queue mang cờ huỷ; client hết 30s thì handler bỏ qua thay vì chạy khi không còn ai nhận kết quả |
| 8 | HTTP Bridge không xác thực | Token 32 byte ngẫu nhiên sinh lúc khởi động, lưu ở `%APPDATA%\DhcbTools\bridge-token.txt`; `/execute` và `/query` bắt buộc header `Authorization: Bearer <token>` (so sánh theo thời gian cố định), `GET /health` vẫn mở |
| 11 | Hanger và PipeSplitter chưa gắn UI | Thêm `HangerAutoCommand`, `PipeSplitterAutoCommand` và hai nút trong panel MEPF (Bridge đã dispatch sẵn từ trước) |

### Còn lại

9. **Trùng lặp ~40%** giữa `Core` và `Core.AutoCAD`: `ICoreCommand`, `CommandResult`, `Polyfills`,
   và gần như toàn bộ phần HTTP của hai Bridge (nay thêm cả `BridgeToken`). Chi phí sẽ nhân lên
   theo mỗi tính năng mới — cần tách `DhcbTools.Shared` **trước** khi thêm routing MEPF.
10. **Hiệu năng collector.** `ParameterExport` và `AutoNumbering` dùng `FilteredElementCollector`
    rồi lọc bằng LINQ trong bộ nhớ. Nên dùng `ElementMulticategoryFilter` để Revit lọc ở tầng dưới.
12. **Không có test nào.** Nợ lớn nhất còn lại: logic hình học (giao cắt, khoảng cách hanger),
    parser CSV và thuật toán gom hàng đều là logic thuần, test được mà chưa có test.

---

## Việc tiếp theo

Theo [`roadmap.md`](roadmap.md) — Giai đoạn 0 (trả nợ kỹ thuật) rồi Giai đoạn 1 (batch runner):

1. Thêm `DhcbTools.Core.Tests` (xUnit) — phủ trước `TryParseDouble`, `Bucket`, parser CSV.
2. Tách `DhcbTools.Shared` (#9) — làm **trước** khi thêm routing MEPF (khối lượng lớn nhất còn lại).
3. Chuyển collector sang `ElementMulticategoryFilter` (#10).
4. Batch runner chạy đêm (Giai đoạn 1) — đích của dự án theo tài liệu nghiên cứu.
