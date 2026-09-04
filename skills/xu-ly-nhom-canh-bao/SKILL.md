---
name: xu-ly-nhom-canh-bao
description: Phân tích cảnh báo (warnings) trong mô hình Revit, gom theo nguyên nhân gốc, xếp thứ tự xử lý và chỉ từng phần tử cho kỹ sư. Dùng khi người dùng nói "model nhiều warning quá", "xử lý cảnh báo", "dọn warning", "review warnings".
---

# Xử lý một nhóm cảnh báo

Model thật thường có hàng trăm đến hàng nghìn cảnh báo. Việc có ích không phải là liệt kê chúng ra, mà là
**gom theo nguyên nhân và chỉ ra nhóm nào đáng sửa trước**.

## Trình tự

### 1. Lấy toàn bộ cảnh báo

```
query warnings { "limit": 0 }
exec WarningsExport { "outputPath": "<Documents>/warnings.csv" }
```

`query warnings` cho dữ liệu để phân tích ngay; `WarningsExport` cho file CSV kỹ sư lọc trong Excel (ghi file,
không sửa mô hình). `limit: 0` nghĩa là **không giới hạn** — lấy hết để đếm đúng; chỉ đặt `limit > 0` khi
muốn xem nhanh vài dòng đầu.

### 2. Gom theo loại, không theo từng cái

Đếm số cảnh báo theo mô tả. Kết quả cần có dạng:

| Loại cảnh báo | Số lượng | Mức |
|---|---:|---|
| Highlighted elements are joined but do not intersect | 312 | Cảnh báo |
| Room không bao kín | 46 | Cảnh báo |
| Elements have duplicate "Mark" values | 12 | Cảnh báo |

### 3. Xếp thứ tự theo mức độ hại thật, không theo số lượng

Thứ tự đề xuất:

1. **Trùng giá trị định danh** (duplicate Mark/Number) — làm sai bảng thống kê và hồ sơ, phải sửa.
2. **Room/Space không bao kín** — sai diện tích, sai tính tải.
3. **Phần tử trùng chỗ nhau** — sai khối lượng.
4. **Joined but do not intersect** — thường vô hại, số lượng lớn, để cuối.

Nói rõ nhóm đông nhất chưa chắc là nhóm quan trọng nhất.

### 4. Chỉ từng nhóm cho kỹ sư xem

Với nhóm đang bàn, lấy ElementId từ kết quả cảnh báo rồi:

```
query show_elements    { "elementIds": [...] }
query element_geometry { "elementIds": [...] }
query snapshot         { "imageWidth": 1600 }
```

Có `element_geometry` mới nói được những câu có ích như "12 phần tử này đều nằm ở tầng 3, quanh trục C-D"
thay vì chỉ đọc lại mã cảnh báo.

### 5. Nhóm sửa được bằng lệnh sẵn có

Một số nhóm có lệnh xử lý thẳng:

| Nhóm cảnh báo | Lệnh | Ghi chú |
|---|---|---|
| Trùng `Mark` | `AutoNumbering` | Đánh lại toàn bộ category, xem trước trước |
| View/sheet thừa | `RemoveUnusedViews` | Luôn xem trước, kỹ sư duyệt danh sách |
| Style không dùng | `StylePurge` | Xem trước, đọc kỹ danh sách sắp xoá |

Mỗi lệnh đều: xem trước → báo số lượng → xin xác nhận → chạy → kiểm lại bằng `changedIds`.

### 6. Cái không tự sửa được

Phần lớn cảnh báo cần người quyết định (xoá cái nào, giữ cái nào). Với những nhóm đó, đưa ra danh sách
ElementId theo nhóm và mô tả cách sửa — **đừng tự đoán rồi sửa**.

## Không được làm

- Không dùng lệnh ghi để "dọn" cảnh báo mà kỹ sư chưa duyệt từng nhóm.
- Không báo "đã xử lý xong cảnh báo" khi mới chỉ chạy `WarningsExport` — đó là xuất báo cáo, không phải sửa.
- Không xoá phần tử để làm mất cảnh báo. Cảnh báo mất mà mô hình sai là tệ hơn.
