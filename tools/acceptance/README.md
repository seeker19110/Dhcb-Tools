# Nghiệm thu AutoCAD trên bản sao

Đây là plugin kiểm tra cho người phát triển, **không đóng gói/cài vào DHCB Tools**. Cần Windows,
.NET SDK 10 và AutoCAD 2026 đã được cấp phép. Chạy từ repo bằng Windows PowerShell 5.1 hoặc 7:

```powershell
./tools/acceptance/run-query.ps1
./tools/acceptance/run-pdf.ps1
```

Hai script dựng DLL từ source trước khi chạy; có thể đổi `-AcadDirectory` và `-OutputRoot`.
Plugin dùng API/runtime 2026 nên thư mục AutoCAD phải là **2026**, không trỏ sang bản net48/net8.
Không chạy hai script đồng thời với tác vụ sửa cùng fixture.
Nếu môi trường terminal làm mất `.EXE` khỏi `PATHEXT`, chỉ khôi phục trong phiên hiện tại:
`$env:PATHEXT = '.COM;.EXE;.BAT;.CMD'`. Không cần thay thiết lập hệ thống.

`run-query.ps1` dùng bản sao sample Autodesk để mở host và tạo database riêng chứa 25.000 Line + 5.000 DBText.
Nó kiểm 10 trường hợp: giới hạn mặc định, offset sau bộ lọc, trang cuối, text, layer,
blocks/inserts rỗng và tham số sai. Report giữ JSON trả về và thời gian từng truy vấn.
Script trả lỗi nếu ca kiểm sai, Core Console thất bại hoặc SHA-256 của DWG gốc/bản sao thay đổi.
Đây là phép đo một fixture; cần nhiều bản vẽ thực tế để xác nhận p50/p95 và độ phản hồi UI.

`run-pdf.ps1` chỉnh **bản sao tạm** rồi SAVEAS thành fixture mới: khung 10.000 × 5.000 mm,
text cao 250 mm trong Model, named setup 1:100 và layout A3 có text cao 3,5 mm.
Nó chạy ba job PlotPdf với `saveMode=None` rồi kiểm PDF tồn tại, mã thoát và hash DWG.
**Mã PASS của script chưa chứng minh hình học PDF đúng.** Nghiệm thu thêm bằng PDF viewer hoặc
công cụ đo vector: khung khoảng 100 × 50 mm, A3 ngang khoảng 420 × 297 mm, layout in 1:1.
Đối chiếu ảnh render/text; độ cao glyph SHX không bằng nominal text height.

Kết quả nằm trong `%USERPROFILE%\DHCB-test-results\query-readiness-*` hoặc `pdf-readiness-*`:
script, console log, report, job và PDF. Hai script kiểm hash sample gốc và bản sao đầu vào;
PDF còn kiểm hash fixture qua cả ba job. Không dùng lệnh `DHCB_PDF_SETUP` trên bản vẽ sản xuất.

Bằng chứng và các phần chưa nghiệm thu: [báo cáo 2026-10-08](../../docs/nang-cap-san-xuat-2026-10-08.md).
