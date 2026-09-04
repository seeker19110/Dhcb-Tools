---
name: kiem-model-truoc-sync
description: Kiểm mô hình Revit trước khi sync lên central — cảnh báo, tham số thiếu, va chạm, sức khoẻ file — rồi báo cáo cho kỹ sư bằng tiếng Việt. Dùng khi người dùng nói "kiểm model", "trước khi sync", "model có sạch không", "review trước khi nộp".
---

# Kiểm mô hình trước khi sync

Chạy một vòng kiểm tiêu chuẩn rồi **báo cho kỹ sư biết cái gì cần sửa trước**, không phải đổ ra một đống số.

Yêu cầu: Revit đang mở với mô hình cần kiểm, add-in DHCB đã nạp (Bridge ở `127.0.0.1:8765`).

## Trình tự

### 1. Xem đang làm việc với cái gì

```
query document_info
query active_view
```

Ghi lại tên file, số cảnh báo, số link. Nếu `warningCount` đã lớn hơn 1000 thì nói thẳng với kỹ sư rằng
model đang ở tình trạng cần dọn nghiêm túc, không chỉ là sửa vài chỗ.

### 2. Sức khoẻ tổng quát

```
exec HealthReport   { "outputPath": "<Documents>/health-<tên model>.html" }
```

Đọc `Messages` để lấy các con số: dung lượng file, số view chưa đặt lên sheet, family in-place, import CAD.

`HealthReport` và `WarningsExport` (bước 3) **ghi file báo cáo** ra `outputPath` nhưng **không sửa mô hình** —
trong catalog chúng là lệnh đọc (không có `dryRun`), nên chạy trong vòng kiểm này là an toàn.

### 3. Cảnh báo — nhóm theo loại, không liệt kê từng cái

```
exec WarningsExport { "outputPath": "<Documents>/warnings.csv" }
```

Đọc kết quả và **gom theo loại**. Kỹ sư cần biết "412 cảnh báo nhưng chỉ có 6 loại, loại đầu chiếm 300 cái"
chứ không cần 412 dòng.

### 4. Tham số bắt buộc

```
query parameters_of { "categories": ["Doors", "Windows", "Rooms"], "writableOnly": true }
exec ParameterRuleCheck { "rulesPath": "<file quy tắc>", "outputPath": "<Documents>/rules.html" }
```

Dùng `parameters_of` **trước** để biết mô hình thực sự có tham số nào — đừng đoán tên rồi báo nhầm là "thiếu".

### 5. Va chạm (chỉ khi model có MEP)

```
query stats
exec ClashDetection { "categoriesA": ["Ducts","Pipes"], "categoriesB": ["Structural Framing"],
                      "outputPath": "<Documents>/clash.html", "create3dView": false }
```

### 6. Chỉ cho kỹ sư xem chỗ nặng nhất

Lấy `changedIds` hoặc ElementId trong kết quả, rồi:

```
query show_elements { "elementIds": [...] }
query snapshot      { "imageWidth": 1600 }
```

## Báo cáo

Viết bằng tiếng Việt, theo thứ tự **việc cần làm trước**, không theo thứ tự đã chạy:

1. Chặn sync (phải sửa ngay): cảnh báo nghiêm trọng, link đứt, tham số bắt buộc thiếu.
2. Nên sửa trong hôm nay.
3. Ghi nhận, để sau.

Mỗi mục kèm số lượng và ElementId đại diện để kỹ sư bấm vào xem. Kết thúc bằng đường dẫn các file báo cáo HTML.

## Không được làm

- Không chạy lệnh ghi (`confirm: true`) trong quy trình này. Đây là vòng **kiểm**: lệnh nào có `dryRun` thì để
  `dryRun`; `HealthReport`/`WarningsExport`/`ParameterRuleCheck`/`ClashDetection` chỉ ghi file báo cáo, không đụng mô hình.
- Không tự sửa cảnh báo. Nêu ra và để kỹ sư quyết định.
- Không báo "model sạch" chỉ vì lệnh chạy xong không lỗi — phải đọc thực sự các con số trong `Messages`.
