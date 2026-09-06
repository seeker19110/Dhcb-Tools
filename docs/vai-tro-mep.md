# DHCB Tools cho MEP (1 trang)

> Dành cho kỹ sư điện/nước/HVAC dựng hệ thống trong Revit. Cài đặt và tổng quan chung: [`tong-quan.md`](tong-quan.md).

## Ba lệnh nên bấm trước

1. **`HangerAuto`** — đặt hanger tự động dọc theo ống/đường ống theo khoảng cách khai báo, thay vì đặt tay từng cái.
2. **`SlopePipes`** — kiểm/áp độ dốc đường ống thoát nước theo tiêu chuẩn, báo ngay đoạn nào sai độ dốc.
3. **`SetoutExport`** — xuất toạ độ định vị (điểm neo sleeve, hanger…) cho máy toàn đạc — dùng khi ra công trường,
   xem thêm [`toa-do-dinh-vi.md`](toa-do-dinh-vi.md).

Cả ba đều ở tab **DHCB Tools** trên Ribbon, nhóm *MEPF* và *Trắc đạc*.

## Trước khi bấm `HangerAuto`/`SleeveAuto`

Nhóm lệnh MEPF cần đúng **tên family/tham số của dự án đang mở** (ví dụ family hanger, tên tham số cao độ).
Nếu báo lỗi "không tìm thấy" family/tham số:

- Khai tên riêng của dự án vào `%APPDATA%\DHCB\dictionary.json` (mẫu: `configs/dictionary.sample.json`) —
  lệnh tra theo tên khai trước, không phải thay hẳn tên chuẩn.
- Chưa có family mẫu đúng chuẩn dự án thì đây vẫn là hạn chế đang mở — xem
  [`danh-gia-va-tam-nhin-2026-09-06.md`](danh-gia-va-tam-nhin-2026-09-06.md) §4.4.

## Khi cần kiểm tra chéo hệ thống

- **`ParameterRuleCheck`** — kiểm tham số theo quy tắc khai trong `configs/parameter-rules`.
- **`ClashDetection`** — dò va chạm giữa các hệ, xuất kèm BCF để phối hợp với các bộ môn khác.

## Cách dùng

Mở model → bấm lệnh trên Ribbon → chọn hệ/tuyến cần chạy → bấm **Xem trước** để thấy trước kết quả (không
ghi gì vào model) → đúng ý thì bấm **Chạy thật**.

## Gặp lỗi?

Mã lỗi `E-...` kèm mô tả và (với lỗi thiếu family/tham số) danh sách tên đã thử tìm — tra tại
[`ma-loi.md`](ma-loi.md).
