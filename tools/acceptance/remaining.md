# Nghiệm thu còn cần host/dữ liệu thật

PR #184 đã merge tại `e088829`; các kết quả mới không tự thay bằng chứng Revit lịch sử.
Máy hiện tại thiếu Revit.exe, computer-use lỗi cwd WSL. Có một cert ký mã tự cấp tại máy;
đó chưa là chứng thư tổ chức dùng phát hành trên máy sạch. Không thay kho cert hoặc tự cấp cert để ghi đạt.

## Revit — sáu ca preview đã chuẩn bị

```powershell
./scripts/run-in-revit-tests.ps1 -Suite readiness -RevitVersion 2024 -NoPrune -KeepRuns 0
```

Suite `tests/suites/revit-readiness.json` dùng Snowdon HVAC và đủ link kết cấu. Nó kiểm sinh/chọn/từ chối OPT-id,
clash mặc định/phân loại/BCF và khoảng hở sai. Không có ca ghi model; báo cáo có thể được xuất trong preview.
Đây là **suite chuẩn bị**, chưa có host hiện tại để ghi nhận đạt. Một model công ty cần tọa độ/bộ lọc được chọn riêng,
không dùng điểm Snowdon cho model khác.

## GUI, hủy, rollback và Undo

Dùng bản sao model, ghi version/DLL SHA, config, ảnh và đếm model line/MEP trước/sau từng bước.

1. Preview AutoRoute có `generateOptions=true`; chọn đúng `OPT-1/2/3` trong danh sách rồi preview lại.
2. Chạy BuildRoute trên bản sao đã có model line cùng style: MEP chỉ được dựng từ line mới; tuyến cũ giữ nguyên.
3. Chạy thật qua Bridge async với documentId/previewToken đã duyệt. Khi progress có `canCancel=true`, gửi cancel;
   `202` là đã nhận yêu cầu. Kết quả cuối báo hủy; đếm line/MEP và nội dung model phải khớp trước chạy.
4. Khi quyền hủy đã đóng ở `building-mep`, cancel trả `409`; giữ kết quả cuối, không gửi lại lệnh ghi.
5. Khi dựng MEP gặp lỗi thật trên fixture, cả line/MEP của lượt đó rollback. Ghi bước tái hiện lỗi;
   không chỉ kiểm một config bị từ chối trước transaction rồi gọi đó là đã kiểm rollback.
6. Sau một lượt hoàn tất, một Undo phục hồi line/MEP; Redo khôi phục đúng. Preview/hủy không có Undo thừa.
7. Clash: so hộp/solid với cặp giao thật, sát dưới/đúng/trên clearance, giao nông/sâu; có link xoay/dịch.
   Hủy lúc scan không tạo view/report; sau Seal vẫn hoàn tất xuất report. Nhãn hộp bao là ước lượng.
8. Với form AutoCAD/Revit: preview lỗi/partial không được ghi; đổi CSV/config/model sau preview yêu cầu preview mới;
   gửi lại cùng token không ghi hai lần. Undo TextReplace/AttributeImport phục hồi toàn bộ.

## Dữ liệu để khép các phần khác

| Phần | Đầu vào cần có | Bằng chứng cần ghi |
|---|---|---|
| BCF viewer | Viewer công ty + model Revit/IFC tương ứng | Topic, nhãn, ID trong file chủ/link, camera/snapshot đúng; [reader độc lập](../bcf-acceptance/README.md) chỉ là bước trước |
| Family | Family/type công ty và tiêu chuẩn kích thước/connector/load | Đổi kích thước làm geometry đổi đúng; đặt lại không nhân đôi; không dùng FamilyStarter thay family đã duyệt |
| Tọa độ | Hệ tọa độ/đơn vị, mốc đã duyệt, model và phép đo hiện trường | So một điểm bằng phương pháp độc lập và ký nhận; không suy rằng round-trip CSV đủ chứng minh tọa độ công trường |
| Thí điểm | Người dùng và tác vụ thật theo CSV đo | Số phút gồm cấu hình/sửa, lỗi/bỏ cuộc, phản hồi sau 2/4 tuần; không điền task mẫu thành dữ liệu thật |
| Ký số | Cert tổ chức có private key/timestamp và secret release | Chữ ký Valid trên máy sạch, đúng signer; cert tự cấp tại máy chưa chứng minh phát hành doanh nghiệp |

Thiếu đầu vào thì ghi `pending` kèm lý do. Bảng đo mẫu vẫn `no-data`; không nâng trạng thái hỗ trợ từ suite chưa chạy.
