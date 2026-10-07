# Chẩn đoán cài đặt Windows

Từ thư mục cài DHCB Tools, chạy:

```powershell
python scripts\dhcb_doctor.py --app autocad --offline
```

Doctor chỉ đọc file. Trên Windows, nó kiểm manifest bundle trong
`%APPDATA%\Autodesk\ApplicationPlugins\DhcbTools.bundle`, các assembly bắt buộc,
host ở thư mục chuẩn và runtime của AutoCAD 2025/2026. AutoCAD 2026 cần runtime
`net10.0` của Update 1.2 trở lên; bản host dùng `net8.0` sẽ được báo lỗi dù các DLL
plugin đã chép đầy đủ. Với AutoCAD 2024, doctor chỉ kiểm host và module, chưa
xác minh .NET Framework.

Nếu AutoCAD 2026 cài ở vị trí khác:

```powershell
python scripts\dhcb_doctor.py --app autocad --offline --acad-dir "D:\Autodesk\AutoCAD 2026"
```

Không thấy host tại vị trí chuẩn được báo cảnh báo vì có thể cài nơi khác.
Không thấy host ở đường đã chỉ định hoặc thấy runtime sai được báo lỗi.
Không thấy bundle trong APPDATA cũng được báo cảnh báo: cài thủ công bằng
NETLOAD hoặc bundle ở vị trí khác cần kiểm riêng.

Sau khi mở AutoCAD bình thường, bỏ `--offline` để kiểm thêm Bridge và danh mục
lệnh. Doctor không chạy lệnh vào bản vẽ, không tải DLL, không mở host và không
in token hay nội dung cấu hình. Thêm `--json` để lấy báo cáo; báo cáo đạt vẫn
chưa xác nhận giao diện, thao tác ghi hoặc chất lượng mô hình.
