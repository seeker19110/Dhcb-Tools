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

## Bridge endpoints (port 8766)

| Endpoint | Mô tả |
|----------|-------|
| `GET /health` | Status check |
| `POST /query` | Đọc dữ liệu bản vẽ |
| `POST /execute` | Thực thi lệnh |
