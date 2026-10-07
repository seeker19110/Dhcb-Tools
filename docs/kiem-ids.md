# Kiểm mô hình theo IDS — `IdsValidate`

Mục **11.1** của [`roadmap.md`](roadmap.md) (= đề xuất **C3** trong
[`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md)): chủ đầu tư hoặc tư vấn thẩm tra
khai **yêu cầu thông tin** một lần bằng file **IDS 1.0** (buildingSMART, chuẩn chính thức từ 01/6/2024), rồi
DHCB kiểm **thẳng trên mô hình Revit** — kỹ sư sửa ngay tại chỗ phần tử sai, không phải xuất IFC rồi đọc lỗi ở
một id không mở lại được trong Revit.

> **Vì sao là IDS chứ không phải một định dạng JSON tự nghĩ.** Cùng một file IDS, DHCB / IfcTester / Solibri
> phải ra **cùng kết luận** — đó chính là điều IDS được lập ra để bảo đảm. Một định dạng riêng thì mỗi phần mềm
> hiểu một kiểu, và tranh cãi giữa các bên quay về đúng chỗ cũ. `ParameterRuleCheck` + `configs/checksets/`
> **giữ nguyên** cho quy tắc nội bộ công ty mà IDS không mô tả (đặt tên view/sheet theo BEP, workset, ngưỡng
> cảnh báo, dung lượng file).

## Ranh giới — nói trước khi ai kịp hiểu nhầm

DHCB đọc mô hình Revit theo **ánh xạ Revit → IFC**: tham số `IfcExportAs` (instance rồi type), bảng category →
lớp IFC, và tham số Revit đóng vai property. Đó là **cùng ánh xạ mà bộ xuất IFC dùng**, nhưng **không phải
chính file IFC**. Nên kết luận ở đây là *"mô hình sẽ đạt khi xuất"*, không thay cho một lượt kiểm trên file đã
nộp. Mở hay không mở đường kiểm thẳng trên IFC là câu hỏi của mục **11.4**, quyết định sau khi có phản hồi của
chủ đầu tư/thẩm tra.

## Chạy

```json
{
  "idsPath": "C:/DHCB/yeu-cau/chu-dau-tu.ids",
  "outputPath": "C:/DHCB/bao-cao/ids.html",
  "csvPath": "C:/DHCB/bao-cao/ids.csv",
  "categories": ["Doors", "Walls"],
  "levelName": ""
}
```

`categories` rỗng = mọi phần tử mô hình (bỏ annotation, view, sheet). Lệnh **chỉ đọc**, không có đường ghi nào.

Ribbon: *Kiểm tra & AI → Kiểm theo IDS*. Bridge/MCP/batch đêm: lệnh `IdsValidate` như mọi lệnh khác.

**Trên chính file IFC** (mục 11.4, không cần Revit, chạy được trên CI):

```bash
DhcbTools.BatchRunner --verify-ifc xuat/toa-a.ifc --verify-ids yeu-cau/chu-dau-tu.ids --ids-report bao-cao/ids-ifc.html
```

Mã thoát 0 = mọi specification đạt, 1 = có specification không đạt (phần tử trượt, **hoặc specification bắt buộc
mà không phần tử nào lọt bộ lọc**), 2 = thiếu file / IDS hỏng. Báo cáo cùng dạng với đường Revit, phần tử ghi theo
`#id` trong file. Đã đối chiếu 10 specification với IfcTester trên Snowdon: khớp từng con số
([`bang-chung-test.md`](bang-chung-test.md) §41), và chạy trên **bộ ca chính thức của buildingSMART** (mục cuối).
Cột "DHCB đọc từ đâu" dưới đây là đường Revit; đường IFC đọc thẳng thực thể (tên lớp so **đúng lớp**,
Pset/vật liệu/phân loại thừa kế từ kiểu, boolean `.T./.F.` → `true/false` chữ thường, `.U.` = không có giá trị).

## Hỗ trợ tới đâu

| Facet IDS | DHCB đọc từ đâu trong Revit |
|---|---|
| `entity` (+ `predefinedType`) | `IfcExportAs` (dạng `IfcWall.SOLIDWALL`), rồi bảng category → lớp IFC |
| `attribute` | `Name`, `Tag` (= Mark, rỗng thì = ElementId — đúng như bộ xuất IFC, §43), `Description`, `ObjectType`, `GlobalId` (**22 ký tự nén** đúng như bộ xuất IFC sinh từ UniqueId, không phải UniqueId 45 ký tự); tên khác thì thử như một tham số cùng tên |
| `property` | tham số `"Pset_Tên.Prop"`, rồi tham số cùng tên ở instance, rồi ở type — đúng thứ tự bộ xuất IFC lấy giá trị |
| `classification` | theo `system`: OmniClass → `OmniClass Number`; Uniformat/Uniclass → `Assembly Code`; Keynote → `Keynote`; hệ khác → `ClassificationCode` hoặc tham số cùng tên hệ; không khai `system` → cả bốn. Hệ không biết → không trả gì (trước đây mọi hệ đều nhận Assembly Code nên Revit "đạt" mà IFC trượt) |
| `material` | vật liệu của phần tử, kể cả vật liệu lớp cấu tạo |
| `partOf` | tầng và `System Name` / `System Classification` — tên thật (`"Tầng 1"`), không phải tên lớp IFC như đường IFC (mục "Còn thiếu"). Khai `relation` thì tầng khớp `IFCRELCONTAINEDINSPATIALSTRUCTURE`, hệ khớp `IFCRELASSIGNSTOGROUP`; không khai vẫn khớp cả hai như trước |

Ràng buộc giá trị: `simpleValue`, `xs:enumeration`, `xs:pattern` (**neo hai đầu** — XSD khớp toàn bộ chuỗi,
không neo thì `AB-01-rác` cũng đạt quy tắc `AB-\d\d`; biên dịch ngay lúc đọc file, có timeout 2 s),
`minInclusive` / `maxInclusive` / `minExclusive` / `maxExclusive`, `length` / `minLength` / `maxLength`.
Nhiều ràng buộc khác loại trong cùng một `xs:restriction` là **hội** — tất cả phải đúng; nhiều `xs:pattern` với
nhau là **hoặc** (quy tắc XSD). Pattern dùng được `\i`, `\c`, `\I`, `\C` của XSD (dịch sang lớp ký tự .NET).
Chuỗi so **phân biệt hoa thường** (IDS 1.0: `FireRating = "ei60"` không đạt `"EI60"`); boolean phải viết chữ
thường `true`/`false` — IDS viết `TRUE` là sai chuẩn và không khớp. Hai bên đều là số thì **so số** với dung sai
IDS 1.0: `|thực − kỳ vọng| ≤ |kỳ vọng|·10⁻⁶ + 10⁻⁶` (`0.3` bằng `IFCREAL(0.29999999999999999)`, `3.` bằng `3.0`);
biên `min/max…` so chặt. Riêng **tên lớp IFC** không phân biệt hoa thường (`IfcWall` vẫn khớp, lint cảnh báo
phải viết `IFCWALL`) — không có trường hợp nào khớp sai, và file viết tay không bị hỏng.
`cardinality="prohibited"` (có mới là sai) và `"optional"` (không bắt buộc có, nhưng đã có thì phải đúng) đều đọc.

Ở mức **specification** (IDS 1.0, thuộc tính đặt trên `<applicability>`; bản nháp cũ đặt trên `<specification>`
vẫn đọc): `minOccurs="0" maxOccurs="0"` là specification **cấm** — mọi phần tử lọt applicability là một vi phạm
("không có ống nước trên mái"); specification cấm không được có `<requirements>` (có là file sai).
`minOccurs="0"` là tuỳ chọn. Mặc định (`minOccurs="1"`) là **bắt buộc**: không phần tử nào lọt bộ lọc thì
specification **không đạt**. `ifcVersion` được **lọc**: file IFC lược đồ `IFC4` thì
specification khai `ifcVersion="IFC2X3"` bị bỏ qua và báo cáo ghi rõ (đường Revit không biết lược đồ nên không lọc).

**Gặp thứ chưa hỗ trợ thì từ chối file, không bỏ qua im lặng** — facet lạ, ràng buộc lạ (`totalDigits`,
`whiteSpace`…), `<simpleValue>` rỗng, file không có `<specification>` nào, specification không cấm mà không có
`<requirements>` nào. Lý do: một quy tắc bị lờ đi vẫn in ra dấu ✓, và người đọc báo cáo không có cách nào biết
là nó chưa từng được kiểm.

## File IDS lệch chuẩn — cảnh báo, không chặn

Bộ đọc cố ý dễ tính (bỏ qua namespace, thứ tự thẻ) để file "gần đúng" vẫn kiểm được. Cái giá: IfcTester hay
Solibri kiểm theo XSD sẽ **từ chối** đúng file đó (§39). Nên sau khi đọc, `IdsValidate` soát file theo các
quy tắc rút từ `ids.xsd` 1.0 — namespace gốc và namespace `xs:` của `restriction`, `ifcVersion` bắt buộc,
thứ tự facet trong `applicability`, thẻ con bắt buộc của từng facet — và **liệt kê từng chỗ lệch kèm số
dòng** trong summary, messages và báo cáo HTML. Kết quả kiểm mô hình không đổi; việc của kỹ sư là sửa file
IDS trước khi nộp cho bên thẩm tra. Không kiểm bằng XSD thật vì .NET không biên dịch được `XMLSchema.xsd`
mà `ids.xsd` import (bằng chứng §40).

## Đọc báo cáo

Báo cáo HTML có ba con số cho mỗi specification: **áp dụng cho** bao nhiêu phần tử, **đạt** bao nhiêu, **không
đạt** bao nhiêu; kèm bảng liệt kê từng phần tử không đạt và **thiếu gì** (`cần property Pset_WallCommon.FireRating
thuộc {EI60, EI90}`). CSV cùng nội dung để lọc trong Excel.

> **"0 không đạt" không phải lúc nào cũng là đạt.** Specification mà **không phần tử nào lọt bộ lọc** được
> đánh dấu riêng và đếm riêng trong summary. Nếu nó bắt buộc (mặc định) thì tính là **KHÔNG ĐẠT** — đúng
> IDS 1.0 ("required specifications need at least one applicable entity"); trước 2026-09-28 DHCB chỉ cảnh báo
> và mã thoát vẫn 0. Nhóm phần tử thật sự không bắt buộc thì khai `minOccurs="0"` trên `<applicability>`.

Danh sách phần tử không đạt cắt ở **200 cái mỗi specification**; con số tổng vẫn đếm đủ. Cắt là cắt danh sách,
không phải cắt kết luận.

## Đã chạy thật

Revit 2024, model mẫu `Snowdon Towers Sample Architectural.rvt`, fixture
[`tests/suites/fixtures/yeu-cau-thong-tin.ids`](../tests/suites/fixtures/yeu-cau-thong-tin.ids):

```
Kiểm 1270 phần tử theo 3 specification: 42 phần tử không đạt ở 1 specification,
1 specification không có phần tử nào để kiểm → ids-check.html
```

42 phần tử không đạt đều là tường kính (`Glazing Wall - Stair`…) không khai vật liệu; cửa đạt hết vì model mẫu
có sẵn Mark; specification nhắm `IfcTank` — lớp không có trong model — rơi vào nhóm "không kiểm được gì" đúng
như fixture cố ý gài. Bằng chứng: [`bang-chung-test.md`](bang-chung-test.md) §32.

## Còn thiếu

- **Đã đối chiếu với IfcTester** trên chính IFC xuất từ Snowdon: §39 (3 specification, sau khi sửa ánh xạ
  tường kính), §41 (10 specification, mỗi loại facet một cái), §71 (13 specification chỉ về thuộc tính
  `relation` của `partOf`) — cả ba lượt khớp từng con số sau khi sửa lỗi của DHCB mà chính lượt đó lộ ra.
  Solibri không có trên máy.
- **Tên facet khai bằng `xs:pattern`** (ví dụ "mọi property khớp `Fire.*`") không suy ngược ra tên được, nên
  facet đó **trượt** thay vì âm thầm coi như đạt.
- ✅ Ràng buộc độ dài chuỗi (`length`/`minLength`/`maxLength`), so số theo giá trị, ràng buộc hội, cardinality
  mức specification và lọc `ifcVersion` — thêm 2026-09-23 (`IdsComplianceTests`). Đường IFC nay đọc đúng
  `IfcPropertyEnumeratedValue`/`ListValue`/`BoundedValue`/`ComplexProperty` và `RelatingPropertyDefinition`
  dạng tập (IFC4 ADD2); PredefinedType đọc đúng vị trí lược đồ (IfcDoor/IfcWindow ở 10, không "enum đầu tiên
  sau Tag"); Space/Storey/Building/Site không có `Tag`. Số thực trên đường Revit đổi theo loại đại lượng
  (dài → mm, diện tích → m², thể tích → m³, góc → độ; tỉ số giữ nguyên) thay vì nhân 304,8 tất cả.
- ✅ **`partOf` theo quan hệ IFC** (thuộc tính `relation` của facet, `ids.xsd` 1.0, tài liệu
  `Documentation/UserManual/partof-facet.md` của buildingSMART/IDS): đường IFC nay tính riêng năm chuỗi thuần
  (`IFCRELAGGREGATES`, `IFCRELASSIGNSTOGROUP`, `IFCRELCONTAINEDINSPATIALSTRUCTURE`, `IFCRELNESTS`, cặp gộp
  `IFCRELVOIDSELEMENT`/`IFCRELFILLSELEMENT`) — khai `relation` thì chỉ chuỗi đúng loại đó mới khớp, không rơi
  về chuỗi trộn nhiều loại như trước; không khai vẫn như cũ (test mới: `ThuocVe_TheoDungMotQuanHe_KhongRoiVeQuanHeKhac`,
  `PartOf_KhaiRelation_ChiNhanDungMotLoaiQuanHe_KhongRoiVeChuoiTron`, `PartOf_DocThuocTinhRelation_VaMoTaKemTheo`,
  `PartOf_RelationLa_KhongThuoc5GiaTri_TuChoiFile` — cùng ba ca cũ về vòng lặp hai chiều aggregate của §16 vẫn
  giữ nguyên kết luận). Đường Revit khớp gần đúng hai trong năm loại (tầng ↔ `CONTAINEDINSPATIALSTRUCTURE`, hệ ↔
  `ASSIGNSTOGROUP`) vì Revit không có đối tượng quan hệ IFC thật để đọc — **đã chạy thật trong Revit 2024**
  (§69: cửa khớp 142/142, đối chứng `IFCRELAGGREGATES` trượt 142/142, duct khớp 1053/1053).
  **Phần của một tổ hợp thừa vị trí không gian của tổng** ở đường IFC: cửa của curtain wall không có
  `IfcRelContainedInSpatialStructure` của riêng nó vẫn thuộc đúng tầng của curtain wall — kế thừa qua cha
  phân rã gần nhất (aggregates → nests → voids/fills), chỉ một bậc không gian, giống `get_container` của
  IfcOpenShell (§71). Đường **Revit** dùng `Element.LevelId` — **đã xác nhận thật không có lỗi tương tự**
  (§72): Revit gán `LevelId` trực tiếp cho mọi phần tử kể cả phần tử lồng trong host khác, nên không có
  bước suy luận qua cha nào để mà sai.
- Bảng category → lớp IFC là **bảng rút gọn** cho nhóm hay gặp; family lạ thì khai `IfcExportAs` để chắc chắn.

## Đối chiếu bộ ca buildingSMART

buildingSMART công bố 334 ca kiểm (`IDS/Documentation/ImplementersDocumentation/TestCases`, mỗi ca một cặp
`.ids` + `.ifc`, tên bắt đầu bằng kết quả đúng `pass-`/`fail-`/`invalid-`). CI chạy cả bộ trên đường IFC bằng
[`scripts/ids-conformance.py`](../scripts/ids-conformance.py) (commit ghim `870f9c4e`) và **đỏ khi có hồi quy** —
ca lệch ngoài danh sách đã biết [`tests/ids-buildingsmart/known-gaps.txt`](../tests/ids-buildingsmart/known-gaps.txt).
Chạy tại chỗ:

```bash
git init /tmp/bsids && git -C /tmp/bsids fetch --depth 1 https://github.com/buildingSMART/IDS.git 870f9c4e6e8f414e737b4d84ca1aa9b46fc6c8f3
git -C /tmp/bsids checkout FETCH_HEAD -- Documentation/ImplementersDocumentation/TestCases
dotnet build src/DhcbTools.BatchRunner/DhcbTools.BatchRunner.csproj -c Release
python3 scripts/ids-conformance.py /tmp/bsids/Documentation/ImplementersDocumentation/TestCases \
  --runner src/DhcbTools.BatchRunner/bin/Release/net10.0/DhcbTools.BatchRunner.dll
```

| Nhóm | 2026-09-23 | 2026-09-28 | 2026-10-07 |
|---|---|---|---|
| tolerance | 22/36 | **36/36** | **36/36** |
| material | 23/29 | **29/29** | **29/29** |
| partof | 30/34 | **34/34** | **34/34** |
| entity | 24/33 | 27/33 | 32/33 |
| restriction | 18/25 | 20/25 | **25/25** |
| ids | 7/12 | 9/12 | 9/12 |
| classification | 24/27 | 23/27 | **27/27** |
| attribute | 38/56 | 36/56 | **56/56** |
| property | 54/82 | 57/82 | **82/82** |
| **Tổng** | **240/334** | **271/334** | **330/334** |

Hai nhóm giảm ở 09-28 (attribute, classification) là **đạt oan trước đây**: specification bắt buộc không có phần tử nào (vì DHCB chưa liệt kê thực thể
không có GlobalId như `IfcSurfaceStyleRefraction`, `IfcMaterial`) từng được tính là đạt. Nay tính đúng là không đạt.

### Đường IFC so giá trị có kiểu (2026-10-07)

59 ca lệch của 09-28 đã khớp. Mọi thay đổi chỉ ở **đường file IFC** (`--verify-ifc … --verify-ids`, gói bàn giao);
đường Revit giữ nguyên cách so bằng chuỗi.

- **Bảng thuộc tính theo lược đồ** — [`Ifc/ifc-schema.txt`](../src/DhcbTools.Shared.Logic/Ifc/ifc-schema.txt), mọi
  lớp của IFC2X3, IFC4, IFC4X3_ADD2 (vị trí, thừa kế, thuộc tính khai lại thành dẫn xuất), sinh bằng
  [`scripts/gen-ifc-schema.py`](../scripts/gen-ifc-schema.py) từ IfcOpenShell và nhúng trong DLL. Facet `attribute`
  hỏi được thuộc tính bất kỳ (`IfcTask.IsMilestone`, `IfcStairFlight.NumberOfRisers`…); thuộc tính inverse/dẫn xuất
  không kiểm được nên trượt; `$` là "không khai" (tuỳ chọn thì đạt), `''`/tập hợp rỗng/logical `.U.` là "có ghi mà
  rỗng" (luôn trượt).
- **Giá trị có kiểu** — số so theo số (`42` khớp `42.`, `1.2345e3` khớp `1234.5`), số nguyên không nhận `42.0`,
  boolean chỉ nhận `true`/`false`/`1`/`0`, pattern không bao giờ khớp một số, biên số chỉ áp cho số, giá trị trỏ thực
  thể/tập hợp chỉ đạt khi facet không ràng buộc giá trị.
- **Property** — kiểm `dataType` (kiểu của giá trị, quantity theo loại số đo: `IfcQuantityLength` →
  `IFCLENGTHMEASURE`); property danh sách/liệt kê/khoảng/bảng: một giá trị khớp là đủ (bảng: chỉ cột đúng
  `dataType`); complex/reference: không kiểm được, trượt; pset định nghĩa sẵn (`IfcDoorPanelProperties`…) và
  property của vật liệu (`IfcMaterialProperties`, IFC2X3 `IfcExtendedMaterialProperties`) đọc được; tên pset/property
  khai bằng pattern: **mọi** cái khớp đều phải đạt. Pset của kiểu thừa kế xuống phần tử, phần tử đè theo từng property.
- **Đổi đơn vị về SI** — số đo đổi theo đơn vị riêng của property hoặc đơn vị mặc định của dự án (tiền tố SI,
  bình phương/lập phương cho diện tích/thể tích, gam → kg, đơn vị quy đổi như foot/độ theo `ConversionFactor`).
  **Thay đổi hành vi:** file IFC ghi mm thì IDS phải viết mét — `Width = 200` (mm) nay trượt, phải viết `0.2`; đúng
  chuẩn IDS và đúng như IfcTester. Thuộc tính (facet `attribute`) không đổi đơn vị, như IfcTester. Độ C và đơn vị
  dẫn xuất giữ nguyên số.
- **Phân loại** — gắn thẳng `IfcClassification` (không mã) vẫn là "có phân loại theo hệ đó"; phân loại của kiểu thừa
  kế xuống phần tử **theo từng hệ** (bản trước bỏ hẳn của kiểu khi phần tử có bất kỳ phân loại nào); tài nguyên không
  có GlobalId (`IfcMaterial`) nhận phân loại qua `IfcExternalReferenceRelationship`; `system` khai bằng pattern được
  hiểu đúng (bản trước coi là "mọi hệ").
- **Ánh xạ kiểu IFC2X3** — `IfcFlowTerminal` mang kiểu `IfcAirTerminalType` khớp `IFCAIRTERMINAL`: kiểu `IFC…TYPE`
  mà lớp bỏ đuôi không có trong IFC2X3 nhưng có trong IFC4.
- **Thực thể không có GlobalId** (`IfcMaterial`, `IfcTaskTime`…) là phần tử IDS khi applicability nêu đích danh lớp
  đó bằng facet `entity` — không liệt kê hàng triệu thực thể hình học chỉ để lọc bỏ.
- **Specification không có requirements** nay nhận khi nó **bắt buộc và có lọc applicability** ("mô hình phải có ít
  nhất một X"); vẫn từ chối khi tuỳ chọn hoặc applicability rỗng (không kiểm gì).

Ngoài bộ ca, đối chiếu trực tiếp với **IfcTester 0.9** trên 10 file mẫu của
[buildingSMART/Sample-Test-Files](https://github.com/buildingSMART/Sample-Test-Files) (Simple-Scene Architecture/Hvac/
Structural ở IFC2X3, IFC4, IFC4.3 và `wall-with-opening-and-window`): 2.815 specification sinh tự động — mỗi lớp × mỗi
thuộc tính lược đồ (có mặt, bằng giá trị của phần tử đầu), mỗi property/quantity (có mặt + `dataType`, bằng giá trị
gốc, bằng giá trị ÷ 1000) cùng phân loại và vật liệu. Số phần tử áp dụng và số đạt **trùng 2.815/2.815**, khoảng 40 %
specification có phần tử trượt ở cả hai bên. (Lượt đo tại máy; script sinh specification không đưa vào repo vì cần IfcOpenShell và tải file mẫu — CI giữ phần đối chiếu bằng bộ ca buildingSMART ở trên.)

4 ca còn lệch, đều là **lựa chọn cố ý**:

- **Lọc `ifcVersion`** (3 ca `ids/`): bộ ca kỳ vọng kiểm bất kể lược đồ (IfcTester mặc định không lọc); DHCB
  **cố ý** bỏ qua specification không nhắm lược đồ của file (mục 11.4) — giữ nguyên.
- **Tên lớp viết thường** (1 ca): DHCB nhận `IfcWall` và cảnh báo thay vì coi file là sai — xem trên.
