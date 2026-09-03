---
name: dung-grid-level-tu-cad
description: Dựng trục (grid) và tầng (level) trong Revit từ bản vẽ CAD hoặc file CSV, qua hai chặng AutoCAD → CSV → Revit, có xem trước và kiểm lại toạ độ. Dùng khi người dùng nói "dựng trục từ CAD", "lấy lưới trục từ dwg", "tạo tầng theo cao độ", "grid từ AutoCAD".
---

# Dựng grid/level từ CAD

Việc này đi qua **hai phần mềm**: AutoCAD đọc đường trục trên bản vẽ ra CSV, Revit đọc CSV dựng trục.
Chỗ hỏng nặng nhất không phải ở lệnh mà ở **gốc toạ độ**: CAD và Revit hiếm khi cùng gốc, dựng lệch
rồi thì mọi thứ neo vào trục (tường, cột, view range) đều phải làm lại.

## Trước khi làm gì cả

Hỏi kỹ sư ba điều nếu chưa rõ:

1. **Layer nào chứa đường trục** trong bản vẽ (mặc định `AXIS`, nhiều công ty dùng tên khác).
2. **Gốc CAD nằm đâu so với gốc Revit** — nếu chưa ai đo, xem mục "Dò offset" bên dưới.
3. **Có dựng cả level không**, và cao độ lấy từ đâu (CSV riêng, không lấy được từ đường trục).

Đừng đoán offset. Sai một lần là dựng lại cả bộ trục.

## Trình tự

### 1. AutoCAD — lấy trục ra CSV

Bản vẽ phải đang mở trong AutoCAD (hoặc chạy qua batch `accoreconsole`):

```
exec GridExtract { "gridLayer": "AXIS", "outputPath": "C:/tmp/grids.csv" }
```

Kết quả là CSV `Name,X1,Y1,X2,Y2` (mm). **Mở ra đọc trước khi đi tiếp**: số dòng có khớp số trục kỹ sư
nói không. Ít hơn hẳn nghĩa là layer sai hoặc trục vẽ bằng polyline thay vì line — báo lại, đừng tự
đoán sang layer khác.

### 2. Dò offset giữa gốc CAD và gốc Revit

Chọn **một** giao trục dễ nhận (ví dụ A×1), đọc toạ độ của nó trong CSV, rồi so với toạ độ mong muốn
trong Revit. Nếu model Revit đã có sẵn dù chỉ một trục:

```
query elements { "categories": ["Grids"] }
```

`offsetXMm` / `offsetYMm` là hiệu số. Model trắng hoàn toàn thì offset = 0 và gốc CAD trở thành gốc
Revit — nói rõ điều đó cho kỹ sư biết trước khi chạy.

### 3. Xem trước trong Revit

```
exec GridFromCsv {
  "gridCsvPath": "C:/tmp/grids.csv",
  "levelCsvPath": "C:/tmp/levels.csv",
  "offsetXMm": 0, "offsetYMm": 0,
  "skipExisting": true
}
```

Đọc `Summary` và `Messages`, kiểm cụ thể:

- **Số trục sắp tạo** khớp số dòng CSV không. Lệch = có dòng hỏng hoặc trùng tên với trục đã có.
- Dòng "bỏ qua, đã có" — chạy lại lần hai thì tất cả phải rơi vào đây, đó là cách kiểm lần một đã ghi.

Nói cho kỹ sư biết **sẽ tạo bao nhiêu trục, bao nhiêu tầng** rồi mới xin xác nhận.

### 4. Chạy thật

Chỉ sau khi kỹ sư đồng ý, và **chỉ khi offset đã chốt**.

### 5. Tự kiểm lại — bắt buộc

```
query elements  { "categories": ["Grids"] }
query levels    {}
query snapshot  { "imageWidth": 1600 }
```

Kiểm ba điều: đúng số lượng · tên trục theo đúng quy ước (chữ một chiều, số chiều kia) · **vị trí một
giao trục quen thuộc** khớp bản vẽ. Ảnh chụp gửi kỹ sư nhìn là bước cuối, không phải bước duy nhất.

## Level thì khác trục ở chỗ nào

Cao độ **không đọc được từ đường trục** — cần CSV `Name,Elevation` (mm) tự lập từ bảng cao độ của dự án.
Không có file đó thì dùng `LevelSetup` với danh sách gõ tay:

```
exec LevelSetup { "levels": [{ "name": "Tầng 1", "elevationMm": 0, "createFloorPlan": true }], "skipExisting": true }
```

`skipExisting: true` là mặc định và nên giữ: chạy lại không nhân đôi tầng.

## Không được làm

- **Không chạy thật khi offset chưa chốt.** Đây là chỗ đắt nhất của cả quy trình.
- Không đổi tên trục bằng `renameByRule` khi kỹ sư chưa yêu cầu — tên trục trong CSV thường là tên đang
  dùng trên hồ sơ, đổi là lệch với bản vẽ đã phát hành.
- Không dựng trục vào model đã có trục mà không đọc `Messages` xem cái nào bị bỏ qua.
- Không báo xong khi chưa kiểm lại vị trí một giao trục.
