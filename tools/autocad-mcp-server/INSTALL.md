# AutoCAD MCP Server — Cài đặt nhanh

Cần **Python 3.10+** (fastmcp 4.x) và Hermes CLI. `<repo>` = thư mục repo trên máy bạn.

```bash
pip install -r <repo>/tools/autocad-mcp-server/requirements.txt

hermes mcp add autocad-tools \
  --command python \
  --args "<repo>/tools/autocad-mcp-server/server.py"
```

Nạp plugin vào AutoCAD: cài bằng [installer](../../installer/dhcb-tools.iss) thì plugin **tự nạp**, không cần
`NETLOAD`. Bản build tay thì `NETLOAD` → chọn
`<repo>\src\DhcbTools.AutoCAD\bin\<Cấu hình>\<TFM>\DhcbTools.AutoCAD.dll`.

Mở session Hermes mới → gõ: **"mở bảng điều khiển autocad"** (lúc này server mới khởi động gateway panel ở
`127.0.0.1:8767`; trước đó nó không chiếm port nào).
