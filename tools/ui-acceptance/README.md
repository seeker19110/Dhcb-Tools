# Kiểm thử giao diện AutoCAD trên Windows

Chạy Windows PowerShell 5.1 hoặc 7, .NET SDK 10 và Windows Desktop Runtime 10:

```powershell
.\tools\ui-acceptance\run-ui.ps1
```

Bộ kiểm dựng các control WPF thật cho cả 16 công cụ, kiểm xem trước/ghi một lần, đổi cấu hình,
đổi revision/document, file đầu vào đổi nội dung dù giữ mtime/kích thước, số không hợp lệ,
dấu phẩy thập phân trên locale tiếng Việt, tìm không dấu và lưu/khôi phục cấu hình.
`workspace.png` render giao diện bằng WPF trên Windows; `ui.json` chứa kết quả từng ca.

Bộ này dùng **recording context**, không mở AutoCAD và không sửa DWG. `ICadCommandContext` tách
quy tắc giao diện khỏi API host; `AutoCadCommandContext` chạy lệnh thật trên command thread và giữ
DocumentLock. Kiểm database AutoCAD thật chạy riêng:

```powershell
.\tools\acceptance\run-text.ps1
.\tools\acceptance\run-query.ps1
.\scripts\run-in-autocad-tests.ps1 -Suite smoke
.\scripts\run-in-autocad-tests.ps1 -Suite write -AllowWrites
```

Không đóng gói executable kiểm thử này vào add-in hoặc installer.
