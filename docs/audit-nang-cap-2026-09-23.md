# Audit toàn diện và nâng cấp ngày 2026-09-23

Mốc mã nguồn trước sửa: `d730dd5` (sau PR #160). Audit chạy song song năm hướng — logic thuần, Core
Revit/AutoCAD, Python/CI/installer, tài liệu, và review sâu PR #160 — cho ~90 phát hiện. Triển khai
thành ba PR theo tầng để mỗi PR kiểm chứng được bằng đúng công cụ của tầng đó.

| PR | Tầng | Kiểm chứng |
|---|---|---|
| #161 | `Shared.Logic` + `Shared.Hosting` | 1.753 ca xUnit, phủ dòng 100 %; build hai vỏ |
| #162 | Core Revit/AutoCAD, vỏ WPF, BatchRunner | build Revit 2024/2026 + AutoCAD 2026; ba bộ ca chạy **trong Revit 2024.3** (smoke 43/44, write-asbuilt 3/3, autoroute) |
| #163 | CI, script, tài liệu | pytest + coverage 100 %; YAML đọc lại bằng parser; số lệnh đối chiếu bằng `CommandCatalog` |

## Phát hiện đáng chú ý nhất

1. **Gói bàn giao "ĐẠT" dù thiếu file** (`HandoverPackageValidator`, PR #160). File có trong manifest
   nhưng không có trên đĩa được đếm là *đã xác minh* — xoá `drawings.pdf` khỏi gói, báo cáo vẫn "4/4 file".
   Test cũ khẳng định đúng hành vi sai. Đây là đúng kiểu gian lận mà chuỗi băm NĐ 207 sinh ra để chặn.
2. **`StylePurge` có thể xoá line pattern / material đang dùng.** Một category ném lỗi khi đọc line
   pattern làm cả vòng lặp dừng giữa chừng mà không đánh dấu "không chắc"; pattern chưa kịp ghi nhận bị
   coi là thừa và `document.Delete`. Material tương tự với phần tử hình học hỏng.
3. **`AutoRoute` xem trước không nói phần dựng MEP thật.** `buildRoute=true` chạy `RouteFromLines`
   với `DryRun=false` cứng SAU khi preview đã return — token xem-trước của Bridge xác nhận một việc nhỏ
   hơn việc sẽ làm.
4. **`ClashClassifier` xếp cặp cách nhau 5 m thành "Tolerance Flaw"** (thành topic BCF) và so thể tích
   với dung sai *lập phương* (5 mm → 125 mm³). Camera BCF ở mm trong khi `BcfWriter` ghi mét.
5. **`ObstacleSpatialIndex3D` và mock HTML ở gốc repo** không nơi nào dùng; `OccupancyGrid` đã raster
   hoá chướng ngại một lần nên A* vốn tra O(1). Xoá cả hai, đặc tả ghi rõ lý do.
6. **CI**: `tests.yml` không có `permissions`; `release.yml` cấp `contents: write` cho cả 5 job build;
   `inputs.version` nội suy thẳng vào bash; job `installer` không chờ `verify`.
7. **Tài liệu lệch mã**: 8 tên lệnh AutoCAD trong checklist kiểm thử tay không tồn tại (`DHCB_EXEC`,
   `DHCB_AI`, `DHCB_LAYTRANS`…); "64 lệnh" / "49 lệnh" trong khi catalog có 53 Revit + 15 AutoCAD = 68.

## Đã sửa

### PR #161 — tầng thuần (27 file, +1.530/−704)

Gói bàn giao: thiếu file, đường dẫn thoát thư mục, băm ngắn → trượt; đối chiếu danh mục bản vẽ với
file PDF/DWG (`UnboundSheets`); hàm băm tiêm được. Clash: thêm `None`, phân loại theo độ sâu xuyên
(`penetrationDepthMm`, không có thì căn bậc ba thể tích), camera mét. Tuyến: đếm rẽ theo hướng đơn
vị, gộp theo hình học, chấm điểm tuyệt đối (Manhattan / rẽ tối thiểu / khoảng hở yêu cầu), hệ số ×3 đúng
đặc tả, `searchMarginMm`. Tiến độ 4D: trễ đo tới ngày xong thật, `ProgressTask.PercentComplete`. Trắc
đạc: `NotSurveyed`, mã trùng lấy lần đo sau. Chuẩn hoá tham số: bảng ánh xạ là đầu vào, mặc định chỉ
Pset IFC4 có thật. IDS: `optional` có-nhưng-sai → trượt. CSV báo cáo chặn công thức Excel (giữ số âm;
CSV nhập ngược không đổi). `NumericText` từ chối NaN/∞; `UsageLog` bỏ dòng số quá cỡ; `ProgressCsv`
khoá trùng theo giá trị ElementId; `JobTokens` `{d}`/`{M}`; `FileNaming` tránh CON/NUL; `HashChain.Verify`
nhận mốc băm cuối → `Truncated`; HTML escape băm/kind/scopeNote; `ClashReport` culture bất biến. Bridge:
env token < 32 ký tự bị từ chối; hai Bridge tạo token cùng lúc không đè nhau; vỏ ném sau `await` không
treo job; request đã nhận luôn được trả lời khi Stop.

### PR 2 — Core, vỏ, BatchRunner

| Chỗ | Sửa |
|---|---|
| `StylePurge` | từng category/subcategory tự bọc; mọi lỗi đọc line pattern/material → `uncertain` (mặc định giữ) + thông báo |
| `AutoRoute` | preview với `buildRoute` nói rõ sẽ dựng loại/kích cỡ MEP nào và có xoá line không |
| `ColorByParameter` | legend CSV chỉ ghi khi chạy thật; preview báo sẽ ghi ở đâu |
| `AsBuiltStamp` | preview và chạy thật cùng bộ lọc dấu cũ; preview liệt kê số phần tử sẽ xoá khi `overwrite` |
| `BatchJobRunner` | `Save` ném (đường dẫn lạ, log khoá) → ghi entry lỗi, chạy tiếp file sau; `Close` an toàn |
| `CommandFormWindow` | ảnh chụp preview gồm nội dung thư mục đầu vào (như `BridgeCommitGuard`) |
| `HealthReport` | không đọc được cỡ file → ghi Messages thay vì im lặng 0 MB; "(unnamed)" → "(không tên)" |
| `RevitIdsElement` | Tag qua `BuiltInParameter.ALL_MODEL_MARK`, không phụ thuộc tên "Mark" theo ngôn ngữ |
| `CommandCatalog` | `FamilyUpgrade` có đủ field (trước đây form Ribbon in "lệnh không có tham số", `/tools` không dùng được) |
| BatchRunner | Revit: `Kill` rồi `WaitForExit(10 s)` và báo nếu còn sống; AutoCAD: timeout không tràn `int` |

### PR 3 — CI, script, tài liệu

CI: `permissions: contents: read` mặc định, `write` chỉ ở `publish`; `inputs.version` qua env + kiểm ký
tự; `installer` chờ `verify`; `softprops/action-gh-release` ghim SHA. Script: hai runner hỏi TFM từ
MSBuild theo phiên bản (không còn bảng tay thiếu net10 / lấy DLL mới nhất sai runtime);
`install-nightly-task` từ chối đường dẫn có `"`; `dhcb_agent` CSV mặc định vào `%LOCALAPPDATA%\DHCB\exports`
thay vì `C:\Users\Public`, không gửi `Bearer` rỗng (tránh tự khoá 5 phút); `pyproject` dev extra có
`coverage`; panel AutoCAD tô cú pháp JSON khớp `&quot;`. Tài liệu: tên lệnh AutoCAD trong checklist,
68/53/15 lệnh, 3 workflow, Revit 2026 trong release, lệnh test tại chỗ đúng CI.

## Không sửa trong đợt này (cần quyết định của người dùng)

| Phát hiện | Vì sao để lại |
|---|---|
| Bridge khởi động mặc định mỗi phiên Revit, không có khoá trong `settings.json` | Đổi hành vi mặc định của sản phẩm; `BridgeCommitGuard` đã chặn ghi không có preview token |
| Lệnh chỉ đọc (HealthReport, ScheduleExport…) nhận `outputPath` tuyệt đối bất kỳ qua Bridge | Cần chốt thư mục cho phép (model folder? `%USERPROFILE%`?) — ảnh hưởng batch đêm ghi ra ổ mạng |
| `AuthLockout` khoá toàn cục 5 phút khi một tiến trình gửi 5 token sai | Khoá theo client cần định danh client; loopback không có |
| `sign-addin.ps1` cài root tự ký vào `LocalMachine\Root` | Script ghi rõ vì sao (Authenticode không nhận `CurrentUser\Root`); thay bằng chứng chỉ thật là việc mua sắm |
| Release không ký DLL/installer | Cần secret PFX của tổ chức |
| `dhcb_mcp_server` `confirm:true` là boolean do model điền | Bridge vẫn đòi `previewToken` một lần từ preview trước đó, nên `confirm` một mình không ghi được; nâng lên chuỗi xác nhận là đổi hợp đồng MCP |
| `AutoNumbering`/`FlowNumbering` mặc định `ParameterName = "Mark"` | Đổi mặc định sang từ điển làm đổi hành vi mọi job đang chạy |
| `RouteOptionGenerator`/`ClashClassifier` chưa dây vào `AutoRoute`/`ClashDetection` | Cần bộ ca trong Revit riêng; roadmap 8 đang ưu tiên độ tin cậy hơn bề mặt lệnh |
| IDS: so giá trị không phân biệt hoa thường | Có thể lệch IfcTester; đổi phải chạy lại bộ ca buildingSMART |
| `AcadQueryHandler`/`SleeveCommand` 680 dòng chưa tách phần thuần | Việc dài, làm dần theo §49–§51 |

## Kiểm chứng tại máy

| Kiểm tra | Kết quả |
|---|---|
| `Shared.Logic.Tests` (PR #161) | 1.753 đạt, phủ dòng 100 % |
| `BatchRunner.Tests` | 21 đạt |
| Build vỏ Revit 2024 (WPF), Revit 2026, AutoCAD 2026, BatchRunner | 0 lỗi |
| Bộ `smoke` trong Revit 2024.3 (PR 2) | 43 đạt / 0 trượt / 1 bỏ qua trên 44 ca |
| Bộ `write-asbuilt` trong Revit 2024.3 (PR 2, ghi thật 55 sheet ×2) | 3/3 đạt |
| Bộ `autoroute` trong Revit 2024.3 (PR 2) | 13/13 đạt |
| Python: `coverage run -m pytest` (PR 3) | 325 đạt, phủ câu lệnh 100 % |
