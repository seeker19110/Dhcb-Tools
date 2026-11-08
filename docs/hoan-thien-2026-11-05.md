# Hoàn thiện và kiểm chứng — 2026-11-05

> Quy trình hiện tại đã chuyển sang Windows. Xem [hướng dẫn AutoCAD Windows](autocad-windows.md) và [kết quả nâng cấp](nang-cap-autocad-windows-2026-11-05.md).

Rà soát trên mã nguồn nền `6991165`, nhánh `fix/project-readiness-20261105`.
Các kết quả dưới đây thuộc bản sửa trong working tree; không thay cho nghiệm thu các profile khác.

## Lỗi đã sửa

| Phần | Trước sửa | Sau sửa |
|---|---|---|
| BatchRunner | Truyền nhiều chế độ có thể chỉ kiểm log rồi trả thành công, bỏ qua yêu cầu khác; tham số IFC/IDS/dossier thiếu phụ thuộc cũng bị bỏ qua | Chỉ nhận một chế độ chính; từ chối tham số mâu thuẫn hoặc thiếu phụ thuộc bằng mã 2 và lý do. Thêm 10 ca hồi quy CLI |
| MCP AutoCAD | Gateway probe lỗi với JSON không phải object; phản hồi HTML có thể bị hiểu là cổng trống | Nhận diện dịch vụ khác đang chiếm cổng, báo lỗi rõ và không khởi chạy gateway thứ hai |
| Panel AutoCAD | Thiếu token vẫn gửi query/execute/progress, gây khóa xác thực Bridge sau nhiều lần thử | Báo thiếu token trước khi gửi request; `/health` vẫn kiểm được khi chưa có token |
| Build/checkout | Quy trình kiểm tra cũ cần chuẩn hóa xuống dòng | Đã được thay bằng quy trình Windows PowerShell trong lượt nâng cấp tiếp theo |
| Ký add-in bằng PFX | `Get-PfxCertificate -Password` không chạy trên Windows PowerShell 5.1 | Nhập PFX có mật khẩu qua `X509Certificate2`/`SecureString`, xử lý literal path và từ chối cert thiếu private key |

Mỗi lần gọi CLI chọn một trong `--job`, `--verify-log`, `--verify-ifc`, `--dossier`.
Muốn kiểm cả IFC theo quy tắc nội bộ và IDS thì chạy hai lượt riêng; `--dry-run` và `--report-only` cũng tách riêng.
Chi tiết ở [hướng dẫn BatchRunner](batch-runner.md#chạy).

## Kết quả kiểm tra

| Kiểm tra | Kết quả |
|---|---|
| C# Shared.Logic/Hosting, SDK 10.0.400 | 2.226/2.226 đạt; phủ 100% dòng |
| BatchRunner CLI | 102/102 đạt, gồm 10 ca hồi quy mới |
| Python ở lượt rà soát trước | 468 đạt, 41 bỏ qua, 187 subtest đạt; phủ 2.757/2.757 câu lệnh (100%); pyflakes sạch. Kết quả Windows hiện tại nằm trong báo cáo nâng cấp |
| Installer thật, Windows Python + Inno Setup 6 | 16/16 đạt, gồm 64 tổ hợp lựa chọn AutoCAD và kiểm nâng cấp/bỏ chọn |
| Nhập PFX trên Windows PowerShell 5.1 | Ca hồi quy đạt, gồm mật khẩu, đường dẫn có `[]` và cert thiếu private key; chỉ dùng file tạm, không ký DLL hoặc sửa kho tin cậy |
| PowerShell 5.1 | 14 script `.ps1` đọc được, không lỗi cú pháp |
| Công cụ đóng gói/audit ở lượt trước | Dependency audit 4/4, package scripts 8/8 đạt |
| Checkout Windows | BOM UTF-8 PowerShell được bảo toàn; hiện dùng CRLF theo quy ước Windows |
| Panel JavaScript | Kiểm cú pháp đạt, có đường chạy cleanup thật |
| Mục lục bằng chứng | `scripts/muc-luc-bang-chung.py --check` đạt |

Build Windows Release đầy đủ, mỗi cấu hình đều **0 lỗi / 0 cảnh báo**:

- AutoCAD 2026 UI: `AcadVersion=2026`, `AcadRuntime=net10`, có WPF.
- AutoCAD 2026 Core Console: cùng profile net10.
- Revit 2024: `RevitVersion=2024`, net48, có WPF.
- Revit 2027: `RevitVersion=2027`, net10, có WPF.

## Chạy AutoCAD thật

AutoCAD 2026 có runtime `net10.0`; BatchRunner chỉ định DLL vừa build trong repo.
Chạy từng suite trên bản sao riêng của `Data Extraction and Multileaders Sample.dwg`, `saveMode=None`:

| Suite | Đạt | Trượt | Bỏ qua |
|---|---:|---:|---:|
| `autocad-smoke` | 18 | 0 | 0 |
| `autocad-write` | 12 | 0 | 0 |

Suite ghi thật kiểm thay đổi một ô layer, chạy lại không còn thay đổi và khôi phục giá trị ban đầu.
SHA-256 bản mẫu trước/sau và cả hai bản sao sau chạy đều giữ nguyên:
`6a267ebb162c2a8524609f5b550ecb8c7055e92f20db4e17e270a7423a637d60`.

DLL Core Console dùng trong lượt này:
`1264f52f6f2440d2d833466c1189a933ed036746d2b96946191dbc8b0e8faf16`.

Bằng chứng cục bộ giữ tại `out/validation-20261105/`: `summary.json`, log build, `autocad-smoke/`
và `autocad-write/` chứa job, log runner, JSONL, báo cáo Markdown/HTML và TRX.
C# coverage/TRX ở `TestResults/completion-shared/` và `TestResults/completion-cli/`.
Các thư mục sinh này được gitignore; không phải artifact phát hành.

## Phần nghiệm thu còn mở

Build Revit đạt nhưng máy này không có `Revit.exe` để kiểm trong host.
Lượt này chưa kiểm đường gateway mới với AutoCAD GUI/Bridge đang chạy và chưa chạy toàn bộ ma trận CI 2022–2027.
Các phần cần model/family công ty, BCF viewer, đo tọa độ hiện trường, người dùng thí điểm và chứng thư
ký số tổ chức vẫn theo [bảng nghiệm thu](../tools/acceptance/remaining.md).
Không nâng trạng thái các mục đó từ kết quả unit test, build hoặc fixture.
