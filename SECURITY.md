# Bảo mật

## Báo lỗ hổng

Đừng mở issue công khai kèm chi tiết khai thác. Dùng **Security → Report a vulnerability** trên GitHub
(private vulnerability reporting). Nếu repo chưa bật nút đó, mở một issue chỉ ghi "cần kênh riêng để báo
lỗ hổng" — không kèm chi tiết — và chờ phản hồi.

Nên ghi kèm: phiên bản (tag hoặc commit), Revit/AutoCAD nào, bước tái hiện, và bạn đã thấy gì.

## Mô hình an toàn — cái gì được bảo vệ, cái gì không

| Lớp | Bảo vệ | Không bảo vệ |
|---|---|---|
| HTTP Bridge (`127.0.0.1:8765` Revit, `:8766` AutoCAD) | Chỉ bind IPv4 loopback; token 256 bit trong `%APPDATA%\DHCB\bridge-token.txt` (ACL thu về chủ sở hữu); so token thời gian hằng số; sai 5 lần/60 s → khoá 5 phút; request từ trang web (header `Origin`/`Sec-Fetch-Site`) → `403` và không tính vào khoá; body ≤ 4 MB; ≤ 8 request đồng thời | Mã chạy dưới **cùng tài khoản Windows** đọc được file token như chính add-in — giới hạn cố hữu, xem `BridgeAuth` |
| Lệnh ghi | `dryRun` mặc định bật; ghi thật qua Bridge cần `previewToken` + `documentId` của lần xem trước (`BridgeCommitGuard`); một transaction mỗi lệnh | Người đã có token vẫn tự xem trước rồi ghi được — token là ranh giới tin cậy |
| File lệnh đọc/ghi qua Bridge | Mọi trường đường dẫn file phải mang đuôi thuộc định dạng DHCB dùng (`.csv .html .json .pdf .dwg .ifc .rvt`…), không `:` ngoài ổ đĩa → `E-PATH-UNSAFE` (`BridgePathPolicy`) — agent bị dắt không ghi được `.bat`/`.ps1`/`.lnk` vào Startup | Không chốt thư mục: ghi đè một file `.csv`/`.html` bất kỳ mà tài khoản đó có quyền vẫn được |
| Tắt hẳn Bridge | `%APPDATA%\DHCB\settings.json` → `{"bridge": {"enabled": false}}`: không mở cổng 8765/8766 nào | Mặc định vẫn bật (agent/MCP/panel cần) |
| Client Python (`scripts/`, `tools/autocad-mcp-server/`) | Gọi Bridge/Ollama qua opener **không dùng proxy** (`LOOPBACK`) — token không lọt ra proxy hệ thống/công ty; gọi `127.0.0.1`, không `localhost` | — |
| Panel web AutoCAD (`:8767`) | Kiểm `Host` (chống DNS rebinding), whitelist `Origin`, token phiên, whitelist lệnh/truy vấn, chuỗi xác nhận cho lệnh ghi | **AI Chat gửi nội dung bản vẽ tới provider mà Hermes cấu hình** — xem `tools/autocad-mcp-server/README.md` |
| Lớp AI trong add-in | Endpoint model bắt buộc loopback; mặc định chỉ heuristic | — |
| Regex do người dùng/agent nhập | Có trần thời gian (IDS pattern, TextReplace, LayerRule, find/replace của SheetRename/FamilyAudit) — mẫu backtrack quá mức báo lỗi thay vì treo Revit/AutoCAD | — |
| File sinh ra | CSV chặn công thức Excel; HTML escape; script AutoCAD bỏ nháy/xuống dòng khỏi đường dẫn | — |

Không sửa Bridge để bind `0.0.0.0`; agent ở máy khác dùng SSH tunnel.

## Kiểm tra tự động

| Workflow | Làm gì |
|---|---|
| `secret-scan.yml` | gitleaks trên mọi PR và push `main` |
| `dependency-audit.yml` | `scripts/check-vulnerable.sh`: NuGet (`dotnet list package --vulnerable`, ba TFM) + `pip-audit`; khi PR đổi dependency, trên `main`, và hằng tuần |
| `tests.yml` | test + cổng phủ 100 % (C# dòng, Python câu lệnh), pyflakes, build năm phiên bản, bộ ca IDS chính thức của buildingSMART (đỏ khi hồi quy) |
| `.github/dependabot.yml` | PR cập nhật GitHub Actions và pip hằng tháng |

Release: `scripts/sign-release.ps1` ký Authenticode các file `DhcbTools*` khi repo có secret chứng chỉ; chưa có thì gói
phát hành **chưa ký**.

Mọi action ghim theo **commit SHA**; mọi workflow mặc định `contents: read`, chỉ job `publish` của
`release.yml` có quyền ghi; `actions/checkout` không lưu lại `GITHUB_TOKEN` (`persist-credentials: false`).

## Việc còn phụ thuộc chủ repo

Ghi ở [`docs/audit-bao-mat-2026-09-28.md`](docs/audit-bao-mat-2026-09-28.md#việc-cần-chủ-repo-bật):
branch protection cho `main`, private vulnerability reporting, Dependency graph, chứng chỉ ký mã.
