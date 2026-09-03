# Bảng mã lỗi

Mã lỗi là tiền tố dạng `E-<NHÓM>-<TÌNH-TRẠNG>` đứng đầu thông báo, để **tra cứu được** và để
script/agent bắt theo mã thay vì so chuỗi tiếng Việt:

```
E-CONFIG-MISSING: thiếu trường bắt buộc trong config (SheetBatchCreateConfig): inputPath.
```

Nguyên tắc: mã đứng **đầu** thông báo, phần sau vẫn là tiếng Việt đầy đủ cho người đọc. Một tình
trạng chỉ có một mã; không đặt mã mới cho cùng một chuyện diễn đạt khác đi.

Trang này là **danh sách đầy đủ** — `MaLoiTests` đối chiếu nó với mã nguồn theo cả hai chiều, nên
thêm mã mà quên ghi vào đây (hoặc ngược lại) là test đỏ.

| Mã | Nghĩa | Thường gặp khi | Cách xử lý |
|---|---|---|---|
| `E-CONFIG-MISSING` | Config của lệnh thiếu trường bắt buộc | Gõ tay file job/JSON gửi qua Bridge, hoặc đổi tên trường sau khi nâng cấp | Thông báo có kèm tên kiểu config và danh sách trường thiếu — bổ sung đúng những trường đó. Xem danh mục tham số của lệnh bằng `CommandCatalog` |
| `E-PATH-MISSING` | Đường dẫn trong config không tồn tại | Thư mục family, file CSV, thư mục xuất bị đổi tên/di chuyển; job chạy trên máy khác | Kiểm đường dẫn tuyệt đối trong job; dùng token `{outputFolder}`, `{suiteFolder}` thay vì đường dẫn cứng của một máy |
| `E-PARAM-MISSING` | Không tìm thấy tham số trong model | Tên tham số khác giữa các dự án (Việt/Anh), hoặc category không có tham số đó | Tra tham số có thật bằng query `parameters_of`; thêm bí danh vào từ điển `%APPDATA%\DHCB\dictionary.json` — thông báo có liệt kê những tên đã thử |
| `E-PARAM-READONLY` | Tham số có thật nhưng chỉ đọc | Ghi vào tham số Revit tự tính (diện tích, thể tích, tham số của type dùng chung) | Không ghi được bằng API — chọn tham số khác hoặc sửa ở nguồn sinh ra giá trị |

## Vì sao chỉ có bốn mã

Mã lỗi chỉ đặt cho tình trạng **người dùng xử lý được và lặp lại nhiều lệnh**. Lỗi chỉ xảy ra ở một
lệnh, hoặc lỗi mà người dùng không làm gì được (Revit từ chối dịch điểm cuối ống đã nối hai đầu), thì
thông báo tiếng Việt cụ thể có ích hơn một mã. Thêm mã cho mọi thứ chỉ tạo ra một từ điển không ai tra.

Cảnh báo của Revit trong lệnh chạy không người ngồi máy **không có mã**: chúng bị nuốt và ghi vào
`CoreContext.SuppressedWarnings`, hiện lại trong báo cáo dưới dạng `[Cảnh báo Revit] …`. Xem
[`bang-chung-test.md`](bang-chung-test.md) §12 về việc cảnh báo lúc mở model từng làm treo cả batch.
