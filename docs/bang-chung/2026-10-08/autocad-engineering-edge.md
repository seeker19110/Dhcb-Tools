# DHCB locked-layer engineering contract

**Model:** ``  
**Chạy lúc:** 2026-10-08 00:51  
**Kết quả:** 51 đạt / 0 trượt / 0 bỏ qua trên 51 ca.

## Toàn bộ

| Ca | Lệnh | Kết quả | ms | Summary |
|---|---|---|---:|---|
| TextReplace locked preview stays partial and unchanged | `TextReplace` | ✅ đạt | 6 | Không có văn bản nào thay được — xem lý do bên dưới. |
| TextReplace locked real write stays partial and unchanged | `TextReplace` | ✅ đạt | 2 | Không có văn bản nào thay được — xem lý do bên dưới. |
| AutoNumbering locked preview stays partial and unchanged | `AutoNumbering` | ✅ đạt | 2 | [Xem trước] Sẽ đánh số 0 Block "DHCB-QA-ENGINEERING" vào attribute "NUM". |
| AutoNumbering locked real write stays partial and unchanged | `AutoNumbering` | ✅ đạt | 0 | Đã đánh số 0/3 Block "DHCB-QA-ENGINEERING". |
| AttributeIncrement locked preview stays partial and unchanged | `AttributeIncrement` | ✅ đạt | 2 | [Xem trước] Sẽ gán 0 giá trị vào attribute "NUM" của block "DHCB-QA-ENGINEERING". |
| AttributeIncrement locked real write stays partial and unchanged | `AttributeIncrement` | ✅ đạt | 0 | Đã gán 0/3 giá trị attribute "NUM" cho block "DHCB-QA-ENGINEERING". |
| AttributeImport locked preview stays partial and unchanged | `AttributeImport` | ✅ đạt | 2 | [Xem trước] Sẽ cập nhật 0 attribute, bỏ qua 1 dòng (chưa ghi vào drawing). |
| AttributeImport locked real write stays partial and unchanged | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 0 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617\locked/../attributes-edit.csv", bỏ qua 1 dòng. |
| LayerTranslate locked preview stays partial and unchanged | `LayerTranslate` | ✅ đạt | 7 | [Xem trước] Sẽ đổi layer của 0 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| LayerTranslate locked real write stays partial and unchanged | `LayerTranslate` | ✅ đạt | 3 | Đã đổi layer của 0 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-lock.csv", xoá 0 layer nguồn rỗng. |
| AttributeImport unsupported CSV rows preview | `AttributeImport` | ✅ đạt | 2 | [Xem trước] Sẽ cập nhật 0 attribute, bỏ qua 3 dòng (chưa ghi vào drawing). |
| AttributeImport unsupported CSV rows real | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 0 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617\locked/../attributes-incomplete.csv", bỏ qua 3 dòng. |
| AutoNumbering missing requested tag preview | `AutoNumbering` | ✅ đạt | 2 | [Xem trước] Sẽ đánh số 0 Block "DHCB-QA-ENGINEERING" vào attribute "MISSING". |
| AutoNumbering missing requested tag real | `AutoNumbering` | ✅ đạt | 0 | Đã đánh số 0/3 Block "DHCB-QA-ENGINEERING". |
| AttributeIncrement missing requested tag preview | `AttributeIncrement` | ✅ đạt | 2 | [Xem trước] Sẽ gán 0 giá trị vào attribute "MISSING" của block "DHCB-QA-ENGINEERING". |
| AttributeIncrement missing requested tag real | `AttributeIncrement` | ✅ đạt | 0 | Đã gán 0/3 giá trị attribute "MISSING" cho block "DHCB-QA-ENGINEERING". |
| TextReplace match crossing MText formatting preview | `TextReplace` | ✅ đạt | 4 | Không có văn bản nào thay được — xem lý do bên dưới. |
| TextReplace match crossing MText formatting real | `TextReplace` | ✅ đạt | 1 | Không có văn bản nào thay được — xem lý do bên dưới. |
| TextReplace no matching text preview | `TextReplace` | ✅ đạt | 1 | Không tìm thấy văn bản nào khớp để thay. |
| TextReplace no matching text real | `TextReplace` | ✅ đạt | 1 | Không tìm thấy văn bản nào khớp để thay. |
| AttributeImport unchanged locked CSV preview | `AttributeImport` | ✅ đạt | 2 | [Xem trước] Sẽ cập nhật 0 attribute, bỏ qua 0 dòng (chưa ghi vào drawing). |
| AttributeImport unchanged locked CSV real | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 0 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617\locked/../attributes-baseline.csv", bỏ qua 0 dòng. |
| AutoNumbering already correct locked number preview | `AutoNumbering` | ✅ đạt | 2 | [Xem trước] Sẽ đánh số 0 Block "DHCB-QA-NOOP" vào attribute "NUM". 1 attribute đã đúng số, sẽ không ghi. |
| AutoNumbering already correct locked number real | `AutoNumbering` | ✅ đạt | 0 | Đã đánh số 0/1 Block "DHCB-QA-NOOP". 1 attribute đã đúng số, không ghi. |
| AttributeIncrement already correct locked pattern preview | `AttributeIncrement` | ✅ đạt | 2 | [Xem trước] Sẽ gán 0 giá trị vào attribute "NUM" của block "DHCB-QA-NOOP". 1 attribute đã đúng số, sẽ không ghi. |
| AttributeIncrement already correct locked pattern real | `AttributeIncrement` | ✅ đạt | 0 | Đã gán 0/1 giá trị attribute "NUM" cho block "DHCB-QA-NOOP". 1 attribute đã đúng số, không ghi. |
| LayerTranslate same layer map preview | `LayerTranslate` | ✅ đạt | 5 | [Xem trước] Sẽ đổi layer của 0 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| LayerTranslate same layer map real | `LayerTranslate` | ✅ đạt | 14 | Đã đổi layer của 0 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-noop.csv", xoá 0 layer nguồn rỗng. |
| LayerTranslate same layer map case insensitive preview | `LayerTranslate` | ✅ đạt | 5 | [Xem trước] Sẽ đổi layer của 0 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| LayerTranslate same layer map case insensitive real | `LayerTranslate` | ✅ đạt | 2 | Đã đổi layer của 0 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-noop-case.csv", xoá 0 layer nguồn rỗng. |
| LayerTranslate mixed invalid rules with valid noop preview | `LayerTranslate` | ✅ đạt | 11 | [Xem trước] Sẽ đổi layer của 0 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| LayerTranslate mixed invalid rules with valid noop real | `LayerTranslate` | ✅ đạt | 2 | Đã đổi layer của 0 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-incomplete.csv", xoá 0 layer nguồn rỗng. |
| TextReplace respects locked parent INSERT with layer0 attribute preview | `TextReplace` | ✅ đạt | 1 | Không có văn bản nào thay được — xem lý do bên dưới. |
| TextReplace respects locked parent INSERT with layer0 attribute real | `TextReplace` | ✅ đạt | 1 | Không có văn bản nào thay được — xem lý do bên dưới. |
| AutoNumbering respects locked parent INSERT with layer0 attribute preview | `AutoNumbering` | ✅ đạt | 2 | [Xem trước] Sẽ đánh số 0 Block "DHCB-QA-PARENT-LOCK" vào attribute "NUM". |
| AutoNumbering respects locked parent INSERT with layer0 attribute real | `AutoNumbering` | ✅ đạt | 0 | Đã đánh số 0/1 Block "DHCB-QA-PARENT-LOCK". |
| AttributeIncrement respects locked parent INSERT with layer0 attribute preview | `AttributeIncrement` | ✅ đạt | 2 | [Xem trước] Sẽ gán 0 giá trị vào attribute "NUM" của block "DHCB-QA-PARENT-LOCK". |
| AttributeIncrement respects locked parent INSERT with layer0 attribute real | `AttributeIncrement` | ✅ đạt | 0 | Đã gán 0/1 giá trị attribute "NUM" cho block "DHCB-QA-PARENT-LOCK". |
| AttributeImport respects locked parent INSERT with layer0 attribute preview | `AttributeImport` | ✅ đạt | 2 | [Xem trước] Sẽ cập nhật 0 attribute, bỏ qua 1 dòng (chưa ghi vào drawing). |
| AttributeImport respects locked parent INSERT with layer0 attribute real | `AttributeImport` | ✅ đạt | 0 | Đã cập nhật 0 attribute từ "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617\locked/../attributes-parent-lock.csv", bỏ qua 1 dòng. |
| LayerTranslate moves unlocked layer0 entities and preserves locked-parent attributes preview | `LayerTranslate` | ✅ đạt | 5 | [Xem trước] Sẽ đổi layer của 602 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| LayerTranslate moves unlocked layer0 entities and preserves locked-parent attributes real | `LayerTranslate` | ✅ đạt | 9 | Đã đổi layer của 602 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-parent-lock.csv", xoá 0 layer nguồn rỗng. |
| LayerTranslate attribute-only source stays used behind locked parent preview | `LayerTranslate` | ✅ đạt | 12 | [Xem trước] Sẽ đổi layer của 0 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| LayerTranslate bad properties remain incomplete preview | `LayerTranslate` | ✅ đạt | 19 | [Xem trước] Sẽ đổi layer của 0 đối tượng (entity + attribute), xoá 0 layer nguồn rỗng. |
| LayerTranslate attribute-only source stays used behind locked parent real | `LayerTranslate` | ✅ đạt | 5 | Đã đổi layer của 0 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-attribute-only.csv", xoá 0 layer nguồn rỗng. |
| LayerTranslate bad properties remain incomplete real | `LayerTranslate` | ✅ đạt | 12 | Đã đổi layer của 0 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-bad-properties.csv", xoá 0 layer nguồn rỗng. |
| LayerTranslate unavailable linetype fallback is incomplete | `LayerTranslate` | ✅ đạt | 5 | Đã đổi layer của 0 đối tượng (entity + attribute) theo "C:\Users\liend\Dhcb Tools\tests\suites/fixtures/layers-engineering-missing-linetype.csv", xoá 0 layer nguồn rỗng. |
| TextReplace formatted-span identity remains a true noop preview | `TextReplace` | ✅ đạt | 1 | Không tìm thấy văn bản nào khớp để thay. |
| TextReplace formatted-span identity remains a true noop real | `TextReplace` | ✅ đạt | 1 | Không tìm thấy văn bản nào khớp để thay. |
| Export locked fixture for independent unchanged-value check | `AttributeExport` | ✅ đạt | 0 | Đã xuất 6 attribute từ 3 block ra "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617\locked/attributes-locked.csv". |
| Export new-layer property fallbacks for independent verification | `LayerExport` | ✅ đạt | 3 | Đã xuất 75 layer ra "C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617\locked/layers-final.csv". |
