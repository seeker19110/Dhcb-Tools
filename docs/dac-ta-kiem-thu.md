# Đặc tả kiểm thử

Tài liệu này mô tả **kiểm thử cho toàn bộ tool**: phần đã tự động hoá, phần chưa thể tự động (vì cần
Revit/AutoCAD thật), và kịch bản thủ công cho từng lệnh. Đặc tả tính năng ở
[`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md).

## 1. Chiến lược

Revit API và AutoCAD .NET API **không mock được một cách trung thực** (`Document`, `Element`,
`Parameter` đều là class kín, không interface, không constructor công khai; giả lập chúng chỉ test
được chính bản giả lập). Vì vậy:

| Tầng | Chứa gì | Kiểm thử thế nào |
|---|---|---|
| `DhcbTools.Shared.Logic` | CSV, số, đánh số, số học MEPF, tên file, HTML, token | **xUnit tự động, chạy trên CI Linux** |
| `DhcbTools.Core*` | logic có `Document`/`Database` | kịch bản thủ công trên file mẫu (§4), có checklist |
| `DhcbTools.Revit` / `DhcbTools.AutoCAD` | Ribbon, ExternalCommand, Bridge | kịch bản thủ công (§4.1) |

**Quy tắc bắt buộc từ nay:** tính năng mới phải đẩy phần tính toán được xuống `Shared.Logic` để có
test tự động. Nếu một thuật toán "không test được", gần như chắc chắn nó đang bị trộn với Revit API
một cách không cần thiết.

## 2. Test tự động hiện có

Chạy:

```bash
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj
```

CI chạy đúng lệnh này trên mỗi push/PR (`.github/workflows/tests.yml`).

### 2.1 `CsvTextTests` — đọc/ghi CSV

Bọc ô chỉ khi cần; nhân đôi dấu nháy; tách ô có dấu phẩy trong nháy; ô rỗng ở cuối dòng; dòng rỗng;
đầu vào `null`; round-trip escape ↔ split; **UTF-8 có BOM** (lỗi #4 — thiếu BOM làm Excel hiện sai
tên tiếng Việt), kiểm cả preamble lẫn ghi/đọc lại file thật.

### 2.2 `NumericTextTests` — round-trip số (lỗi #1)

Mọi test đặt `CurrentCulture = vi-VN` để tái hiện đúng máy kỹ sư Việt Nam. Bao: ghi luôn dùng dấu
chấm; đọc chấp nhận cả `1234.5` lẫn `1234,5`; **từ chối** chuỗi nhập nhằng `1,234.5` thay vì đoán;
round-trip không mất giá trị; `null`/rỗng/chữ trả `false`.

### 2.3 `NumberingPlannerTests` — đánh số theo vị trí (lỗi #5)

Ba cửa cùng hàng lệch 1 mm vẫn được sắp trái→phải (đúng cái mà bản cũ làm sai); hai hàng cách xa hơn
dung sai thì hàng trên trước; hướng quét theo cột; dung sai tuỳ chỉnh đổi kết quả đúng như mong đợi;
toạ độ trùng hoàn toàn giữ thứ tự đầu vào (ổn định); chuỗi dài lệch dần dưới dung sai không "trôi
dải" thành một hàng khổng lồ; sinh nhãn có tiền tố, đệm 0, số âm, bước nhảy âm.

### 2.4 `MepLayoutTests` — số học MEPF

Đổi đơn vị mm ↔ feet; vị trí hanger (cách đều, nằm trong đoạn, khoảng cách không vượt spacing, đoạn
ngắn đặt đúng **một** hanger — bản cũ đặt hai cái chồng nhau); điểm cắt ống (cắt đều, đoạn đã ngắn
thì không cắt, không tạo mẩu thừa siêu ngắn ở cuối, mọi đoạn sau khi cắt không vượt max); cao độ
đáy/đỉnh/tim kể cả khi đầu vào đảo ngược; giao bounding box kể cả trường hợp chạm biên và có dung sai;
tham số ≤ 0 ném `ArgumentOutOfRangeException` thay vì lặp vô hạn.

### 2.5 `FileNamingTests`, `ExportVersionMapTests`, `HtmlTextTests`, `BridgeAuthTests`

- Tên file: thay ký tự cấm (kể cả tập ký tự cấm của Windows khi chạy trên Linux), giữ tiếng Việt,
  cắt dấu chấm/dấu cách cuối, tên rỗng → `unnamed`, token trong mẫu được sanitize **trước** khi ghép
  (để `A/101` không thành thư mục con), và chống trùng tên không phân biệt hoa thường.
- Phiên bản xuất: `AcadRelease2018`/`2013` → hằng đúng; `IFC2x3 CV 2.0 + 4D` **không** bị đọc nhầm
  thành IFC4 (bản cũ chỉ tìm ký tự `4`); không nhận ra thì báo `false` để lệnh gọi cảnh báo, thay vì
  im lặng đổi phiên bản.
- HTML: thoát `& < > " '`, thoát dấu `&` trước để không escape kép, giữ nguyên tiếng Việt.
- Bridge: token đủ dài, ngẫu nhiên, chỉ ký tự an toàn cho header; tách `Bearer` không phân biệt hoa
  thường; so sánh thời gian hằng số; chỉ cho qua khi đủ **cả** token đúng lẫn `Content-Type: application/json`.

### 2.6 `CommandCatalogTests` — đối chiếu danh mục lệnh với mã nguồn (đã có)

Đọc thẳng `src/**/*.cs`: mọi `CommandName => "..."` trong hai Core phải có trong `CommandCatalog` và ngược lại; mọi
lệnh trong catalog phải có `case` trong `RevitCommandTable`/`AcadCommandTable`. Thêm lệnh mà quên khai báo → CI đỏ.
Nút Ribbon đi qua `CommandRunner` theo tên catalog nên không cần đối chiếu riêng.

### 2.7 `BatchTests` — batch runner (đã có)

`JobTokens` (token tên không phân biệt hoa thường, mẫu ngày giờ phân biệt, token lạ giữ nguyên, sanitize tên file),
`BatchJob` (đọc/validate, `onlySteps`, thay token trong config lồng nhau, JSON hỏng báo `InvalidDataException`),
`RunLog` (round-trip một dòng, bỏ dòng hỏng, mã thoát), `BatchReport` (escape HTML, ô xanh/đỏ/bỏ qua), `AcadScriptGen`.

### 2.8 Hình học và MEP (đã có)

`GridClustering`/`GridNaming` (gom đoạn thẳng hàng theo dung sai, bỏ đoạn ngắn/xiên, tên A,B,C bỏ I/O, CSV round-trip kể
cả số dấu phẩy), `RouteGraph` (chữ U + T → 2 elbow 1 tee, gộp đầu mút theo dung sai, thẳng hàng không elbow, chu trình bị
loại và báo, 5 nhánh không hỗ trợ, thứ tự dựng liên tục), `DevicePattern` (lưới cách tường đủ margin, căn giữa, loại điểm
trong lỗ, chèn thêm khi thiếu phủ, phòng hẹp một thiết bị, phòng chữ L), `DuctSizing`/`PipeSizing` (giá trị đối chiếu
ASHRAE ductulator và bảng SCH40), `SystemNaming`, `FlowNumbering` (nhánh phân cấp 1.1, 1.2.1, chu trình không lặp),
`PathFinder3D` (đi thẳng, vòng tường ít rẽ nhất, khoảng hở, bị chặn, giới hạn ô).

### 2.9 Kiểm tra và AI (đã có)

`RuleChecker`, `ClashAcceptance` (khoá ổn định theo cặp id + vị trí làm tròn), `LayerMappingSuggester` (tường 200 đúng
type, tiếng Việt có/không dấu, loại type bịa), `SpecTextExtractor` (m/mm, tầng hầm âm, tên chuẩn hoá, dòng gốc, cảnh
báo), `WarningAnalyzer`, `CommandIntentParser` (chỉ trả lệnh trong whitelist, luôn `dryRun:true`).

### 2.10 Giai đoạn 7 (đã có — `Phase7Tests`)

`NamePattern` (token, bộ đếm `{n:00}`, định dạng upper/left, regex tìm/thay, chống trùng trong lô và với tên đã có),
`PaletteGenerator` (màu kề nhau khác xa, cùng giá trị cùng màu, màu cố định), `ThresholdRule` (đọc cùng file với
ParameterRule, max/min, số đo thiếu), `LayerMapTable` (khớp chính xác/wildcard/phủ định, plan bỏ layer đã chuẩn),
`DiffSummary` (thêm/xoá/đổi layer/dời/đổi text, dung sai, CSV/HTML escape), `RvtFileInfo` (UTF-16 "Format: 2024",
chuỗi build cũ, không nhận ra → null), `AcadScriptGen.PlotPdf` (thứ tự prompt -PLOT, chèn trước SAVEAS),
`CommandIntentParser.Candidates` (≤ 8, lệnh khớp đứng đầu).

### 2.11 Giai đoạn 7 P2 (đã có — `Phase7P2Tests`)

`SlopeMath` (bảng dốc tối thiểu theo DN, độ hạ, kiểm dốc đạt/không đạt/ngược, kick 45°/90°, chiều dài tối thiểu cho kick,
cao độ dọc tuyến), `BomAggregator` (gom theo spool/hệ/type/size, tổng chiều dài, số cây có hao hụt, CSV, tổng theo hệ),
`PolylineSimplifier` (bỏ điểm thẳng hàng/trùng, không gộp đoạn quay đầu, chiều dài).

## 3. Ngưỡng chất lượng

- Mọi thuật toán trong `Shared.Logic` phải có test cho: đường đi thường, biên (0, âm, rỗng, `null`),
  và **đúng cái lỗi cũ đã gây ra** — mỗi lỗi trong `progress.md` khi sửa phải kèm một test tái hiện.
- Test không được phụ thuộc culture máy chạy: chỗ nào liên quan tới số/ngày thì tự đặt culture.
- Test không đọc/ghi ngoài thư mục tạm.

## 4. Kịch bản thủ công (phần cần Revit/AutoCAD thật)

Quy trình cài đặt + checklist đi từ đầu đến cuối (kèm mẫu ghi kết quả) ở
[`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md); mục này giữ kỳ vọng chi tiết từng lệnh.

Chuẩn bị: một file mẫu `test-model.rvt` có ít nhất 2 tầng, 20 cửa (trong đó vài cửa cùng hàng lệch
vài mm), 10 đoạn ống dài ngắn khác nhau, 1 tường bị ống xuyên qua, vài view thừa và vài warning; một
file `test-drawing.dwg` có layer trùng tên, layer rỗng, linetype chỉ dùng bởi layer.

### 4.1 HTTP Bridge

| # | Việc | Kỳ vọng |
|---|---|---|
| 1 | `GET /health` không token | 200, body chỉ có trạng thái và version |
| 2 | `POST /execute` không token | 401, mô hình không đổi |
| 3 | `POST /execute` token sai 5 lần | Lần 6 bị khoá 5 phút |
| 4 | `POST /execute` đúng token, `Content-Type: text/plain` | 401 |
| 5 | Gửi lệnh nặng rồi Ctrl-C client | Sau timeout, lệnh **không** chạy tiếp (lỗi #7) |
| 6 | Kết nối từ máy khác trong LAN | Từ chối (chỉ bind 127.0.0.1) |
| 7 | `GET /tools` đúng token | Danh sách lệnh khớp `CommandCatalog` |
| 8 | `POST /chat` "đánh số cửa" | Trả `AutoNumbering`, `dryRun:true`, `requiresConfirmation:true`, mô hình không đổi |

### 4.2 Từng lệnh Core

| Lệnh | Kịch bản | Kỳ vọng |
|---|---|---|
| `ParameterExport` | Xuất Doors 3 tham số | Mở bằng Excel trên Windows tiếng Việt: tên hiện đúng dấu; số dùng dấu chấm |
| `ParameterImport` | Sửa vài ô rồi nhập lại | Đúng số ô đã sửa; giá trị Double khớp (lỗi #1); mỗi ô bị bỏ qua có một dòng lý do (lỗi #2, #3) |
| `ParameterImport` | File chỉ có tiêu đề / ElementId không tồn tại | `Fail` rõ ràng, không ném exception |
| `AutoNumbering` | Cửa cùng hàng lệch vài mm | Đánh số trái→phải trong hàng (lỗi #5); `DryRun` liệt kê đủ |
| `AutoNumbering` | Tham số đích chỉ đọc | Báo đủ số phần tử bị bỏ qua kèm lý do |
| `BatchExport` | Xuất PDF+DWG toàn bộ sheet | Đủ số file; hai sheet trùng tên không ghi đè nhau; `dwgVersion` sai → báo lỗi rõ |
| `HealthReport` | Model có warning và view thừa | Báo cáo mở được, tên view có `<`, `&` không làm vỡ HTML |
| `RemoveUnusedViews` | `DryRun` rồi chạy thật | Danh sách xem trước khớp đúng những view bị xoá |
| `ProjectInit/*` | Config JSON mẫu | Level/Grid/Family/Project info đúng; chạy lần hai không nhân đôi |
| `SleeveAuto` | Ống xuyên tường | Sleeve đúng vị trí, đúng kích thước; ống chỉ chạm mép không sinh sleeve |
| `ElevationTag` | Ống nghiêng | Đáy/đỉnh/tim đúng; máy tiếng Việt ghi `3200.0` chứ không phải `3200,0` |
| `HangerAuto` | Đoạn 10 m spacing 2 m; đoạn 1 m | Đoạn dài: hanger cách đều; đoạn ngắn: đúng **một** hanger, không chồng |
| `PipeSplitter` | Đoạn 13 m, max 6 m | Cắt thành 6+6+1; đoạn 6,005 m không bị cắt ra mẩu 5 mm |
| `ConnectorCheck` | Model có connector hở | Đúng số lượng; view khoanh vùng mở được |
| `LayerExport`/`LayerImport` | DWG có layer tiếng Việt | Round-trip không mất dấu; layer trùng tên được cập nhật, không nhân đôi |
| `DrawingCleanup` | Layer hiện hành + linetype chỉ dùng bởi layer | Không xoá nhầm, không hỏng transaction (lỗi #6) |
| `RouteFromLines` | Tuyến chữ U + nhánh T vẽ bằng model line "DHCB-Route" | Duct liền mạch, elbow + tee đúng type, không warning "not connected"; đoạn ngắn hơn fitting → báo ElementId, phần còn lại vẫn dựng |
| `DevicePlacement` | Phòng 12×9 m, cột giữa phòng | Lưới cách tường ≥ margin, không có thiết bị trong cột, phủ đủ bán kính |
| `SizingProposal` → `ApplySizing` | Duct có lưu lượng 500 L/s | Đề xuất ~355 mm ở 1 Pa/m; áp lại đúng đoạn theo ElementId |
| `SystemColor` / `SystemName` | View template + 3 hệ | Filter tạo đúng, màu đúng hex; tên hệ `MEC-SA-01`; chạy lại không đổi tên đã đặt tay |
| `FlowNumbering` | Chọn AHU rồi chạy | Số tăng dọc trục chính, nhánh `1.1`, `1.2` |
| `ProjectFromTemplate` | Template .rte, thư mục trống | File central + workset đúng danh sách; file đã tồn tại → Fail rõ |
| `TransferStandards` | File chuẩn có 5 view template, 2 trùng tên | Chuyển 3, bỏ 2 có ghi lý do |
| `GridFromCsv` | CSV từ `DHCB_GRID_EXTRACT` | Trục đúng vị trí ±1 mm, tên A,B,C / 1,2,3; chạy lại không nhân đôi |
| `SheetBatchCreate` | CSV 5 sheet, 1 trùng số | Tạo 4, đặt view đúng, bỏ 1 có ghi lý do |
| `ParameterRuleCheck` | Rule Doors Mark `^D-\d{3}$` | HTML đúng số vi phạm, 3D view khoanh vùng |
| `ClashDetection` | Duct xuyên dầm | Báo đúng, chấp nhận vào `clash-accepted.json` → lần sau không báo |
| `ElevationUpdater` | Bật trong settings.json, kéo một ống | Tham số cao độ đổi theo; sửa 1 000 ống < 200 ms, vượt → tự tắt và báo |
| `CadLayerMap` | CSV layer AIA | `A-WALL-200` → tường 200, `E-LTG` cần xem |
| `SpecToConfig` | Thuyết minh .txt | JSON `levelSetup` đúng cao độ, `dryRun:true` |
| `AttributeExport`/`AttributeImport` | Block DOOR có MARK | Round-trip đúng theo Handle |
| `TextReplace` | MText có "TANG 1" | Dry-run liệt kê đúng, chạy thật đổi, attribute cũng đổi |
| `GridExtract` | Layer AXIS, bản vẽ mm | CSV đúng số trục |
| `LayerStandardCheck` | layer-rules.sample.json | HTML liệt kê layer sai tên |
| `DHCB_RUN` qua accoreconsole | Script từ BatchRunner | run.jsonl có đủ dòng, không hộp thoại |
| `SheetRename` | 20 sheet, mẫu `A-{Level}-{n:00}` | Số mới đúng thứ tự, không trùng, đổi chéo A↔B không lỗi |
| `RevisionOnSheets` | Revision 2 lên sheet A-1xx | Đúng sheet, chạy lại không nhân đôi |
| `StylePurge` | View template dùng ở 1 view + 3 không dùng | Chỉ xoá 3; `<Solid fill>` không bị đụng |
| `ColorByParameter` | Tường theo Fire Rating | Mỗi giá trị một màu, chú giải CSV đúng số lượng; `reset` trả về bình thường |
| `FamilyAudit` | Mẫu `DHCB_{Category:upper}_{Name}` | CSV đủ cột; đổi tên bỏ qua in-place |
| `WarningsExport` | Model có warning | CSV có ElementId mở được trong Excel tiếng Việt |
| `ParameterRuleCheck` + thresholds | warnings max 200 | Dòng "Model / warnings" xuất hiện khi vượt |
| `LayerTranslate` | layer-map.sample.csv, bản vẽ WALL/TUONG-200 | Entity sang A-WALL (kể cả trong block), layer nguồn rỗng bị xoá, CLAYER giữ |
| `DrawingCompare` | Hai bản của cùng DWG | Handle thêm/xoá/dời đúng; HTML mở được |
| `BlockQuantity` | Block DOOR có SIZE | BOM nhóm theo SIZE đúng số |
| `AttributeIncrement` | Mẫu `P-{n:000}` | Thứ tự trái→phải trên→dưới, `P-001…` |
| BatchRunner autodetect | Job lẫn file 2023 và 2024 | Mở Revit 2024, cảnh báo file 2023 |
| `SlopePipes` | 5 ống Sanitary DN100 nằm ngang, `checkOnly` rồi chạy thật | Báo 5 chưa đạt; sau khi chạy: dốc 1 %, đầu cuối hạ 60 mm/6 m; ống đã nối hai đầu báo lỗi rõ |
| `PipeKick` | Ống thẳng 3 m, offset 300 Up, cút 45° | 3 đoạn + 2 cút nối kín; thiếu cút 45° trong routing preference → báo, không rollback đoạn |
| `SystemBom` | Model có 2 hệ, tham số spool | CSV đúng tổng mét, số cây = ceil(m×1,05/6) |
| `AutoRoute` | 2 điểm cách 12 m, dầm chắn giữa | Tuyến né dầm, ≤ 4 lần rẽ, `buildRoute` dựng duct liền mạch |
| `ScheduleExport` | Door Schedule có header 2 dòng | CSV đủ header + body, tiếng Việt đúng trong Excel |
| `ViewportCopy` | Sheet nguồn có 1 legend + 1 schedule + 1 plan | Legend/schedule sang mọi sheet đích cùng toạ độ, plan báo bỏ qua |
| `DHCB_RUN` core-only | accoreconsole NETLOAD DhcbTools.AutoCAD.Core.dll | Không lỗi "assembly references AcMgd", run.jsonl có dòng |
| `PlotPdf` accoreconsole | Layout A3 | PDF sinh ra đúng thư mục |

### 4.3 Kiểm thử hồi quy trước mỗi lần phát hành

1. `dotnet test` xanh.
2. Chạy toàn bộ §4.2 trên `test-model.rvt` với Revit của phiên bản thấp nhất và cao nhất đang hỗ trợ
   (hiện là 2021 và 2025 — hai TargetFramework khác nhau, `ElementId` đổi kiểu giữa hai bản).
3. Chạy một job batch runner nhỏ (2 file × 3 step) và đối chiếu log.

## 5. Nợ kiểm thử đã biết

- Chưa có test tích hợp nào chạm Revit thật. Hướng khả dĩ khi cần: bộ test chạy **bên trong** Revit
  qua một add-in test runner, kích hoạt bằng journal file trên máy có license — cùng hạ tầng với
  batch runner ở §1 của đặc tả tính năng, nên hợp lý để làm ngay sau batch runner.
- `ParameterImport` đọc CSV theo từng dòng nên **không đọc được ô có xuống dòng bên trong dấu nháy**.
  Export hiện có thể sinh ra ô như vậy (tên phần tử chứa xuống dòng). Cần một hàm đọc CSV theo
  luồng ký tự trong `Shared.Logic` — kèm test — trước khi coi round-trip là an toàn tuyệt đối.
