# Lộ trình phát triển DHCB Tools

Tài liệu này mô tả **kế hoạch phía trước**. Hiện trạng thực tế (đã làm được gì, còn lỗi gì)
nằm ở [`progress.md`](progress.md). Cơ sở kỹ thuật và khảo sát tính năng nằm ở
[`nghien-cuu-dhcb-revit-tools.md`](nghien-cuu-dhcb-revit-tools.md).

Ký hiệu: ✅ xong · 🟡 làm dở · ⬜ chưa bắt đầu.

## Nguyên tắc xuyên suốt

1. **Core không biết UI.** Mọi lệnh giữ chữ ký `Document`/`Database` + config → `CommandResult`.
   Đây là lý do một lệnh chạy được cả trên Ribbon, HTTP Bridge lẫn batch runner mà không sửa logic.
2. **`DryRun` mặc định bật.** Lệnh nào sửa mô hình cũng phải xem trước được.
3. **Một lệnh = một transaction**, kèm `IFailuresPreprocessor` để chạy được khi không có người.
4. **AI chỉ sinh đề xuất.** Mọi thay đổi mô hình đi qua transaction của tool và có kỹ sư duyệt.

---

## Giai đoạn 0 — Trả nợ kỹ thuật ⬜

**Vì sao đứng trước:** mọi giai đoạn sau đều xây trên `DhcbTools.Core`. Sửa nền móng bây giờ rẻ
hơn nhiều so với sau khi thêm routing MEPF (khối lượng lớn nhất còn lại).

| Việc | Lý do |
|---|---|
| Project `DhcbTools.Core.Tests` (xUnit) | Parser CSV, thuật toán đánh số, logic hình học MEPF đều là logic thuần, test được không cần Revit |
| Sửa nhóm lỗi âm thầm | Xem mục "Lỗi đã biết" trong `progress.md` |
| Token xác thực cho HTTP Bridge | Bridge đang mở cổng không xác thực trên máy kỹ sư |
| Tách `DhcbTools.Shared` | `CommandResult`, `ICoreCommand`, phần HTTP chung đang bị nhân đôi giữa Revit và AutoCAD |
| Gắn Hanger/PipeSplitter vào Ribbon + Bridge | Core command đã có, chưa gọi được từ đâu cả |

**Xong khi:** test chạy xanh trong CI, Bridge yêu cầu token, không còn class trùng lặp giữa hai Core.

---

## Giai đoạn 1 — Batch runner chạy đêm ⬜

**Đã có nguyên liệu, chưa có bản thân batch runner.** `BatchExportCommand`, `HealthReportCommand`
và các lệnh nền tảng đều xử lý **một file đang mở**. Còn thiếu lớp điều phối chạy nhiều file
không người trực — đây mới là "đích của dự án" theo tài liệu nghiên cứu (tự động hoá cấp 3).

| Việc | Ghi chú |
|---|---|
| Batch runner: mở → xử lý → lưu → đóng từng file theo danh sách | Mọi lệnh hiện có cắm thẳng vào được, không cần sửa logic |
| Chạy theo lịch qua Windows Task Scheduler | Vẫn cần một máy có license Revit — không có headless mode chính thức |
| Log tập trung + báo cáo tổng hợp sau mỗi lần chạy | Điều kiện để tin được kết quả chạy đêm |

**Vì sao vẫn ưu tiên cao nhất:** biến toàn bộ lệnh đã có (kể cả MEPF nền tảng) thành giá trị chạy
đêm ngay lập tức, và hoàn tất ý nghĩa cho HTTP Bridge đã xây từ sớm (xem "Ghi chú về thứ tự" bên dưới).

**Xong khi:** một file danh sách dự án + một lệnh hẹn giờ là đủ để sáng hôm sau có PDF, health
report, và log kết quả sleeve/tag/hanger đã chạy qua đêm.

---

## Giai đoạn 2 — Khởi tạo dự án & hồ sơ bản vẽ 🟡

**Đã xong:** `ProjectInit/*` — dựng Level, Grid, load family theo danh mục, gán project info,
tất cả từ config JSON.

**Còn thiếu:**
- Tạo file từ template chuẩn công ty (đặt tên theo quy tắc, tạo workset) — hiện `ProjectInit`
  giả định file đã tồn tại, chưa tự sinh file mới từ template.
- Transfer project standards (browser organization, view template, filter) từ file chuẩn.
- Grid/level sinh từ **bản CAD** hoặc bảng Excel — hiện chỉ nhận config JSON viết tay.
- Tạo sheet hàng loạt, tag và dim hàng loạt (thuộc Giai đoạn 4 quy trình A→Z).

---

## Giai đoạn 3 — MEPF 🟡

Khối lượng lớn nhất của toàn dự án. Phần nền tảng (làm từ dưới lên) đã xong; routing là phần còn
lại và nặng nhất.

| Bước | Trạng thái | Ghi chú |
|---|---|---|
| 1. Sleeve/opening tại giao cắt tường-sàn-dầm | ✅ | `MEPF/SleeveCommand`, lọc 2 lớp BoundingBox → IntersectsSolid |
| 2. Tag hàng loạt + điền cao độ đáy/đỉnh/tim | ✅ | `MEPF/ElevationTagCommand` |
| Hanger/support theo khoảng cách chuẩn | ✅ (chưa gắn UI) | `MEPF/HangerCommand` — xem Giai đoạn 0 |
| Chia ống/máng theo cây 3m/6m | ✅ (chưa gắn UI) | `MEPF/PipeSplitterCommand` (`BreakCurve`) — xem Giai đoạn 0 |
| Kiểm tra connector hở toàn mô hình | ✅ | `MEPF/ConnectorCheckerCommand`, tuỳ chọn tạo 3D view khoanh vùng |
| 3. Routing mức A — bán tự động theo tuyến | ⬜ | Kỹ sư vẽ model line, tool dựng duct/pipe/tray hoàn chỉnh với fitting đúng routing preference |
| 5. Routing mức B — tự động cục bộ theo quy tắc | ⬜ | Rải sprinkler/miệng gió theo pattern chuẩn rồi nối về trục chính |
| 6. Sizing theo hệ | ⬜ | Chỉ ở mức *đề xuất*, kỹ sư duyệt |
| Tô màu/filter theo hệ, cập nhật System Name | ⬜ | |
| Đánh số thiết bị/đoạn theo tuyến hoặc phòng | ⬜ | Sắp theo connector graph |

Chi tiết từng hệ (HVAC / Nước / Điện / PCCC) xem mục 4.3 của tài liệu nghiên cứu.

**Rủi ro cho phần routing:** chất lượng kết quả phụ thuộc family fitting và routing preference của
từng dự án — phải đọc `RoutingPreferenceManager` thay vì hard-code, và báo lỗi rõ khi thiếu fitting.
Auto-connect có thể fail khi góc lệch nhỏ/đoạn ngắn hơn fitting — cần fallback (dời điểm, báo user)
thay vì rollback cả transaction.

---

## Giai đoạn 4 — Tự động hoá cấp 2 (theo sự kiện) ⬜

- `IUpdater` điền cao độ và cập nhật tham số theo thời gian thực — hiện `ElevationTagCommand` chỉ
  chạy theo lệnh (cấp 1), chưa chạy real-time.
- Checker: tham số thiếu, đặt tên sai quy tắc (connector hở đã có ở cấp 1, xem Giai đoạn 3).
- Clash detection nội bộ + báo cáo.

**Rủi ro:** `IUpdater` chạy trong mọi transaction của người dùng — một lỗi ở đây làm chậm toàn bộ
Revit. Cần đo hiệu năng trước khi bật mặc định.

---

## Giai đoạn 5 — Lớp AI ⬜

Hai điểm cắm ROI cao nhất, làm trước:

1. **Map layer/block CAD → Revit type.** Gửi danh sách layer + danh mục type của template,
   nhận bảng mapping JSON đúng schema. Kỹ sư duyệt bảng thay vì map tay hàng trăm dòng.
2. **PDF thuyết minh/spec → config khởi tạo dự án.** Gửi PDF thẳng lên API, trích số tầng,
   cao độ, hệ thống, tiêu chuẩn — đúng định dạng config mà `ProjectInit/*` đã nhận sẵn.

Về sau: phân tích báo cáo clash/warning (chạy đêm bằng Batch API), và tool use với whitelist
lệnh Core để ra lệnh bằng tiếng Việt — endpoint `/query` của Bridge (đọc ngữ cảnh model, không
ghi transaction) đã là bước chuẩn bị cho hướng này.

API key lưu ngoài repo (biến môi trường hoặc DPAPI), không commit.

---

## Giai đoạn 6 — Tuỳ nhu cầu ⬜

- **Routing mức C** — pathfinding 3D (A*) né va chạm. Giới hạn phạm vi (1 hệ, 1 tầng, hành lang)
  để khả thi; nếu không sẽ là hố đen thời gian.
- Mở rộng HTTP Bridge thành MCP server.

---

## Ghi chú về thứ tự

Tài liệu nghiên cứu (mục 6) xếp HTTP Bridge là việc **cuối cùng, tuỳ chọn**, nhưng thực tế nó đã
được làm ngay sau giai đoạn nền tảng — và kể từ đó, một lượng lớn tính năng (Giai đoạn 5+0, MEPF
nền tảng) đã được thêm vào **trước** khi có batch runner. Hệ quả: Bridge và các lệnh mới đều đã
sẵn sàng, nhưng chỉ chạy được từng file một, có người bấm nút — chưa ai được hưởng lợi ích "chạy
đêm, sáng ra có kết quả" mà tài liệu nghiên cứu đặt làm đích.

Đây là lý do **Giai đoạn 1 (batch runner) vẫn giữ ưu tiên cao nhất**, giờ còn cấp thiết hơn lúc
trước: càng nhiều lệnh nền tảng có sẵn mà chưa chạy được hàng loạt, chi phí cơ hội của việc
thiếu batch runner càng lớn.

Rủi ro còn lại: routing MEPF (mức A/B/C) là phần nặng nhất chưa động tới, và không có gì đảm bảo
chất lượng nếu thiếu Giai đoạn 0 (test) đi trước — logic hình học (giao cắt, khoảng cách, dung
sai) sai rất dễ xảy ra và khó phát hiện bằng mắt thường trên model lớn.
