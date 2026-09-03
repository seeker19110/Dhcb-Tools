---
name: xuat-bo-pdf-theo-revision
description: Xuất một bộ bản vẽ PDF/DWG/IFC theo revision trong Revit — gán revision cho đúng nhóm sheet rồi xuất, có xem trước và đối chiếu số file. Dùng khi người dùng nói "xuất bộ bản vẽ", "phát hành revision", "xuất PDF theo đợt", "in bộ hồ sơ".
---

# Xuất bộ PDF theo revision

Phát hành hồ sơ là việc **ra ngoài công ty**: thiếu một bản vẽ hoặc phát nhầm bản cũ thì người nhận
mới là người phát hiện. Quy trình dưới đây luôn đối chiếu **số sheet dự kiến ↔ số file thật sự ra**.

## Trước khi làm gì cả

Hỏi kỹ sư ba điều nếu chưa rõ:

1. **Revision nào** — số thứ tự (Sequence Number) trong Sheet Issues/Revisions, không phải nhãn hiển thị.
2. **Những sheet nào thuộc đợt phát hành này** — danh sách số sheet, hay theo tiền tố.
3. **Định dạng và nơi lưu**: PDF thôi hay kèm DWG/IFC; thư mục đích.

Đừng đoán phạm vi. "Xuất bộ điện" mà lấy nhầm cả bộ kiến trúc là gửi đi 200 file thừa.

## Trình tự

### 1. Xem hiện trạng sheet và revision

```
query sheets {}
```

Trả về số sheet, tên, revision đang gắn, view trên sheet. Từ đây chốt **danh sách số sheet** của đợt
phát hành và đọc lại cho kỹ sư xác nhận. Nếu số revision kỹ sư nói không tồn tại, `RevisionOnSheets` sẽ
báo kèm danh sách revision có thật — đọc danh sách đó lên, đừng tự chọn cái gần giống.

### 2. Gán revision cho đúng nhóm sheet — xem trước

```
exec RevisionOnSheets {
  "revisionSequence": 3,
  "sheetNumbers": ["E-101", "E-102", "E-103"]
}
```

Dùng `sheetNumbers` (danh sách chính xác) khi đã chốt được danh sách; `sheetNumberContains` chỉ khi kỹ
sư nói theo tiền tố. Đọc `Summary`: **số sheet sắp gắn revision phải khớp đúng danh sách**. Lệch một cái
là dừng lại hỏi.

### 3. Chạy thật rồi kiểm ngay

Sau khi kỹ sư đồng ý. Kiểm lại bằng chính truy vấn ban đầu:

```
query sheets {}
```

Đúng những sheet đó, và **chỉ** những sheet đó, mang revision mới.

### 4. Xuất — xem trước trước đã

```
exec BatchExport {
  "outputFolder": "C:/PhatHanh/2026-09-03",
  "formats": ["Pdf"],
  "fileNamePattern": "{SheetNumber}-{SheetName}",
  "sheetNumbers": ["E-101", "E-102", "E-103"],
  "dryRun": true
}
```

Bản xem trước liệt kê tên file sẽ ra. Kiểm: đủ số lượng · tên file không có ký tự lạ · không trùng tên
(hai sheet cùng số là hỏng dữ liệu, không phải lỗi xuất).

### 5. Xuất thật rồi đối chiếu

Chạy lại với `dryRun: false`, rồi **đếm file trong thư mục đích** so với số sheet dự kiến. Đây là bước
hay bị bỏ nhất và cũng là bước duy nhất bắt được lỗi "xuất xong nhưng thiếu 3 bản".

Báo cho kỹ sư: bao nhiêu sheet, bao nhiêu file, thư mục nào, revision số mấy.

## Kèm DWG/IFC

Cùng lệnh, thêm định dạng: `"formats": ["Pdf", "Dwg", "Ifc"]`. `dwgVersion` mặc định `AcadRelease2018`,
`ifcVersion` mặc định `IFC2x3` — chỉ đổi khi bên nhận yêu cầu, và ghi lại đã đổi thành gì.

## Không được làm

- **Không gán revision cho toàn bộ sheet** vì bộ lọc dễ viết hơn. Revision là dấu vết pháp lý của đợt
  phát hành, gắn thừa là sai hồ sơ.
- Không xuất khi chưa chạy `dryRun` và đọc danh sách tên file.
- Không báo "đã xuất xong" khi chưa đối chiếu số file thật trong thư mục.
- Không tự chọn revision khác khi số kỹ sư đưa không tồn tại — đọc danh sách có thật lên và hỏi.
