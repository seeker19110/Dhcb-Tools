# Nghiên cứu: rút ngắn chuỗi Thiết kế → BIM → Shop → Thi công → Hoàn công

> Ngày khảo sát: 2026-09-04, trên `main` @ `749d642`. Câu hỏi đặt ra: **tính năng nào giúp kỹ sư đi
> nhanh nhất từ bản thiết kế tới hồ sơ hoàn công**, ưu tiên thứ tự động hoặc bán tự động được.
>
> Cách làm: (a) đếm lệnh **có thật trong `CommandCatalog`** rồi xếp theo chặng — không đếm theo tài liệu;
> (b) đối chiếu với tool đang bán ngoài thị trường cho từng chặng; (c) tra lại văn bản pháp lý còn hiệu lực
> tại thời điểm khảo sát. Bổ sung cho [`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md)
> (khảo sát theo *tool*) — tài liệu này khảo sát theo *chặng công việc*.

## 1. Bản đồ: 43 lệnh Revit đang nằm ở đâu trên chuỗi

Xếp toàn bộ lệnh công khai trong `CommandCatalog` (45 mục, trừ `RunTests` và `UsageReport` là công cụ nội bộ)
vào năm chặng:

| Chặng | Số lệnh | Lệnh |
|---|---|---|
| **1. Thiết kế → BIM** (dựng & chuẩn hoá mô hình) | **27** | ProjectFromTemplate, TransferStandards, LevelSetup, GridSetup, GridFromCsv, FamilyLoader, ProjectInfo, ParameterExport/Import, AutoNumbering, CadLayerMap, SpecToConfig, DictionaryLearn, SleeveAuto, ElevationTag, HangerAuto, PipeSplitter, RouteFromLines, DevicePlacement, SizingProposal, ApplySizing, SystemColor, SystemName, FlowNumbering, SlopePipes, PipeKick, AutoRoute |
| **2. Kiểm & vệ sinh mô hình** | **9** | RemoveUnusedViews, HealthReport, StylePurge, FamilyAudit, WarningsExport, ParameterRuleCheck, ClashDetection, ConnectorChecker, ColorByParameter |
| **3. Shop / hồ sơ bản vẽ** | **7** | SheetBatchCreate, SheetRename, RevisionOnSheets, ViewportCopy, ScheduleExport, BatchExport, SystemBom |
| **4. Thi công** | **0** | — |
| **5. Hoàn công** | **0** | — |

Phía AutoCAD (15 lệnh) cũng vậy: toàn bộ nằm ở chặng 1–2, trừ `BlockQuantity` chạm nhẹ vào bóc khối lượng.

**Kết luận thẳng:** 36/43 lệnh dồn vào hai chặng đầu. Chặng 3 có 7 lệnh nhưng **không lệnh nào tạo ra nội dung
bản vẽ** — `SheetBatchCreate` và `ViewportCopy` chỉ *đặt* view đã có sẵn lên sheet; `git grep` xác nhận trong
toàn bộ `src/` không có `IndependentTag`, `NewDimension`, `AssemblyInstance`, `ViewSection.CreateSection`.
Nghĩa là mô hình có đúng đến mấy thì kỹ sư vẫn ngồi cắt mặt cắt, gắn tag, ghi kích thước bằng tay. Chặng 4 và 5
trống hoàn toàn — mà đó lại là hai chặng luật vừa siết.

Đây không phải lỗi lập kế hoạch: giai đoạn 0–7 chọn bề rộng ở chặng 1–2 là đúng thứ tự (không có mô hình đúng
thì không có gì để ra bản vẽ). Nhưng chuỗi giá trị đang dừng ngay tại chỗ mô hình vừa đúng, mà phần thời gian
lớn nhất của một dự án nằm sau chỗ đó.

## 2. Bốn ràng buộc mới từ luật và chuẩn mở — đổi thứ tự ưu tiên

Tra lại tại thời điểm khảo sát, có **hai** nghị định cùng hiệu lực **01/7/2026** chạm trực tiếp vào chuỗi này.
`roadmap.md` §11 lúc đó mới nhắc cái thứ nhất; **đã viết lại theo cả hai ngày 2026-09-04** — mục 11 hiện là
bản chi tiết hơn bảng này (số điều, số phụ lục), hai tài liệu phải sửa cùng nhau nếu luật đổi tiếp:

| Văn bản | Nội dung chạm tới DHCB | Hệ quả |
|---|---|---|
| **NĐ 217/2026/NĐ-CP** (thay NĐ 175/2024) | BIM bắt buộc với công trình mới **từ cấp II trở lên**, từ bước báo cáo khả thi (bỏ điều kiện "nhóm B trở lên"). Chủ đầu tư phải **cung cấp dữ liệu BIM cho cơ quan chuyên môn**; mô hình phải **cập nhật sau hoàn công** rồi chuyển cho đơn vị vận hành; cấp I phải lập **CDE**. Dữ liệu BIM có giá trị pháp lý tương đương hồ sơ giấy khi cơ quan quản lý đủ hạ tầng | Mô hình hoàn công thành **sản phẩm phải nộp**, không còn là việc nội bộ. Cần đường "kiểm trước khi nộp" theo chuẩn đọc được bằng máy |
| **NĐ 207/2026/NĐ-CP** ngày 15/6/2026 (thay **NĐ 06/2021**, hiệu lực 01/7/2026, quy định chi tiết Luật Xây dựng số 135/2025/QH15, **11 phụ lục**) | Nhật ký thi công (Phụ lục II, nghĩa vụ nhà thầu thi công theo Điều 15) phải ghi **đồng thời với sự kiện, không ghi bù**. Chấp nhận nhật ký điện tử **với điều kiện**: dấu thời gian **không thể chỉnh sửa ngược**, cơ chế xác nhận của các bên, sao lưu độc lập. **Mẫu dấu bản vẽ hoàn công: Phụ lục IIb, hai mẫu** (Mẫu 2 cho thầu chính/thầu phụ, EPC, chìa khoá trao tay). Hồ sơ hoàn thành công trình ở **Điều 28 + Phụ lục VII**, trách nhiệm lập thuộc **chủ đầu tư**. **Điều 11**: hồ sơ điện tử phải trích xuất, **in ra giấy và được chủ đầu tư xác nhận** khi cơ quan có thẩm quyền yêu cầu | ✅ `roadmap.md` §11 **đã viết lại theo văn bản này (2026-09-04)**, không còn chỗ nào trong repo căn cứ vào NĐ 06/2021. Đổi lại: yêu cầu "dấu thời gian không sửa ngược" mô tả gần đúng thứ batch runner đã ghi — xem đề xuất C1 |
| **IDS 1.0** (buildingSMART, chuẩn chính thức từ 01/6/2024) | Định dạng XML khai yêu cầu thông tin, kiểm tự động được, **cho kết quả giống nhau ở mọi phần mềm kiểm** | Thay vì DHCB tự định nghĩa checkset JSON riêng ở 11.1, đọc file IDS: chủ đầu tư khai một lần, kiểm được bằng cả DHCB lẫn IfcTester/Solibri |
| **BCF 2.1/3.0** (buildingSMART) | Định dạng trao đổi *vấn đề* kèm góc nhìn; mọi phần mềm điều phối đều mở được | Báo cáo va chạm HTML của DHCB hiện chỉ DHCB đọc. Xuất `.bcf` là ra khỏi ốc đảo |

Điểm đáng chú ý: **hai yêu cầu khó nhất của luật lại rơi đúng vào chỗ DHCB mạnh nhất.** "Dấu thời gian không
thể chỉnh sửa ngược" là bài toán thuần (băm nối chuỗi), và "kiểm trước khi nộp" là thứ `ParameterRuleCheck` +
`BatchExport` + batch runner đêm đã làm phần lớn.

## 3. Đối chiếu thị trường theo chặng

| Chặng | Ai đang bán gì | DHCB thiếu gì |
|---|---|---|
| 3 — Shop | **Victaulic Tools for Revit** (BOM spool, tạo bản vẽ spool từ view 3D), **eVolve MEP** (chọn phần tử → tự sinh spool sheet/view/schedule), **GTP Stratus** (nối Revit/AutoCAD sang máy gia công, TigerStop/PypeServer) | Toàn bộ đường **assembly → view → sheet**. `SystemBom` đã có tham số spool nhưng dừng ở bảng CSV |
| 3→4 — Định vị | **Trimble Field Points**, **Autodesk Point Layout** (điểm định vị trong Revit → FieldLink/iCON, máy toàn đạc robot) | Không có gì. Trắc đạc đang gõ tay toạ độ từ bản vẽ vào máy |
| 4 — Thi công | Navisworks/Synchro (4D), BIMcollab/Revizto (điều phối vấn đề), phần mềm dự toán nội địa (Dự toán GXD, Escon) | Trạng thái thi công trên phần tử; khối lượng ra **mã hiệu định mức** (TT 12/2021, sửa bởi TT 09/2024); xuất BCF |
| 5 — Hoàn công | Ở Việt Nam chủ yếu làm tay: đóng dấu hoàn công, ghép hồ sơ, sửa mô hình theo thực tế | Toàn bộ |

## 4. Mười một đề xuất

Mỗi mục ghi: **việc tay hôm nay → lệnh đề xuất → API dựa vào đâu → tầng thuần test được → rủi ro**.
Tầng thuần là bắt buộc theo nguyên tắc 5 của `roadmap.md`.

### Đợt A — chặng 3, biến mô hình đã đúng thành bản vẽ gia công

| # | Lệnh | Việc tay thay thế | Tầng thuần | Rủi ro | Cỡ |
|---|---|---|---|---|---|
| A1 ✅ 🧪 | **`SetoutExport`** — mã nguồn 2026-09-05, chờ chạy thật | Trắc đạc đọc bản vẽ, **gõ tay** toạ độ tim cột / lỗ mở / giá đỡ vào máy toàn đạc | `Setout/SetoutPlanner` + `SetoutCsv` + `SetoutDxf` + `Geometry/GridIntersections` (thứ tự cột theo máy `PNEZD`/`PENZD`, đơn vị, làm tròn, tên điểm qua `NamePattern` đã có, chống trùng, giao trục) — 50 ca test | **Thấp nhất cả danh sách** — chỉ đọc, không transaction | 2–3 ngày |
| A2 | **`OpeningReport`** + ghi kích thước tới trục | Lập bảng lỗ mở gửi kết cấu duyệt; ghi dim từ sleeve tới hai trục gần nhất | `Grid/GridProximity` (chọn 2 trục gần nhất, khoảng cách có dấu) — dùng lại `GridClustering`/`GridNaming` | Thấp cho phần bảng; trung bình cho phần dim | 3–4 ngày |
| A3 | **`AssemblySpool`** | Gom cụm → cắt view → lập sheet → đánh số cho từng spool | `SheetLayoutPlanner` (tách phần tính toạ độ viewport đang nằm trong `SheetBatchCreate`) | Trung bình — phụ thuộc template view/sheet từng dự án | ~1 tuần |
| A4 | **`TagAll`** | Gắn tag ống/thiết bị/phòng trên hàng chục view, rồi **kéo tay từng cái cho hết chồng** | `Tag/TagPlacement` (tránh chồng nhãn 2D) | Trung bình | 3–4 ngày |

**A1 — `SetoutExport` (toạ độ định vị ra máy toàn đạc).** ✅ **Đã có mã nguồn 2026-09-05** — xem
[`toa-do-dinh-vi.md`](toa-do-dinh-vi.md); hai điều chỉnh so với đề xuất gốc: không có "bảng mẫu máy" nào
để bảo trì mà kỹ sư gõ thẳng **thứ tự cột** máy nhận (`PNEZD`, `PENZD`…), và chiều của
`GetTotalTransform()` được **tự kiểm** bằng `GetProjectPosition` tại hai điểm thay vì tin tài liệu API.
🧪 Chưa chạy thật trong Revit. Đề xuất gốc: chọn category + bộ lọc → lấy điểm đặc trưng (tâm
sleeve, tim cột, đầu/cuối giá đỡ, điểm nối thiết bị) → đổi sang toạ độ Survey → CSV theo mẫu máy
(`Tên,N,E,Z,Mô tả`) và DXF điểm cho máy đời cũ. API: `Document.ActiveProjectLocation.GetTotalTransform()`
(dùng `.Inverse` để đưa về hệ toạ độ chung); `BasePoint` có `IsShared = true` là survey point. Đây là mục
**giá trị trên công sức cao nhất**: không ghi gì vào mô hình nên gần như không có rủi ro, mà thay đúng khâu có
sai số đắt nhất — gõ nhầm một chữ số toạ độ là đục lại bê tông. Kiểm ngược được ngay bằng `element_geometry`
đã có (trả toạ độ mm), tức agent tự chứng minh được kết quả.

**A2 — `OpeningReport`.** `SleeveAuto` đã đặt **345 sleeve thật** trên Snowdon HVAC ([`bang-chung-test.md`](bang-chung-test.md) §14)
và 445 ở §22 — nhưng việc *sau* đó vẫn làm tay: bảng lỗ mở (tầng, hai trục gần nhất + khoảng cách, cao độ
tim/đáy, DN, hệ, cấu kiện chủ) gửi kết cấu duyệt, rồi ghi kích thước lên mặt bằng. Phần bảng chỉ đọc nên làm
trước; phần dim dùng `Document.Create.NewDimension(view, line, referenceArray)` và **phải báo rõ khi không lấy
được `Reference` hợp lệ** thay vì lặng lẽ bỏ qua — đúng bài học `E-PARAM-MISSING` của 9.2.

**A3 — `AssemblySpool`.** Đây là thứ Victaulic/eVolve/Stratus bán, và Revit API cho làm đủ:
`AssemblyInstance.Create(doc, elementIds, namingCategoryId)` (phải commit transaction trước khi thao tác tiếp),
rồi `AssemblyViewUtils` có sẵn `Create3DOrthographic`, `CreateDetailSection`, `CreateSingleCategorySchedule`,
`CreatePartList`, `CreateMaterialTakeoff`, `CreateSheet` — tất cả trừ `CreateSheet` đều có nạp chồng nhận
template. Gom phần tử theo tham số spool mà `SystemBom` **đã có** (`SpoolParameter`), nên đây là phần nối tiếp
tự nhiên chứ không phải nhánh mới. `dryRun` phải liệt kê "sẽ tạo N assembly, N×4 view, N sheet" trước khi ghi —
lệnh này tạo nhiều phần tử nhất trong cả bộ.

### Đợt B — chặng 4, thi công

| # | Lệnh | Việc tay thay thế | Tầng thuần | Ghi chú |
|---|---|---|---|---|
| B1 ✅ 🧪 | **`ConstructionStatus`** + **`ProgressReport`** — mã nguồn 2026-09-05, chờ chạy thật | Nhập trạng thái lắp đặt/nghiệm thu vào Excel rời, vẽ tay biểu đồ tiến độ | `Progress/StatusRoll` + `WeeklyProgress` + `ConstructionStatusValue` + `ProgressCsv` (gộp theo tầng/hệ/category và theo tuần, % theo số lượng **và** theo chiều dài) — 41 ca test | **Rẻ nhất**: ghép `ParameterImport` + `ColorByParameter` + `snapshot` đã có |
| B2 | **`QtoExport`** | Bóc khối lượng rồi gõ lại vào phần mềm dự toán | `Qto/QtoMapTable` + `QtoAggregator` | Bảng map category/type → **mã hiệu công tác** để **ngoài repo** (`configs/qto-map.sample.json`), giống `dictionary.json` |
| B3 ✅ | **BCF 2.1 — đầu ra `bcfPath` của `ClashDetection`** | Chụp màn hình va chạm dán vào Word gửi tư vấn | `Bcf/BcfWriter` — **100% thuần** | Dùng chung cho `ClashDetection`, `ParameterRuleCheck`, `WarningsExport` |

**B1 — trạng thái thi công và báo cáo tiến độ.** ✅ **Đã có mã nguồn 2026-09-05** — xem
[`tien-do-thi-cong.md`](tien-do-thi-cong.md). Ba điều chốt bằng test vì đó là chỗ báo cáo tiến độ hay nói
dối: mẫu số gồm **cả cấu kiện chưa ai ghi nhận**; **"đang lắp" không có trọng số** (không phải nửa cái
ống); phần đã lắp mà **không có ngày** đếm riêng vì không vẽ được lên trục thời gian. Ngoài dự tính ban
đầu, phần đắt nhất hoá ra không phải phép tính mà là **từ vựng trạng thái**: file CSV do người gõ tay ngoài
công trường nên phải nhận cả tiếng Việt có dấu, không dấu lẫn tiếng Anh — mà chữ lạ thì vẫn phải báo đúng
số dòng. 🧪 Chưa chạy thật trong Revit. Đề xuất gốc: nó gần như không có mã mới ở tầng Revit. Trạng thái vào bằng CSV hiện
trường (mã cấu kiện → trạng thái/ngày/người) qua `ParameterImport`; tô màu bằng `ColorByParameter`; ảnh bằng
`snapshot`. Phần phải viết chỉ là lệnh gộp và tầng thuần tính phần trăm.

**B2 — ranh giới phải giữ.** DHCB xuất **khối lượng + mã hiệu + căn cứ (id phần tử)**, *không* tính đơn giá,
không sinh dự toán. Sai đơn giá là chuyện pháp lý và là việc của người dự toán; giá trị của DHCB là **truy được
ngược từ từng dòng khối lượng về từng phần tử trong mô hình** — thứ mà bảng Excel gõ tay không làm được.

**B3 — BCF.** ✅ **Đã làm 2026-09-05** — `ClashDetection` nhận thêm `bcfPath` (và `bcfProjectName`), ghi
BCF 2.1 bằng `Shared.Logic/Bcf` (`BcfWriter`, `BcfIssue` — thuần, 21 ca test đọc lại chính file vừa ghi).
Không làm thành lệnh Core riêng: dữ liệu đã nằm sẵn trong lệnh **đã chạy thật**, nên thêm đầu ra không
vướng nguyên tắc 6, còn thêm một lệnh mới thì có. Hai điều chốt bằng test vì hỏng thì không ai thấy ngay:
**GUID topic sinh từ chính `key` va chạm** (xuất lại lần hai vẫn là vấn đề cũ, không tách nhận xét người
duyệt đã ghi ra vấn đề mới) và **camera không bao giờ NaN** — kể cả khi nhìn thẳng đứng hoặc hướng nhìn
là vector 0. Toạ độ đổi foot → **mét** ở đúng một chỗ, vì BCF quy định mét.
Cơ sở kỹ thuật khi làm: `ClashDetectionCommand` đã giữ đúng dữ liệu cần: `Clash(A, B, Centre, Key, LinkName,
LinkInstanceId)` — có cặp phần tử, có tâm va chạm để đặt camera. Cấu trúc `.bcf` là zip: mỗi vấn đề một thư mục
tên GUID chứa `markup.bcf` (bắt buộc) + `.bcfv` (góc nhìn) + ảnh PNG cạnh dài ≤ 1500 px; gốc zip có
`bcf.version`, `extensions.xml`, tuỳ chọn `project.bcfp`. Toàn bộ là XML + zip nên viết được trong
`Shared.Logic` và test bằng cách đọc lại chính file vừa ghi. Đuôi file: `.bcfzip` cho 1.0/2.0, `.bcf` từ 2.1.

### Đợt C — chặng 5, hoàn công (nơi luật vừa đổi)

| # | Lệnh | Vì sao | Tầng thuần |
|---|---|---|---|
| C1 ✅ | Chuỗi băm nhật ký — `RunLog.Append` gắn, `BatchRunner --verify-log` kiểm | NĐ 207/2026 đòi dấu thời gian **không thể chỉnh sửa ngược** cho nhật ký điện tử | `Evidence/HashChain` — **thuần tuyệt đối**, 24 ca test |
| C2 | **`AsBuiltStamp`** + bộ hồ sơ hoàn công | Đóng dấu hoàn công + ghép danh mục đang làm tay 100% | `AsBuilt/DossierIndex` (danh mục theo Phụ lục VII) |
| C3 | **`IdsValidate`** | NĐ 217/2026 bắt nộp dữ liệu BIM; IDS là chuẩn kiểm đọc được bằng máy | `Ids/IdsSpec` + `Ids/IdsEvaluator` (6 loại facet) |
| C4 ✅ | **`ModelLinesFromCad`** | Mắt xích còn thiếu ở chặng 1: `CadLayerMap` map layer, `RouteFromLines` cần model line — không có ai dựng model line từ DWG | `PolylineSimplifier` (**đã có**) + `CadCurveFilter` |

**C1 — nhật ký bằng chứng, khác biệt lớn nhất mà rẻ nhất.** ✅ **Đã làm 2026-09-04** — xem `roadmap.md` §11.5 và
[`bang-chung-test.md`](bang-chung-test.md) §23. Batch runner đã ghi `run-HHmmss.jsonl` mỗi lượt; nay mỗi dòng mang
thêm `prevHash` và `hash` = SHA-256 của **chính chuỗi ký tự đã ghi ra file** (tính đến trước trường `hash`, không
serialize lại object — vòng JSON → object → JSON không bảo đảm ra byte y hệt). Sửa một dòng cũ là gãy chuỗi từ dòng
đó trở đi, và bản kiểm chỉ ra **đúng dòng bị sửa**. Không ai trong danh sách đối thủ có thứ này, và nó đến từ luật
chứ không từ ý thích.

Hai điều chỉnh so với đề xuất gốc, đều theo hướng làm ít đi: gắn dấu vết đặt ở **`RunLog.Append`** — điểm ghi duy
nhất của cả batch Revit lẫn AutoCAD — nên không cần một lệnh `EvidenceLog` riêng; và bản kiểm là cờ
**`BatchRunner --verify-log`** chứ không phải lệnh Core `EvidenceVerify`, vì kiểm log không cần `Document` nào mà
thêm lệnh Core thì vướng nguyên tắc 6.

> **Phải nói thật trong tài liệu sản phẩm:** chuỗi băm chứng minh **tính toàn vẹn nội bộ** của log, *không* thay
> chữ ký số hay dấu thời gian của một CA. Muốn đủ giá trị pháp lý còn cần chữ ký số của các bên và bản sao lưu
> độc lập — đúng ba điều kiện NĐ 207/2026 nêu. DHCB làm được điều kiện thứ nhất và tạo điều kiện cho hai điều
> còn lại; hứa quá là tự tạo rủi ro cho khách hàng.

**C2 — `AsBuiltStamp`.** ✅ **Đã tra xong phụ lục (2026-09-04).** Mẫu dấu bản vẽ hoàn công nay ở **Phụ lục IIb
NĐ 207/2026** — vẫn **hai mẫu** như trước: Mẫu 1 cho hợp đồng thường, **Mẫu 2** cho thầu chính/thầu phụ, EPC,
chìa khoá trao tay (Mẫu 2 tách riêng dòng của tổng thầu). Các dòng trong dấu: tên nhà thầu thi công · "BẢN VẼ
HOÀN CÔNG" · ngày tháng năm · người lập · chỉ huy trưởng hoặc giám đốc dự án · tư vấn giám sát trưởng. Kích
thước thực tế **không vượt dung sai** thì photocopy bản vẽ thi công rồi đóng dấu, ký xác nhận; vẽ lại thì khung
tên phải tương tự mẫu Phụ lục IIb. Nội dung từng dòng vẫn phải đối chiếu bản gốc Công báo trước khi dựng family
— danh mục hồ sơ đi kèm lấy theo **Phụ lục VII**. DHCB cung cấp **family mẫu + cơ chế điền** (tên nhà thầu, ngày,
người ký, số hợp đồng lấy từ config), doanh nghiệp chịu trách nhiệm nội dung. Phần tự động: gán dấu lên loạt
sheet, đặt revision "Hoàn công", xuất PDF theo danh mục — ba việc mà `RevisionOnSheets` + `BatchExport` đã làm
được một nửa.

**C3 — `IdsValidate` thay cho checkset tự nghĩ.** `roadmap.md` §11.1 trước đây định viết checkset JSON riêng;
đề nghị đổi sang đọc file **IDS** chuẩn **đã được nhận vào §11.1 ngày 2026-09-04**, và §11.4 thu hẹp lại thành
câu hỏi "có mở sang kiểm trên file IFC hay không". Lý do: chủ đầu tư/tư vấn thẩm tra khai yêu cầu **một lần** rồi kiểm được bằng cả DHCB lẫn
IfcTester/Solibri và nhận **cùng một kết quả** — đó chính là điều IDS được lập ra để bảo đảm. Kiểm thẳng trên mô
hình Revit (không qua vòng IFC) là lợi thế riêng: kỹ sư sửa được ngay tại chỗ thay vì xuất IFC → kiểm → quay lại
Revit. `ParameterRuleCheck` giữ nguyên cho quy tắc nội bộ công ty.

## 5. Thứ tự đề nghị

Ràng buộc phải tôn trọng: nguyên tắc 6 của `roadmap.md` — **không thêm lệnh Core khi chưa có ca kiểm chạy trong
Revit cho lệnh đó**; và [`progress.md`](progress.md) nói việc có giá trị nhất lúc này vẫn là
**9.4 — đưa cho nhóm kỹ sư dùng thật**.

| Đợt | Làm gì | Vì sao đúng thời điểm |
|---|---|---|
| **Ngay, không cần chờ số liệu** | ~~**A1 `SetoutExport`**~~ ✅ · ~~**B1 `ConstructionStatus`/`ProgressReport`**~~ ✅ — cả hai mã nguồn **2026-09-05**, 🧪 chờ chạy thật · ~~**C1** chuỗi băm nhật ký~~ ✅ **xong 2026-09-04** | Cả ba **chỉ đọc hoặc ghi tham số**, tầng thuần chiếm phần lớn công sức, không phụ thuộc thư viện/template của dự án. Giá trị không phụ thuộc kết quả 9.4 — trắc đạc, chỉ huy trưởng và bộ phận hồ sơ là ba nhóm người **khác** với nhóm đang dùng 43 lệnh hiện có, nên mở thêm được tệp người dùng cho chính vòng 9.4 |
| **Song song, rẻ** | ~~**B3 BCF**~~ ✅ **xong 2026-09-05** (đầu ra `bcfPath` của `ClashDetection`) · ~~**C4 `ModelLinesFromCad`**~~ ✅ **xong 2026-09-05** | Thuần gần hết; B3 chỉ thêm đầu ra cho lệnh đã chạy thật, C4 nối hai lệnh đã có |
| **Sau khi có số liệu 9.4/`UsageReport`** | **A2** → **A4** → **A3** → **B2** → **C3** → **C2** | Sáu mục này đắt hoặc phụ thuộc thói quen từng công ty (template view/sheet, thư viện tag, bảng mã định mức, mẫu dấu). Làm trước khi biết kỹ sư thật cần gì là lặp lại đúng sai lầm "bề rộng trước" mà `roadmap.md` đã ghi lại |

Một việc **không phải code, nên làm trước tất cả**: sửa `roadmap.md` §11, khi đó còn căn cứ vào **NĐ 06/2021 đã
hết hiệu lực từ 01/7/2026**. ✅ **Xong 2026-09-04** — giai đoạn 11 đã viết lại theo NĐ 207/2026 + NĐ 217/2026 và
nhận C1 → 11.5, C2 → 11.6, C3 → 11.1; cả repo không còn chỗ nào lấy NĐ 06/2021 làm căn cứ.

## 6. Không nên làm (và vì sao)

| Hạng mục | Quyết định | Lý do |
|---|---|---|
| 4D / nối Primavera–MS Project | Không | Navisworks/Synchro đã làm; cần server và dữ liệu tiến độ ngoài mô hình — ngoài phạm vi chạy tại chỗ |
| Tính đơn giá, sinh dự toán | Không | Ranh giới pháp lý. Dừng ở khối lượng + mã hiệu + căn cứ truy ngược |
| Scan-to-BIM, so lệch đám mây điểm | Không | Cần thư viện nặng và phần cứng; độ chính xác phụ thuộc thiết bị chứ không phụ thuộc mã |
| Mở rộng `AutoRoute` | Không, tới khi chất lượng tuyến chứng minh được | `progress.md` ghi rõ: việc còn lại là chọn được hai điểm trong cùng khoang trần, không phải hiệu năng |
| CDE / đồng bộ đám mây | Không lúc này | NĐ 217/2026 chỉ bắt buộc CDE với **cấp I**; và đó là hạ tầng, không phải add-in |
| Thêm lệnh mới ở chặng 1–2 | Không | 36 lệnh ở đó rồi; thêm nút không phải là thứ đang thiếu |

## 7. Đo thế nào để biết đúng hướng

Bốn chỉ số, mỗi cái đo được bằng máy chứ không bằng cảm giác:

| Chỉ số | Mốc | Đo bằng |
|---|---|---|
| Số điểm định vị xuất ra và **được trắc đạc dùng thật** trên một dự án | ≥ 1 dự án, ≥ 200 điểm | `UsageReport` (số ngày dùng `SetoutExport`) + xác nhận của tổ trắc đạc |
| Thời gian ra một bộ bản vẽ spool cho một hệ | Giảm ≥ 50% so với làm tay | Bấm giờ hai lần trên **cùng một hệ** — cách §19/§21 đã đo |
| Số chặng của chuỗi có ít nhất một lệnh chạy thật | 5/5 (nay 3/5 — chặng 3 và 4 đã có mã nguồn A1/B1 nhưng chưa lượt chạy thật nào) | [`bang-chung-test.md`](bang-chung-test.md) |
| Chuỗi băm nhật ký batch kiểm lại được sau 30 ngày | `BatchRunner --verify-log` mã thoát 0 trên log thật | Chạy trên log của đêm batch dự án A |

Chỉ số thứ ba là chỉ số của chính tài liệu này: **hôm nay chuỗi đứt ở chặng 3.**

## Nguồn

**Văn bản pháp lý** — [Nghị định 217/2026/NĐ-CP về quản lý hoạt động xây dựng](https://luatvietnam.vn/tin-van-ban-moi/da-co-nghi-dinh-217-2026-nd-cp-quy-dinh-ve-quan-ly-hoat-dong-xay-dung-tu-01-7-2026-186-109712-article.html) ·
[Bắt buộc BIM với công trình từ cấp II từ 01/7/2026](https://luatvietnam.vn/tin-van-ban-moi/tu-01-7-2026-bat-buoc-ap-dung-bim-doi-voi-cong-trinh-xay-dung-moi-tu-cap-ii-tro-len-186-109713-article.html) ·
[9 điểm về BIM trong NĐ 217/2026](https://storekonia.com/9-diem-noi-bat-ve-bim-trong-nghi-dinh-217-2026-nd-cp-ma-doanh-nghiep-xay-dung-can-nam-ro/) ·
[Nhật ký thi công & hồ sơ hoàn công theo NĐ 207/2026 — HTIC Law](https://hticlaw.vn/tintuc/nhat-ky-thi-cong-ho-so-hoan-cong-nghi-dinh-207-2026-nd-cp-gia-tri-chung-cu/) ·
[Toàn văn NĐ 207/2026 — Công báo Chính phủ](https://congbao.chinhphu.vn/van-ban/nghi-dinh-so-207-2026-nd-cp-469769.htm) ·
[Toàn bộ 11 phụ lục NĐ 207/2026](https://thuvienphapluat.vn/chinh-sach-phap-luat-moi/vn/ho-tro-phap-luat/chinh-sach-moi/114944/toan-bo-phu-luc-nghi-dinh-207-2026-nd-cp-quan-ly-cong-trinh-xay-dung-tu-01-7-2026) ·
[Mẫu dấu bản vẽ hoàn công theo Phụ lục IIb NĐ 207/2026](https://thuvienphapluat.vn/phap-luat/tu-01-07-2026-mau-dau-ban-ve-hoan-cong-theo-nghi-dinh-207-2026-nd-cp-huong-dan-ap-dung-mau-dau-ban--275398.html) ·
[Định mức xây dựng TT 12/2021/TT-BXD, sửa bởi TT 09/2024/TT-BXD](https://bacnam.com.vn/thong-tu-092024tt-bxd-sua-doi-bo-sung-mot-so-dinh-muc-xay-dung-ban-hanh-tai-thong-tu-122021tt-bxd)

**Chuẩn mở** — [Information Delivery Specification (IDS) — buildingSMART](https://www.buildingsmart.org/standards/bsi-standards/information-delivery-specification-ids/) ·
[IDS — buildingSMART Technical](https://technical.buildingsmart.org/projects/information-delivery-specification-ids/) ·
[IfcTester — IfcOpenShell](https://docs.ifcopenshell.org/ifctester.html) ·
[BCF-XML 3.0 — cấu trúc file](https://github.com/buildingSMART/BCF-XML/blob/release_3_0/Documentation/README.md) ·
[BIM Collaboration Format — buildingSMART](https://www.buildingsmart.org/standards/bsi-standards/bim-collaboration-format/)

**Revit API** — [Assemblies and Views (AssemblyViewUtils)](https://help.autodesk.com/cloudhelp/2025/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Construction_Modeling/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Construction_Modeling_Assemblies_and_Views_html.html) ·
[AssemblyInstance.Create](https://www.revitapidocs.com/2024/e0a37a7b-b157-b992-21d2-95f68cc76abd.htm) ·
[Survey and Project Base Point — Jeremy Tammik](http://jeremytammik.github.io/tbc/a/0861_survey_base_pnt.htm) ·
[Đổi hệ toạ độ trong Revit API](https://www.learnrevitapi.com/blog/convert-coordinate-systems-in-revit-api-draft) ·
[Sheet Collections & tự động đặt view lên sheet — What's New in Revit 2026](https://www.autodesk.com/blogs/aec/2025/04/02/whats-new-in-revit-2026/)

**Thị trường** — [Victaulic Tools for Revit — spool & BOM](https://www.victaulic.com/blog/piping-system-design-in-half-the-time-with-victaulic-tools-for-revit/) ·
[eVolve MEP](https://evolvemep.com/) ·
[GTP Stratus](https://www.stratus.build/stratus) ·
[Trimble Field Points](https://www.trimble.com/en/products/building-construction-field-systems/field-points) ·
[BIM 101: chuẩn bị dữ liệu cho định vị hiện trường](https://bimlearningcenter.com/bim-101-how-to-prep-model-data-for-field-layout/) ·
[BIMcollab — clash detection & điều phối](https://www.bimcollab.com/en/clash-detection-in-bim/)
