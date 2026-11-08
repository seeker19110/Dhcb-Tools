# Nâng cấp AutoCAD và quy trình Windows — 2026-11-05

Mã nguồn nền `6991165`, nhánh `fix/project-readiness-20261105`; kết quả thuộc working tree đã sửa.
Hướng dẫn thao tác: [AutoCAD Windows](autocad-windows.md).

## Thay đổi

| Phần | Kết quả |
|---|---|
| Phát triển và CI | Toàn bộ workflow dùng Windows; thay hai script shell bằng `check-build.ps1` và `check-vulnerable.ps1`; bỏ nhánh build loại UI, dùng WPF đầy đủ. Core Console vẫn là project Windows riêng không WPF |
| Giao diện | `DHCB_TOOLS` tìm và mở 15 công cụ; nút Ribbon mở form phù hợp. Đầy đủ editor theo kiểu config thật, hộp chọn file, nhớ cấu hình, số thập phân tiếng Việt và tìm không dấu |
| Xem trước/ghi | Đổi cấu hình, document, revision hoặc nội dung file đầu vào làm preview mất hiệu lực; kết quả một phần/lỗi không được ghi. Quyền ghi bị thu hồi trước dispatch để chặn bấm lặp |
| TextReplace | Phạm vi Model/Paper/block definitions, lọc layer/tag/loại text; giữ định dạng MText/multiline attribute và field; giới hạn chi tiết xem trước và thời gian lập kế hoạch |
| Truy vấn | `textContains` tìm chữ hiển thị trước phân trang; có handle/Z/displayText; lọc lớp native trước khi mở entity. Query ghim documentId; document_context trả revision để client phát hiện thay đổi giữa các trang |
| Agent schema | Công bố đầy đủ tùy chọn config của 15 lệnh AutoCAD, gồm đánh số, dọn bản vẽ và tìm/thay; kiểm tra hai chiều với config Core để tránh tùy chọn có mã mà agent không khám phá được |
| MCP/panel | Lọc/phân trang tới 10000, job nền, progress/cancel/khôi phục job ID; giữ preview/config đã duyệt. Panel dừng phân trang khi DWG hoặc revision đổi; bỏ fallback thư mục cấu hình ngoài Windows |
| Kiểm chất lượng | Giữ cổng phủ 100%, kiểm PowerShell 5.1 thật, installer sandbox, BCF/IDS và cổng tổng hợp. Gitleaks dùng CLI Windows ghim SHA-256. Dependency audit xử lý requirements UTF-8 trên Windows |

## Bằng chứng Windows

| Kiểm tra | Kết quả |
|---|---|
| Ma trận build | 8 profile × 6 project = **48/48** thành công; mỗi lượt 0 lỗi, 0 cảnh báo |
| C# Shared.Logic/Hosting | **2242/2242 đạt; phủ 100% dòng**, gồm vòng đời native database và nhiều managed wrapper |
| BatchRunner CLI | **102/102** đạt |
| Python đầy đủ trên Windows | **519 đạt + 395 subtest**, không skip; kiểm tra transport bổ sung sau chỉnh cuối **270 đạt + 129 subtest** |
| Python coverage | **2822/2822 câu lệnh, 100%**; pyflakes và cú pháp JavaScript panel bằng Node đều đạt |
| WPF Windows | **27/27** kiểm tra: 15 form thật và các chốt preview, kiểu số, locale, tìm kiếm, lưu config, render |
| PowerShell 5.1 | Parser kiểm 52 script/bước workflow, không lỗi |
| Secret scan | Gitleaks Windows quét 203 commit, không phát hiện bí mật |
| Dependency audit | 8/8 profile NuGet và pip-audit không phát hiện lỗ hổng đã biết; mã thoát 0 |

Ma trận: 2022/net48, 2023/net48, 2024/net48, 2025/net8, 2025/net10, 2026/net8,
2026/net10 và 2027/net10. Mỗi profile build BatchRunner, Core Revit, Core AutoCAD, Revit WPF,
AutoCAD WPF và AutoCAD Core Console bằng Windows SDK 8/10.

## AutoCAD 2026 thật

| Bộ nghiệm thu | Đạt | Trượt |
|---|---:|---:|
| Smoke | 18 | 0 |
| Ghi layer và khôi phục | 12 | 0 |
| Engineering: attribute/text/layer/format | 24 | 0 |
| Engineering: layer khóa và ca biên | 51 | 0 |
| TextReplace nâng cấp | 12 | 0 |
| Query trên database 30001 entity | 20 | 0 |

Tổng cộng **137 ca AutoCAD thật** đạt. TextReplace kiểm scope, layer kế thừa/tag, multiline attribute/definition,
field, timeout và hủy toàn kế hoạch, giới hạn preview trên 5000 đối tượng. Query kiểm tìm text trước phân trang,
lọc native type, handle/Z, MText hiển thị và đầu vào không hợp lệ; truy vấn chậm nhất **72,53 ms** trên fixture này.
Đây là số đo fixture, không phải cam kết độ trễ cho mọi bản vẽ.

Runner engineering kiểm độc lập giá trị attribute, định dạng text và layer cuối; SHA-256 của DWG gốc và bản sao giữ
nguyên. Các ca ghi dùng bản sao hoặc database riêng trong RAM, không lưu bản vẽ sản xuất.

## Nơi lưu và giới hạn

- `out/windows-cad-upgrade-20261105/`: log ma trận, tổng hợp JSON, WPF `ui-final/ui.json` và `workspace.png`,
  kết quả smoke/write/engineering và `dependency-audit.log`.
- `TestResults/native-autocad-upgrade/`: `text.json` và `queries.json` của bộ nghiệm thu mới.
- `TestResults/windows-cad-final-success/`: C# TRX và coverage cuối.
- `TestResults/windows-cad-cli-final/`: CLI TRX.
- `TestResults/autocad-native-identity-fix/`: 2242 ca và coverage sau sửa nhận diện database.

Kiểm tra cài đè trên AutoCAD GUI phát hiện managed `Database` wrapper có thể thay đổi dù vẫn là cùng DWG.
Bridge, Ribbon và form nay dùng chung context theo native database pointer, đăng ký sự kiện một lần và loại
context khi database bị hủy. Hai helper dispatch chờ awaitable của AutoCAD hoàn tất trước khi trả kết quả HTTP.
Cả 8 profile AutoCAD WPF được build lại sau sửa này; chi tiết cài đặt và kiểm chứng nằm trong
[biên bản cài đè](cai-de-windows-2026-11-05.md).

WPF được kiểm bằng control thật trên Windows với recording context. Việc mở modal từ Ribbon và thao tác đầy đủ
trong AutoCAD GUI vẫn cần nghiệm thu trên phiên CAD; các lệnh database đã kiểm riêng trên host thật.
Máy này không có Revit.exe nên Revit mới được kiểm build WPF. Nghiệm thu các phiên bản host khác, model/family công ty,
BCF viewer, tọa độ hiện trường và chứng thư phát hành vẫn theo [bảng nghiệm thu](../tools/acceptance/remaining.md).

Các workflow đã được cập nhật và kiểm tại máy; chưa chạy GitHub Actions cho cây mã chưa commit.
