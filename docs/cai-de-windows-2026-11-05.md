# Biên bản cài đè Windows — 2026-11-05

Đã cài bản phát triển `0.9.0-dev.20261105.6991165.dirty.fix1` vào AutoCAD 2026 (.NET 10), BatchRunner và bộ script MCP đang dùng. Installer báo thành công và không cần khởi động lại Windows. Gói được tạo từ mã đang làm việc trên `6991165`.

| Kiểm tra | Kết quả |
|---|---|
| Gói cài | 170/170 tệp trong stage khớp SHA-256; đủ 8 profile AutoCAD WPF đã build lại |
| Tệp cài thực tế | 9/9 DLL, deps và profile AutoCAD; 15/15 tệp chương trình BatchRunner và script khớp gói |
| Dữ liệu hiện có | 113/113 tệp được bảo vệ giữ nguyên SHA-256, gồm cấu hình, token và add-in Revit hiện có |
| AutoCAD 2026 Core | 75/75 ca trên fixture độc lập với Core đã cài; bộ DLL Core và dependency giữ nguyên SHA-256 ở fix1 |
| Bridge trong AutoCAD GUI | 15 lệnh, đúng schema mới. 10 lượt ghim `document_context` → `layers` trên fixture giữ nguyên ID và revision, mỗi lượt 42–116 ms. ID sai bị từ chối; lọc text hoạt động |
| DWG gốc | Ở trạng thái đã lưu khi đóng AutoCAD để cài fix1; đã mở lại và đưa lên tab hoạt động. Đọc context và hai layer với cùng ID thành công, không gửi lệnh ghi |
| MCP stdio mới | `initialize` và `tools/list` thành công, 17 công cụ (15 lệnh AutoCAD, `query`, `chat`) |
| BatchRunner | Kiểm chuỗi băm log nghiệm thu: 2/2 dòng nguyên vẹn, mã thoát 0 |
| Doctor | 0 lỗi; 3 cấu hình tùy chọn chưa có, đang dùng mặc định |

Fix1 giữ một ID theo native database trong suốt phiên mở DWG, kể cả khi AutoCAD đổi managed wrapper. Bridge chờ command context hoàn tất trước khi trả HTTP. Revision vẫn có thể tăng khi AutoCAD phát sự kiện database; đã quan sát điều này trên DWG gốc dù chỉ gửi truy vấn đọc. ID và truy vấn ghim vẫn hoạt động. Bộ kiểm tra fixture đo revision khi không có sự kiện database.

Bản sao trước khi cài nằm tại `C:\Users\liend\DHCB-Backups\upgrade-20261105-before-install`. Không thay đổi chứng thư hoặc thiết lập tin cậy của AutoCAD. Máy chưa có Revit để kiểm runtime. Các phiên Claude đang mở cần kết nối lại MCP để nạp script mới.

Gói cài SHA-256: `9870FCF5ED108C5B48277B5668563FE8498BA130ADD181A56BB798D34D5613DE`. Bằng chứng cục bộ trong `out/install-20261105/`: `installer-fix1.log`, `installed-hashes-fix1.json`, `runtime-reload-save.json`, `autocad-reopened.json`, `live-verification.json`, `original-live-verification.json`, `mcp-fresh-smoke.json` và `verified-installed/`.
