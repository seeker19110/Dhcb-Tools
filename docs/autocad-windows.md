# AutoCAD trên Windows — công cụ và kiểm chứng

DHCB Tools dùng Windows 10/11 x64 cho phát triển, kiểm thử và sử dụng. Build giao diện dùng WPF đầy đủ.
AutoCAD Core Console là chương trình Windows riêng cho batch; vẫn dùng vỏ `DhcbTools.AutoCAD.Core` không WPF.

## Cửa sổ công cụ

Gõ **`DHCB_TOOLS`** hoặc chọn **Bảng công cụ** trên Ribbon. Các nút nghiệp vụ trên Ribbon mở đúng form
qua `DHCB_FORM`; các lệnh gõ tay cũ vẫn có luồng command line cho script.

- Tìm 15 công cụ theo tên, bí danh hoặc tiếng Việt có/không dấu.
- Chỉnh đầy đủ thuộc tính config: số bắt đầu, bước nhảy, số chữ số, hướng đánh số, dung sai gom hàng,
  tùy chọn dọn bản vẽ, lọc text và phạm vi xử lý. Editor lấy đúng kiểu và mặc định từ config Core.
- Chọn file bằng hộp thoại Windows; cấu hình hợp lệ được nhớ theo lệnh tại `%APPDATA%\DHCB\configs\autocad`.
- Lệnh ghi yêu cầu **Xem trước → kiểm kết quả → Ghi vào bản vẽ → xác nhận**. Nút ghi mất hiệu lực khi sửa
  ô nhập; trước khi ghi kiểm lại document, revision và băm nội dung file đầu vào. Chạy xong phải xem trước
  mới được ghi tiếp. Kết quả một phần hoặc có lỗi không mở nút ghi.
- Kết quả hiển thị số đối tượng, thời gian, lỗi và chi tiết có giới hạn để tránh làm treo giao diện.

Lệnh xuất/kiểm chỉ đọc mô hình dùng nút **Thực hiện**, có thể ghi file báo cáo vào nơi đã chọn.

## Tìm/thay văn bản có phạm vi

`TextReplace` nhận mẫu [autocad-text-replace.sample.json](../configs/autocad-text-replace.sample.json).

| Tùy chọn | Hành vi |
|---|---|
| `scope` | `All` (mặc định), `ModelSpace`, `PaperSpace` hoặc `BlockDefinitions` |
| `layerContains` | Lọc tên layer; attribute nằm trên layer 0 kế thừa layer của block chèn |
| `attributeTag` | Khi khai, chỉ tìm attribute đúng tag, không tìm text thường |
| `includeDbText`, `includeMText`, `includeAttributes` | Chọn loại văn bản, mặc định đều bật |
| `maxPreviewItems` | Giới hạn chi tiết xem trước 0–10000, mặc định 200; tổng số đối tượng vẫn đếm đầy đủ |
| `timeBudgetSeconds` | Ngân sách lập kế hoạch 1–600 giây, mặc định 30; hết hạn hủy toàn kế hoạch trước khi ghi |

MText và multiline attribute/definition giữ định dạng, xuống dòng và field động. Nội dung có field được giữ
nguyên và báo phần chưa xử lý; kết quả chưa hoàn tất phải được kiểm tra trước khi dùng. Regex có timeout riêng
cho từng phép khớp. Vượt ngân sách toàn lệnh trả `E-TEXT-BUDGET`, chưa ghi đối tượng nào.

## Truy vấn bản vẽ lớn

`text` hỗ trợ `textContains`; kết quả có handle, tọa độ Z và chữ hiển thị của MText. Các truy vấn hỗ trợ
`limit`/`offset` với giới hạn trang 10000; lọc lớp native trước khi mở entity giảm chi phí đọc database.

Client đọc `document_context` để lấy `documentId` và `revision`. Gửi `documentId` ở cấp ngoài của `POST /query`
để Bridge từ chối khi người dùng đã đổi DWG. Panel giữ context giữa các trang, kiểm revision và yêu cầu tải lại
khi bản vẽ thay đổi. Không dùng offset của bản vẽ cũ cho bản vẽ mới.

## Build và kiểm thử Windows

```powershell
.\scripts\check-build.ps1 -RevitVersion 2027 -AcadVersion 2026 -AcadRuntime net10
.\scripts\check-vulnerable.ps1
.\tools\ui-acceptance\run-ui.ps1
.\tools\acceptance\run-text.ps1
.\tools\acceptance\run-query.ps1
```

CI dùng Windows cho cả unit test, coverage, PowerShell, WPF, đóng gói, quét secret và audit dependency.
Script PowerShell chạy được trên 5.1; không cần shell Bash hoặc bản build bỏ giao diện.
Đối chiếu runtime và phiên bản được hỗ trợ ở [ma trận 2022–2027](tuong-thich-2022-2027.md).
