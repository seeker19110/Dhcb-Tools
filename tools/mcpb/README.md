# Gói `.mcpb` cho Claude Desktop

Giai đoạn 10.4. Cài DHCB Tools vào Claude Desktop bằng một cú mở file, thay vì sửa
`claude_desktop_config.json` bằng tay.

## Đóng gói

```powershell
.\scripts\pack-mcpb.ps1
```

Script chép `scripts/dhcb_mcp_server.py` + `scripts/dhcb_agent.py` vào thư mục dựng tạm cùng
`manifest.json`, rồi gọi `npx @anthropic-ai/mcpb pack`. Kết quả: `dist/dhcb-revit-<phiên bản>.mcpb`.

## Cài

Mở file `.mcpb` bằng Claude Desktop → *Install*. Không phải sửa file cấu hình nào.

Trường **Token Bridge** để trống là được: server tự đọc `%APPDATA%\DHCB\bridge-token.txt` do add-in sinh ra.
Chỉ điền khi Revit chạy trên máy khác và bạn nối qua SSH tunnel.

## Điều kiện chạy

| Cần | Vì sao |
|---|---|
| Revit đang mở | MCP server chỉ là client HTTP; mọi thứ chạy trong add-in |
| Add-in DHCB đã nạp | Bridge nghe ở `127.0.0.1:8765` |
| Python 3.9+ | Server không có dependency ngoài, chỉ dùng thư viện chuẩn |

Chưa mở Revit thì `tools/list` báo lỗi kết nối — đó là hành vi đúng, không phải hỏng gói.

## Vì sao không đóng gói kèm add-in

Add-in Revit là DLL .NET phải nằm trong thư mục add-in của Revit và chỉ nạp lúc Revit khởi động, nên nó đi
theo [installer riêng](../../installer/dhcb-tools.iss). Gói `.mcpb` chỉ là cầu nối Claude ↔ Bridge.

Thứ tự cài: installer trước (add-in + Bridge), rồi `.mcpb` sau.
