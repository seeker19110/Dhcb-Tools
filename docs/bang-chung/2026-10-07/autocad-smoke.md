# DHCB — kiểm thử trong AutoCAD (smoke)

**Model:** `Data Extraction and Multileaders Sample.dwg`  
**Chạy lúc:** 2026-10-07 23:39  
**Kết quả:** 18 đạt / 0 trượt / 0 bỏ qua trên 18 ca.

## Toàn bộ

| Ca | Lệnh | Kết quả | ms | Summary |
|---|---|---|---:|---|
| Xuất layer ra CSV | `LayerExport` | ✅ đạt | 17 | Đã xuất 70 layer ra "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-smoke-2026-10-07_23-39-30/layers.csv". |
| Nhập lại chính CSV vừa xuất — phải không đổi layer nào | `LayerImport` | ✅ đạt | 7 | [Xem trước] Sẽ cập nhật 0 layer, tạo mới 0 layer (chưa ghi vào drawing). |
| Nhập CSV đổi đúng một ô — phải thấy đúng một layer đổi | `LayerImport` | ✅ đạt | 0 | [Xem trước] Sẽ cập nhật 1 layer, tạo mới 0 layer (chưa ghi vào drawing). |
| Xem trước dọn bản vẽ (purge sâu) | `DrawingCleanup` | ✅ đạt | 39 | [Xem trước] Sẽ xoá 10 đối tượng thừa. |
| Xuất attribute của block ra CSV | `AttributeExport` | ✅ đạt | 4 | Đã xuất 50 attribute từ 10 block ra "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-smoke-2026-10-07_23-39-30/attributes.csv". |
| Nhập lại chính CSV attribute vừa xuất | `AttributeImport` | ✅ đạt | 2 | [Xem trước] Sẽ cập nhật 0 attribute, bỏ qua 0 dòng (chưa ghi vào drawing). |
| Xem trước thay text | `TextReplace` | ✅ đạt | 6 | Không tìm thấy văn bản nào khớp để thay. |
| Kiểm layer theo bộ quy tắc | `LayerStandardCheck` | ✅ đạt | 4 | Đã kiểm tra 70 layer, 56 layer không đúng chuẩn. Báo cáo: "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-smoke-2026-10-07_23-39-30/layer-check.html". |
| Báo lỗi rõ khi không có trục trên layer chỉ định | `GridExtract` | ✅ đạt | 1 | Không tìm thấy trục nào (Line, Polyline, Xline, Ray) trên layer "AXIS". |
| Kiểm kê xref | `XrefAudit` | ✅ đạt | 1 | Bản vẽ không có Xref nào. |
| Đếm block ra CSV | `BlockQuantity` | ✅ đạt | 5 | Đã đếm 24 block (5 nhóm) ra "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-smoke-2026-10-07_23-39-30/blocks.csv". |
| So bản vẽ với chính nó — phải không có khác biệt | `DrawingCompare` | ✅ đạt | 90 | So sánh với "C:\Users\liend\DHCB-test-results\quality-20261007-host\autocad-smoke-2026-10-07_23-39-30\ban-chep-Data Extraction and Multileaders Sample.dwg": 0/6 layer khác nhau; theo Handle: 0 di chuyển > 0.00, 0 thêm mới, 0 đã xoá. Báo cáo: "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-smoke-2026-10-07_23-39-30/compare.csv". |
| Xem trước đổi layer theo bảng chuẩn | `LayerTranslate` | ✅ đạt | 6 | [Xem trước] Sẽ đổi layer của 0 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| Gợi ý map layer → type Revit | `CadLayerMap` | ✅ đạt | 56 | Đã gợi ý map cho 70 layer (70 cần xem lại, nguồn: heuristic offline) ra "C:/Users/liend/DHCB-test-results/quality-20261007-host/autocad-smoke-2026-10-07_23-39-30/layer-map.csv". |
| Báo lỗi rõ khi không có block cần đánh số | `AutoNumbering` | ✅ đạt | 8 | Không tìm thấy Block "DHCB-KHONG-CO-BLOCK" trong Model Space. Block có trong bản vẽ (5 tên): AMB006 ×10, AVE_RENDER ×8, AMB013 ×4, AB00522AAM01 ×1, AVE_GLOBAL ×1. |
| Báo lỗi rõ khi không có block cần tăng số attribute | `AttributeIncrement` | ✅ đạt | 4 | Không tìm thấy Block "DHCB-KHONG-CO-BLOCK" trong Model Space. Block có trong bản vẽ (5 tên): AMB006 ×10, AVE_RENDER ×8, AMB013 ×4, AB00522AAM01 ×1, AVE_GLOBAL ×1. |
| Báo lỗi rõ khi thiếu trường bắt buộc outputPath | `LayerExport` | ✅ đạt | 2 | E-CONFIG-MISSING: thiếu trường bắt buộc trong config (LayerExportConfig): "outputPath". |
| Báo lỗi rõ khi thiếu file quy tắc | `LayerStandardCheck` | ✅ đạt | 0 | Không tìm thấy file quy tắc: "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/khong-co-quy-tac.json". |
