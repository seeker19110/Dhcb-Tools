# AutoCAD MCP Server — Cài đặt nhanh

```bash
pip install fastmcp

hermes mcp add autocad-tools \
  --command python \
  --args "tools/autocad-mcp-server/server.py"
```

Mở AutoCAD → `NETLOAD` → chọn `build_v2\DhcbTools.AutoCAD.dll`

Mở session Hermes mới → gõ: **"mở bảng điều khiển autocad"**
