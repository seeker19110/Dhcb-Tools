# Agent khép vòng cho Revit 2021–2026

Giai đoạn 10 của [`roadmap.md`](roadmap.md) — hướng khác biệt lớn nhất của dự án.

## Vì sao

| | Revit 2027 MCP Server (Autodesk) | revit-mcp mã mở | DHCB |
|---|---|---|---|
| Phiên bản Revit | Chỉ 2027 | 2023–2027 | **2021–2026** |
| Ghi vào mô hình | Không (chỉ đọc) | Có, không có rào | Có, **`dryRun` mặc định + xác nhận** |
| Xác thực | Tài khoản Autodesk | Không | Token + khoá khi dò |
| Chạy đêm không người | Không | Không | Batch runner |
| AutoCAD song hành | Không | Không | Có |
| Tiếng Việt | Không | Không | Có |

Phần lớn văn phòng Việt Nam còn ở Revit 2022–2024 nhiều năm nữa, nên "chỉ 2027" là một khoảng trống thật.

Cái DHCB thiếu trước giai đoạn 10 không phải là *số lượng tool* mà là **khép vòng**: agent đọc được số đếm nhưng
không nhìn thấy gì, không chỉ được phần tử nào cho kỹ sư, và không tự kiểm được việc mình vừa làm.

## Vòng khép kín

```
parameters_of  →  dựng config đúng, không đoán tên tham số
      ↓
execute (dryRun) →  xem trước, đọc Summary + Messages
      ↓
execute (confirm) →  ChangedIds: đúng những phần tử vừa đổi
      ↓
element_geometry / show_elements  →  kiểm lại, hoặc chỉ cho kỹ sư nhìn
      ↓
snapshot  →  ảnh PNG để agent tự nhìn kết quả
```

## Truy vấn mới (10.1)

Gọi qua `POST /query` hoặc tool `query` của MCP.

| Query | Params | Trả về |
|---|---|---|
| `parameters_of` | `categories`, `writableOnly` | Tên tham số, `storageType`, đơn vị, chỉ đọc, giá trị mẫu |
| `element_geometry` | `elementIds` hoặc `categories`, `limit` | Hộp bao, đường tâm, **connector kèm tình trạng nối**, host, level — toạ độ mm |
| `schedule_rows` | `scheduleName`, `limit` | Bảng thống kê dạng hàng; bỏ trống tên thì liệt kê schedule đang có |
| `snapshot` | `viewName`, `imageWidth` | PNG base64 của view, kèm đường dẫn file |
| `selection` | `elementIds` (tuỳ chọn) | Phần tử đang chọn; có `elementIds` thì **đặt** lựa chọn |
| `show_elements` | `elementIds` | Zoom tới phần tử và chọn — kỹ sư nhìn thấy ngay |
| `active_view` | — | Kỹ sư đang nhìn view nào, tỉ lệ, mức chi tiết, đang chọn mấy phần tử |

Ba query cuối cần `UIDocument` nên nằm ở vỏ Revit (`UiQueryHandler`); Core cố ý không tham chiếu RevitAPIUI.

**Toạ độ và chiều dài trả ra đều là mm.** Agent không phải biết Revit dùng feet bên trong.

## `ChangedIds` (10.2)

Mọi `CommandResult` nay mang theo `ChangedIds` — ElementId của phần tử vừa tạo/sửa. Chỉ có số đếm thì agent biết
*"đã đổi 37 phần tử"* mà không chỉ ra được phần tử nào.

Đã gắn cho: `SleeveAuto`, `HangerAuto`, `AutoNumbering`, `ElevationTag`, `SheetRename`.

Giới hạn 500 id một lượt để lệnh sửa cả vạn phần tử không làm phình response; `AffectedCount` vẫn là con số đầy đủ.

## Ví dụ một vòng hoàn chỉnh

Đánh số cửa tầng 3, rồi tự kiểm:

```bash
# 1. Tham số nào ghi được trên Doors?
python scripts/dhcb_agent.py revit query parameters_of \
  --params '{"categories":["Doors"],"writableOnly":true}'

# 2. Xem trước
python scripts/dhcb_agent.py revit exec AutoNumbering \
  --config '{"category":"Doors","parameterName":"Mark","prefix":"D3-","digits":3}'

# 3. Chạy thật (agent phải truyền confirm qua MCP) — kết quả có changedIds

# 4. Kiểm lại đúng những phần tử vừa đổi
python scripts/dhcb_agent.py revit query element_geometry \
  --params '{"elementIds":[123456,123457]}'

# 5. Chỉ cho kỹ sư xem
python scripts/dhcb_agent.py revit query show_elements \
  --params '{"elementIds":[123456,123457]}'

# 6. Nhìn kết quả
python scripts/dhcb_agent.py revit query snapshot --params '{"imageWidth":1600}'
```

## An toàn giữ nguyên

Không có gì trong giai đoạn 10 nới lỏng các rào đã có:

- `POST /execute` vẫn ép `dryRun:true` trừ khi có `confirm:true`.
- Bridge vẫn bind 127.0.0.1, vẫn cần token, vẫn khoá 5 phút sau 5 lần sai.
- `snapshot` chỉ đọc và ghi ảnh vào thư mục tạm; không đụng mô hình.
- `selection`/`show_elements` chỉ đổi lựa chọn trên màn hình, không sửa mô hình, không mở transaction.

## Playbook nghiệp vụ

Thứ 138 tool rời rạc không có: **trình tự làm việc**. Trong [`skills/`](../skills/):

| Playbook | Dùng khi |
|---|---|
| `kiem-model-truoc-sync` | "kiểm model", "trước khi sync", "model có sạch không" |
| `danh-so-hang-loat` | "đánh số cửa", "đánh lại Mark", "numbering" |
| `xu-ly-nhom-canh-bao` | "nhiều warning quá", "dọn warning" |

Mỗi playbook có mục **Không được làm** — phần quan trọng không kém phần hướng dẫn, vì nó chặn những việc
trông có vẻ hữu ích mà thực ra làm hỏng mô hình (xoá phần tử cho hết cảnh báo, chạy thật khi chưa xem trước).

## Cài vào Claude Desktop

```powershell
.\scripts\pack-mcpb.ps1
```

Ra `dist/dhcb-revit-<phiên bản>.mcpb`; mở bằng Claude Desktop là xong, không phải sửa file cấu hình.
Cài add-in trước ([installer](../installer/dhcb-tools.iss)) để có Bridge mà nối. Chi tiết:
[`tools/mcpb/README.md`](../tools/mcpb/README.md).

## Lệnh chạy lâu

Mặc định Bridge chờ 30 giây — đủ cho lệnh đọc, không đủ cho `SleeveAuto`/`AutoRoute`/`ClashDetection` trên
model thật. Gửi kèm `timeoutSeconds`:

```json
{ "command": "SleeveAuto", "config": { ... }, "timeoutSeconds": 300 }
```

Server chặn trên ở 10 phút: Revit chỉ có một luồng nên không thể để một request giữ hàng đợi vô hạn.

## Còn lại của giai đoạn 10

- Phần AutoCAD tương ứng của các query mới.
- `/progress/<id>` để theo dõi lệnh chạy lâu thay vì chỉ ngồi chờ.
- Hai playbook còn lại: dựng grid/level từ CAD, xuất bộ PDF theo revision.
