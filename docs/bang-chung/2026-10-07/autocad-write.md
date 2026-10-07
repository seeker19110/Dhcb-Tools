# DHCB — đường ghi thật trong AutoCAD

**Model:** `Data Extraction and Multileaders Sample.dwg`  
**Chạy lúc:** 2026-10-07 23:38  
**Kết quả:** 12 đạt / 0 trượt / 0 bỏ qua trên 12 ca.

## Toàn bộ

| Ca | Lệnh | Kết quả | ms | Summary |
|---|---|---|---:|---|
| Xuất layer — bản gốc để khôi phục | `LayerExport` | ✅ đạt | 25 | Đã xuất 70 layer ra "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-write-2026-10-07_23-38-38/layers-goc.csv". |
| Nhập layer có thay đổi — xem trước không phát sự kiện ghi tạm | `LayerImport` | ✅ đạt | 7 | [Xem trước] Sẽ cập nhật 1 layer, tạo mới 0 layer (chưa ghi vào drawing). |
| Nhập CSV đổi đúng một ô — GHI THẬT | `LayerImport` | ✅ đạt | 1 | Đã nhập 1 layer từ "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-doi-mot-o.csv" (1 cập nhật, 0 tạo mới). |
| Nhập lại chính fixture đó — phải không còn gì để đổi | `LayerImport` | ✅ đạt | 0 | Đã nhập 0 layer từ "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-doi-mot-o.csv" (0 cập nhật, 0 tạo mới). |
| Nhập lại CSV gốc — GHI THẬT, trả layer về như cũ | `LayerImport` | ✅ đạt | 0 | Đã nhập 1 layer từ "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-write-2026-10-07_23-38-38/layers-goc.csv" (1 cập nhật, 0 tạo mới). |
| Kiểm lại lần cuối — bản vẽ đã về trạng thái gốc | `LayerImport` | ✅ đạt | 0 | Đã nhập 0 layer từ "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-write-2026-10-07_23-38-38/layers-goc.csv" (0 cập nhật, 0 tạo mới). |
| Dòng trùng giống nhau — xem trước tạo đúng một layer | `LayerImport` | ✅ đạt | 0 | [Xem trước] Sẽ cập nhật 0 layer, tạo mới 1 layer (chưa ghi vào drawing). |
| Dòng trùng giống nhau — ghi thật tạo đúng một layer | `LayerImport` | ✅ đạt | 1 | Đã nhập 1 layer từ "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-duplicate-same.csv" (0 cập nhật, 1 tạo mới). |
| Nhập lại layer mới — không còn gì để đổi | `LayerImport` | ✅ đạt | 0 | Đã nhập 0 layer từ "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-duplicate-same.csv" (0 cập nhật, 0 tạo mới). |
| Dòng trùng xung đột — xem trước không ghi | `LayerImport` | ✅ đạt | 1 | [Xem trước] Sẽ cập nhật 0 layer, tạo mới 0 layer (chưa ghi vào drawing). |
| Dòng trùng xung đột — ghi thật cũng giữ nguyên | `LayerImport` | ✅ đạt | 0 | Đã nhập 0 layer từ "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-duplicate-conflict.csv" (0 cập nhật, 0 tạo mới). |
| Sau xung đột — layer vẫn giữ giá trị đã duyệt | `LayerImport` | ✅ đạt | 0 | Đã nhập 0 layer từ "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-duplicate-same.csv" (0 cập nhật, 0 tạo mới). |
