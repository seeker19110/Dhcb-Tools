# DHCB Tools cho kiến trúc (1 trang)

> Dành cho kỹ sư/kiến trúc sư dựng hồ sơ sheet, bản vẽ trong Revit. Cài đặt và tổng quan chung:
> [`tong-quan.md`](tong-quan.md).

## Ba lệnh nên bấm trước

1. **`SheetRename`** — đổi tên hàng loạt sheet theo mẫu (số tầng, tên hệ, mã dự án…), thay vì sửa tay từng sheet.
2. **`RevisionOnSheets`** — gắn revision đã tạo trong dự án vào đúng các sheet cần, kèm ô "Revision on Sheet".
3. **`BatchExport`** — xuất hàng loạt PDF/DWG/IFC/NWC từ một danh sách sheet/view, không cần mở từng cái để in.

Cả ba đều ở tab **DHCB Tools** trên Ribbon, nhóm *Sheet & bản vẽ*.

## Khi mô hình cần dọn hoặc kiểm

- **`WarningsExport`** — xuất toàn bộ cảnh báo Revit ra bảng, dễ lọc theo loại thay vì cuộn hộp thoại Warnings.
- **`HealthReport`** — báo cáo nhanh tình trạng mô hình (số family, số warning, view chưa dùng…) trước khi giao hồ sơ.

## Cách dùng

Mở model → bấm lệnh trên Ribbon → form hiện ra đúng field cần nhập (chọn sheet, chọn mẫu tên…) → bấm
**Xem trước** để thấy kết quả sẽ đổi gì mà chưa ghi gì vào model → thấy đúng thì bấm **Chạy thật**.

## Gặp lỗi?

Mọi lỗi có mã dạng `E-...` kèm mô tả rõ nguyên nhân (ví dụ thiếu tham số, thiếu sheet khớp mẫu). Tra mã tại
[`ma-loi.md`](ma-loi.md). Lệnh không tự "làm liều" khi thiếu dữ liệu — báo lỗi và dừng lại thay vì đoán.

## Còn hạn chế gì

Đây là add-in đang trong giai đoạn tìm người dùng thật đầu tiên — xem
[`danh-gia-va-tam-nhin-2026-09-06.md`](danh-gia-va-tam-nhin-2026-09-06.md) §4.1 trước khi dùng cho hồ sơ chính thức.
