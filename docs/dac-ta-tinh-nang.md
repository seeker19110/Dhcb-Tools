# Đặc tả chi tiết — giai đoạn 0–6 (tài liệu lịch sử)

> ⚠️ **Đây là bản đặc tả LỊCH SỬ của giai đoạn 0–6, và toàn bộ giai đoạn đó nay đã **làm xong**
> (kể cả P1/P2 của giai đoạn 7). Giữ lại vì nó ghi rõ *vì sao* từng tính năng được thiết kế như vậy,
> nhưng **không phải nguồn sự thật về hành vi hiện tại**: chỗ nào lệch thì tin mã nguồn,
> [`progress.md`](progress.md) (hiện trạng) và [`roadmap.md`](roadmap.md) (nguyên tắc + việc phía trước).
> Việc còn lại của dự án nằm ở `roadmap.md`, không phải ở đây.

Tài liệu này đặc tả những gì **khi viết ra thì chưa làm**, đủ chi tiết để một người khác cầm lên viết code mà không
phải hỏi lại. Hiện trạng nằm ở [`progress.md`](progress.md), thứ tự ưu tiên ở
[`roadmap.md`](roadmap.md), cơ sở kỹ thuật ở [`nghien-cuu-dhcb-revit-tools.md`](nghien-cuu-dhcb-revit-tools.md).
Kế hoạch kiểm thử ở [`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md).

## Quy ước chung cho mọi đặc tả dưới đây

1. **Chữ ký lệnh:** `CommandResult Execute(Document doc, TConfig config)` — không TaskDialog, không
   Selection, không WPF trong Core.
2. **`DryRun` mặc định `true`** với mọi lệnh có ghi vào mô hình (kể cả `ParameterImport` và `BatchExport`);
   ở chế độ này lệnh phải liệt kê đầy đủ dự định trong `Messages` và không mở transaction ghi.
3. **Một lệnh = một transaction.** `SetFailuresPreprocessor(new SilentFailuresPreprocessor())` **chỉ dùng cho
   batch** (`BatchJobRunner`); lệnh chạy tương tác (Ribbon, Bridge) phải để kỹ sư **thấy** cảnh báo của Revit —
   xem nguyên tắc 3 trong [`roadmap.md`](roadmap.md). Nuốt cảnh báo ở đường tương tác chính là nhóm lỗi mà
   giai đoạn 8.1 đi dọn.
4. **Không nuốt cảnh báo.** Mọi phần tử bị bỏ qua phải có một dòng trong `Messages` nêu rõ lý do;
   `CommandResult` trả về phải giữ nguyên `Messages` đã gom (đây chính là lỗi #2 trong `progress.md`).
5. **Số và văn bản:** ghi/đọc số qua `DhcbTools.Shared.Logic.NumericText`, CSV qua `CsvText`
   (UTF-8 có BOM), tên file qua `FileNaming`. Không tự viết lại các hàm này.
6. **Logic thuần tách khỏi Revit.** Thuật toán nào không cần `Document` thì đặt trong
   `DhcbTools.Shared.Logic` để test được trên CI Linux.
7. **Định nghĩa "xong" (DoD)** của mỗi mục dưới đây bao gồm: code + test (theo mục
   "Kiểm thử" của chính mục đó) + một dòng cập nhật `progress.md`.

---

# Giai đoạn 0 — Trả nợ kỹ thuật (phần còn lại)

## 0.1 Token xác thực cho HTTP Bridge (lỗi #8)

**Vấn đề:** `DhcbHttpBridge` mở cổng 8765 (Revit) / 8766 (AutoCAD) trên `localhost` không xác thực.
Mọi tiến trình trên máy — kể cả macro trong một file lạ, hay trang web đang mở nếu trình duyệt cho
phép — gửi được `POST /execute` với `dryRun:false`.

**Thiết kế:**

- Khi Bridge khởi động: đọc token ở `%APPDATA%\DHCB\bridge-token.txt`. Không có thì sinh mới bằng
  `BridgeAuth.GenerateToken()` (256 bit, base64url) và ghi file với quyền chỉ chủ sở hữu đọc được.
- Mọi request tới `/execute` và `/query` phải qua `BridgeAuth.IsAuthorized(token, authHeader, contentTypeHeader)`:
  đúng `Authorization: Bearer <token>` **và** `Content-Type: application/json`.
- `GET /health` không cần token nhưng chỉ trả `{"status":"ok","version":"..."}` — không lộ tên file
  đang mở, không lộ danh sách lệnh.
- Sai token → `401` với body `{"error":"unauthorized"}`, không nêu lý do chi tiết. Ghi log kèm thời
  điểm; **≥ 5 lần sai trong 60 giây thì khoá nhận request 5 phút** để chặn dò token.
- Bridge chỉ bind `127.0.0.1`, không bind `0.0.0.0` (kiểm tra lại prefix của `HttpListener`).
- `scripts/dhcb_agent.py` đọc token từ cùng đường dẫn, cho phép ghi đè bằng biến môi trường
  `DHCB_BRIDGE_TOKEN`.

**Kiểm thử:** `BridgeAuthTests` đã bao phần thuần (sinh token, tách header, so sánh thời gian hằng
số, ràng buộc Content-Type). Phần còn lại — 401, khoá tạm, bind localhost — kiểm bằng kịch bản thủ
công trong `dac-ta-kiem-thu.md` §4.1.

**DoD:** gọi `/execute` không kèm token trả 401 trên cả hai vỏ; `dhcb_agent.py` chạy được không cần
sửa tay; token không bao giờ nằm trong log hay trong body trả về.

## 0.2 Tách phần dùng chung (lỗi #9)

Đã tách xong phần **logic thuần**: `src/DhcbTools.Shared.Logic` (CSV, số, đánh số, hình học MEPF,
tên file, HTML, token) — không tham chiếu Revit lẫn AutoCAD, nên test chạy được trên CI Linux.

**Còn lại — cần một assembly thứ hai, `DhcbTools.Shared.Hosting`** (netstandard2.0, chỉ phụ thuộc
`System.Net.HttpListener` + `Newtonsoft.Json`), chứa:

| Kiểu | Nội dung | Đang bị nhân đôi ở |
|---|---|---|
| `CommandResult` | y nguyên bản hiện tại | `Core/CommandResult.cs`, `Core.AutoCAD/CommandResult.cs` |
| `ICoreCommand<TConfig, TDocument>` | thêm tham số kiểu document để dùng chung `Document` và `Database` | hai file `ICoreCommand.cs` |
| `Polyfills` | `IsExternalInit`, `RequiredMember`… | hai file `Polyfills.cs` |
| `HttpBridgeServer` | vòng lặp `HttpListener`, phân giải route, xác thực, đọc/ghi JSON, timeout | ~90% hai file `DhcbHttpBridge.cs` |

`HttpBridgeServer` nhận một delegate `Func<string, JObject, CommandResult>` để phần vỏ (Revit hoặc
AutoCAD) tự dispatch — phần **khác nhau thật sự** giữa hai vỏ chỉ là chỗ này và cách đưa việc vào
luồng UI (`ExternalEvent` bên Revit, `ExecuteInCommandContextAsync` bên AutoCAD).

**DoD:** không còn class trùng tên giữa hai Core; `git grep -c "class CommandResult"` trả về 1.

## 0.3 Gắn Hanger và PipeSplitter vào Ribbon + Bridge (lỗi #11)

Core command đã có và đã được test phần số học. Còn thiếu:

- **Ribbon:** hai nút trong panel MEPF của `DhcbTools.Revit/App.cs`, kèm `ExternalCommand` tương ứng
  trong `Commands/MepfCommands.cs`, theo đúng khuôn của nút Sleeve hiện có (đọc config JSON, hiện
  kết quả `DryRun` trước, hỏi xác nhận rồi chạy thật).
- **Bridge:** thêm `case "HangerAuto"` và `case "PipeSplitter"` trong `DispatchCommand`.
- **Tên lệnh phải khớp `CommandName`** đã khai báo trong Core (`"HangerAuto"`, `"PipeSplitter"`) —
  đây là khoá tra cứu của cả batch runner sau này.

**DoD:** cả hai lệnh gọi được từ Ribbon và từ `scripts/dhcb_agent.py`; không lệnh Core nào còn thiếu
điểm gọi (kiểm bằng một test đối chiếu danh sách `CommandName` với danh sách case trong dispatch —
xem `dac-ta-kiem-thu.md` §2.6).

## 0.4 DrawingCleanup: xoá nhầm và hỏng transaction (lỗi #6)

**Ba sửa đổi bắt buộc trong `Core.AutoCAD/DrawingCleanup/DrawingCleanupCommand.cs`:**

1. `CollectUsedLinetypeIds` phải duyệt **cả layer definition**, không chỉ entity: một linetype chỉ
   được dùng bởi layer sẽ bị coi là "không dùng" và bị xoá, làm mọi entity `ByLayer` đổi nét.
2. Loại trừ khỏi danh sách xoá: layer hiện hành (`Database.Clayer`), layer `0`, layer `Defpoints`,
   linetype `Continuous`, `ByLayer`, `ByBlock`, và mọi layer đang được tham chiếu bởi xref hoặc
   block definition.
3. Bọc `try/catch` **quanh từng item**, gom lỗi vào `Messages` rồi mới commit — hiện một `Erase()`
   thất bại làm hỏng cả transaction và mất toàn bộ phần đã dọn.

**Kiểm thử:** tách hàm quyết định thành `CleanupDecider.ShouldErase(name, isUsed, isCurrent, isSystem)`
thuần trong `Shared.Logic` để test bảng quyết định; phần AutoCAD kiểm thủ công theo §4.2.

## 0.5 Request timeout vẫn thực thi (lỗi #7)

**Vấn đề:** Bridge trả timeout sau 30 s nhưng item vẫn nằm trong hàng đợi; khi Revit rảnh nó vẫn
chạy dù client đã bỏ đi — với `dryRun:false` là sửa mô hình ngoài ý muốn.

**Thiết kế:** mỗi item trong hàng đợi mang một `CancellationTokenSource` và cờ `Abandoned`. Khi hết
thời gian chờ, Bridge đặt `Abandoned = true` **trước khi** trả `504` cho client. Vòng lặp thực thi
kiểm tra cờ ngay trước khi mở transaction: đã bỏ thì gỡ khỏi hàng đợi, ghi log
`"Bỏ qua <CommandName>: client đã timeout"`, không chạy. Lệnh đã mở transaction rồi thì chạy nốt —
huỷ giữa chừng nguy hiểm hơn là chạy hết.

**DoD:** kịch bản "gửi lệnh nặng rồi Ctrl-C client" không để lại thay đổi nào trong mô hình.

## 0.6 Hiệu năng collector (lỗi #10)

`ParameterExportCommand` và `AutoNumberingCommand` gọi `FilteredElementCollector` rồi lọc bằng LINQ
trong bộ nhớ. Đổi sang `ElementMulticategoryFilter` (dựng từ `HashSet<BuiltInCategory>` đã phân giải)
để Revit lọc ở tầng dưới, và `ElementLevelFilter` khi config có `LevelName`.

**Ngưỡng chấp nhận:** trên mô hình ~200 nghìn phần tử, xuất tham số 3 category phải < 5 giây (đo bằng
`Stopwatch` ghi vào `Messages` khi bật `config.Verbose`).

---

# Giai đoạn 1 — Batch runner chạy đêm

Đây là ưu tiên cao nhất: biến toàn bộ lệnh đã có thành giá trị chạy đêm.

## 1.1 Project `DhcbTools.BatchRunner`

Ứng dụng console chạy trên máy có license Revit, dùng **Revit trong chế độ có UI nhưng không người
trực** (không có headless chính thức): runner khởi động Revit qua `Autodesk.RevitAddIns` +
`ExternalDBApplication`, hoặc — đơn giản hơn cho bản đầu — chạy như một add-in được kích hoạt bởi
`journal file` do runner sinh ra. Chọn phương án journal cho bản đầu tiên vì không cần license
Design Automation.

**Dòng lệnh:**

```
DhcbTools.BatchRunner.exe --job jobs/nightly.json [--dry-run] [--log-dir logs] [--max-minutes 480]
```

## 1.2 Định dạng file job

```jsonc
{
  "name": "Chạy đêm - dự án Landmark",
  "revitVersion": 2024,
  "stopOnError": false,          // false: một file lỗi không chặn các file sau
  "saveMode": "SaveAs",          // None | Save | SaveAs (SaveAs ghi ra outputFolder, không đụng bản gốc)
  "saveOnError": false,          // false: không lưu file có bước lỗi
  "dwgVersion": "2018",          // chỉ dùng cho job AutoCAD: phiên bản SAVEAS
  "outputFolder": "D:/DHCB/nightly/{yyyy-MM-dd}",
  "files": [
    { "path": "P:/Landmark/ARC.rvt", "worksets": ["Shared Levels and Grids"] },
    { "path": "P:/Landmark/MEP.rvt", "detachFromCentral": true }
  ],
  "steps": [
    { "command": "HealthReport",  "config": { "outputPath": "{outputFolder}/{fileName}-health.html" } },
    { "command": "BatchExport",   "config": { "outputFolder": "{outputFolder}/pdf", "formats": ["Pdf"] } },
    { "command": "ConnectorChecker","config": { "create3dView": false } },   // create3dView mặc định false
    { "command": "SleeveAuto",    "config": { "dryRun": true } }
  ]
}
```

**Token thay thế trong chuỗi:** `{outputFolder}`, `{fileName}` (tên file không đuôi), `{yyyy-MM-dd}`,
`{HH-mm}`. Thay bằng một hàm thuần `JobTokens.Expand(text, context)` đặt trong `Shared.Logic`
(test được, xem §2.7 của tài liệu kiểm thử).

## 1.3 Luồng chạy một file

1. Mở file (`OpenDocumentFile` với `DetachFromCentralOption` khi được yêu cầu, `Audit = false`,
   `worksetsToOpen` theo config).
2. Với mỗi step: tra `command` trong bảng lệnh (khoá là `ICoreCommand.CommandName`), deserialize
   `config`, gọi `Execute`, ghi kết quả.
3. `saveMode`: `None` → đóng không lưu; `Save` → `doc.Save()`; `SaveAs` → lưu bản sao vào
   `outputFolder`, **luôn là mặc định an toàn cho bản đầu tiên**.
4. Đóng file, giải phóng bộ nhớ, sang file kế tiếp.
5. Một file lỗi (không mở được, crash) không được làm dừng cả lô khi `stopOnError:false`; ghi lỗi và
   đi tiếp.
6. Quá `--max-minutes` thì dừng sạch sẽ sau file đang chạy và ghi rõ những file chưa kịp chạy.

## 1.4 Log và báo cáo tổng hợp

- **Log dòng-JSON** (`logs/{yyyy-MM-dd}/run-HHmmss.jsonl` — mỗi lượt chạy một file), mỗi dòng một step:
  `{"time","file","command","success","affected","summary","messages":[],"errors":[],"elapsedMs"}`.
- **Báo cáo HTML tổng hợp** sau mỗi lần chạy: bảng file × step, ô xanh/đỏ, bấm vào mở chi tiết
  `Messages`. Dùng lại `HtmlText.Escape`.
- **Mã thoát:** `0` mọi step thành công; `1` có step lỗi; `2` lỗi cấu hình (không đọc được job).
  Task Scheduler dựa vào mã thoát này để gửi cảnh báo.

## 1.5 Chạy theo lịch

Kèm `scripts/install-nightly-task.ps1`: đăng ký Windows Task Scheduler chạy `--job` vào 23:00 hàng
đêm, chạy dưới tài khoản có license Revit, `--max-minutes 480`, ghi log ra thư mục dùng chung.

**DoD Giai đoạn 1:** một file job + một task hẹn giờ là đủ để sáng hôm sau có PDF, health report và
log kết quả sleeve/tag/hanger; chạy lại cùng job hai lần cho kết quả như nhau (idempotent với các
lệnh `DryRun`).

---

# Giai đoạn 2 — Khởi tạo dự án (phần còn lại)

## 2.1 `ProjectFromTemplateCommand`

Hiện `ProjectInit/*` giả định file đã tồn tại. Cần lệnh tạo file mới:

```jsonc
{
  "templatePath": "P:/Standards/DHCB_ARC_2024.rte",
  "outputPath": "P:/{projectCode}/{projectCode}-{discipline}-R{revitVersion}.rvt",
  "projectCode": "LMK",
  "discipline": "ARC",
  "createCentral": true,
  "worksets": ["Shared Levels and Grids", "Kiến trúc", "Kết cấu", "MEP", "Liên kết CAD"]
}
```

Luồng: `Application.NewProjectDocument(templatePath)` → bật worksharing
(`doc.EnableWorksharing`) → tạo workset theo danh sách (bỏ qua tên đã tồn tại, ghi `Messages`) →
`SaveAs` với `WorksharingSaveAsOptions.SaveAsCentral = true` → đóng.
Tên file sinh qua `FileNaming.ApplyPattern` mở rộng thêm token `{projectCode}`, `{discipline}`,
`{revitVersion}`. **Không ghi đè file đã tồn tại** — trả `CommandResult.Fail` nêu rõ đường dẫn.

## 2.2 `TransferStandardsCommand`

Chuyển browser organization, view template, filter, line style, object style từ file chuẩn sang file
đích. API: `doc.Import(...)` không có sẵn cho mọi loại → dùng `CopyPasteOptions` với
`ElementTransformUtils.CopyElements` theo từng nhóm loại, xử lý trùng tên bằng
`IDuplicateTypeNamesHandler` trả `DuplicateTypeAction.UseDestinationTypes` (giữ bản đích) và ghi rõ
từng cái bị bỏ qua.

## 2.3 Grid/Level sinh từ bản CAD hoặc Excel

- **Từ CAD:** đọc file DWG liên kết, lấy các đoạn thẳng thuộc layer do config chỉ định
  (`gridLayer`, mặc định `"AXIS"`), gom các đoạn thẳng hàng theo dung sai, sinh `Grid` và đặt tên
  theo quy tắc (`1,2,3…` cho trục ngang, `A,B,C…` cho trục dọc, cấu hình được).
- **Từ Excel/CSV:** cột `Name,Elevation` cho level; `Name,X1,Y1,X2,Y2` cho grid. Đọc bằng `CsvText`,
  số qua `NumericText` (bắt buộc — đây đúng là chỗ lỗi locale hay tái diễn).
- Phần gom đoạn thẳng và sinh tên trục là **logic thuần** → đặt ở `Shared.Logic.GridNaming` +
  `GridClustering`, test không cần Revit.

## 2.4 `SheetBatchCreateCommand`

Tạo sheet hàng loạt từ bảng: `SheetNumber,SheetName,TitleBlockType,ViewsToPlace`. Đặt view vào sheet
theo vị trí cấu hình được (`center`, hoặc toạ độ mm). Bỏ qua sheet đã tồn tại và ghi `Messages`.

---

# Giai đoạn 3 — MEPF (phần còn lại)

## 3.1 Routing mức A — bán tự động theo tuyến vẽ tay

**Đầu vào:** model line / detail line kỹ sư đã vẽ (chọn theo `LineStyleName` trong config), cộng:

```jsonc
{
  "systemType": "Supply Air",
  "lineStyleName": "DHCB-Route",
  "levelName": "L3",
  "elementType": "Duct",           // Duct | Pipe | CableTray | Conduit
  "typeName": "Rectangular Duct - Mitered Elbows",
  "sizeMm": { "width": 400, "height": 200 },
  "offsetMm": 3200,
  "connectToNearestMm": 300,       // tự nối vào đầu nối có sẵn trong bán kính này
  "dryRun": true
}
```

**Thuật toán:**

1. Gom các line đã chọn thành **chuỗi liên tục** (graph: đỉnh = điểm đầu/cuối trong dung sai 1 mm).
   Nhánh rẽ được phép; chu trình thì báo lỗi rõ và bỏ qua nhánh đó.
2. Với mỗi cạnh: `Duct.Create` / `Pipe.Create` / `CableTray.Create` theo type và size.
3. Tại mỗi đỉnh bậc 2: `NewElbowFitting`; bậc 3: `NewTeeFitting`; bậc 4: `NewCrossFitting`.
   **Đọc `RoutingPreferenceManager` của type** để lấy fitting đúng — không hard-code family name.
4. Fitting không dựng được (góc quá nhỏ, đoạn ngắn hơn chiều dài fitting): **không rollback cả
   transaction** — bỏ qua riêng chỗ đó, để hở connector, ghi `Messages` kèm ElementId và toạ độ, và
   đếm vào `Errors`. Kỹ sư sửa tay chỗ đó; phần còn lại vẫn dùng được.
5. `connectToNearestMm > 0`: sau khi dựng, quét connector hở và nối vào connector có sẵn gần nhất
   trong bán kính, chỉ khi cùng `Domain` và cùng `SystemType`.

**Phần thuần tách ra được (bắt buộc test):** dựng graph từ danh sách đoạn thẳng, phân loại bậc đỉnh,
phát hiện chu trình, thứ tự duyệt — `Shared.Logic.RouteGraph`.

**DoD:** vẽ một tuyến chữ U có một nhánh rẽ T trên model mẫu → dựng ra duct liền mạch, elbow + tee
đúng type, không warning "Elements are not connected".

## 3.2 Routing mức B — tự động cục bộ theo quy tắc

Rải thiết bị đầu cuối theo pattern rồi nối về trục chính:

```jsonc
{
  "deviceFamily": "Sprinkler - Pendent",
  "roomFilter": { "levelName": "L3", "nameContains": "Văn phòng" },
  "pattern": { "type": "grid", "spacingXMm": 3000, "spacingYMm": 3000, "marginMm": 1500 },
  "maxCoverageRadiusMm": 2300,
  "connectToMainWithin": 6000,
  "dryRun": true
}
```

**Thuật toán:** lấy biên phòng (`Room.GetBoundarySegments`) → sinh lưới điểm trong biên, cách tường
tối thiểu `marginMm` → loại điểm nằm trong lỗ/cột → kiểm tra phủ (mọi điểm trong phòng cách một đầu
phun ≤ `maxCoverageRadiusMm`, nếu thiếu thì chèn thêm điểm và ghi `Messages`) → đặt family → nối
nhánh về trục chính gần nhất bằng thuật toán của §3.1.

**Phần thuần:** sinh lưới điểm trong đa giác + kiểm tra phủ → `Shared.Logic.DevicePattern`
(đầu vào: danh sách đỉnh đa giác; đầu ra: danh sách điểm) — đây là phần dễ sai nhất và test được
hoàn toàn không cần Revit.

## 3.3 Sizing theo hệ (chỉ ở mức đề xuất)

Tính kích thước đề xuất cho từng đoạn theo lưu lượng: `Duct` theo phương pháp ma sát đều
(`Pa/m` cấu hình được), `Pipe` theo vận tốc tối đa. **Không tự sửa mô hình**: kết quả ghi ra CSV
`ElementId,SystemName,FlowLps,CurrentSizeMm,SuggestedSizeMm,Reason` để kỹ sư duyệt, và một lệnh
riêng `ApplySizingCommand` áp lại đúng file CSV đó (cùng khuôn ParameterExport ↔ ParameterImport).

**Phần thuần:** bảng tra kích thước chuẩn + công thức → `Shared.Logic.DuctSizing`, `PipeSizing`.
Test bằng bảng giá trị lấy từ tiêu chuẩn (ghi rõ nguồn trong test).

## 3.4 Tô màu/filter theo hệ và cập nhật System Name

Tạo `ParameterFilterElement` + `OverrideGraphicSettings` cho từng hệ theo bảng màu trong config
(`{"Supply Air":"#0070C0", ...}`), áp vào view template chỉ định. Đồng thời điền `System Name` theo
quy tắc `{Discipline}-{SystemAbbreviation}-{Zone}-{Số}`.

**Phần thuần:** phân giải mã màu hex → RGB, và sinh tên hệ theo quy tắc → `Shared.Logic.SystemNaming`.

## 3.5 Đánh số thiết bị/đoạn theo tuyến (connector graph)

Khác với `AutoNumbering` hiện có (sắp theo toạ độ), lệnh này sắp theo **thứ tự dòng chảy**: bắt đầu
từ thiết bị nguồn (AHU, tủ điện, bơm) do config chỉ định, duyệt theo connector (BFS/DFS cấu hình
được), đánh số dọc theo tuyến; gặp nhánh thì đánh số theo quy tắc `{Số nhánh}.{Số trong nhánh}`.

**Phần thuần:** duyệt đồ thị và sinh nhãn phân cấp → `Shared.Logic.FlowNumbering` (đầu vào là danh
sách cạnh dạng `(id, id)` — hoàn toàn không cần Revit).

---

# Giai đoạn 4 — Tự động hoá cấp 2 (theo sự kiện)

## 4.1 `IUpdater` điền cao độ thời gian thực

Đăng ký `UpdaterId` cố định, trigger `ElementChangeType.GeometryChange` trên các category MEP.
Khi phần tử đổi hình học → tính lại cao độ đáy/đỉnh/tim bằng đúng `MepLayout.Elevations` mà lệnh
cấp 1 dùng, ghi vào cùng tham số.

**Bắt buộc trước khi bật mặc định:**

- Đo hiệu năng: thời gian xử lý một transaction sửa 1 000 phần tử phải < 200 ms; vượt thì tự tắt
  updater và báo một lần.
- Có công tắc trong `%APPDATA%\DHCB\settings.json` (`"updaters": {"elevation": false}`), mặc định
  **tắt** cho tới khi đo xong trên dự án thật.
- Không bao giờ ném exception ra ngoài `Execute` của updater — một lỗi ở đây làm hỏng transaction
  của người dùng.

## 4.2 Checker tham số thiếu và đặt tên sai quy tắc

Quét theo bộ quy tắc trong JSON: `{"category":"Doors","parameter":"Mark","required":true,"pattern":"^D-\\d{3}$"}`.
Kết quả ra báo cáo HTML cùng khuôn `HealthReport`. **Phần thuần:** khớp regex + gom vi phạm theo
category → `Shared.Logic.RuleChecker`.

## 4.3 Clash detection nội bộ

Quét cặp category (ví dụ Duct × Structural Framing) bằng `ElementIntersectsSolidFilter`, lọc thô
bằng `MepLayout.BoundingBoxesIntersect` trước. Kết quả: báo cáo HTML + tuỳ chọn tạo 3D view khoanh
vùng cho từng va chạm (dùng lại cơ chế của `ConnectorCheckerCommand`).

> Hành vi hiện tại (khác bản đặc tả gốc ở trên): `ConnectorChecker` **mặc định không tạo 3D view**
> (`create3dView` mặc định `false`, và lệnh có `dryRun`); `ClashDetection` cùng `ParameterRuleCheck` chỉ tạo
> view khi **chạy thật**, không tạo ở lần xem trước.
Bỏ qua cặp đã được đánh dấu "chấp nhận" trong file `clash-accepted.json` (khoá là cặp ElementId +
hash vị trí, để cặp cũ không quay lại báo sau mỗi lần chạy đêm).

**Xuất BCF 2.1** (đề xuất B3 của [`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md)):
khai thêm `bcfPath` (ví dụ `C:\...\clash.bcf`, tuỳ chọn `bcfProjectName`) thì ngoài HTML, lệnh ghi một
file BCF mở thẳng được trong Navisworks / Solibri / BIMcollab — mỗi va chạm là một topic có camera phối
cảnh nhìn vào tâm va chạm và hai phần tử liên quan (ElementId hai phía, phía link ghi kèm tên link).
GUID của topic sinh từ chính `key` trong `clash-accepted.json`, nên **xuất lại cùng một va chạm vẫn ra
đúng topic cũ** thay vì đẻ ra vấn đề mới bên phần mềm điều phối. Toạ độ trong file là **mét** theo chuẩn
BCF. Ghi BCF hỏng thì chỉ báo trong `Messages`, không làm hỏng lượt quét đã ghi HTML xong.
**Phần thuần:** `Shared.Logic.Bcf` (`BcfWriter` — zip + XML, không đụng `Document` nào).

---

# Giai đoạn 5 — Lớp AI

Nguyên tắc không đổi: **AI chỉ sinh đề xuất**, mọi thay đổi mô hình đi qua transaction của tool và
có kỹ sư duyệt. API key lưu ở biến môi trường hoặc DPAPI, không commit.

## 5.1 Map layer/block CAD → Revit type

Gửi danh sách layer (lấy bằng `LayerExportCommand` đã có) + danh mục type của template, yêu cầu trả
JSON đúng schema:

```jsonc
{ "mappings": [ { "layer": "A-WALL-200", "revitType": "Basic Wall: DHCB-Tuong 200", "confidence": 0.93, "reason": "..." } ] }
```

Ràng buộc: chỉ nhận `revitType` **có thật** trong danh sách đã gửi (kiểm tra lại phía tool, loại bỏ
dòng bịa ra và ghi `Messages`); `confidence < 0.7` thì đánh dấu vàng để kỹ sư xem trước. Kết quả ghi
ra CSV để duyệt trong Excel rồi mới áp.

## 5.2 PDF thuyết minh → config khởi tạo dự án

Gửi thẳng PDF lên API, yêu cầu trả đúng schema config mà `ProjectInit/*` đã nhận (level, grid,
project info). Bắt buộc validate bằng JSON Schema trước khi cho chạy; sai schema thì hiện lỗi kèm
đoạn văn bản gốc đã trích, không tự đoán.

## 5.3 Phân tích báo cáo clash/warning chạy đêm

Đầu vào là log JSONL của batch runner (`logs/{yyyy-MM-dd}/run-HHmmss.jsonl`, §1.4). Chạy theo lô, xuất bản tóm tắt tiếng Việt:
nhóm warning theo nguyên nhân, đề xuất thứ tự xử lý. Chỉ đọc, không sửa mô hình.

## 5.4 Ra lệnh bằng tiếng Việt (tool use)

Whitelist đúng các `CommandName` đã có; mô hình chỉ được chọn lệnh + điền config, không được sinh
code. Mọi lệnh chạy qua Bridge với `dryRun:true` trước, hiển thị kết quả xem trước, kỹ sư bấm xác
nhận mới chạy thật. Endpoint `/query` (đọc ngữ cảnh, không ghi) là nguồn ngữ cảnh duy nhất.

---

# Giai đoạn 6 — Tuỳ nhu cầu

## 6.1 Routing mức C — pathfinding 3D né va chạm

**Giới hạn phạm vi ngay từ đầu**: một hệ, một tầng, một hành lang. A* trên lưới 3D bước 100 mm,
chi phí phạt khi đổi hướng (mỗi lần rẽ = một fitting) và khi đi gần kết cấu. Chướng ngại lấy từ
bounding box của kết cấu + các hệ đã có. Kết quả trả về là **tuyến (polyline)** rồi đưa vào routing
mức A ở §3.1 để dựng — không dựng thẳng, để hai phần kiểm thử độc lập được.
**Phần thuần:** toàn bộ A* → `Shared.Logic.PathFinder3D`, test bằng lưới dựng sẵn có chướng ngại.

## 6.2 Mở rộng HTTP Bridge thành MCP server

Giữ nguyên tầng thực thi; thêm lớp giao thức MCP: `tools/list` sinh từ chính bảng `CommandName` +
JSON Schema của từng config, `tools/call` gọi lệnh. Xác thực dùng lại token ở §0.1.

---

# Phụ lục — thứ tự đề nghị

1. §0.1 token Bridge, §0.3 gắn Hanger/PipeSplitter (nhỏ, chặn nhiều thứ khác).
2. §0.2 tách `Shared.Hosting` — **trước** khi thêm routing, vì routing sẽ nhân đôi chi phí trùng lặp.
3. §0.4–0.6 các lỗi còn lại.
4. Giai đoạn 1 batch runner — điểm hoà vốn của mọi thứ đã làm.
5. §3.1 routing mức A, rồi §3.3 sizing (đề xuất), rồi phần còn lại theo roadmap.
