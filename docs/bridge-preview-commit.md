# Preview và ghi có chống lặp trên Bridge

Phạm vi: HTTP Bridge Revit/AutoCAD. Ribbon và batch gọi Core trực tiếp vẫn theo quy trình riêng.
Ribbon Revit và AutoCAD cũng yêu cầu preview hoàn tất trước khi ghi, kiểm lại revision model và nội dung file đầu vào.
Thay đổi nối tiếp PR #150. Cập nhật DLL cùng CLI/MCP/panel trước khi dùng đường ghi mới.

## Giao thức

1. Gửi `POST /execute` với tên lệnh và config, mặc định `dryRun:true`.
2. Preview thành công, không lỗi/một phần, trả `previewToken`, `documentId`, `previewExpiresUtc`.
3. Kỹ sư kiểm tra và duyệt kết quả. Client giữ nguyên config và hai trường token/ID;
   chỉ đổi `dryRun:false` để ghi. Server không tự coi token là bằng chứng con người đã duyệt.
4. Gửi lại cùng token/config/ID sẽ trả kết quả đã lưu; không chạy model lần nữa.

```json
{
  "command": "AutoNumbering",
  "documentId": "ID_TU_PREVIEW",
  "previewToken": "TOKEN_TU_PREVIEW",
  "config": {"category":"Doors","parameterName":"Mark","prefix":"D-","dryRun":false}
}
```

Token có hạn 10 phút trước lần ghi đầu, tối đa 256 preview còn hiệu lực mỗi Bridge.
Model đổi phiên, commit/Undo làm đổi revision, hoặc file đầu vào đổi nội dung đều yêu cầu preview mới.
Lệnh kiểm tra có tùy chọn tạo view (ConnectorChecker, ParameterRuleCheck, ClashDetection)
được phân loại có khả năng ghi và cũng đi qua cơ chế này.
Lệnh nội bộ như RunTests không được gọi qua HTTP công khai.

CLI:

```text
python scripts/dhcb_agent.py revit exec AutoNumbering --config '{"category":"Doors","parameterName":"Mark","prefix":"D-"}'
python scripts/dhcb_agent.py revit exec AutoNumbering --config '{"category":"Doors","parameterName":"Mark","prefix":"D-"}' --document-id ID_TU_PREVIEW --preview-token TOKEN_TU_PREVIEW --no-dry-run
```

Chỉ chạy dòng thứ hai sau khi duyệt. Với `raw`, hai trường nằm ở cấp ngoài JSON.
`--background` giữ token và trả kết quả qua `/progress/<id>` như trước.
MCP chung nhận `documentId/previewToken`; MCP AutoCAD nhận `document_id/preview_token`.
Panel giữ token trong trang của người dùng; chỉnh config sau preview sẽ bị server từ chối ghi.
Client không tự preview lại để vượt lỗi hết hạn/khác model.

## Job nền, timeout và hủy việc còn xếp hàng

```text
python scripts/dhcb_agent.py revit progress ID_JOB
python scripts/dhcb_agent.py revit cancel ID_JOB
```

`cancel` gửi `POST /cancel/<id>` với Bearer token và `Content-Type: application/json`.
Job chưa được nhận chuyển sang `abandoned`, không chạy về sau; hủy lại trả cùng trạng thái.
Job đã được nhận thường trả `409`; AutoRoute/ClashDetection có thể nhận hủy hợp tác (`202`) khi
`canCancel=true`, xem mục tiến độ bên dưới. Tiếp tục hỏi `progress`, không gửi lại lệnh để tránh ghi hai lần.
Job `done`/`error` giữ kết quả, còn ID không tồn tại hoặc đã hết thời hạn giữ trả `404`.
Dừng Bridge cũng hủy các việc chưa được nhận; việc đã nhận được phép kết thúc.
Hủy hàng đợi giữ hành vi cũ; hủy lệnh đã nhận chỉ được thực hiện tại điểm kiểm token do lệnh hỗ trợ.

Sau timeout `504`, xem `id` và trạng thái trước khi thao tác tiếp. Khi phản hồi không có bằng chứng
việc chưa chạy, client báo kết quả chưa xác định; không diễn giải timeout thành “chắc chắn không chạy”.
CLI, MCP và panel báo cảnh báo/lỗi cho `partialSuccess:true` hoặc `errors` không rỗng dù `success:true`.
Panel chỉ giữ token từ preview hoàn tất, không mở đường ghi từ preview một phần.

## Lưu chống lặp và phục hồi

Mỗi token đã nhận ghi có file claim tại `%LOCALAPPDATA%/DHCB/bridge-commits/<app>/`.
Server tạo độc quyền và flush claim xuống đĩa trước dispatch, sau đó lưu kết quả riêng.
Chỉ lưu dấu băm cấu hình và kết quả; không lưu toàn bộ nội dung file đầu vào.

- Mất phản hồi sau khi xong: gửi lại cùng payload/token để lấy kết quả.
- Tiến trình dừng sau claim nhưng trước kết quả: trả `E-COMMIT-UNKNOWN`, không tự chạy lại.
- Khởi động lại: kết quả đã lưu vẫn truy xuất bằng cùng token, kể cả model đã đóng.
  Preview chưa commit chỉ lưu trong RAM, phải preview lại sau restart.
- Không có giao dịch chung giữa filesystem và Autodesk. Cam kết là không tự dispatch lại một token
  đã có claim, không phải bảo đảm mọi lệnh đều hoàn thành đúng một lần.
- Không tự xoá claim. Sao lưu cùng trạng thái vận hành; xoá/khôi phục riêng ledger có thể làm mất
  bằng chứng chống lặp. Không dùng thư mục mạng chia sẻ làm nơi lưu.

## Phạm vi dấu vết đầu vào và giới hạn

Config được chuẩn hóa thứ tự khóa JSON; chỉ bỏ khác biệt dryRun. Trường đường dẫn đầu vào
trong config được băm SHA-256 nội dung; thư mục được xét đệ quy (giới hạn 4.096 đường dẫn).
Thư mục liên kết/junction bị từ chối. Trường output/report không tính là đầu vào.

Revision lấy từ DocumentChanged của Revit và các sự kiện sửa/thêm/xoá object của AutoCAD.
Cần nghiệm thu trên host thật, đặc biệt Undo/Redo, wrapper document, xref và model liên kết.
Không chụp mọi nguồn ngầm như từ điển toàn máy, template mặc định, model liên kết trong RAM.
Kiểm file diễn ra ngay trước dispatch, không khóa ứng dụng bên ngoài khỏi sửa file trong lúc lệnh chạy.
Vì vậy quy trình này chưa bảo đảm snapshot bất biến cho mọi nguồn phụ thuộc.

## Nghiệm thu trước phát hành

- Preview A → đổi tab B → ghi: từ chối, không model nào đổi.
- Preview → sửa/Undo model hoặc sửa CSV giữ cùng kích thước/mtime → ghi: từ chối.
- Gửi hai request ghi đồng thời cùng token: chỉ một lần thay đổi.
- Mất kết nối sau khi dispatch → gửi lại đúng token: nhận kết quả cũ, không nhân đôi.
- Dừng tiến trình giữa claim và result trên model thử: khởi động lại phải báo unknown.
- Kiểm cả sync/background, CLI/MCP/panel và từng phiên bản host công bố.

Test tự động kiểm guard độc lập với Autodesk, lưu/đọc ledger thật trong thư mục tạm và HTTP mô phỏng host.
Kết quả build/coverage xem CI của PR; không dùng CI thay bằng chứng nghiệm thu host.

## Tiến độ và hủy hợp tác

`GET /progress/<id>` có `progress:{stage,completed,total}`, `canCancel`, `cancellationRequested`.
`total:0` nghĩa là chưa biết tổng; không suy số phần trăm. Job chưa nhận vẫn hủy theo cơ chế cũ.
Với AutoRoute/ClashDetection đang ở giai đoạn hỗ trợ, `POST /cancel/<id>` trả 202 nghĩa là **đã nhận yêu cầu**,
chưa có nghĩa đã rollback. Phải chờ kết quả cuối ở progress; không gửi lại thao tác ghi.

AutoRoute kiểm token trong thu thập vật cản, raster hóa, A*, flood-fill và dựng model line.
Nếu hủy trước điểm đóng, transaction group rollback các thay đổi của lượt đó. Bước dựng MEP khóa quyền hủy
trước khi gọi RouteFromLines; nếu dựng không trọn vẹn, rollback cả line/MEP. Một nhóm Undo cho lượt AutoRoute.
ClashDetection kiểm token khi quét; khóa quyền hủy trước xuất báo cáo/tạo view để không công bố báo cáo bị bỏ dở.
Lệnh khác hoặc giai đoạn đã đóng vẫn trả 409 khi đang chạy. Không hủy cưỡng bức luồng UI/transaction của host.
Hành vi rollback Revit mới cần nghiệm thu bằng host thật trước khi công bố đã hỗ trợ vận hành.
