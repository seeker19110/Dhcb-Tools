---
name: xuat-toa-do-dinh-vi
description: Xuất toạ độ định vị (tim cột, tâm thiết bị/sleeve, giao trục) từ Revit ra CSV cho máy toàn đạc bằng SetoutExport — chốt hệ toạ độ Survey, xem trước, đối chiếu một điểm bằng element_geometry. Dùng khi người dùng nói "xuất toạ độ tim cột", "file cho máy toàn đạc", "cắm mốc", "setout", "stake out", "toạ độ cho trắc đạc".
---

# Xuất toạ độ định vị cho máy toàn đạc

Trắc đạc gõ nhầm **một chữ số** toạ độ là đục lại bê tông. Lệnh `SetoutExport` chỉ đọc mô hình
(không transaction), nhưng file nó ghi ra đi thẳng ra hiện trường, nên quy trình dưới đây luôn có một
bước **đối chiếu ngược một điểm** trước khi giao file.

## Trước khi làm gì cả

Hỏi kỹ sư ba điều nếu chưa rõ:

1. **Cắm gì** — tim cột, tâm sleeve/lỗ mở, thiết bị, hay giao trục; tầng nào.
2. **Máy nhận thứ tự cột nào** — `PNEZD` (Trimble, Leica) hay `PENZD` (Topcon/Sokkia); mét hay mm; có
   nhận dòng tiêu đề không. Không biết thì để mặc định `PNEZD`, mét, có tiêu đề và nói rõ trong báo cáo.
3. **Hệ toạ độ** — mặc định là **Survey** (toạ độ chung theo điểm khảo sát). Chỉ dùng `Internal` khi
   tổ trắc đạc nói rõ họ đang làm việc theo gốc nội bộ của mô hình.

## Trình tự

### 1. Xem mô hình có gì để cắm

```
query levels {}
```

```
query elements { "categories": ["Structural Columns"], "limit": 5 }
```

Nếu không có cột nào (mô hình kiến trúc thuần), hỏi lại; giao trục thì mô hình nào cũng có.

### 2. Xuất — lần đầu chỉ để đọc thông báo

```
exec SetoutExport {
  "categories": ["Structural Columns", "Columns"],
  "levelName": "Level 1",
  "includeGridIntersections": true,
  "columns": "PNEZD",
  "unit": "m",
  "outputPath": "C:/DHCB/setout/L1-tim-cot.csv",
  "dxfPath": "C:/DHCB/setout/L1-tim-cot.dxf"
}
```

Đọc kỹ `Messages`:

- Dòng **`Site "…": gốc nội bộ ở E=… N=… Z=… mm, True North xoay …°`** — đọc lên cho kỹ sư. Nếu có
  dòng *"Hệ Survey trùng hệ nội bộ"* thì mô hình **chưa khai toạ độ chung**: file ra là toạ độ Revit,
  không phải toạ độ khảo sát. Dừng lại, nói rõ, để kỹ sư quyết định.
- Số điểm theo mã (`COL: 48 điểm`, `GRD: 20 điểm`) phải khớp số cột kỹ sư mong đợi trên tầng đó.
- Ghi chú *"tên điểm bị cắt còn 16 ký tự"* hoặc *"tên trùng đã thêm hậu tố"* — đổi `namePattern`
  (ví dụ `{Level}-{Mark}`) nếu kỹ sư cần tên đúng như bản vẽ.
- Ghi chú *"lấy tâm hộp bao"* — những phần tử đó không có điểm đặt rõ ràng, kiểm tay trước khi cắm.

### 3. Đối chiếu ngược một điểm

Mở file CSV, chọn một điểm có `ElementId` (thêm chữ `I` vào `columns` nếu cần: `PNEZDI`), rồi:

```
query element_geometry { "elementIds": [123456] }
```

`element_geometry` trả toạ độ **nội bộ** theo mm. Nếu xuất theo `Internal` thì hai số phải trùng
nhau tới mm. Nếu xuất theo `Survey`, chênh lệch phải đúng bằng gốc/góc xoay đã đọc ở bước 2 — hoặc
nhờ kỹ sư đặt một *Spot Coordinate* lên đúng phần tử đó trong Revit và so với dòng CSV. Không khớp thì
**không giao file**.

### 4. Chỉ cho kỹ sư nhìn

```
query show_elements { "elementIds": [123456] }
```

Zoom tới phần tử vừa đối chiếu để kỹ sư xác nhận đó đúng là cột/thiết bị cần cắm.

### 5. Báo cáo

Số điểm, tầng, hệ toạ độ, thứ tự cột, đơn vị, đường dẫn CSV/DXF, và **điểm nào đã được đối chiếu
ngược**. Nhắc kỹ sư: chuỗi băm nhật ký batch không áp cho file này — file CSV là dữ liệu giao cho
trắc đạc, ai nhận thì ký nhận theo quy trình của công trường.

## Chọn đúng phần tử thay vì cả category

Kỹ sư chọn trong Revit rồi:

```
query selection {}
```

Lấy danh sách id, truyền vào `elementIds` — `categories` bị bỏ qua khi có `elementIds`:

```
exec SetoutExport {
  "elementIds": [123456, 123457],
  "outputPath": "C:/DHCB/setout/chon-tay.csv"
}
```

## Không được làm

- **Không giao file khi thông báo nói "Hệ Survey trùng hệ nội bộ"** mà chưa hỏi lại — toạ độ đó không
  phải toạ độ khảo sát.
- Không bỏ qua bước đối chiếu ngược một điểm. Đây là bước duy nhất bắt được sai hệ toạ độ.
- Không tự đổi `columns`/`unit` để "cho giống file cũ" khi chưa biết máy nhận gì — hỏi tổ trắc đạc.
- Không xuất cả mô hình mọi tầng khi kỹ sư chỉ cần một tầng: file 2.000 điểm là chỗ chọn nhầm điểm.
- Không dùng `SetoutExport` cho phần tử nằm trong **model liên kết** — lệnh chỉ đọc mô hình đang mở;
  mở đúng file chứa phần tử đó.
