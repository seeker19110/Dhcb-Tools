---
name: dem-co-ong-nuoc-ngung
description: Đếm co (45°/90°) của ống nước ngưng/thoát nước uPVC theo từng cỡ (D21, D27, D34, D42…) trên bản vẽ ĐHKK AutoCAD xuất từ Revit — nhận cỡ ống bằng khoảng cách hai nét, nhận góc co bằng hình học tuyến, tính cả chi tiết đổi cao độ (2 co 45). Dùng khi người dùng nói "đếm co 45 ống D34", "đếm co ống nước ngưng", "bao nhiêu co ống DP", "đếm phụ kiện ống thoát".
---

# Đếm co ống nước ngưng (uPVC) trên DWG xuất từ Revit

Bản vẽ ĐHKK xuất từ Revit không còn đối tượng ống. Mỗi ống là **hai Line song song** trên layer
`P-PIPE-*`, và mỗi co là một block riêng tên `…uPVC_Elbow - …-<id>-…`. **Tên block không ghi góc, cũng không
ghi cỡ ống**: mỗi instance co là một block có tên riêng, nên đếm theo tên block là vô ích. Góc và cỡ của co
phải suy ra từ **ống nối vào nó**.

Skill này chỉ đọc bản vẽ, không có bước ghi.

## Lưu ý về Bridge `query`

- Các tham số lọc (`layer`, `nameContains`, `blockName`, `contains`) của `entities` / `inserts` / `text`
  hiện **bị bỏ qua**: Bridge vẫn trả cả bản vẽ. Hãy lấy hết rồi lọc ở máy:
  `entities` phân trang `limit` 10000 + `offset`, `text` `limit` 10000.
- `entities` không trả đỉnh của Polyline. Muốn lấy toạ độ thì gọi `entity_geometry { "handles": [...] }`;
  hộp bao (extents) của block co cũng lấy bằng lệnh này.
- `snapshot` dễ quá 30 giây trên bản vẽ lớn. Đừng dựa vào nó; nếu cần nhìn, nhờ kỹ sư chụp màn hình.

## Trình tự

### 1. Nhận ống theo cỡ

Ghép từng cặp Line song song trên `P-PIPE-1` (layer uPVC; kiểm lại bằng `query stats`). Khoảng cách giữa
hai nét chính là **đường kính ngoài**: 21 / 27 / 34 / 42 mm. Đường tâm là trung điểm của hai nét.

- Nếu kỹ sư đã vẽ tuyến bóc khối lượng (layer kiểu `KL-DP-D34`), hãy đối chiếu tuyến đó với ống ghép được.
  Hai bên khớp nhau thì tuyến KL đã đủ.
- Tag `DP- D34 i=1.0%` / `BOP= FFL+ …` chỉ cho biết vị trí chữ (có leader kéo ra), **không** phải vị trí ống.

### 2. Gắn co vào đầu ống

Với mỗi đầu đường tâm của ống cỡ cần đếm, tìm block co có hộp bao chạm đầu đó (dung sai khoảng 30 mm).
Sau đó phân loại:

| Hình học quanh co | Loại co | Tính |
|---|---|---|
| Đổi hướng trên mặt bằng, giữa ống thẳng (0°/90°) và ống chéo 45° | co 45 mặt bằng | 1 co 45 |
| Đổi hướng 90° trên mặt bằng | co 90 | 1 co 90 |
| **Đoạn ngắn thẳng hàng với tuyến, kẹp giữa 2 co** | **chi tiết đổi cao độ** | **2 co 45** |
| Co ở cuối tuyến, phía sau không còn ống trên mặt bằng | co chuyển sang ống đứng | xem mục 3 |
| Đầu đoạn chéo không có co, nối vào ống to hơn | tê chếch / Y | không phải co |

**Chi tiết đổi cao độ** là chỗ dễ bỏ sót nhất. Trên mặt bằng, tuyến vẫn thẳng, chỉ có một đoạn ngắn
(vài chục đến vài trăm mm) với ký hiệu co ở hai đầu. Đoạn đó thực chất chạy chéo 45° theo phương đứng, nên
chiều dài trên mặt bằng ≈ chênh cao độ BOP hai bên. Hãy đối chiếu với tag BOP gần đó để xác nhận. Mỗi chi
tiết như vậy = **2 co 45**.

### 3. Loại trừ trước khi chốt

- **Co của ống cỡ khác nằm chồng trên mặt bằng.** Một nhánh nhỏ (ví dụ D27) chạy chồng lên ống lớn ở cao
  độ khác. Co của nó có hộp bao chạm ống lớn, nhưng hai nét nối vào nó cách nhau theo cỡ nhỏ. Đo khoảng cách
  nét của ống nối vào co, đừng chỉ dựa vào chuyện co chạm ống.
- **Khe nhỏ trên tuyến chỗ nhánh nhập vào** (tê) không có co của ống chính.
- **Co cuối tuyến chuyển sang ống đứng:** không tự suy ra được góc. Ghi riêng ra và hỏi kỹ sư (hoặc kiểm
  trong mô hình Revit).
- Bỏ các khung chi tiết/mặt cắt/xref (layer kiểu `…$0$…`, toạ độ nằm ngoài khung mặt bằng), không thì cộng
  trùng.

### 4. Báo cáo

Lập bảng gồm: vị trí (toạ độ gần đúng), handle co, loại co, số lượng. Tách riêng thành ba nhóm: **co 45 mặt
bằng**, **co 45 ở chi tiết đổi cao độ**, và **chưa chắc**. Nói rõ những gì chỉ suy ra từ hình 2D, chưa kiểm
trong mô hình.

## Không được làm

- Không đếm co theo tên block hay theo layer: tên block Revit không mang góc, cũng không mang cỡ ống.
- Không tính co chỉ vì hộp bao của nó chạm ống: phải đo cỡ của chính ống nối vào co.
- Không bỏ qua đoạn ngắn thẳng hàng kẹp giữa 2 co: đó là chi tiết đổi cao độ, tính 2 co 45.
- Không tự gán góc cho co chuyển sang ống đứng khi chưa có cao độ hoặc mô hình; ghi riêng ra và hỏi kỹ sư.
- Không sửa hay xoá entity trong bản vẽ; skill chỉ đọc.
- Không commit tên bản vẽ, handle hay số khối lượng thật của dự án vào repo public.
