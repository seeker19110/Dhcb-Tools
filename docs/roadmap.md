# Lộ trình phát triển DHCB Tools

Tài liệu này mô tả **kế hoạch phía trước**. Hiện trạng thực tế (đã làm được gì, còn lỗi gì)
nằm ở [`progress.md`](progress.md). Cơ sở kỹ thuật và khảo sát tính năng nằm ở
[`nghien-cuu-dhcb-revit-tools.md`](nghien-cuu-dhcb-revit-tools.md).

## Nguyên tắc xuyên suốt

1. **Core không biết UI.** Mọi lệnh giữ chữ ký `Document`/`Database` + config → `CommandResult`.
   Đây là lý do một lệnh chạy được cả trên Ribbon, HTTP Bridge lẫn batch runner mà không sửa logic.
2. **`DryRun` mặc định bật.** Lệnh nào sửa mô hình cũng phải xem trước được.
3. **Một lệnh = một transaction**, kèm `IFailuresPreprocessor` để chạy được khi không có người.
4. **AI chỉ sinh đề xuất.** Mọi thay đổi mô hình đi qua transaction của tool và có kỹ sư duyệt.

---

## Giai đoạn 0 — Trả nợ kỹ thuật

**Vì sao đứng trước:** mọi giai đoạn sau đều xây trên `DhcbTools.Core`. Sửa nền móng khi
codebase còn ~2.700 dòng rẻ hơn nhiều so với sau khi thêm MEPF.

| Việc | Lý do |
|---|---|
| Project `DhcbTools.Core.Tests` (xUnit) | Parser CSV và thuật toán đánh số là logic thuần, test được không cần Revit |
| Sửa nhóm lỗi âm thầm | Xem mục "Lỗi đã biết" trong `progress.md` |
| Token xác thực cho HTTP Bridge | Bridge đang mở cổng không xác thực trên máy kỹ sư |
| Tách `DhcbTools.Shared` | `CommandResult`, `ICoreCommand`, phần HTTP chung đang bị nhân đôi giữa Revit và AutoCAD |

**Xong khi:** test chạy xanh trong CI, Bridge yêu cầu token, không còn class trùng lặp giữa hai Core.

---

## Giai đoạn 1 — Batch runner (ưu tiên cao nhất)

Đây là **đích của dự án** theo tài liệu nghiên cứu (tự động hoá cấp 3): máy chạy đêm, sáng ra có kết quả.

| Việc | Ghi chú |
|---|---|
| Batch runner: mở → xử lý → lưu → đóng từng file theo danh sách | Ba lệnh hiện có cắm thẳng vào được, không cần sửa |
| Chạy theo lịch qua Windows Task Scheduler | Vẫn cần một máy có license Revit — không có headless mode chính thức |
| Batch export PDF / DWG / IFC | Giai đoạn 5 của quy trình A→Z, ~95% tự động |
| Health report + purge tổng toàn bộ dự án | Mở rộng từ `RemoveUnusedViewsCommand` đã có |
| Log tập trung + báo cáo tổng hợp sau mỗi lần chạy | Điều kiện để tin được kết quả chạy đêm |

**Vì sao ưu tiên:** biến ba lệnh sẵn có thành giá trị chạy đêm ngay lập tức, và hoàn tất ý nghĩa
cho HTTP Bridge đã xây (xem "Ghi chú về thứ tự" bên dưới).

**Xong khi:** một file danh sách dự án + một lệnh hẹn giờ là đủ để sáng hôm sau có PDF và health report.

---

## Giai đoạn 2 — Khởi tạo dự án & hồ sơ bản vẽ

Phần lớn tự động toàn phần, rủi ro kỹ thuật thấp.

- Tạo file từ template chuẩn, đặt tên theo quy tắc, tạo workset.
- Gán shared parameters và project info từ config JSON/Excel.
- Load family theo bộ môn, transfer project standards.
- Grid/level sinh từ bản CAD hoặc bảng Excel.
- Tạo sheet hàng loạt, tag và dim hàng loạt.

---

## Giai đoạn 3 — MEPF

Khối lượng lớn nhất của toàn dự án. Làm **từ dưới lên** để mỗi bước có giá trị dùng được ngay:

1. **Sleeve/opening** tại giao cắt với tường-sàn-dầm — độc lập, giá trị ngay.
2. **Tag hàng loạt + điền cao độ** đáy/đỉnh/tim.
3. **Routing mức A** — bán tự động theo tuyến: kỹ sư vẽ model line, tool dựng duct/pipe/tray
   hoàn chỉnh với fitting đúng routing preference.
4. **Hanger/support + chia ống** theo cây 3m/6m.
5. **Routing mức B** — tự động cục bộ theo quy tắc (rải sprinkler, miệng gió rồi nối về trục chính).
6. **Sizing theo hệ** — chỉ ở mức *đề xuất*, kỹ sư duyệt.

Chi tiết từng hệ (HVAC / Nước / Điện / PCCC) xem mục 4.3 của tài liệu nghiên cứu.

**Rủi ro:** chất lượng kết quả phụ thuộc family fitting và routing preference của từng dự án —
phải đọc `RoutingPreferenceManager` thay vì hard-code, và báo lỗi rõ khi thiếu fitting.

---

## Giai đoạn 4 — Tự động hoá cấp 2 (theo sự kiện)

- `IUpdater` điền cao độ và cập nhật tham số theo thời gian thực.
- Checker: connector hở, tham số thiếu, đặt tên sai quy tắc.
- Clash detection nội bộ + báo cáo.

**Rủi ro:** `IUpdater` chạy trong mọi transaction của người dùng — một lỗi ở đây làm chậm toàn bộ
Revit. Cần đo hiệu năng trước khi bật mặc định.

---

## Giai đoạn 5 — Lớp AI

Hai điểm cắm ROI cao nhất, làm trước:

1. **Map layer/block CAD → Revit type.** Gửi danh sách layer + danh mục type của template,
   nhận bảng mapping JSON đúng schema. Kỹ sư duyệt bảng thay vì map tay hàng trăm dòng.
2. **PDF thuyết minh/spec → config khởi tạo dự án.** Gửi PDF thẳng lên API, trích số tầng,
   cao độ, hệ thống, tiêu chuẩn.

Về sau: phân tích báo cáo clash/warning (chạy đêm bằng Batch API), và tool use với whitelist
lệnh Core để ra lệnh bằng tiếng Việt.

API key lưu ngoài repo (biến môi trường hoặc DPAPI), không commit.

---

## Giai đoạn 6 — Tuỳ nhu cầu

- **Routing mức C** — pathfinding 3D (A*) né va chạm. Giới hạn phạm vi (1 hệ, 1 tầng, hành lang)
  để khả thi; nếu không sẽ là hố đen thời gian.
- Mở rộng HTTP Bridge thành MCP server.

---

## Ghi chú về thứ tự

Tài liệu nghiên cứu (mục 6) xếp HTTP Bridge là việc **cuối cùng, tuỳ chọn**, nhưng thực tế nó đã
được làm ngay sau giai đoạn nền tảng. Hệ quả: Bridge hiện chỉ điều khiển được đúng ba lệnh nền
tảng — hạ tầng điều khiển từ xa đang chờ tính năng để điều khiển.

Đây không phải việc cần sửa, mà là lý do **Giai đoạn 1 (batch runner) được đẩy lên ưu tiên cao
nhất**: batch runner là cặp đôi tự nhiên của Bridge và làm cho cả hai cùng có giá trị.

Rủi ro còn lại của lộ trình: MEPF chiếm khoảng một nửa khối lượng còn lại nhưng nằm ở giữa. Nếu
làm tuần tự nghiêm ngặt sẽ mất nhiều tháng trước khi chạm tới batch chạy đêm và lớp AI — những
thứ tạo cảm nhận rõ nhất về thay đổi cách làm việc. Vì vậy Giai đoạn 1 nên làm dứt điểm trước.
