# Chẩn đoán cài đặt Windows

Từ thư mục cài DHCB Tools, chạy kiểm tra chỉ đọc:

```powershell
python scripts\dhcb_doctor.py --app autocad --year 2026 --offline
python scripts\dhcb_doctor.py --app revit --year 2022 --offline
```

Doctor nhận Revit/AutoCAD 2022–2027. Nó kiểm manifest, DLL bắt buộc và
`dhcb-host-profile.json` của gói; trên Windows kiểm thêm executable và runtime host.
AutoCAD 2025/2026 có profile .NET 8 hoặc .NET 10, phải khớp host đã cài.
Bản 2027 dùng .NET 10; 2022–2024 dùng .NET Framework 4.8.
Với gói net48, doctor kiểm executable/module nhưng chưa xác minh runtime Framework.

Revit 2025.5/2026.5 chạy .NET 10 trong khi gói DHCB giữ baseline net8. Doctor báo
cảnh báo cần nghiệm thu trên host cập nhật. Cùng tên manifest ở APPDATA và ProgramData,
Revit ưu tiên bản APPDATA; doctor kiểm bản đang có hiệu lực và báo nơi bị che.

Host cài ở nơi khác phải chỉ rõ năm và thư mục:

```powershell
python scripts\dhcb_doctor.py --app autocad --year 2025 --offline --acad-dir "D:\Autodesk\AutoCAD 2025"
python scripts\dhcb_doctor.py --app revit --year 2027 --offline --revit-dir "D:\Autodesk\Revit 2027"
```

Không có `--year`, doctor kiểm các gói phát hiện được; `--acad-dir`/`--revit-dir`
không kèm năm giữ mặc định 2026 để tương thích cách gọi cũ. Host thiếu ở vị trí chuẩn
được báo cảnh báo vì có thể cài nơi khác; đường đã chỉ định mà thiếu host hoặc runtime
sai được báo lỗi. Gói cũ thiếu profile có cảnh báo chưa xác minh runtime DLL.

Sau khi mở host, bỏ `--offline` để kiểm thêm Bridge và danh mục lệnh. Thêm `--json`
để lấy báo cáo máy đọc được. Doctor không chạy lệnh vào bản vẽ, không tải DLL, không
mở host, không in token hay nội dung cấu hình. Profile chỉ khai báo gói đã build;
báo cáo đạt chưa chứng nhận DLL đã nạp, giao diện, thao tác ghi hay chất lượng mô hình.
[Ma trận, cách build và bằng chứng nghiệm thu](tuong-thich-2022-2027.md).
