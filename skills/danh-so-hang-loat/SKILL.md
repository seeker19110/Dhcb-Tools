---
name: danh-so-hang-loat
description: Đánh số cửa, thiết bị, phòng hoặc phần tử MEP trong Revit theo vị trí hoặc theo dòng chảy, có xem trước và tự kiểm lại sau khi ghi. Dùng khi người dùng nói "đánh số cửa", "đánh số thiết bị", "đánh lại Mark", "numbering".
---

# Đánh số hàng loạt

Đánh số là việc dễ làm hỏng: ghi nhầm tham số, ghi đè số cũ, hoặc chạy trên nhầm category thì phải Undo cả loạt.
Quy trình dưới đây bắt buộc **xem trước và tự kiểm lại**.

## Trước khi làm gì cả

Hỏi kỹ sư ba điều nếu chưa rõ trong yêu cầu:

1. **Category nào** (Doors, Mechanical Equipment, Rooms…).
2. **Ghi vào tham số nào** — mặc định `Mark`, nhưng nhiều công ty dùng tham số riêng.
3. **Mẫu số**: tiền tố, số chữ số, có theo tầng không.

Đừng đoán. Đánh số sai vào tham số đang dùng cho việc khác là mất dữ liệu.

## Trình tự

### 1. Xác nhận tham số có thật và ghi được

```
query parameters_of { "categories": ["Doors"], "writableOnly": true }
```

Nếu tham số kỹ sư nói không có trong danh sách trả về, **dừng lại và nói rõ** — kèm danh sách tham số ghi
được thật để họ chọn. Đây là chỗ hay hỏng nhất: gõ `"Ký hiệu"` trên model tiếng Anh thì lệnh chạy xong mà
không ghi được gì.

### 2. Xem trước

```
exec AutoNumbering {
  "category": "Doors",
  "parameterName": "Mark",
  "prefix": "D3-",
  "digits": 3,
  "levelName": "Tầng 3"
}
```

Đọc `Summary` và `Messages`. Cụ thể phải kiểm:

- Số phần tử sắp đổi có khớp con số kỹ sư mong đợi không. Lệch nhiều là bộ lọc sai.
- Có dòng "Bỏ qua phần tử … tham số không ghi được" không. Có thì báo trước khi chạy thật.

Nói cho kỹ sư biết **sẽ đổi bao nhiêu phần tử** rồi mới xin xác nhận.

### 3. Chạy thật

Chỉ sau khi kỹ sư đồng ý. Truyền `confirm: true`.

### 4. Tự kiểm lại — bắt buộc

Kết quả có `changedIds`. Lấy vài id đầu rồi:

```
query element_geometry { "elementIds": [<3-5 id đầu>] }
query show_elements    { "elementIds": [<3-5 id đầu>] }
```

Xác nhận đúng phần tử, đúng tầng. Rồi chụp lại cho kỹ sư nhìn:

```
query snapshot { "imageWidth": 1600 }
```

## Đánh số theo dòng chảy MEP

Với ống/ống gió cần số chạy theo hướng dòng chảy thay vì theo vị trí, dùng `FlowNumbering` thay cho
`AutoNumbering`. Trình tự y hệt: `parameters_of` → xem trước → xác nhận → kiểm lại bằng `element_geometry`
(kết quả có `connectors` nên kiểm được tuyến đã liền chưa).

## Không được làm

- Không chạy thật khi chưa xem trước.
- Không chạy trên toàn mô hình khi kỹ sư chỉ nói một tầng — luôn truyền `levelName`.
- Không báo xong khi chưa kiểm lại `changedIds`.
