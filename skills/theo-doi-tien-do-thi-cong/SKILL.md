---
name: theo-doi-tien-do-thi-cong
description: Ghi trạng thái lắp đặt từ hiện trường vào mô hình Revit bằng ConstructionStatus rồi ra báo cáo tiến độ bằng ProgressReport — % theo số lượng và chiều dài, gộp theo tầng/hệ, luỹ kế theo tuần. Dùng khi người dùng nói "cập nhật tiến độ", "đã lắp bao nhiêu phần trăm", "báo cáo tiến độ", "nhập trạng thái hiện trường", "nghiệm thu tới đâu".
---

# Theo dõi tiến độ thi công

Con số tiến độ đi lên bàn chủ đầu tư. Quy trình dưới đây có hai chỗ **không được bỏ**: kiểm tham số
trạng thái có thật trước khi ghi, và đọc to phần "chưa ai ghi nhận" khi báo cáo — vì đó chính là hai chỗ
một báo cáo tiến độ hay nói dối.

## Trước khi làm gì cả

1. **Tham số trạng thái nằm ở đâu** — dự án dùng shared parameter tên gì, gắn cho category nào. Không
   biết thì chạy `DictionaryLearn` hoặc hỏi.
2. **Phạm vi** — tầng nào, hệ nào, category nào tính vào tiến độ.
3. **Nguồn dữ liệu** — CSV hiện trường (mã cấu kiện → trạng thái) hay đã có sẵn trong mô hình.

## Trình tự

### 1. Kiểm tham số trạng thái có thật

```
query parameters_of { "categories": ["Pipes"], "writableOnly": true }
```

Tìm tham số trạng thái trong danh sách trả về. **Không thấy thì dừng lại**: ghi vào một tham số không tồn
tại là không ghi được gì, còn báo cáo trên một tham số không tồn tại là 0 % cho mọi nhóm — con số nói về
tham số chứ không nói về công trường. Cả hai lệnh đều chặn bằng `E-PARAM-MISSING`, nhưng biết trước thì
đỡ một vòng.

### 2. Xem trước phần ghi

```
exec ConstructionStatus {
  "inputPath": "C:/DHCB/hien-truong/2026-09-05.csv",
  "statusParameter": "DHCB_Trang_Thai",
  "dryRun": true
}
```

CSV cần hai cột: mã cấu kiện (`ElementId` / `Id` / `Mã cấu kiện`) và trạng thái (`TrangThai` / `Trạng
thái` / `Status`); thêm được `Ngày`, `Người xác nhận`, `Ghi chú`. Trạng thái nhận cả tiếng Việt có dấu,
không dấu, lẫn tiếng Anh.

Đọc `Messages` trước khi chạy thật:

- **Dòng lỗi có số dòng cụ thể** — sửa file rồi chạy lại, đừng bỏ qua. Một dòng bị nuốt là một cấu kiện
  biến mất khỏi báo cáo.
- **"lùi trạng thái nên bỏ qua"** — CSV đang hạ cấp một cấu kiện đã nghiệm thu. Gần như luôn là nhập đè
  file cũ. Hỏi lại; chỉ đặt `allowDowngrade: true` khi kỹ sư xác nhận đúng là muốn sửa.
- **"không có phần tử … trong mô hình"** — file của mô hình khác. Mã cấu kiện là ElementId của đúng file
  đang mở.

### 3. Ghi thật rồi kiểm bằng chính id vừa đổi

Sau khi kỹ sư đồng ý, chạy lại với `dryRun: false`. Kết quả trả `changedIds`; kiểm lại vài cái:

```
query element_geometry { "elementIds": [123456] }
```

```
query show_elements { "elementIds": [123456] }
```

### 4. Báo cáo

```
exec ProgressReport {
  "groupBy": "Level",
  "statusParameter": "DHCB_Trang_Thai",
  "outputPath": "C:/DHCB/tien-do/2026-09-05.html",
  "csvPath": "C:/DHCB/tien-do/2026-09-05.csv"
}
```

`groupBy` nhận `Level` (tầng), `System` (hệ), `Category`. Đọc cho kỹ sư nghe **cả ba con số**, không chỉ
con số đẹp nhất:

- % đã lắp trở lên theo **số lượng**;
- % theo **chiều dài** — chỉ có nghĩa với ống/duct/tray, và thường khác hẳn số lượng: một tuyến trục
  90 m đã lắp không bằng ba đoạn nhánh 5 m;
- số cấu kiện **chưa ai ghi nhận**, và nói rõ rằng chúng vẫn nằm trong mẫu số.

### 5. Cho kỹ sư nhìn

Tô màu theo chính tham số trạng thái rồi chụp lại:

```
exec ColorByParameter {
  "categories": ["Pipes", "Ducts"],
  "parameterName": "DHCB_Trang_Thai",
  "legendCsvPath": "C:/DHCB/tien-do/chu-giai.csv",
  "dryRun": true
}
```

```
query snapshot {}
```

## Không được làm

- **Không bỏ cấu kiện "chưa ai ghi nhận" ra khỏi mẫu số** để phần trăm đẹp lên. Chưa nhập thì chưa lắp.
- **Không cộng nửa phần trăm cho "đang lắp"**. Một cấu kiện đang lắp không phải nửa cái ống; lệnh cố ý
  không có trọng số đó và báo cáo cũng không được tự thêm.
- Không báo % theo chiều dài cho nhóm chỉ có thiết bị đếm theo cái — cột đó hiện `—` là có lý do.
- Không chạy `ConstructionStatus` với `dryRun: false` ngay lần đầu, và không bỏ qua dòng lỗi trong bản
  xem trước.
- Không tự bật `allowDowngrade` để "cho hết lỗi".
- Không lấy đường luỹ kế theo tuần làm tiến độ tổng: phần đã lắp mà **không có ngày** không nằm trên
  đường đó, và báo cáo đã nói rõ số lượng ấy.
