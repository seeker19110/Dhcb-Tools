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
├── server.py      # MCP server (FastMCP)
├── panel.html     # Bảng điều khiển HTML (widget chat)
└── README.md      # File này
```

## Bridge endpoints (port 8766)

| Endpoint | Mô tả |
|----------|-------|
| `GET /health` | Status check |
| `POST /query` | Đọc dữ liệu bản vẽ |
| `POST /execute` | Thực thi lệnh |
