# Quy trình chuẩn dựng Revit A→Z với tự động hoá tối đa (kết hợp AI API)

Rà soát toàn bộ quy trình dựng mô hình Revit từ nhận đầu vào đến bàn giao hồ sơ, mỗi bước gắn với mức tự động hoá cao nhất khả thi trên **Revit desktop** (phạm vi đã chốt) và điểm cắm AI API khi AI thực sự có giá trị.

Ký hiệu mức tự động: 🟢 tự động toàn phần · 🟡 bán tự động (kỹ sư duyệt/chọn) · 🔴 thủ công là chính, tool chỉ hỗ trợ.

## Giai đoạn 0 — Chuẩn bị & khởi tạo dự án

| Bước | Mức | Cách tự động hoá |
|---|---|---|
| Tạo file từ template chuẩn công ty | 🟢 | Tool "New Project": chọn loại dự án → copy template, đặt tên theo quy tắc, tạo sẵn workset chuẩn |
| Thiết lập shared parameters, project info | 🟢 | Đọc file cấu hình JSON/Excel của công ty → gán tự động toàn bộ |
| Load family theo bộ môn/loại dự án | 🟢 | Thư viện family có index; tool load theo danh mục đã chọn |
| Thiết lập browser organization, view template, filter | 🟢 | Transfer project standards tự động từ file chuẩn |

**Điểm cắm AI:** đọc tài liệu đầu vào của dự án (thuyết minh, tiêu chuẩn áp dụng — file PDF gửi thẳng lên API dạng document input) → AI trích xuất thông số: số tầng, cao độ, loại hệ thống, tiêu chuẩn → sinh file cấu hình JSON để tool khởi tạo. Kỹ sư duyệt JSON trước khi chạy.

## Giai đoạn 1 — Dựng lưới trục, cao độ, khung nền

| Bước | Mức | Cách tự động hoá |
|---|---|---|
| Link CAD/IFC/mô hình kiến trúc | 🟢 | Batch link + pin + đặt đúng shared coordinates theo cấu hình |
| Tạo Grid từ CAD | 🟢 | Đọc line + text layer trục trong DWG → tạo `Grid` đúng tên |
| Tạo Level từ bảng cao độ | 🟢 | Nhập Excel (tên tầng, cao độ) → tạo `Level` + view plan tương ứng hàng loạt |
| Copy/Monitor grid-level từ link | 🟡 | Tool chạy Copy/Monitor hàng loạt, báo cáo phần tử đã monitor |
| Scope box, view range chuẩn theo tầng | 🟢 | Sinh tự động từ phạm vi grid |

## Giai đoạn 2 — Dựng mô hình theo bộ môn

### Kiến trúc / Kết cấu
| Bước | Mức | Cách tự động hoá |
|---|---|---|
| Dựng tường/cột/dầm/sàn từ CAD link | 🟡 | Nhận diện layer + polyline khép kín → dựng phần tử; kỹ sư map layer↔type một lần, lưu preset |
| Vách/lanh tô/cửa từ block CAD | 🟡 | Map block name → family type, đặt theo insertion point |
| Phòng (Room/Space) + tên | 🟢 | Tạo room mọi vùng kín theo tầng, đặt tên từ text CAD gần tâm phòng |
| Hoàn thiện (ốp lát, trần) theo phòng | 🟡 | Gán finish theo bảng phòng-vật liệu từ Excel |

**Điểm cắm AI (giá trị cao):** map layer/block CAD → Revit type. Thay vì kỹ sư map tay hàng trăm layer, gửi danh sách layer + danh mục type của template lên AI (structured outputs để trả JSON đúng schema mapping) → kỹ sư chỉ duyệt bảng map. Tương tự cho đặt tên phòng từ text lộn xộn trong CAD.

### MEPF (chi tiết trong `nghien-cuu-mepf-tu-dong.md`)
- Routing mức A (theo tuyến vẽ/CAD) 🟡 → mức B (tự sinh nhánh thiết bị) 🟢 → mức C (pathfinding) để sau.
- Sleeve, hanger, fitting theo routing preference: 🟢.
- Sizing duct/pipe/cable theo lưu lượng: 🟢 (giá trị đề xuất, kỹ sư duyệt).

## Giai đoạn 3 — Kiểm tra & phối hợp

| Bước | Mức | Cách tự động hoá |
|---|---|---|
| Model checker theo bộ quy tắc công ty | 🟢 | Chạy khi sync (event) + chạy đêm batch; báo cáo Excel/HTML |
| Clash check nội bộ + với link | 🟢 | Solid intersection, nhóm theo cặp hệ, tạo 3D view khoanh vùng từng clash |
| Xử lý warnings | 🟡 | Phân loại tự động, tự sửa nhóm sửa được (duplicate mark, room không kín…), còn lại giao kỹ sư kèm nút zoom |
| Đồng bộ dữ liệu Space↔Room, tham số liên bộ môn | 🟢 | IUpdater hoặc lệnh chạy định kỳ |

**Điểm cắm AI:** tổng hợp báo cáo clash/warning hàng nghìn dòng → AI phân nhóm theo nguyên nhân gốc, xếp ưu tiên, viết tóm tắt cho họp phối hợp (Batch API chạy đêm, chi phí giảm 50%). AI không tự sửa mô hình — chỉ phân tích và đề xuất.

## Giai đoạn 4 — Hồ sơ bản vẽ

| Bước | Mức | Cách tự động hoá |
|---|---|---|
| Tạo sheet hàng loạt từ danh mục Excel | 🟢 | Số hiệu, tên, title block, tham số khung tên |
| Nhân bản view + áp view template + đặt lên sheet | 🟢 | Theo quy tắc tầng/bộ môn, căn vị trí theo lưới sheet |
| Tạo mặt cắt/chi tiết qua cấu kiện | 🟡 | Section tự động qua tuyến MEP/dầm chọn sẵn |
| Dimension + tag hàng loạt | 🟡 | Dim lưới trục, tường, tag cao độ/kích thước; kỹ sư dọn vị trí đè nhau |
| Schedule/bảng thống kê chuẩn | 🟢 | Sinh schedule từ định nghĩa lưu sẵn, xuất Excel |
| Đánh số revision, cloud | 🟡 | Gán revision hàng loạt theo danh sách sheet |

## Giai đoạn 5 — Xuất bản & bàn giao

| Bước | Mức | Cách tự động hoá |
|---|---|---|
| In PDF/DWG hàng loạt đúng quy tắc đặt tên | 🟢 | Batch export theo bộ chọn sheet, chạy đêm được |
| Xuất IFC/NWC theo mapping chuẩn | 🟢 | Cấu hình export lưu sẵn, batch nhiều file |
| Bóc khối lượng xuất Excel | 🟢 | Template bóc tách theo bộ môn |
| Health report + purge trước bàn giao | 🟢 | Lệnh tổng: audit → purge → compact → báo cáo |
| Biên bản/thuyết minh bàn giao | 🟡 | AI soạn thảo từ health report + danh mục bản vẽ, kỹ sư duyệt |

## Kiến trúc tích hợp AI API

- **SDK**: dùng SDK C# chính thức của Anthropic (cùng ngôn ngữ với add-in), gọi từ `DhcbTools.Core`; model mặc định `claude-opus-5`.
- **Các dạng gọi phù hợp**:
  - *Structured outputs*: mọi tác vụ mapping/trích xuất (layer→type, PDF→config) bắt buộc trả JSON đúng schema để tool tiêu thụ trực tiếp, không parse text tự do.
  - *Document input (PDF)*: gửi thuyết minh/tiêu chuẩn/spec trực tiếp, không cần OCR riêng.
  - *Batch API*: các việc chạy đêm không cần trả lời ngay (phân tích clash, soạn báo cáo) — rẻ hơn 50%.
  - *Tool use*: giai đoạn sau có thể cho AI gọi các lệnh Core qua danh sách tool giới hạn (whitelist, chỉ đọc hoặc chỉ tạo bản nháp) — tiền đề cho trợ lý "ra lệnh bằng tiếng Việt" trong Revit.
- **Nguyên tắc an toàn**: AI chỉ sinh *đề xuất/cấu hình*, mọi thay đổi mô hình đi qua transaction của tool và có bước duyệt của kỹ sư; không gửi dữ liệu dự án nhạy cảm nếu chưa được phép; API key lưu ngoài repo (biến môi trường/DPAPI).

## Bức tranh giờ công (ước lượng phần việc lặp lại tự động hoá được)

- Giai đoạn 0–1: ~90% tự động (khởi tạo, grid/level gần như 🟢 toàn bộ).
- Giai đoạn 2: ~40–60% — dựng hình là phần "người" nhất; CAD-to-Revit + routing A/B là đòn bẩy chính.
- Giai đoạn 3: ~80% — checker/clash chạy máy, người chỉ xử lý ngoại lệ.
- Giai đoạn 4: ~70% — sheet/schedule 🟢, dim/tag còn cần dọn tay.
- Giai đoạn 5: ~95% — gần như chạy đêm hoàn toàn.

## Thứ tự triển khai gợi ý (khớp lộ trình đã có)

1. Giai đoạn 5 + 0 trước (dễ, 🟢 nhiều, thấy hiệu quả ngay): batch export, health report, khởi tạo dự án.
2. Giai đoạn 1 + 4: grid/level từ CAD-Excel, sheet hàng loạt, tag/dim.
3. Giai đoạn 2: CAD-to-Revit + routing MEPF mức A/B.
4. Giai đoạn 3: checker + clash + IUpdater.
5. Lớp AI: bắt đầu bằng 2 điểm cắm ROI cao nhất — map layer CAD→type và trích xuất PDF→config khởi tạo.
