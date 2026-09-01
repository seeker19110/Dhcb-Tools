# Nghiên cứu tính năng triển khai cho DHCB Revit Tools

Tài liệu nghiên cứu toàn bộ các nhóm tính năng có thể triển khai cho một bộ công cụ (add-in) Revit, dựa trên khả năng của Revit API (.NET), kèm đánh giá độ khó, giá trị sử dụng và đề xuất lộ trình.

## 1. Nền tảng kỹ thuật

- **Ngôn ngữ / môi trường**: C# (.NET Framework 4.8 cho Revit 2021–2024, .NET 8 cho Revit 2025+), tham chiếu `RevitAPI.dll` và `RevitAPIUI.dll`.
- **Kiến trúc add-in**: `IExternalApplication` (tạo Ribbon tab riêng "DHCB Tools") + nhiều `IExternalCommand` cho từng lệnh.
- **UI**: WPF (khuyến nghị) hoặc WinForms; hỗ trợ modeless qua `ExternalEvent` / `IExternalEventHandler`.
- **Multi-version**: dùng multi-targeting (một solution build cho nhiều phiên bản Revit bằng conditional compilation / Directory.Build.props).
- **Đóng gói triển khai**: file `.addin` vào `%ProgramData%\Autodesk\Revit\Addins\<version>\`, hoặc installer (MSI/Inno Setup), hoặc phân phối qua Autodesk App Store.

## 2. Nhóm tính năng theo mức khả thi

### 2.1 Quản lý mô hình / dọn dẹp (dễ – giá trị cao)
| Tính năng | Mô tả | API chính |
|---|---|---|
| Purge nâng cao | Xoá family, kiểu, vật liệu, filter, view template không dùng | `Document.Delete`, `FilteredElementCollector` |
| Xoá view/sheet thừa | Liệt kê view không nằm trên sheet, xoá hàng loạt | `View`, `Viewport` |
| Kiểm tra warning | Xuất danh sách warnings, nhóm theo loại, zoom tới phần tử | `Document.GetWarnings()` |
| Quản lý CAD link/import | Liệt kê, xoá DWG import, tìm DWG bị "explode" | `ImportInstance`, `CADLinkType` |
| Đổi tên hàng loạt | Đổi tên view, sheet, family, kiểu theo quy tắc/tiền tố | `Element.Name` |
| Kiểm tra line styles, patterns thừa | Dọn line style/fill pattern rác từ CAD | `GraphicsStyle`, `FillPatternElement` |

### 2.2 Tham số & dữ liệu (dễ→trung bình – giá trị rất cao)
- **Gán/copy tham số hàng loạt**: đọc–ghi parameter theo bộ lọc category, copy giá trị giữa các tham số.
- **Xuất/nhập Excel**: xuất schedule, parameter ra Excel (ClosedXML/EPPlus), chỉnh sửa rồi nhập ngược lại — tính năng "killer" của hầu hết bộ tools thương mại.
- **Quản lý Shared Parameter**: tạo/gán shared parameter hàng loạt vào nhiều category, nhiều file.
- **Đánh số tự động**: đánh số phòng, cửa, thiết bị theo hướng chọn hoặc theo tuyến (spline).
- **Tính toán giá trị**: điền tham số bằng công thức (ví dụ mã hoá cấu kiện = tầng + loại + số thứ tự).

### 2.3 Sheet & View (trung bình – giá trị cao)
- Tạo sheet hàng loạt từ Excel/danh sách (số hiệu, tên, title block).
- Đặt view lên sheet tự động, căn theo lưới.
- Nhân bản view + áp view template hàng loạt.
- Tạo view theo tầng/scope box tự động (plan, ceiling, structural).
- Renumber sheet, sắp lại thứ tự bản vẽ.
- In/Export PDF, DWG hàng loạt theo bộ chọn sheet (`Document.Export`, `PrintManager`; Revit 2022+ có PDF export API gốc).

### 2.4 Dựng hình & chỉnh sửa nhanh (trung bình)
- Tạo tường/dầm/cột từ CAD link (đọc layer DWG → dựng phần tử Revit).
- Chia tường/dầm theo tầng, join/unjoin hàng loạt.
- Căn chỉnh (align) phần tử, xoay/di chuyển hàng loạt theo điều kiện.
- Tạo lỗ mở (opening/sleeve) tại giao cắt MEP với tường/sàn — cần `ReferenceIntersector` hoặc kiểm tra giao solid.
- Copy phần tử giữa các tầng/giữa các file link (`ElementTransformUtils.CopyElements`).

### 2.5 MEP chuyên biệt (khó hơn – giá trị cao cho hạ tầng cơ điện)
> Đã mở rộng thành danh mục MEPF đầy đủ (bao gồm auto routing 3 mức) trong `nghien-cuu-mepf-tu-dong.md`.
- Đặt sleeve tự động tại giao ống/máng cáp với kết cấu.
- Đánh tag hàng loạt cho duct/pipe (kích thước, cao độ, hệ thống).
- Tính toán và điền cao độ đáy/đỉnh ống vào tham số.
- Tạo hanger/support tự động theo khoảng cách.
- Kiểm tra va chạm (clash) nội bộ bằng solid intersection (`BooleanOperationsUtils`, `ElementIntersectsElementFilter`).

### 2.6 Kết cấu / kiến trúc chuyên biệt
- Tạo thép hình, đánh số cấu kiện kết cấu.
- Tự động tạo mặt cắt qua cấu kiện (section theo dầm/cột) phục vụ shop drawing.
- Thống kê cốt thép, khối lượng bê tông (đọc `Rebar` API).
- Tạo phòng/room tự động, gán finish (ốp lát) theo phòng.
- Tạo dimension tự động cho lưới trục, tường (`Document.Create.NewDimension`).

### 2.7 Kiểm tra chất lượng mô hình (QA/QC)
- Model checker: kiểm tra quy tắc đặt tên, tham số bắt buộc, phần tử trùng lặp, phần tử không đúng workset/level.
- Báo cáo sức khoẻ mô hình (số warning, kích thước file, số view, số family in-place…) xuất HTML/Excel.
- So sánh 2 phiên bản mô hình (phần tử thêm/xoá/sửa) qua `ElementId` + hash geometry.

### 2.8 Cộng tác / Worksharing
- Quản lý workset: tạo, gán phần tử vào workset theo quy tắc.
- Liệt kê phần tử bị checkout bởi ai (`WorksharingUtils.GetCheckoutStatus`).
- Đồng bộ + dọn dẹp tự động theo lịch (kết hợp `Idling` event hoặc chạy batch qua Revit journal / Design Automation for Revit trên cloud).

### 2.9 Tự động hoá nâng cao
- **Dynamo node/package** đóng gói từ code C# (ZeroTouch).
- **Batch processing nhiều file**: mở lần lượt các file, chạy lệnh, lưu (dựa trên `Application.OpenDocumentFile` + `IExternalApplication` events).
- **Cập nhật tự động (Updater)**: `IUpdater` + `DynamicModelUpdate` — ví dụ tự điền tham số khi người dùng tạo phần tử mới.
- **Kết nối dữ liệu ngoài**: đồng bộ với Google Sheets/DB nội bộ, gửi báo cáo qua email.

## 3. Giới hạn của Revit API cần lưu ý

- Chỉ chạy trong ngữ cảnh Revit (không sửa file .rvt trực tiếp từ ngoài, trừ Design Automation trên Forge/APS).
- Mọi thay đổi mô hình phải nằm trong `Transaction`; UI modeless phải dùng `ExternalEvent`.
- Một số thứ API không làm được hoặc rất hạn chế: chỉnh sửa một số dialog hệ thống, một vài loại phần tử (ví dụ chỉnh chi tiết stair by sketch hạn chế), in ấn tuỳ chỉnh sâu ở bản Revit cũ.
- Hiệu năng: thao tác hàng loạt cần gom vào ít transaction, tránh `Regenerate` nhiều lần.

## 4. Đề xuất lộ trình triển khai

**Giai đoạn 1 (nền tảng, 1–2 tuần):**
1. Khung add-in: Ribbon tab "DHCB Tools", hệ thống load lệnh, logging, cấu hình multi-version.
2. 3 lệnh đầu tiên giá trị cao/dễ làm: Xuất-nhập Excel tham số, Purge/dọn view thừa, Đánh số tự động.

**Giai đoạn 2 (sheet/view + in ấn):**
3. Tạo sheet hàng loạt từ Excel, đặt view lên sheet, in PDF/DWG hàng loạt.

**Giai đoạn 3 (chuyên ngành):**
4. Chọn theo mảng người dùng chính (MEP sleeve/tag, kết cấu shop drawing, hoặc QA/QC checker).

**Giai đoạn 4 (nâng cao):**
5. Batch nhiều file, model health report, cập nhật tự động, installer + auto-update.

## 5. Cấu trúc dự án đề xuất

```
Dhcb-Tools/
├── src/
│   ├── DhcbTools.Core/          # logic chung, không phụ thuộc UI
│   ├── DhcbTools.Revit/         # ExternalApplication, các ExternalCommand
│   │   ├── Commands/
│   │   ├── UI/                  # WPF windows/viewmodels
│   │   └── Resources/           # icon ribbon
│   └── DhcbTools.Installer/
├── docs/
└── Directory.Build.props        # multi-target Revit 2021–2025
```
