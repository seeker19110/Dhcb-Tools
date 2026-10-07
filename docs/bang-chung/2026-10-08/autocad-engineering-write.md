# DHCB deterministic AutoCAD engineering writes

**Model:** `Copied Autodesk sample with autocad-engineering.lsp fixture`  
**Chạy lúc:** 2026-10-08 00:51  
**Kết quả:** 24 đạt / 0 trượt / 0 bỏ qua trên 24 ca.

## Toàn bộ

| Ca | Lệnh | Kết quả | ms | Summary |
|---|---|---|---:|---|
| Attribute fixture baseline | `AttributeExport` | ✅ đạt | 20 | Đã xuất 6 attribute từ 3 block ra "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-baseline.csv". |
| Text preview covers DBText, formatted MText and attribute | `TextReplace` | ✅ đạt | 16 | [Xem trước] Sẽ thay 3 đối tượng văn bản. |
| Repeated text preview has no model changes | `TextReplace` | ✅ đạt | 4 | [Xem trước] Sẽ thay 3 đối tượng văn bản. |
| Text replacement commits three entities | `TextReplace` | ✅ đạt | 3 | Đã thay 3 đối tượng văn bản. |
| Repeated committed text replacement is a no-op | `TextReplace` | ✅ đạt | 12 | Không tìm thấy văn bản nào khớp để thay. |
| Text restoration preview sees committed values | `TextReplace` | ✅ đạt | 4 | [Xem trước] Sẽ thay 3 đối tượng văn bản. |
| Restore fixture text | `TextReplace` | ✅ đạt | 2 | Đã thay 3 đối tượng văn bản. |
| Restoring fixture text again is a no-op | `TextReplace` | ✅ đạt | 2 | Không tìm thấy văn bản nào khớp để thay. |
| Geometric numbering preview | `AutoNumbering` | ✅ đạt | 10 | [Xem trước] Sẽ đánh số 3 Block "DHCB-QA-ENGINEERING" vào attribute "NUM". |
| Geometric numbering commits | `AutoNumbering` | ✅ đạt | 0 | Đã đánh số 3/3 Block "DHCB-QA-ENGINEERING". |
| Repeated numbering is a no-op | `AutoNumbering` | ✅ đạt | 0 | Đã đánh số 0/3 Block "DHCB-QA-ENGINEERING". 3 attribute đã đúng số, không ghi. |
| Export actual numbering for exact spatial assertion | `AttributeExport` | ✅ đạt | 0 | Đã xuất 6 attribute từ 3 block ra "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-numbered.csv". |
| Pattern increment preview | `AttributeIncrement` | ✅ đạt | 6 | [Xem trước] Sẽ gán 3 giá trị vào attribute "NUM" của block "DHCB-QA-ENGINEERING". |
| Pattern increment commits | `AttributeIncrement` | ✅ đạt | 0 | Đã gán 3/3 giá trị attribute "NUM" cho block "DHCB-QA-ENGINEERING". |
| Repeated pattern increment is a no-op | `AttributeIncrement` | ✅ đạt | 0 | Đã gán 0/3 giá trị attribute "NUM" cho block "DHCB-QA-ENGINEERING". 3 attribute đã đúng số, không ghi. |
| Export actual pattern values | `AttributeExport` | ✅ đạt | 0 | Đã xuất 6 attribute từ 3 block ra "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-incremented.csv". |
| CSV attribute with comma and quotes preview | `AttributeImport` | ✅ đạt | 5 | [Xem trước] Sẽ cập nhật 1 attribute, bỏ qua 0 dòng (chưa ghi vào drawing). |
| CSV attribute with comma and quotes commits | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 1 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-edit.csv", bỏ qua 0 dòng. |
| Repeated CSV attribute import is a no-op | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 0 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-edit.csv", bỏ qua 0 dòng. |
| Export actual CSV attribute value | `AttributeExport` | ✅ đạt | 0 | Đã xuất 6 attribute từ 3 block ra "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-imported.csv". |
| Attribute restore preview proves four committed changes | `AttributeImport` | ✅ đạt | 2 | [Xem trước] Sẽ cập nhật 4 attribute, bỏ qua 0 dòng (chưa ghi vào drawing). |
| Restore all fixture attributes | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 4 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-baseline.csv", bỏ qua 0 dòng. |
| Repeated fixture restoration is a no-op | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 0 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-baseline.csv", bỏ qua 0 dòng. |
| Export restored fixture attributes | `AttributeExport` | ✅ đạt | 0 | Đã xuất 6 attribute từ 3 block ra "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617/attributes-restored.csv". |
