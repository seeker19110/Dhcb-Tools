# Kế hoạch nâng chất lượng DHCB Tools — 2026-10-07

Mục tiêu: kỹ sư cài được, biết cấu hình, xem trước đáng tin, chạy không mất dữ liệu và nhận kết quả trung thực.
Mốc bắt đầu: `af617fe`; nhánh triển khai: `feat/reliability-and-adoption`. Không tăng số lệnh Core chỉ để tăng bề mặt.

## Thứ tự thực hiện và tiêu chí nghiệm thu

| Ưu tiên | Công việc | Điều kiện hoàn thành | Trạng thái |
|---|---|---|---|
| P0 | Đồng nhất kết quả thành công/một phần/lỗi giữa Ribbon, Bridge, batch | Xem trước có lỗi không mở chạy thật; batch không lưu mặc định sau bước một phần; log, báo cáo và mã thoát nhất quán | Đã sửa và test; UI host còn nghiệm thu tay |
| P0 | Sửa tranh chấp hàng đợi Bridge và quyền nhận việc | Request đồng thời không vượt trần; một work item chỉ được claim một lần; kết quả cuối không bị ghi đè | Đã sửa và test đồng thời |
| P1 | Kiểm soát job còn xếp hàng | Có đường hủy job chưa chạy, giữ kết quả đã chạy; nghiệm thu HTTP thật và luồng UI host | Đã có HTTP/CLI và test; còn luồng UI host |
| P1 | Chẩn đoán cài đặt bằng công cụ chỉ đọc | Báo Python, cấu hình, token, Bridge và danh mục; không in bí mật, không thực thi lệnh/model; đi kèm gói phát hành | Đã thêm doctor, test và manifest gói |
| P1 | Khép bộ kiểm thử trong AutoCAD và ma trận build | Test logic/CLI/Python + coverage + pyflakes; build API net48/net8/net10 và WPF; chạy bộ host trên bản sao khi host có sẵn | Đạt trên máy này; Revit host còn chờ |
| P1 | Đồng bộ tài liệu hiện trạng | Loại bỏ mục lỗi đã sửa, sửa hướng dẫn Bridge; bằng chứng mới có số liệu và giới hạn rõ | Đã cập nhật |
| P2 | Thí điểm sử dụng thật | Có gói hướng dẫn và bảng đo cho 5–10 lệnh, có người sử dụng thật và phản hồi sau 2/4 tuần | Chuẩn bị được; cần người dùng |
| P2 | Hủy hợp tác lệnh dài, đa phương án tuyến, phân loại clash | Transaction rollback được, kết quả/chống lặp đúng khi hủy; fixture và host test trước khi công bố hỗ trợ | Cần đánh giá và nghiệm thu riêng |
| P2 | Family sản xuất, tọa độ ngoài công trường và khả năng tương tác BCF | Family đạt yêu cầu công ty; đo đối chiếu ngoài công trường; mở BCF bằng ứng dụng nhận | Cần dữ liệu và người nghiệm thu |
| P2 | Quyền sử dụng và phát hành doanh nghiệp | Chủ dự án chọn LICENSE, cấp chứng chỉ ký; kiểm cấu hình bảo vệ nhánh và bảo mật repo | Cần quyết định/tài nguyên của chủ dự án |

## Nguyên tắc triển khai

- Sửa trên nhánh riêng, giữ tương thích cấu hình; thay đổi có ảnh hưởng lưu file được nêu rõ trong tài liệu.
- Lỗi thực tế có test hồi quy ở tầng phù hợp; coverage không thay thế test trong Revit/AutoCAD.
- Không ghi đè model/bản vẽ của người dùng để nghiệm thu; host test dùng bản sao và thư mục kết quả riêng.
- Giữ Bridge ở loopback, token bảo mật và preview/commit chống lặp. Không hủy cưỡng bức transaction đang chạy.
- Không chọn giấy phép, mua chứng chỉ hoặc thu phản hồi giả thay chủ dự án/người dùng.

## Đo thành công

1. Cài đặt và chạy ba tác vụ đầu tiên có kết quả trong 15 phút.
2. Mỗi tác vụ có số phút làm tay, số phút dùng tool, số lần lỗi và số lần phải sửa kết quả.
3. Job không trọn vẹn có mã thoát khác 0 và mặc định không lưu thay model gốc.
4. Các trường hợp đồng thời, timeout và gửi lại không tạo thao tác ghi lặp.
5. Chỉ nâng nhãn hỗ trợ sau khi có bằng chứng host và người dùng thật.

## Đã triển khai

- `IsComplete` thống nhất điều kiện hoàn tất: `success:true`, không một phần, không có `errors`; log còn yêu cầu không bị bỏ qua. Batch không lưu mặc định, không bật bàn giao đạt sau bước chưa hoàn tất. JSON hiện có giữ nguyên cấu trúc.
- Ba wrapper Revit cũ dùng form cấu hình chung. Ribbon Revit/AutoCAD chặn preview chưa hoàn tất, băm nội dung đầu vào và kiểm revision trước khi ghi. Revit kiểm cả thay đổi xảy ra trong lúc preview; thư mục đầu vào áp dụng giới hạn 4.096 file và từ chối junction như Bridge.
- Claim work item chỉ thắng một lần; kiểm trần/thêm job là thao tác nguyên tử. Kết quả cuối của job không bị callback đến muộn ghi đè. Hủy job nền còn xếp hàng qua HTTP/CLI; Stop hủy job nền chưa nhận, giữ job đang chạy.
- CLI phân biệt `429` hàng đợi đầy với khóa xác thực; timeout thiếu bằng chứng không bị báo “chưa chạy”. Phản hồi JSON lỗi được xử lý. Client không theo redirect để tránh gửi token/nội dung yêu cầu sang đích khác. MCP/panel báo đúng kết quả một phần.
- `dhcb_doctor.py` kiểm chỉ đọc Python, cấu hình, token, health và danh mục; có chế độ offline/JSON, không in bí mật. Manifest đóng gói BatchRunner có thêm script này (tổng 7 script); không thay dependency.
- Sửa `LayerImport` preview từng tạm sửa layer rồi abort, gây tăng revision. Bộ host mới đo sự kiện sửa/thêm/xóa, kiểm dòng trùng/xung đột và tính không lặp khi chạy lại.
- Có [hướng dẫn thí điểm](thi-diem-su-dung.md), [CSV đo hiệu quả](mau-do-hieu-qua.csv), hướng dẫn timeout/hủy và chẩn đoán. Bỏ nhãn MIT trong manifest MCPB vì toàn repo chưa có LICENSE do chủ dự án chọn.

## Bằng chứng kiểm chứng tại máy — 2026-10-07

| Kiểm tra | Kết quả |
|---|---|
| Test C# Shared.Logic/Hosting | 2.037 đạt; cổng phủ dòng 100% |
| Test CLI BatchRunner | 27 đạt |
| Test Python và helper panel Node | 384 đạt, 115 subtest đạt, 5 ca installer Windows bị bỏ qua khi chạy từ WSL |
| Phủ Python | 100% câu lệnh, 1.720 câu lệnh; pyflakes sạch |
| Ma trận API | 25 tổ hợp dự án/phiên bản đạt; net48/net8/net10; Revit 2023–2027, AutoCAD 2024–2026 |
| UI Windows | Revit WPF 2023–2027 và AutoCAD UI 2024/2026 biên dịch đạt; Revit API/WPF được kiểm lại sau lần sửa snapshot cuối |
| AutoCAD 2026 Core Console | [18/18 smoke](bang-chung/2026-10-07/autocad-smoke.md) và [12/12 ghi thật](bang-chung/2026-10-07/autocad-write.md) đạt trên bản sao DWG mẫu; `saveMode:None` |
| Installer Windows | Chạy riêng 5/5 test đạt; phủ 8 tổ hợp AutoCAD, nâng cấp/bỏ chọn, giữ file khác, runtime không tương thích và script thiếu; mọi đích ghi ở thư mục tạm |
| Cú pháp và dữ liệu | JavaScript panel hợp lệ; CSV thí điểm 15 cột/10 lệnh hợp lệ; `git diff --check` sạch |

Môi trường: Windows với .NET SDK 10.0.400, AutoCAD 2026; Python test chạy trong venv WSL.
Kết quả `.trx`/coverage và file host nằm ngoài repo tại `C:\Users\liend\DHCB-test-results\quality-20261007-*`.
Hai báo cáo Markdown host được lưu trong repo theo các liên kết trên. Log build/Python nằm ở `/tmp/dhcb-quality-checks/`.

Lệnh lặp lại chính:

```powershell
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory coverage
python scripts/check-coverage.py coverage
dotnet test tests/DhcbTools.BatchRunner.Tests/DhcbTools.BatchRunner.Tests.csproj -c Release
python -m coverage run -m pytest -q
python -m coverage report
python -m pyflakes scripts/*.py tools/autocad-mcp-server/*.py tests/python/*.py
.\scripts\run-in-autocad-tests.ps1 -AcadVersion 2026 -Suite smoke -AllowWrites
.\scripts\run-in-autocad-tests.ps1 -AcadVersion 2026 -Suite write -AllowWrites
```

Máy này có `PATHEXT` thiếu `.EXE`; chỉ bổ sung trong tiến trình PowerShell nghiệm thu để gọi `dotnet`, không sửa cấu hình hệ thống.
Kết quả trên là kiểm tại máy, không phải thông báo CI của PR hoặc một bản phát hành mới đã đạt.

## Phần còn cần dữ liệu/host/người quyết định

Không tìm thấy Revit host tại môi trường đang kiểm; compile và logic test không xác nhận transaction/Undo trong Revit.
AutoCAD Core Console không chứng minh thao tác Ribbon hoặc form GUI. Cần kiểm bằng tay các đường preview → duyệt → ghi,
đổi model/CSV giữa hai bước, hủy job nền khi host đang giữ hộp thoại, Undo/Redo, và các phiên bản host công bố.
Chưa nâng nhãn Revit 2027 từ biên dịch sang hỗ trợ phát hành.

Năm ca installer bị skip trong lượt Python WSL đã được chạy riêng và đạt trên Windows; không đăng ký uninstall hoặc thay bản cài đang dùng. Script doctor đã được kiểm vào manifest đóng gói.
Chưa thu phản hồi người dùng, chưa đo tiết kiệm thời gian thực tế; chưa nghiệm thu family sản xuất/tọa độ công trường/BCF bằng phần mềm nhận.
Hủy giữa lệnh dài cần thiết kế cancellation/rollback riêng. LICENSE và chứng chỉ ký cần chủ dự án quyết định/tài nguyên; trạng thái bảo vệ nhánh cần đối chiếu GitHub.

## Rà soát bổ sung trước triển khai

Rà soát độc lập phát hiện cửa sổ giữa đăng ký job và gắn hook hủy: Stop có thể đánh dấu job
`abandoned` trong khi work item vẫn được nhận chạy. Hook hủy nay được gắn trước khi công bố job;
request giữ token phiên đã nhận để phiên Start mới không hồi sinh việc của phiên cũ.
Hai ca hồi quy bổ sung nâng bộ test logic lên **2.039/2.039 đạt, phủ dòng 100%**.
Kết quả nằm ở `C:\Users\liend\DHCB-test-results\review-race`.

Ribbon AutoCAD nay đăng ký theo dõi sự kiện database trước khi chụp snapshot, dùng chung watcher
với Bridge. Kiểm revision không còn phụ thuộc việc Bridge đã được gọi hay chưa/bật hay tắt.
Thư viện đầu vào Revit lớn hơn 4.096 file cần chia thư mục cho tác vụ preview hiện tại.

Chủ dự án đã chọn merge vào main và cài trên máy này. Phạm vi cài là AutoCAD 2026 (.NET 10)
và BatchRunner/scripts. Cần giữ các Revit manifest hiện có; installer chọn riêng AutoCAD sẽ
dọn thành phần Revit bị bỏ chọn, nên lượt cài tại máy dùng chép gói có backup/rollback.
Không đổi định nghĩa task đêm, cấu hình, token hoặc ledger chống lặp; gói và báo cáo cài đặt
đặt ngoài repo tại `C:\Users\liend\DHCB-deploy`.


## Kiểm vận hành bổ sung — 2026-10-08

Nhánh `feat/operational-readiness` khép các lỗi phát hiện khi chạy bản đã cài:

- BatchRunner chọn cặp host/DLL tương thích trong AutoCAD 2024–2026, đọc metadata DLL mà không nạp mã.
  Host 2022 bị bỏ qua; bản 2026 dùng runtime cũ bị chặn. DLL đã cài trong bundle được tìm tự động.
  Đường host tùy chọn không được suy năm chỉ từ runtime; cần DLL portable/chỉ định và cảnh báo rõ.
- `PlotPdf --dry-run` không chạy `-PLOT`. PDF thật được tạo ở file tạm cùng thư mục và chỉ thay đích
  khi có header PDF, host thoát thành công và bước CAD hoàn tất. File cũ được giữ khi lỗi;
  lỗi in PDF chặn lưu DWG mặc định. Lỗi khởi động Core Console được ghi vào báo cáo/mã thoát.
- Doctor bổ sung kiểm manifest/module/bộ DLL và runtime Windows, có `--acad-dir` cho AutoCAD 2026
  cài nơi khác; vẫn chỉ đọc và không in token/nội dung cấu hình. [Hướng dẫn](chan-doan-windows.md).
- Bộ [engineering write](../tests/suites/autocad-engineering-write.json) dùng
  [fixture AutoLISP](../tests/suites/fixtures/autocad-engineering.lsp) trên bản chép sample Autodesk.
  [Runner](../scripts/run-autocad-fixture-tests.ps1) đối chiếu CSV độc lập với kết quả lệnh:
  đánh số theo vị trí, pattern thuộc tính, CSV có dấu phẩy/ngoặc kép, khôi phục giá trị, định dạng MText.
  Kiểm hash DWG gốc và bản chép để chắc chắn không lưu các sửa đổi thử vào file.

Năm tình huống PDF đã chạy trên AutoCAD 2026 thật: xem trước, xuất thành công, khổ giấy sai,
step CAD không tồn tại, và khổ giấy sai khi `saveMode:SaveAs`. Mã thoát đúng 0/1;
PDF cũ và DWG đích giữ nguyên trong ca lỗi; không còn PDF tạm sau lượt chạy.
Chỉ kiểm header PDF và kết quả tạo file, chưa nghiệm thu nội dung bản in.

Công cụ điều khiển GUI Windows của phiên này lỗi trước khi chạy thao tác vì không nhận cwd WSL.
Không ghi nhận Ribbon/form/Bridge GUI là đã nghiệm thu. Máy không có Revit.exe để chạy host Revit;
ma trận biên dịch không thay thế nghiệm thu trong host.

Ruleset GitHub `main` đã được đối chiếu ngày 2026-10-08: active, yêu cầu PR và11job `tests.yml`,
cấm xóa/force-push, không có bypass actor. Đã sửa hướng dẫn CONTRIBUTING vốn còn mô tả thời kỳ chưa có
required checks; vẫn giữ chờ mọi check xanh và merge tay.

Lệnh ghi CAD nay báo một phần khi công việc bị bỏ qua do layer/INSERT cha khóa,
CSV lỗi hoặc thiếu đối tượng/tag, đoạn MText không thay được mà vẫn giữ định dạng,
regex timeout hoặc cấu hình layer không hợp lệ. Thuộc tính layer0 không được ghi xuyên khóa INSERT cha.
Mapping cùng layer và thay chữ bằng chính chữ hiện có vẫn là no-op, không bị báo partial thừa.
Kết quả một phần giữ số đối tượng đã xử lý và chặn lưu DWG mặc định.

Bản chốt trước PR: **2.048/2.048 Shared.Logic/Hosting, phủ dòng100%; 66/66 BatchRunner;
389 Python +115subtest, phủ100%1.776statement; pyflakes và parserPS5.1 sạch**.
Core AutoCAD2026 build0warning/0error. Host: [24/24 engineering write](bang-chung/2026-10-08/autocad-engineering-write.md)
và [51/51 edge](bang-chung/2026-10-08/autocad-engineering-edge.md), gồm khóa layer/INSERT cha,
CSV/thuộc tính/format không xử lý được, no-op và preview không sửa model.
Runner đối chiếu CSV, layer thuộc tính, định dạng MText và hash cả DWG gốc/bản chép độc lập.
Bằng chứng thô tại `C:\Users\liend\DHCB-test-results\autocad-engineering-write-2026-10-08_00-51-20-617`.
Các kết quả trên là kiểm nguồn trước merge; kiểm lại DLL đã cài sẽ được lưu cùng gói ngoài repo.
