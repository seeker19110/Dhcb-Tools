# DHCB Tools — Tổng quan (1 trang)

> Cửa vào duy nhất cho người chưa biết dự án: quản lý, khách hàng, kỹ sư mới. Chi tiết kỹ thuật, bằng chứng
> test và lộ trình đầy đủ nằm ở các tài liệu khác — trang này chỉ tóm 3 câu hỏi: *là gì, làm được gì hôm nay,
> giới hạn ở đâu*.

## DHCB Tools là gì

Một add-in cho **Revit** và **AutoCAD**, tự động hoá các việc lặp lại của kỹ sư xây dựng — đánh số, đổi tên
sheet, kiểm tham số, kiểm va chạm, xuất báo cáo, xuất toạ độ định vị cho máy toàn đạc — và một lớp kiểm/bàn
giao mô hình BIM theo hai nghị định mới (NĐ 217/2026, NĐ 207/2026). Chạy được từ nút bấm (Ribbon), từ dòng
lệnh (batch chạy đêm), hoặc từ trợ lý AI (qua Bridge/MCP).

## Dùng được ngay hôm nay: 14 lệnh đã kiểm giá trị rõ

Dự án có 64 lệnh, nhưng phần lớn mới chỉ *chạy được* — chưa có bằng chứng người dùng ngoài tác giả thật sự
cần đến. 14 lệnh dưới đây là bậc **hỗ trợ**: giá trị đã rõ qua vòng đóng vai kỹ sư, nên dùng trước tiên.
Nút Ribbon của các lệnh còn lại (bậc **thử nghiệm**) có ghi chú riêng trong tooltip.

| Nhóm | Lệnh |
|---|---|
| Kiểm tra & báo cáo | `WarningsExport`, `HealthReport`, `ParameterRuleCheck`, `ClashDetection`, `IdsValidate` |
| Sheet & bản vẽ | `SheetRename`, `RevisionOnSheets`, `BatchExport` |
| MEPF | `HangerAuto`, `SlopePipes` |
| Trắc đạc | `SetoutExport` |
| AutoCAD | `LayerStandardCheck`, `AttributeIncrement`, `BlockQuantity` |

## Cài đặt nhanh

1. Cài add-in cho Revit hoặc AutoCAD theo [`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md) mục 1–3 (~10 phút).
2. Mở model, bấm một trong 14 lệnh ở bảng trên trên tab **DHCB Tools**.
3. Mọi lệnh ghi vào mô hình đều **xem trước** (dry-run) trước khi hỏi *Chạy thật*.

## Giới hạn cần biết trước khi giao cho người khác dùng

- **Chưa có người dùng thật.** Mọi con số kiểm thử (bộ ca kiểm, vòng đóng vai kỹ sư) đều do tác giả tự tạo
  trên máy của mình — xem [`danh-gia-va-tam-nhin-2026-09-06.md`](danh-gia-va-tam-nhin-2026-09-06.md) §4.1.
- **Chưa có giấy phép (`LICENSE`) và DLL chưa ký** — SmartScreen/chính sách IT có thể chặn ở máy công ty.
  Đây là quyết định đang chờ chốt, không phải việc quên làm.
- **Nhóm MEPF (`SleeveAuto`, `HangerAuto`…) cần family/tham số đúng tên dự án** để hết báo lỗi "không tìm
  thấy" — `DictionaryLearn` giúp dò tên nhưng chưa phải một nút.
- **Phần tuân thủ pháp luật (IDS mẫu, dấu bản vẽ hoàn công)** cần người có chuyên môn QLDA/pháp lý rà soát
  bản gốc Công báo trước khi dùng cho hồ sơ nộp — chưa ai rà.

## Một trang riêng theo vai trò

| Vai trò | Đọc |
|---|---|
| Kiến trúc / dựng sheet | [`vai-tro-kien-truc.md`](vai-tro-kien-truc.md) |
| MEP (điện/nước/HVAC) | [`vai-tro-mep.md`](vai-tro-mep.md) |
| BIM manager / QLDA | [`vai-tro-bim-manager.md`](vai-tro-bim-manager.md) |
| AutoCAD | [`vai-tro-autocad.md`](vai-tro-autocad.md) |

## Đọc tiếp ở đâu

| Muốn biết | Đọc |
|---|---|
| Toàn bộ 64 lệnh, cấu trúc mã nguồn | [`README.md`](../README.md) |
| Việc đang làm, số liệu chi tiết | [`progress.md`](progress.md) |
| Kế hoạch phía trước | [`roadmap.md`](roadmap.md) |
| Đánh giá độc lập, điểm yếu, tầm nhìn 24 tháng | [`danh-gia-va-tam-nhin-2026-09-06.md`](danh-gia-va-tam-nhin-2026-09-06.md) |
| Bằng chứng từng lần chạy thật (phụ lục tra cứu, không phải chỗ bắt đầu đọc) | [`bang-chung-test.md`](bang-chung-test.md) |
