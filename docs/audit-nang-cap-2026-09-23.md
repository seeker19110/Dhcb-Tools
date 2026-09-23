# Audit toàn diện và nâng cấp ngày 2026-09-23

Mốc mã nguồn trước sửa: `d730dd5` (sau PR #160). Trong ngày chạy hai vòng quét, mỗi vòng năm agent
đọc-chỉ theo góc nhìn khác nhau, cho ~90 phát hiện ở vòng 1 và thêm một đợt review hồi quy ở vòng 2.
Kết quả là **tám PR #161–#168, tất cả đã vào `main`**. Nguyên tắc chia PR: theo tầng, để mỗi PR
kiểm chứng được bằng đúng công cụ của tầng đó (xUnit cho tầng thuần; bộ ca trong Revit 2024.3 hoặc
accoreconsole 2026 cho Core/vỏ; pytest + parser YAML cho CI/script).

Mục "Việc để lại" ở cuối là danh sách quyết định còn chờ; `roadmap.md` trỏ vào đó.

## Tổng quan tám PR

| PR | Vòng | Tầng | Nội dung chính | Kiểm chứng |
|---|---|---|---|---|
| #161 | 1 | `Shared.Logic` + `Shared.Hosting` | 20 lỗi tầng thuần: gói bàn giao, clash, tuyến, IDS, CSV, Bridge | 1.753 ca xUnit, phủ dòng 100 %; build hai vỏ |
| #162 | 1 | Core Revit/AutoCAD, vỏ WPF, BatchRunner | 10 lỗi: `StylePurge`, `AutoRoute` preview, form `FamilyUpgrade`… | build Revit 2024/2026 + AutoCAD 2026; smoke 43/44, write-asbuilt 3/3, autoroute 13/13 trong Revit 2024.3 |
| #163 | 1 | CI, script, tài liệu | siết quyền workflow, runner hỏi TFM từ MSBuild, tài liệu lệch mã | pytest 325 ca, phủ câu lệnh 100 %; YAML đọc lại bằng parser; số lệnh đối chiếu `CommandCatalog` |
| #164 | 2 | Logic + CI | tách 6 khối quyết định từ Core xuống tầng thuần; CI nhóm theo SHA; 19 sửa từ review hồi quy | 1.797 ca phủ 100 %; Revit smoke 43/44; AutoCAD smoke 18/18 |
| #165 | 2 | IDS / IFC / Revit | tuân thủ IDS 1.0; đọc đúng loại property IFC; GlobalId 22 ký tự | 1.775 ca phủ 100 % (`IdsComplianceTests` 22 ca); Revit smoke 43/44 |
| #166 | 2 | Core (hiệu năng) | băm không gian cho pha thô Clash/Sleeve/ModelLines; cache solid host; cắt log | 1.761 ca phủ 100 %; Revit `mep` 29/29, `write-mep` 21/21 |
| #167 | 2 | Vỏ Revit | không hộp thoại trong `IUpdater`; truy vấn `rooms` chạy được; Ribbon không NRE khi chưa mở model | build 2024/2026; Revit smoke 43/44 |
| #168 | 2 | AutoCAD | purge/layer/snapshot/true color/LayerTranslate | AutoCAD 2026 accoreconsole smoke + write |

Số ca xUnit khác nhau giữa các PR vì mỗi PR thêm hoặc gộp ca của riêng nó và các PR vòng 2 được viết
song song trên cùng gốc; con số là của nhánh PR tại lúc kiểm, không phải luỹ kế.

## Phát hiện đáng chú ý nhất

Xếp theo mức nguy hiểm nếu bỏ sót, không theo thứ tự PR.

1. **Gói bàn giao "ĐẠT" dù thiếu file** (`HandoverPackageValidator`, PR #160 → sửa ở #161). File có trong
   manifest nhưng không có trên đĩa được đếm là *đã xác minh*: xoá `drawings.pdf` khỏi gói, báo cáo vẫn
   "4/4 file". Test cũ khẳng định đúng hành vi sai. Đây là đúng kiểu gian lận mà chuỗi băm NĐ 207 sinh ra
   để chặn.
2. **`StylePurge` có thể xoá line pattern / material đang dùng** (#162). Một category ném lỗi khi đọc line
   pattern làm cả vòng lặp dừng giữa chừng mà không đánh dấu "không chắc"; pattern chưa kịp ghi nhận bị coi
   là thừa và `document.Delete`. Material tương tự với phần tử hình học hỏng.
3. **IDS: specification cấm bị kiểm như bắt buộc** (#165). `minOccurs`/`maxOccurs` mức specification chưa
   từng được đọc, nên một specification "không được có" cho kết luận ngược. Kèm theo: so số theo chuỗi,
   ràng buộc `xs:restriction` bị coi là tuyển thay vì hội.
4. **`AutoRoute` xem trước không nói phần dựng MEP thật** (#162). `buildRoute=true` chạy `RouteFromLines`
   với `DryRun=false` cứng *sau* khi preview đã return; token xem-trước của Bridge xác nhận một việc nhỏ hơn
   việc sẽ làm.
5. **AutoCAD purge xoá layer chỉ chứa chữ trong title block** (#168). "Layer đang dùng" không tính attribute
   của block reference. Cùng nhóm: `LayerTranslate` xem trước báo "xoá 0" rồi xoá 40 layer.
6. **Truy vấn `rooms` qua Bridge chưa từng chạy được** (#167): `OfClass(SpatialElement)` bị Revit từ chối.
   Cùng PR: `TaskDialog` bên trong `IUpdater.Execute` có thể treo hoặc sập Revit.
7. **`ClashClassifier` xếp cặp cách nhau 5 m thành "Tolerance Flaw"** (#161) thành topic BCF, và so thể tích
   với dung sai *lập phương* (5 mm → 125 mm³). Camera BCF ở mm trong khi `BcfWriter` ghi mét.
8. **CI** (#163): `tests.yml` không có `permissions`; `release.yml` cấp `contents: write` cho cả 5 job build;
   `inputs.version` nội suy thẳng vào bash; job `installer` không chờ `verify`. Vòng 2 (#164): lượt CI của
   commit trước trên `main` bị huỷ khi hai merge cách nhau 2 phút.
9. **Mã chết** (#161): `ObstacleSpatialIndex3D` và mock HTML ở gốc repo không nơi nào dùng; `OccupancyGrid`
   đã raster hoá chướng ngại một lần nên A* vốn tra O(1). Xoá cả hai, đặc tả ghi rõ lý do.
10. **Tài liệu lệch mã** (#163): 8 tên lệnh AutoCAD trong checklist kiểm thử tay không tồn tại (`DHCB_EXEC`,
    `DHCB_AI`, `DHCB_LAYTRANS`…); "64 lệnh" / "49 lệnh" trong khi catalog có 53 Revit + 15 AutoCAD = 68.

## Đã sửa, theo PR

### #161 — tầng thuần (27 file, +1.530/−704)

- **Gói bàn giao**: thiếu file, đường dẫn thoát thư mục, băm ngắn → trượt; đối chiếu danh mục bản vẽ với
  file PDF/DWG (`UnboundSheets`); hàm băm tiêm được. `HashChain.Verify` nhận mốc băm cuối → `Truncated`.
- **Clash**: thêm mức `None`; phân loại theo độ sâu xuyên (`penetrationDepthMm`, không có thì căn bậc ba thể
  tích); camera BCF ở mét; `ClashReport` culture bất biến.
- **Tuyến**: đếm rẽ theo hướng đơn vị, gộp theo hình học, chấm điểm tuyệt đối (Manhattan / rẽ tối thiểu /
  khoảng hở yêu cầu), hệ số ×3 đúng đặc tả, `searchMarginMm`.
- **Tiến độ 4D**: trễ đo tới ngày xong thật, `ProgressTask.PercentComplete`; `ProgressCsv` khoá trùng theo
  giá trị ElementId.
- **Trắc đạc**: `NotSurveyed`; mã trùng lấy lần đo sau.
- **Chuẩn hoá tham số**: bảng ánh xạ là đầu vào, mặc định chỉ Pset IFC4 có thật.
- **IDS**: `optional` có-nhưng-sai → trượt.
- **CSV / văn bản**: chặn công thức Excel trong CSV báo cáo (giữ số âm; CSV nhập ngược không đổi);
  `NumericText` từ chối NaN/∞; `UsageLog` bỏ dòng số quá cỡ; `JobTokens` `{d}`/`{M}`; `FileNaming` tránh
  CON/NUL; HTML escape băm/kind/scopeNote.
- **Bridge**: env token < 32 ký tự bị từ chối; hai Bridge tạo token cùng lúc không đè nhau; vỏ ném sau
  `await` không treo job; request đã nhận luôn được trả lời khi Stop.

### #162 — Core, vỏ, BatchRunner (10 file, +134/−22)

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

### #163 — CI, script, tài liệu (18 file, +193/−38)

- **CI**: `permissions: contents: read` mặc định, `write` chỉ ở `publish`; `inputs.version` qua env + kiểm
  ký tự; `installer` chờ `verify`; `softprops/action-gh-release` ghim SHA.
- **Script**: hai runner hỏi TFM từ MSBuild theo phiên bản (không còn bảng tay thiếu net10 / lấy DLL mới
  nhất sai runtime); `install-nightly-task` từ chối đường dẫn có `"`; `dhcb_agent` CSV mặc định vào
  `%LOCALAPPDATA%\DHCB\exports` thay vì `C:\Users\Public`, không gửi `Bearer` rỗng (tránh tự khoá 5 phút);
  `pyproject` dev extra có `coverage`; panel AutoCAD tô cú pháp JSON khớp `&quot;`.
- **Tài liệu**: tên lệnh AutoCAD trong checklist, 68/53/15 lệnh, 3 workflow, Revit 2026 trong release,
  lệnh test tại chỗ đúng CI.

### #164 — tách logic, CI nhóm theo SHA, review hồi quy (37 file, +748/−321)

- Sáu khối quyết định chuyển từ Core xuống tầng thuần: `TestReportWriter`, `LayerRuleSet`,
  `CadImportOptions`, `TextReplace` (find rỗng từng treo vô hạn), `LineWeightText`, `HandleText`.
- CI `concurrency` nhóm theo SHA thay vì theo nhánh.
- 19 sửa từ review hồi quy của #161–#163; đáng kể nhất: `BridgeJob.Fail` không lật job đã `Done` thành
  `Error`.

### #165 — IDS 1.0, IFC, GlobalId (13 file, +1.075/−95)

- **IDS**: cardinality mức specification; so số theo giá trị; ràng buộc `xs:restriction` là hội;
  `length`/`minLength`/`maxLength`; lọc `ifcVersion`; pattern biên dịch lúc đọc + timeout.
- **IFC**: đọc đúng `IfcPropertyEnumeratedValue`/`ListValue`/`BoundedValue`/`ComplexProperty`;
  `RelatingPropertyDefinition` dạng tập; `#id` > int32 là lỗi đọc file; PredefinedType đúng vị trí lược đồ;
  Space/Storey không có `Tag`.
- **Revit**: GlobalId 22 ký tự nén đúng thuật toán bộ xuất (`IfcGuid`); `Classifications(system)` theo hệ;
  số thực đổi theo loại đại lượng.

### #166 — hiệu năng trên model lớn (9 file, +425/−39)

- `BoxSpatialHash<T>` làm pha thô cho ClashDetection (A×B), SleeveAuto (ống×host, cache solid host, băm
  điểm trùng), ModelLinesFromCad.
- `warnings` cắt trước khi đọc mô tả; cache type ở ParameterExport; DevicePattern tính khoảng cách một
  lần/vòng; RunLog cắt Messages ở 500 dòng.
- Số liệu trong Revit trùng với §22/§50 của `progress.md`: 445 sleeve, 7 va chạm, 551 thiết bị;
  `write-mep` 435+10 → 0/552 và 455 → 0/562.

### #167 — vỏ Revit (11 file, +114/−27)

`TaskDialog` trong `IUpdater.Execute` → log; truy vấn `rooms` dùng `OfClass` hợp lệ; `limit` mặc định 2000;
bốn nút Ribbon không NRE khi chưa mở model; HealthReport mở browser đúng trên .NET; AutoNumbering có chủ +
xác nhận ghi; `SuppressedWarnings` xoá trong `finally`.

### #168 — AutoCAD (12 file, +167/−16)

Layer "đang dùng" tính cả attribute của block reference; block phụ thuộc xref không là ứng viên purge;
linetype của dim style là đang dùng; không `Dispose` view sống của phiên khi snapshot; true color xuất số
nguyên (xuất → nhập từng cảnh báo mọi layer true color) và query `#RRGGBB`; LayerTranslate xem trước báo
đúng layer nguồn sẽ rỗng; `attributes_of` đếm cả insert của block động; regex quy tắc layer có timeout;
`AdjustAlignment` sau khi ghi attribute; AutoNumbering không đếm attribute không đổi;
DrawingCompare/AcadCommandTable không ném ra Bridge.

## Kiểm chứng tại máy

Máy có Revit 2024.3, Revit 2026 và AutoCAD 2026. Mọi bộ ca "trong Revit" / "accoreconsole" là chạy thật
trên model kiểm thử, không phải mock.

| Kiểm tra | PR | Kết quả |
|---|---|---|
| `Shared.Logic.Tests` | #161 / #164 / #165 / #166 | 1.753 / 1.797 / 1.775 / 1.761 ca đạt, phủ dòng 100 % |
| `BatchRunner.Tests` | #162 | 21 đạt |
| Build vỏ Revit 2024 (WPF), Revit 2026, AutoCAD 2026, BatchRunner | #162, #167 | 0 lỗi |
| Bộ `smoke` trong Revit 2024.3 (44 ca) | #162, #164, #165, #167 | 43 đạt / 0 trượt / 1 bỏ qua, ở từng PR |
| Bộ `write-asbuilt` trong Revit 2024.3 (ghi thật 55 sheet ×2) | #162 | 3/3 đạt |
| Bộ `autoroute` trong Revit 2024.3 | #162 | 13/13 đạt |
| Bộ `mep` / `write-mep` trong Revit 2024.3 | #166 | 29/29 và 21/21 đạt |
| AutoCAD 2026 accoreconsole `smoke` | #164 | 18/18 đạt |
| AutoCAD 2026 accoreconsole `smoke` + `write` | #168 | đạt |
| Python `coverage run -m pytest` | #163 | 325 đạt, phủ câu lệnh 100 % |

Cách chạy lại các bộ ca: `kiem-thu-trong-revit.md` (Revit) và `bang-chung-test-autocad-live.md` (AutoCAD).

## Việc để lại (cần quyết định của người dùng)

Gộp cả hai vòng, nhóm theo khu vực. Không mục nào là lỗi âm thầm: mỗi mục hoặc đã có chốt chặn khác, hoặc
đổi hành vi mặc định / hợp đồng đang dùng nên cần người dùng chốt trước.

### Bridge và bảo mật

| Phát hiện | Vì sao để lại |
|---|---|
| Bridge khởi động mặc định mỗi phiên Revit, không có khoá trong `settings.json` | Đổi hành vi mặc định của sản phẩm; `BridgeCommitGuard` đã chặn ghi không có preview token |
| Lệnh chỉ đọc (HealthReport, ScheduleExport…) nhận `outputPath` tuyệt đối bất kỳ qua Bridge | Cần chốt thư mục cho phép (model folder? `%USERPROFILE%`?); ảnh hưởng batch đêm ghi ra ổ mạng |
| `AuthLockout` khoá toàn cục 5 phút khi một tiến trình gửi 5 token sai | Khoá theo client cần định danh client; loopback không có |
| `dhcb_mcp_server` `confirm:true` là boolean do model điền | Bridge vẫn đòi `previewToken` từ preview trước đó nên `confirm` một mình không ghi được; nâng lên chuỗi xác nhận là đổi hợp đồng MCP |
| Batch job chạy trong `ApplicationInitialized`; hàng đợi Bridge không xả item bỏ rơi khi Revit modal; `MaxInFlight` gồm cả request sync; `BridgeCommitGuard` revision tự tăng bởi preview có transaction tạm | Thiết kế vòng đời Bridge; cần bộ ca Bridge riêng trên Revit thật |
| `sign-addin.ps1` cài root tự ký vào `LocalMachine\Root` | Script ghi rõ vì sao (Authenticode không nhận `CurrentUser\Root`); thay bằng chứng chỉ thật là việc mua sắm |
| Release không ký DLL/installer | Cần secret PFX của tổ chức |

### Hành vi lệnh và hợp đồng cấu hình

| Phát hiện | Vì sao để lại |
|---|---|
| `AutoNumbering`/`FlowNumbering` mặc định `ParameterName = "Mark"` | Đổi mặc định sang từ điển làm đổi hành vi mọi job đang chạy |
| `RouteOptionGenerator`/`ClashClassifier` chưa dây vào `AutoRoute`/`ClashDetection` | Cần bộ ca trong Revit riêng; roadmap 8 đang ưu tiên độ tin cậy hơn bề mặt lệnh |
| `CancellationToken`/tiến độ cho lệnh dài (Sleeve, Clash, AutoRoute) | Đổi chữ ký `ICoreCommand` và mọi vỏ; Bridge 504 hiện nói rõ "không huỷ được nữa" |
| Dung sai "Mm" phía AutoCAD so trong đơn vị bản vẽ, không đọc `INSUNITS` | Đổi nghĩa cấu hình đang dùng trong job; cần thống nhất với người dùng |
| AttributeExport chỉ quét model space | Thêm cờ `includePaperSpace` là đổi hợp đồng CSV |
| TextReplace thay chuỗi thường trên `MText.Contents` có thể đụng mã định dạng (`\P`) | Cần ánh xạ offset Text ↔ Contents; hiện chỉ báo khi mã cắt ngang chuỗi |

### IDS / IFC

| Phát hiện | Vì sao để lại |
|---|---|
| So giá trị không phân biệt hoa thường; `xs:pattern` cú pháp XSD (`\i`, `\c`) chưa dịch sang .NET | Có thể lệch IfcTester; đổi phải chạy lại bộ ca buildingSMART |
| `--handover` đọc và parse cùng file IFC hai lần (50 MB → ~3 GB live) | Cần `IfcChecker` và `IfcIdsModel` dùng chung một lần parse |
| `IfcStepParser` cấp phát 2 chuỗi/thực thể; đọc file `Encoding.UTF8` thay vì ISO-8859-1 | Đo trên file 50 MB thật trước khi đổi |

### Nợ cấu trúc

| Phát hiện | Vì sao để lại |
|---|---|
| `AcadQueryHandler`/`SleeveCommand` 680 dòng chưa tách phần thuần | Việc dài, làm dần theo §49–§51 của `progress.md` |
