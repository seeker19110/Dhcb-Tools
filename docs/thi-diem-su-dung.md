# Thí điểm sử dụng DHCB Tools trong 2–4 tuần

Mục tiêu là đo giá trị công việc trên dự án thật. Con số test tự động không phải số người dùng hay số phút tiết kiệm.
Không nâng nhãn hỗ trợ chỉ dựa trên kết quả đóng vai.

## Chuẩn bị trên máy kỹ sư

1. Cài đúng gói theo phiên bản Revit/AutoCAD; dùng bản sao model/bản vẽ cho lần đầu.
2. Chạy `python scripts/dhcb_doctor.py --app revit` hoặc `--app autocad` từ repo/gói BatchRunner.
   Muốn kiểm cấu hình mà không gọi Bridge: thêm `--offline`. Muốn gửi báo cáo: thêm `--json` và chép stdout.
   Báo cáo không in token hay nội dung cấu hình; không gọi lệnh hoặc đọc dữ liệu mô hình.
3. Nếu máy không dùng AI/MCP thì `bridge.enabled=false` là cấu hình hợp lệ; không cần bật Bridge chỉ để dùng Ribbon.
4. Chọn ba lệnh theo vai trò trong tài liệu kiến trúc/MEP/BIM manager/AutoCAD, không học cả 68 lệnh một lượt.
5. Với MEP, chạy `DictionaryLearn` để đề xuất tên tham số có thật, xem báo cáo và duyệt từ điển. Kiểm family/type
   có trong model trước khi đặt hanger/sleeve. `FamilyStarter` là family khởi đầu; phải kiểm hình học/kích thước trước khi dùng cho hồ sơ sản xuất.

## Danh sách đề xuất cho vòng đầu

| Vai trò | Tác vụ | Kết quả cần đối chiếu |
|---|---|---|
| BIM manager | `WarningsExport`, `HealthReport` | Số warning và nhóm lỗi khớp Revit; báo cáo mở được |
| BIM manager | `ParameterRuleCheck`, `IdsValidate` | Bộ quy tắc phù hợp dự án, có phần tử đạt/trượt đã kiểm bằng tay |
| Kiến trúc | `SheetRename`, `BatchExport` | Số/tên sheet đúng, không trùng; đủ file và đúng nội dung xuất |
| MEP | `HangerAuto` | Family đúng, khoảng cách và vị trí đúng; chạy lại không nhân đôi |
| Trắc đạc | `SetoutExport` | Hệ tọa độ/đơn vị đã duyệt; kiểm một điểm bằng phép đo độc lập trước khi đưa ra công trường |
| AutoCAD | `LayerStandardCheck`, `BlockQuantity` | Quy tắc layer đúng công ty; số block khớp một mẫu đếm tay |

Mục tiêu 15 phút để cài và hoàn thành ba tác vụ đầu là **tiêu chí cần đo**, chưa phải cam kết đã chứng minh.
Mỗi người chỉ nhận những tác vụ thuộc công việc của mình.

## Ghi kết quả từng tác vụ

Chép `mau-do-hieu-qua.csv`, mỗi dòng là **một tác vụ thật**. Ghi số phút làm tay, số phút dùng tool bao gồm cấu hình,
xem trước, chạy và sửa kết quả. Ghi cả trường hợp bấm rồi bỏ; không loại dòng lỗi khỏi thống kê.
Không đưa tên người, đường dẫn hay nội dung dự án nhạy cảm vào bản gửi ra ngoài; dùng mã người/mã dự án.

Sau hai tuần, tổng hợp theo lệnh: số tác vụ, số ngày dùng, tổng phút tiết kiệm, tỷ lệ hoàn thành và số lỗi cần sửa.
Lệnh không được dùng cần ghi lý do: không cần, không hiểu, cấu hình quá lâu, thiếu family/tham số, hoặc kết quả sai.
Sau bốn tuần, chọn tối đa ba vấn đề gây mất thời gian nhiều nhất để sửa tiếp.

`UsageReport` là công cụ nội bộ đọc log tại máy và có thể hỗ trợ đếm sử dụng; nó không tự đo thời gian làm tay,
không chứng minh kết quả đúng và không tự thu thập dữ liệu từ các máy khác.

## Khi gặp lỗi

- Dừng thao tác ghi khi xem trước có lỗi/xung đột; sửa dữ liệu đầu vào rồi xem trước lại.
- Gửi tên lệnh, phiên bản host/add-in, bước tái hiện, báo cáo doctor và phần log liên quan sau khi rà dữ liệu nhạy cảm.
- Với Bridge timeout có `id`, chạy `python scripts/dhcb_agent.py revit progress ID` (hoặc `autocad`) để hỏi kết quả.
- Muốn bỏ job còn xếp hàng: `python scripts/dhcb_agent.py revit cancel ID`. Job đã được nhận trả `409`; không gửi
  lại lệnh ghi và không kết thúc tiến trình host để cố hủy.
- Phần hoàn công/biểu mẫu cần người có chuyên môn duyệt trước khi dùng trong hồ sơ chính thức.

Quy trình kỹ thuật và tiêu chí tiếp theo: [kế hoạch chất lượng](ke-hoach-chat-luong-2026-10-07.md).
