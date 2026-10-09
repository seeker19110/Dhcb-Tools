# Audit toàn bộ thành phần và hoàn thiện nguồn — 2026-10-10

Đợt này bắt đầu tối 2026-10-09, tiếp tục sau khi PR #187 được merge. Nhánh
`codex/audit-comprehensive-2026-10-09` lấy từ `origin/main` tại `4110ab1`; giữ riêng
bản cài đang dùng. Audit theo rủi ro, có test tái hiện trước sửa cho Bridge, CLI,
bàn giao, MEP và doctor. Không coi build hay API double là nghiệm thu host.

## Thành phần đã rà

| Thành phần | Phạm vi và kết quả xử lý |
|---|---|
| Revit Core, Ribbon, updater, batch | Giao dịch/failure handling, family, tham số cao độ, query; sửa trạng thái commit, tính nguyên tử, phạm vi instance và giới hạn truy vấn |
| AutoCAD UI, Core Console, CAD → BIM | Đơn vị trục, Ray, tên trục, CSV attribute/layer và vòng đời view; thêm 13 ca nghiệm thu engine |
| Geometry/MEP | A*, ngân sách/bộ nhớ, số hữu hạn, lấy mẫu phủ thiết bị; hết ngân sách phải báo phần chưa hoàn thành |
| BatchRunner, log/report | Không bỏ qua dòng log hỏng/rỗng hoặc hash sai khi quyết định kết quả; giữ tương thích log cũ không seal |
| Bàn giao, hoàn công, IFC/IDS/BCF | Danh mục nhiều model, tên báo cáo IDS, binding số bản vẽ, trạng thái từng bước, đầu vào danh mục; bộ chuẩn IFC/IDS/BCF tiếp tục là cổng CI |
| Bridge/Shared.Hosting | Dừng phiên, hàng đợi, fault/cancel, retry, đường dẫn và Content-Type |
| AI local, MCP, panel, MCPB | URI/timeout/regex, JSON-RPC/cache, recovery sau timeout, file CSV, metadata giấy phép và profile |
| Script chẩn đoán/thí điểm/task | Kiểm response shape doctor, cấu hình/runtime/host; thí điểm và task giữ bộ test hiện có |
| Installer, đóng gói, CI, dependencies | Rà quy trình build/gói/chữ ký, bảo toàn bản cài, action SHA và cổng chất lượng; giữ bộ nghiệm thu installer và quét dependency |

## Lỗi đã sửa

| Mức | Tình huống trước sửa → hành vi sau sửa |
|---|---|
| P1 | Revit `Commit()` trả RolledBack/Pending nhưng lệnh tiếp tục báo OK → kiểm cả giá trị trả về và trạng thái cuối; `E-TRANSACTION-COMMIT`, chặn Dispatch và lưu/đóng document trong BatchJobRunner khi Pending |
| P1 | Dispose transaction/group khi failure processing chưa xong → giữ đối tượng native đến Idling, kiểm IsValidObject, giải phóng sau khi transaction con kết thúc |
| P1 | Lỗi Revit không giải quyết được trong Bridge/batch có thể treo UI hoặc lặp resolution → rollback/clear lỗi, chỉ thử resolution mặc định một lần; Ribbon giữ UI tương tác |
| P1 | FamilyLoader ghi từng phần rồi vẫn báo OK, preview khác commit → một transaction cho cả bộ family, preview thực hiện cùng vòng nạp rồi rollback |
| P1 | Tag/updater cao độ ghi fallback type có thể tác động mọi instance → chỉ ghi tham số instance; Set bị từ chối không được đếm, giá trị đã đúng thành no-op; preview rollback |
| P2 | FamilyUpgrade ghi đè file xuất hiện giữa kiểm tồn tại và SaveAs dù overwrite=false → truyền đúng `config.Overwrite` cho SaveAsOptions |
| P2 | Legend trên nhiều sheet làm query views trùng key; query dựng quá nhiều dữ liệu → mapping 1→n `sheetIds` và giữ `sheetId` cũ, giới hạn trước projection, limit tối đa 10.000 |
| P1 | GridExtract đưa tọa độ mét nguyên trạng sang CSV Revit đọc mm → đổi theo INSUNITS, ngưỡng tìm nhãn cũng đúng đơn vị; thiếu đơn vị có thông báo giả định mm |
| P1 | AttributeImport chọn theo thứ tự dòng khi cùng Handle/Tag có nhiều giá trị → planner gộp dòng giống nhau, loại cặp mâu thuẫn, báo Errors/PartialSuccess, giữ các cặp độc lập |
| P2 | LayerTranslate cùng nguồn nhiều đích tạo layer dư/chọn dòng đầu → từ chối trước transaction; tên AXIS-n tự sinh tránh trùng; Ray giữ đúng nửa hướng dương |
| P3 | Clone current view của AutoCAD không dispose → scope using |
| P1 | Thiết bị hết 500 lượt bổ sung nhưng Uncovered rỗng → trả đúng điểm lấy mẫu chưa phủ, thông báo ngân sách và PartialSuccess ở Core |
| P2 | A* cast kích thước trước kiểm trần, nhận NaN/chi phí âm, ngân sách long tràn → kiểm hữu hạn/chi phí/ngân sách/thể tích trước cast, kẹp chỉ số và ngân sách phù hợp |
| P1 | Dispatch fault/cancel sau await làm Bridge timeout/job treo; item chờ vẫn chạy sau Stop → theo dõi Task, đánh dấu abandoned, hủy item theo phiên server, giữ kết quả đã hoàn tất |
| P2 | Content-Type `application/jsonXYZ` và namespace thiết bị Windows lọt kiểm → so media type chính xác và chặn namespace thiết bị ở trường file |
| P2 | Panel/MCP mất changedIds/job ID/kết quả một phần khi HTTP 500/504 và hiển thị chắc chắn thất bại → giữ payload JSON và trạng thái chưa xác định; thêm `autocad_progress` đọc kết quả job |
| P2 | CSV tên trần theo symlink ra ngoài thư mục tạm; params bypass phân trang → kiểm đường canonical và input shape |
| P2 | JSON-RPC null/array/params sai làm stdio MCP sập; notification thiếu ID có thể khởi chạy lệnh; cache tools hỏng → kiểm envelope/shape, bỏ notification thực thi và fallback danh mục hợp lệ |
| P2 | Local AI nhận URI file/ftp, timeout tràn, regex chat không trần/số vô hạn → HTTP(S) loopback, timeout 5–600 giây, câu lệnh chat tối đa 4.096 ký tự, regex timeout 250 ms và số hữu hạn |
| P2 | Doctor `.get` trên JSON null/list/scalar làm sập → trả chẩn đoán lỗi, không phản chiếu payload/bí mật |
| P1 | Report-only bỏ dòng log hỏng/rỗng hoặc hash sai, có thể trả exit 0 → giữ bằng chứng lỗi trong báo cáo và trả exit 1; không sửa log gốc |
| P2 | Chỉ đọc sheet index CSV đầu, báo cáo IDS trùng basename bị ghi đè → gom tất cả index và giữ thư mục tương đối cho từng báo cáo |
| P2 | A-1 được coi có file vì tìm thấy A-10; HTML tô xanh bước một phần → binding có ranh giới số hiệu, dùng IsComplete, JSON có PartialSuccess/Errors/IsComplete |
| P2 | Danh mục hồ sơ có null groups/items/patterns gây NullReference → lỗi đầu vào ArgumentException rõ ràng |
| P2 | Bộ kiểm file/hash tuyên bố đủ hồ sơ pháp lý → thông báo chỉ xác nhận kết quả kiểm gói, yêu cầu xác nhận hồ sơ theo dự án |
| P3 | MCPB còn license chờ quyết định và matrix 2023–2026 → Apache-2.0 và 2022–2027 |

Contract Revit được đối chiếu với [Autodesk Transaction.Commit](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/32714010-7138-f64f-8fde-a310354448e3.htm):
Commit có thể rollback, Pending cấm giao dịch mới và trạng thái cuối có thể khác kết quả trả về.

## Kiểm chứng

Bằng chứng trên máy ở `out/audit-comprehensive-2026-10-09/` (gitignored).

| Cổng | Kết quả |
|---|---|
| .NET logic/hosting và adapter | 2.225/2.225 đạt; cổng Shared.Logic/Hosting phủ 100% dòng; adapter Revit là API double |
| CLI BatchRunner | 92/92 đạt |
| Python đúng CI Linux | 469 test, 40 skip theo nền tảng; 2.050/2.050 câu lệnh (100%); pyflakes/py_compile và mục lục bằng chứng đạt |
| Python Windows | 469 test: 462 thực thi đạt, 7 skip; phủ 2.050/2.050 câu lệnh (100%); fixture HOME/UTF-8 độc lập môi trường |
| JavaScript panel | Ca trạng thái chưa xác định 1/1 đạt trên Windows Node |
| AutoCAD native audit mới | 13/13 đạt trên AutoCAD 2026 W.179.0.0; hash mẫu gốc và bản thử giữ nguyên ở lượt cuối |
| Build Windows UI/Core | 22/22 profile đạt, 0 lỗi/cảnh báo: 6 Revit, 8 AutoCAD UI, 8 Core Console |
| Hồi quy AutoCAD đã có | 105/105 đạt (18 smoke + 12 write + 24 engineering + 51 locked), hash DWG nguyên vẹn |
| Dependency mới | NuGet 8 profile × 10 project (80 lượt), 58 cặp package/version và pip: không advisory được phát hiện |
| CI PR | Kiểm lại trên head của PR; chỉ merge sau toàn bộ tests.yml và gitleaks đạt |

Các chứng minh trước sửa: `regression-before.log` (13/17 đỏ),
`cli-regression-before.log` (3/4 đỏ), `mep-before.log` (7/8 đỏ),
`doctor-before.log` (12 malformed-response subtest đỏ). Sau sửa có các test
hồi quy trong repo, TRX/coverage và JSON output engine. Fixture obstacle ±1e20
đã đạt trước sửa trên .NET 10; clamp trước cast củng cố tính tương thích runtime,
không được tính là lỗi xuyên vật cản tái hiện trên máy này.

Bộ chuẩn IDS hiện giữ 330/334 và bốn khác biệt chủ ý đã ghi nhận; BCF 2.1 có
schema/reader độc lập. Vòng audit 2026-10-09 đã kiểm installer 16/16, helper 5/5,
NuGet cả tám profile và pip không có advisory được phát hiện tại thời điểm kiểm.
Đợt này dùng CI để kiểm lại trên đúng head, không lấy số lịch sử thay bằng chứng mới.

## Đề xuất nâng cấp và việc đã thực hiện

1. **Độ tin cậy dữ liệu trước tiên — đã thực hiện:** giao dịch, preview/commit,
   lỗi một phần, chống chạy muộn và bảo toàn file đều có hành vi rõ và test hồi quy.
2. **Chẩn đoán và phục hồi — đã thực hiện:** doctor không sập vì response sai,
   MCP đọc lại progress, báo cáo mang đủ ID/trạng thái và không hướng dẫn retry mù.
3. **Tương thích runtime — giữ profile đúng host:** BatchRunner/test đã dùng .NET 10;
   giữ net48/net8 ở host yêu cầu, không ép đổi runtime của Autodesk. Microsoft ghi
   [.NET 10 hỗ trợ đến 14-11-2028, .NET 8 đến 10-11-2026](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
   Host dùng net8 cần kế hoạch cập nhật chính thức tương thích, không chỉ đổi target framework.
4. **Chất lượng BIM/MEP — cần dữ liệu nghiệm thu:** chạy preview/commit/Undo trên RVT/DWG
   dự án thật, kiểm family/type/tọa độ/sheet và người phụ trách xác nhận. Kiểm phủ thiết bị
   là phép lấy mẫu; kết quả không thay tính toán hoặc quy tắc thiết kế của dự án.
5. **Triển khai — tách gói dev và release:** chỉ phát hành installer chính thức qua
   CI xanh, chữ ký và quy trình release hiện có. Hermes panel giữ provider đã cấu hình;
   muốn chuyển sang provider local cần yêu cầu sản phẩm và benchmark riêng.

## Gói dev bàn giao

Gói chưa ký `out/audit-comprehensive-2026-10-09/DhcbTools-audit-dev-20261010.zip`
chứa sáu profile Revit, tám profile AutoCAD UI/Core, BatchRunner, script người dùng,
job/config mẫu, LICENSE/NOTICE và tài liệu. `manifest.json` ghi commit nguồn và hash
từng file; giữ đúng năm/runtime host. Gói không tự cài hoặc thay bản đang dùng.

## Giới hạn bằng chứng

Máy không có Revit.exe. Adapter Revit dùng source-link với API double để kiểm
contract lỗi/lifecycle; build 2022–2027 chỉ kiểm khả năng biên dịch. Chưa xác nhận
Revit GUI, Undo, hành vi family thật hoặc dữ liệu công ty. AutoCAD Core Console
không thay nghiệm thu Ribbon/UI. Lỗi được phát hiện trong phạm vi audit đã sửa;
không có tuyên bố mọi model, mọi điều kiện hoặc mọi lỗi tiềm ẩn đều đã được loại bỏ.
