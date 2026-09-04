# Mẫu thu phản hồi — giai đoạn 9.4

Mục 9.4 của [`roadmap.md`](roadmap.md): *đưa cho một nhóm kỹ sư dùng thật, thu phản hồi theo mẫu (lệnh nào
dùng hằng tuần, lệnh nào bấm rồi bỏ). Số liệu này quyết định giai đoạn 10/11 đi sâu vào đâu.*

Mẫu này tồn tại để phản hồi **đếm được**, không phải để thu cảm nhận. Cảm nhận thì ai cũng nói "tốt";
cái quyết định được giai đoạn 10/11 là bảng đánh dấu bên dưới.

> **Phần đo được đã có máy làm.** Lệnh nội bộ `UsageReport` đọc log của chính máy đó (`%APPDATA%\DHCB\logs`,
> 30 ngày) ra đúng ba cột dưới đây: *dùng bao nhiêu ngày · bấm rồi bỏ (xem trước mà chưa bao giờ chạy thật) ·
> chưa bấm lần nào*. Chạy nó trước khi phát mẫu này, rồi chỉ hỏi người phần mà máy không trả lời được —
> **vì sao bỏ**, và bốn câu hỏi mở cuối trang. Số liệu không rời máy kỹ sư.

## Cách dùng

Mỗi kỹ sư nhận một bản chép của file này, dùng **hai tuần**, rồi điền. Chỉ tick một cột cho mỗi lệnh:

| Cột | Nghĩa |
|---|---|
| **Tuần** | Dùng ít nhất một lần mỗi tuần — lệnh này vào được nếp làm việc |
| **Bỏ** | Đã bấm thử, rồi thôi không dùng nữa. **Bắt buộc ghi lý do** ở cột cuối |
| **Chưa** | Chưa có dịp dùng — không phải chê, chỉ là công việc chưa tới |

Cột **Bỏ** là cột quan trọng nhất và cũng là cột dễ bị bỏ trống nhất. Một lệnh bị bỏ mà không ai ghi lý do
thì với dự án nó không khác gì lệnh chưa ai dùng — mất trắng thông tin.

Mốc của roadmap: **≥ 5 kỹ sư dùng hằng tuần không cần hỏi** sau v1.1.

---

## Revit — 46 lệnh

| Lệnh | Việc nó làm | Tuần | Bỏ | Chưa | Nếu bỏ: vì sao |
|---|---|:--:|:--:|:--:|---|
| `ParameterExport` | Xuất tham số phần tử theo category ra CSV | ☐ | ☐ | ☐ | |
| `ParameterImport` | Nhập CSV ghi ngược tham số vào mô hình | ☐ | ☐ | ☐ | |
| `RemoveUnusedViews` | Xoá view không đặt trên sheet và sheet rỗng | ☐ | ☐ | ☐ | |
| `AutoNumbering` | Đánh số hàng loạt theo vị trí hình học | ☐ | ☐ | ☐ | |
| `BatchExport` | Xuất PDF/DWG/IFC/NWC hàng loạt | ☐ | ☐ | ☐ | |
| `HealthReport` | Báo cáo HTML sức khoẻ mô hình | ☐ | ☐ | ☐ | |
| `ProjectInfo` | Gán thông tin dự án | ☐ | ☐ | ☐ | |
| `LevelSetup` | Tạo tầng + view plan từ danh sách | ☐ | ☐ | ☐ | |
| `GridSetup` | Tạo trục từ danh sách | ☐ | ☐ | ☐ | |
| `FamilyLoader` | Load family theo danh mục | ☐ | ☐ | ☐ | |
| `SleeveAuto` | Đặt sleeve tại giao cắt MEP × tường/sàn | ☐ | ☐ | ☐ | |
| `ElevationTag` | Điền cao độ đáy/đỉnh/tim vào tham số MEP | ☐ | ☐ | ☐ | |
| `HangerAuto` | Đặt hanger theo khoảng cách chuẩn | ☐ | ☐ | ☐ | |
| `PipeSplitter` | Chia ống/duct theo chiều dài cây | ☐ | ☐ | ☐ | |
| `ConnectorChecker` | Liệt kê connector MEP hở | ☐ | ☐ | ☐ | |
| `RouteFromLines` | Routing mức A: dựng duct/pipe/tray từ model line vẽ tay | ☐ | ☐ | ☐ | |
| `DevicePlacement` | Routing mức B: rải thiết bị đầu cuối theo phòng | ☐ | ☐ | ☐ | |
| `SizingProposal` | Đề xuất kích thước duct/pipe theo lưu lượng → CSV | ☐ | ☐ | ☐ | |
| `ApplySizing` | Áp kích thước từ CSV đã duyệt | ☐ | ☐ | ☐ | |
| `SystemColor` | Tạo filter + tô màu theo hệ trong view template | ☐ | ☐ | ☐ | |
| `SystemName` | Đặt System Name theo quy tắc {Discipline}-{Abbr}-{Zone}-{N} | ☐ | ☐ | ☐ | |
| `FlowNumbering` | Đánh số thiết bị theo thứ tự dòng chảy từ nguồn | ☐ | ☐ | ☐ | |
| `ProjectFromTemplate` | Tạo file mới từ template, bật workshare, tạo workset | ☐ | ☐ | ☐ | |
| `TransferStandards` | Chuyển view template, filter, line style… từ file chuẩn | ☐ | ☐ | ☐ | |
| `GridFromCsv` | Tạo trục/level từ CSV (kể cả CSV trích từ bản CAD) | ☐ | ☐ | ☐ | |
| `SheetBatchCreate` | Tạo sheet hàng loạt từ CSV và đặt view | ☐ | ☐ | ☐ | |
| `SheetRename` | Đổi số/tên sheet hoặc view theo mẫu token + regex, chống trùng | ☐ | ☐ | ☐ | |
| `RevisionOnSheets` | Gán hoặc bỏ một revision trên nhiều sheet | ☐ | ☐ | ☐ | |
| `StylePurge` | Liệt kê và xoá style không được tham chiếu | ☐ | ☐ | ☐ | |
| `ColorByParameter` | Tô màu phần tử theo giá trị tham số + chú giải CSV | ☐ | ☐ | ☐ | |
| `FamilyAudit` | Kiểm kê family/type ra CSV; đổi tên theo mẫu | ☐ | ☐ | ☐ | |
| `WarningsExport` | Xuất warning ra CSV kèm ElementId/category, đếm theo loại | ☐ | ☐ | ☐ | |
| `SlopePipes` | Đặt hoặc kiểm tra dốc ống thoát nước theo % hoặc bảng DN | ☐ | ☐ | ☐ | |
| `PipeKick` | Kick/jog một ống bằng hai cút 45° hoặc 90° | ☐ | ☐ | ☐ | |
| `SystemBom` | BOM theo hệ/spool: ống theo mét + số cây, fitting theo số lượng | ☐ | ☐ | ☐ | |
| `AutoRoute` | Routing mức C: A* né chướng ngại giữa 2 điểm *(thử nghiệm)* | ☐ | ☐ | ☐ | |
| `ScheduleExport` | Xuất schedule ra CSV đúng cột/hàng đang hiển thị | ☐ | ☐ | ☐ | |
| `ViewportCopy` | Copy legend/schedule sang nhiều sheet, cùng vị trí, ghim lại | ☐ | ☐ | ☐ | |
| `SetoutExport` | Xuất toạ độ định vị (tim cột, tâm thiết bị, giao trục) ra CSV cho máy toàn đạc + DXF *(thử nghiệm)* | ☐ | ☐ | ☐ | |
| `ConstructionStatus` | Ghi trạng thái thi công từ CSV hiện trường vào mô hình *(thử nghiệm)* | ☐ | ☐ | ☐ | |
| `ProgressReport` | Báo cáo tiến độ: % theo số lượng và chiều dài, gộp theo tầng/hệ, luỹ kế theo tuần *(thử nghiệm)* | ☐ | ☐ | ☐ | |
| `ParameterRuleCheck` | Kiểm tra tham số thiếu / sai quy tắc đặt tên → HTML | ☐ | ☐ | ☐ | |
| `ClashDetection` | Va chạm giữa hai nhóm category → HTML + 3D view | ☐ | ☐ | ☐ | |
| `CadLayerMap` | AI offline: gợi ý map layer CAD → Revit type, CSV để duyệt | ☐ | ☐ | ☐ | |
| `SpecToConfig` | AI offline: trích tầng/cao độ/hệ từ thuyết minh → config | ☐ | ☐ | ☐ | |
| `DictionaryLearn` | AI offline: soi tên tham số thật của dự án → dictionary.json | ☐ | ☐ | ☐ | |

## AutoCAD — 15 lệnh

| Lệnh | Việc nó làm | Tuần | Bỏ | Chưa | Nếu bỏ: vì sao |
|---|---|:--:|:--:|:--:|---|
| `LayerExport` | Xuất layer ra CSV | ☐ | ☐ | ☐ | |
| `LayerImport` | Nhập layer từ CSV | ☐ | ☐ | ☐ | |
| `DrawingCleanup` | Dọn layer rỗng, block/linetype/textstyle/dimstyle không dùng | ☐ | ☐ | ☐ | |
| `AutoNumbering` | Đánh số Block Reference theo attribute | ☐ | ☐ | ☐ | |
| `AttributeExport` | Xuất attribute của block ra CSV | ☐ | ☐ | ☐ | |
| `AttributeImport` | Nhập CSV ghi ngược attribute vào block | ☐ | ☐ | ☐ | |
| `TextReplace` | Tìm/thay văn bản trong Text, MText, Attribute (regex) | ☐ | ☐ | ☐ | |
| `LayerStandardCheck` | Kiểm tra layer theo bộ quy tắc đặt tên → HTML | ☐ | ☐ | ☐ | |
| `GridExtract` | Trích trục từ layer AXIS ra CSV cho Revit GridFromCsv | ☐ | ☐ | ☐ | |
| `XrefAudit` | Liệt kê xref, đường dẫn thiếu, xref chưa load | ☐ | ☐ | ☐ | |
| `LayerTranslate` | Map layer cũ → layer chuẩn theo CSV (như LAYTRANS) | ☐ | ☐ | ☐ | |
| `DrawingCompare` | So bản vẽ với DWG khác ở mức layer → CSV/HTML | ☐ | ☐ | ☐ | |
| `BlockQuantity` | Đếm block theo tên (nhóm theo attribute) → CSV BOM | ☐ | ☐ | ☐ | |
| `AttributeIncrement` | Gán attribute tăng dần theo mẫu {n:000} theo vị trí | ☐ | ☐ | ☐ | |
| `CadLayerMap` | AI offline: gợi ý map layer → Revit type từ danh sách type | ☐ | ☐ | ☐ | |

---

## Bốn câu hỏi mở

Trả lời ngắn cũng được, nhưng **đừng bỏ trống câu 2** — đó là câu bắt được thứ mà bảng tick ở trên không
bắt được.

1. **Việc tay nào bạn vẫn phải làm** mà tưởng bộ công cụ này phải làm hộ?

2. **Lần nào bộ công cụ làm sai / làm hỏng cái gì?** Ghi càng cụ thể càng tốt: lệnh nào, model nào, nó
   làm gì mà bạn không ngờ. Kể cả khi bạn đã Undo được và không mất gì.

3. **Thông báo nào bạn đọc mà không hiểu nó muốn gì?** (chép nguyên câu)

4. Nếu chỉ được giữ lại **năm lệnh** và bỏ hết phần còn lại, bạn giữ lệnh nào?

## Cách tổng hợp

Đếm theo lệnh, không đếm theo người:

- **Tuần ≥ 5 người** → lệnh trụ cột. Giai đoạn 10/11 phải giữ chúng chạy đúng trước mọi thứ khác.
- **Bỏ ≥ 2 người** → đọc lý do trước khi đụng vào mã. Lệnh bị bỏ vì *khó dùng* và lệnh bị bỏ vì *sai việc*
  cần hai cách chữa khác hẳn nhau, mà bảng tick không phân biệt được — chỉ cột lý do phân biệt được.
- **Chưa ≈ tất cả** → chưa có dữ liệu, đừng vội kết luận là lệnh thừa.

Câu trả lời số 2 đi thẳng vào [`bang-chung-test.md`](bang-chung-test.md) thành ca kiểm, kể cả khi chưa sửa
được ngay — cùng lối đã dùng cho §12–§19.

> Danh sách lệnh trong mẫu này được `PhanHoiFormTests` đối chiếu hai chiều với `CommandCatalog`, nên thêm
> hay bỏ một lệnh trong mã mà quên sửa mẫu thì test đỏ.
