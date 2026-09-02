# AutoCAD Tools — MCP Server cho Hermes

Cung cấp 5 tools điều khiển AutoCAD từ Hermes Agent, kết nối qua HTTP Bridge `localhost:8766`.

## Cài đặt

### 1. Cài dependencies

```bash
pip install fastmcp
```

### 2. Đăng ký vào Hermes

```bash
hermes mcp add autocad-tools \
  --command python \
  --args "C:/Users/liend/AppData/Local/hermes/mcp-servers/autocad-tools/server.py"
# → Bấm Y để bật tất cả 5 tools
```

### 3. Load add-in vào AutoCAD

Mở AutoCAD → gõ lệnh `NETLOAD` → chọn file:
```
C:\Users\liend\Dhcb Tools\build_v2\DhcbTools.AutoCAD.dll
```

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
- *"đánh số block Dau Cat với prefix DC, dryRun trước"*
- *"dọn dẹp drawing, xem trước rồi mới xoá"*

---

## Cấu trúc file

```
mcp-servers/autocad-tools/
├── server.py      # MCP server (FastMCP), tự khởi động panel gateway
├── panel_api.py   # CORS proxy + Hermes AI thật tại localhost:8767
├── panel.html     # Bảng điều khiển HTML (widget chat)
└── README.md      # File này
```

## Luồng kết nối panel

```text
panel.html (file:// → tự chuyển hướng sang gateway)
    ↓ http://127.0.0.1:8767/panel (same-origin + token phiên)
panel_api.py
    ├── /health, /query, /execute → AutoCAD Bridge localhost:8766
    └── /ai/chat → Hermes CLI/model đang cấu hình → truy vấn AutoCAD có kiểm soát
```

`server.py` tự khởi động `panel_api.py` nếu port 8767 chưa chạy. Gateway
đọc `DHCB_BRIDGE_TOKEN` hoặc `%APPDATA%\DHCB\bridge-token.txt` để tương thích
Bridge có Bearer authentication. AI Chat dùng provider/model hiện hành của Hermes.
Các thao tác ghi/xóa không được AI Chat chạy trực tiếp — người dùng thực hiện
trong tab AutoNumber hoặc Cleanup, mặc định DryRun để an toàn.

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
| Cắt khối lượng | Tối đa 24.000 ký tự kết quả truy vấn vào prompt. |

**Muốn hoàn toàn offline:** trỏ Hermes vào model local (`hermes model` → Ollama).
Với bản vẽ thuộc diện bảo mật, dùng `DHCB_AI` trong AutoCAD thay cho tab AI Chat.

## Bridge endpoints (port 8766)

| Endpoint | Mô tả |
|----------|-------|
| `GET /health` | Status check |
| `POST /query` | Đọc dữ liệu bản vẽ |
| `POST /execute` | Thực thi lệnh |
