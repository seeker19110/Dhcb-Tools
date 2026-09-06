# DHCB Tools cho AutoCAD (1 trang)

> Dành cho kỹ sư dựng bản vẽ CAD (layer, block, attribute). Cài đặt và tổng quan chung: [`tong-quan.md`](tong-quan.md).

## Ba lệnh nên bấm trước

1. **`LayerStandardCheck`** — kiểm layer trong bản vẽ theo chuẩn khai ở `configs/layer-rules`, báo layer sai
   tên/màu/linetype thay vì dò tay từng đối tượng.
2. **`AttributeIncrement`** — tăng dần giá trị attribute của block theo thứ tự chọn (đánh số phòng, mã thiết bị…).
3. **`BlockQuantity`** — thống kê số lượng block theo tên, xuất bảng để đối chiếu khối lượng.

Cả ba chạy được từ dòng lệnh AutoCAD (gõ `DHCB_LAYER_CHECK`, `DHCB_ATTR_INCREMENT`, `DHCB_BLOCK_QUANTITY`)
hoặc qua HTTP Bridge nếu đã bật.

## Cách dùng

Mở bản vẽ → gõ lệnh (hoặc chọn từ menu nếu add-in đã nạp) → chọn đối tượng/layer cần áp → lệnh luôn chạy
**dry-run trước** (hiện kết quả sẽ đổi gì) → xác nhận thì mới ghi thật vào bản vẽ.

## Việc khác hay dùng

- **`DrawingCleanup`** — dọn CLAYER, linetype rác của layer, xref không dùng — an toàn, không xoá geometry.
- **`XrefAudit`** — kiểm tình trạng các xref đang gắn (thiếu file, sai đường dẫn).
- **`GridExtract`** — trích lưới trục từ layer AXIS ra CSV, dùng làm đầu vào cho `GridFromCsv` bên Revit khi
  cần đồng bộ lưới trục hai phần mềm.

## Gặp lỗi?

Tra mã `E-...` tại [`ma-loi.md`](ma-loi.md).
