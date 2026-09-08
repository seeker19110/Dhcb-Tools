# Sửa CLI sau audit ngày 07/09/2026

Mốc trước sửa: `2df43787b3ccd66987c6b4a0c0bd4fb774d02d31`.

## Hành vi đã sửa

- `raw` giữ `documentId` từ payload qua cả đường đồng bộ và `--background`.
  Lỗi `E-DOCUMENT-CHANGED` được trả về, không tự thử lại với model mới.
- `raw` và `exec` mặc định xem trước. `--dry-run` luôn gửi `dryRun:true`,
  kể cả cấu hình chứa `dryRun:false`. Muốn ghi thật phải truyền `--no-dry-run`.
  Đây là thay đổi tương thích CLI: cập nhật script tự động trước đây chỉ đặt false trong JSON.
  API Python `request/send` và giao thức HTTP không đổi mặc định.
- Config file phải là JSON object trước khi hợp nhất với `--config`.
  Đầu vào sai trả exit code 2, không gửi HTTP. `raw` cũng từ chối config sai kiểu
  và `documentId` được cung cấp nhưng rỗng hoặc sai kiểu.

Giữ cùng ID từ `document_context` cho preview và lần ghi:

```text
python scripts/dhcb_agent.py revit query document_context
python scripts/dhcb_agent.py revit raw '{"command":"AutoNumbering","documentId":"ID_DA_LAY","config":{"category":"Doors","parameterName":"Mark"}}' --dry-run
python scripts/dhcb_agent.py revit raw '{"command":"AutoNumbering","documentId":"ID_DA_LAY","config":{"category":"Doors","parameterName":"Mark"}}' --no-dry-run
```

Chỉ chạy dòng ghi sau khi đã kiểm tra và duyệt kết quả preview. Nếu đổi model,
đọc lại context và bắt đầu lại từ preview.

## Kiểm chứng và phần còn lại

Test hồi quy chạy từ parser CLI qua serialization tới HTTP giả lập, kiểm cả Revit/AutoCAD,
đồng bộ/nền, phản hồi khác model, cờ CLI mâu thuẫn JSON và config file sai kiểu.
Đã chạy 121 test thuộc ba bộ `test_dhcb_*.py` trên môi trường sửa mã.
Toàn bộ Python: 313 test đạt, 51 subtest đạt, 5 test bỏ qua; coverage 100% (1.415 câu lệnh).
Pyflakes toàn bộ script/test Python và `git diff --check` đạt.
Kết quả CI đầy đủ tra tại PR/commit của thay đổi này.

Chưa nghiệm thu Revit/AutoCAD thật trong lượt này. Trước phát hành cần:

1. Preview trên model A, đổi sang B, ghi với ID A: phải bị từ chối và cả hai model không đổi.
2. Gửi nền với ID A rồi đổi tab trước dispatch: phải bị từ chối nếu document hiện hành khác A.
3. Dùng JSON false cùng `--dry-run`: model không đổi; kiểm lại với mặc định không có cờ.
4. Ghi với ID đúng và `--no-dry-run` trên model thử: kết quả đúng, Undo hoạt động.

Thay đổi này không bổ sung preview token phía server hoặc khóa chống gửi lặp bền vững;
không chứng minh an toàn khi model/config thay đổi giữa preview và commit trong cùng phiên.
Các mục đó tiếp tục theo kế hoạch trong `audit-nang-cap-2026-09-07.md`.
