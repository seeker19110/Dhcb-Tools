# Preview và ghi có chống lặp trên Bridge

Phạm vi: HTTP Bridge Revit/AutoCAD. Ribbon và batch gọi Core trực tiếp vẫn theo quy trình riêng.
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
