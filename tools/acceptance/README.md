# Nghiệm thu AutoCAD trên bản sao

Đây là plugin kiểm tra cho người phát triển, **không đóng gói/cài vào DHCB Tools**. Cần Windows,
.NET SDK 10 và AutoCAD 2026 đã được cấp phép. Chạy từ repo bằng Windows PowerShell 5.1 hoặc 7:

```powershell
./tools/acceptance/run-query.ps1
./tools/acceptance/run-pdf.ps1
./tools/acceptance/run-batch.ps1
```

Các script dựng DLL từ source trước khi chạy; có thể đổi `-AcadDirectory` và `-OutputRoot`.
Plugin dùng API/runtime 2026 nên thư mục AutoCAD phải là **2026**, không trỏ sang bản net48/net8.
Chạy lần lượt để tránh DLL đang được host dùng bị khoá khi build.
HostPreflight tự bổ sung phần mở rộng executable nếu môi trường Windows làm mất `.EXE` khỏi `PATHEXT`;
thay đổi chỉ áp dụng trong tiến trình script, không sửa thiết lập hệ thống.

`run-batch.ps1` gài lỗi ở bước đầu trên hai bản sao DWG rồi kiểm chuỗi phụ thuộc, bước độc lập phục hồi và
`stopOnError` dừng các lệnh còn lại/các bản vẽ sau. Nó đối chiếu 9 dòng log, file CSV nào được phép tồn tại,
báo cáo riêng từng lượt, chuỗi băm và SHA-256 của nguồn/bản sao. `Invalid/Command` cố ý là tên lệnh không tồn tại,
để kiểm runner không dùng tên lệnh làm đường dẫn JSON. Kết quả ở `batch-readiness-*`.

`run-query.ps1` dùng bản sao sample Autodesk để mở host và tạo database riêng chứa 25.000 Line + 5.000 DBText.
Nó kiểm 10 trường hợp: giới hạn mặc định, offset sau bộ lọc, trang cuối, text, layer,
blocks/inserts rỗng và tham số sai. Report giữ JSON trả về và thời gian từng truy vấn.
Script trả lỗi nếu ca kiểm sai, Core Console thất bại hoặc SHA-256 của DWG gốc/bản sao thay đổi.
Đây là phép đo một fixture; cần nhiều bản vẽ thực tế để xác nhận p50/p95 và độ phản hồi UI.

`run-pdf.ps1` chỉnh **bản sao tạm** rồi SAVEAS thành fixture mới: khung 10.000 × 5.000 mm,
text cao 250 mm trong Model, named setup `QA Model 1 100` và layout `QA A3` có text cao 3,5 mm.
Tên có dấu cách và đường dẫn OutputRoot có dấu cách dùng để bắt lỗi prompt script.
Nó chạy ba job PlotPdf với `saveMode=None` rồi kiểm PDF tồn tại, mã thoát và hash DWG.
**Mã PASS của script chưa chứng minh hình học PDF đúng.** Nghiệm thu thêm bằng PDF viewer hoặc
công cụ đo vector: khung khoảng 100 × 50 mm, A3 ngang khoảng 420 × 297 mm, layout in 1:1.
Đối chiếu ảnh render/text; độ cao glyph SHX không bằng nominal text height.

Kết quả nằm trong `%USERPROFILE%\DHCB-test-results\query-readiness-*` hoặc `pdf-readiness-*`:
script, console log, report, job và PDF. Các script kiểm hash sample gốc và bản sao đầu vào;
PDF còn kiểm hash fixture qua cả ba job. Không dùng lệnh `DHCB_PDF_SETUP` trên bản vẽ sản xuất.

Bằng chứng mới: [audit 2026-10-09](../../docs/audit-2026-10-09.md).
Các phần nghiệm thu sản xuất: [báo cáo 2026-10-08](../../docs/nang-cap-san-xuat-2026-10-08.md).
