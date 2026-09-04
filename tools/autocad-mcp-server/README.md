# AutoCAD Tools — MCP Server cho Hermes

Cung cấp 5 tools điều khiển AutoCAD từ Hermes Agent, kết nối qua HTTP Bridge `localhost:8766`.

## Cài đặt

### 1. Cài dependencies

Cần **Python 3.10+** (fastmcp 4.x). `panel_api.py` và bộ test chỉ dùng thư viện chuẩn.

```bash
pip install -r <repo>/tools/autocad-mcp-server/requirements.txt
```

### 2. Đăng ký vào Hermes

```bash
hermes mcp add autocad-tools \
  --command python \
  --args "<repo>/tools/autocad-mcp-server/server.py"
# → Bấm Y để bật tất cả 5 tools
```

### 3. Nạp plugin vào AutoCAD

Cài bằng [installer](../../installer/dhcb-tools.iss) thì plugin tự nạp khi AutoCAD khởi động. Bản build tay:
`NETLOAD` → chọn `<repo>\src\DhcbTools.AutoCAD\bin\<Cấu hình>\<TFM>\DhcbTools.AutoCAD.dll`.

---

## Sử dụng (trong chat Hermes)

Sau khi mở session mới, agent có thêm 5 tools:

| Tool | Mô tả |
|------|-------|
| `autocad_health` | Kiểm tra bridge có sống không |
| `autocad_open_panel` | Mở bảng điều khiển ngay trong chat |
| `autocad_query` | Đọc thông tin bản vẽ (layers, stats, text...) |
| `autocad_execute` | Chạy lệnh (AutoNumbering, Cleanup...) |
| `autocad_export_layers` | Xuất layers ra CSV |

### Ví dụ prompt:

- *"mở bảng điều khiển autocad"*
- *"kiểm tra autocad có đang chạy không"*
- *"đọc danh sách layers của bản vẽ"*
- *"đánh số block Dau Cat với prefix DC, dryRun trước"* (chạy thật cần chuỗi xác nhận — xem bảng bên dưới)
- *"dọn dẹp drawing, xem trước rồi mới xoá"*

---

## Cấu trúc file

```text
tools/autocad-mcp-server/
├── server.py           # MCP server (FastMCP); khởi động panel gateway ở LẦN ĐẦU mở panel
├── panel_api.py        # CORS proxy + Hermes AI thật tại 127.0.0.1:8767
├── panel.html          # Bảng điều khiển HTML (widget chat)
├── requirements.txt    # fastmcp (ghim phiên bản)
├── test_panel_api.py   # test: python3 -m pytest tools/autocad-mcp-server -q
└── README.md           # File này
```

## Luồng kết nối panel

```text
panel.html (file:// → tự chuyển hướng sang gateway)
    ↓ http://127.0.0.1:8767/panel (same-origin + token phiên)
panel_api.py
    ├── /health, /query, /execute → AutoCAD Bridge localhost:8766
    └── /ai/chat → Hermes CLI/model đang cấu hình → truy vấn AutoCAD có kiểm soát
```

`server.py` khởi động `panel_api.py` **ở lần đầu gọi `autocad_open_panel`**, không phải lúc import — nạp MCP
server không còn chiếm port 8767. Nếu port đang bị **chương trình khác** chiếm (thăm dò `/alive` không nhận
đúng `{"panelApi":"ok"}`), server **không** spawn thêm mà báo lỗi; tiến trình do nó spawn được `atexit` tắt
theo. Gateway đọc `DHCB_BRIDGE_TOKEN` hoặc `%APPDATA%\DHCB\bridge-token.txt` để tương thích Bridge có Bearer
authentication. AI Chat dùng provider/model hiện hành của Hermes.

**Lệnh ghi cần xác nhận rõ ràng.** Cả panel lẫn tool `autocad_execute` đi qua cùng một bộ kiểm
(`panel_api.validate_proxy_payload`): `dryRun=false` chỉ được chấp nhận khi kèm đúng chuỗi xác nhận của lệnh —
`DrawingCleanup` → `DELETE_UNUSED`, `AutoNumbering` → `WRITE_AUTONUMBER`, `LayerImport` → `IMPORT_LAYERS`.
Thiếu hoặc sai chuỗi thì lệnh bị từ chối kèm hướng dẫn, không có đường nào chạy thật mà không xác nhận.
Đường dẫn CSV bắt buộc nằm trong thư mục tạm; tên file trần được ghim vào đó.

## Vì sao token nằm trong HTML

`GET /panel` nhúng token phiên vào trang thay vì phát qua một endpoint riêng: bất kỳ thứ gì đọc được `/panel`
thì cũng đọc được endpoint đó, nên tách ra không thêm an toàn. Cái thật sự bảo vệ token là ba lớp khác —
gateway chỉ bind `127.0.0.1`; **header `Host` phải là `127.0.0.1:8767` hoặc `localhost:8767`**, sai thì trả
`421` (chặn DNS rebinding: trang web ngoài trỏ tên miền của nó về loopback, request điều hướng không có
`Origin` nên chỉ `Host` chặn được); và mọi XHR còn phải qua whitelist `Origin` + header `X-Panel-Token`.

## Dữ liệu đi đâu

⚠️ **Panel AI Chat KHÔNG offline.** Khác với lớp AI trong add-in Revit/AutoCAD
(heuristic + Ollama local, không có gì rời máy), tab AI Chat của panel gọi
`hermes -z`, nên **prompt — kèm nội dung đọc từ bản vẽ đang mở — được gửi tới
provider inference mà Hermes đang cấu hình**. Nếu provider đó là dịch vụ đám mây
thì dữ liệu bản vẽ rời khỏi máy.

Những gì đã siết để giảm rủi ro:

| Biện pháp | Chi tiết |
|---|---|
| Không toolset | Gọi với `-t ""` (`HERMES_TOOLSETS`) — model không duyệt web, không chạy lệnh, không đọc file. Nó chỉ trả lời từ prompt. |
| Không nạp ngữ cảnh riêng tư | `--ignore-rules` chặn AGENTS.md/SOUL.md/memory của người dùng lọt vào prompt chứa dữ liệu bản vẽ. |
| Chống prompt injection | Nội dung DWG được bọc trong khối `<du_lieu>` kèm chỉ thị coi đó là dữ liệu, không phải mệnh lệnh — text/attribute trong bản vẽ nhận từ bên ngoài không điều khiển được model. |
| Chỉ đọc | AI Chat không chạy được lệnh ghi/xóa; whitelist `ALLOWED_QUERIES` chặn ở gateway, không tin vào model. |
| Cắt khối lượng | Trần **24.000 ký tự cho cả prompt** — đo *sau* khi ghép header + lịch sử + dữ liệu, nên lịch sử dài không đẩy tổng vượt trần. |
| Không lộ qua argv | Prompt đi qua **stdin**, không phải dòng lệnh: `ps`/Task Manager đọc được argv của mọi tiến trình cùng user. |

**Muốn hoàn toàn offline:** trỏ Hermes vào model local (`hermes model` → Ollama).
Với bản vẽ thuộc diện bảo mật, dùng `DHCB_AI` trong AutoCAD thay cho tab AI Chat.

## Bridge endpoints (port 8766)

| Endpoint | Mô tả |
|----------|-------|
| `GET /health` | Status check |
| `POST /query` | Đọc dữ liệu bản vẽ |
| `POST /execute` | Thực thi lệnh |
