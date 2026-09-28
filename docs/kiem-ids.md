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
Pset/vật liệu/phân loại thừa kế từ kiểu, boolean `.T./.F.` → `true/false` chữ thường, `.U.` = không có giá trị)
và **so theo kiểu dữ liệu** — xem mục "Đường IFC so theo kiểu" bên dưới.

## Hỗ trợ tới đâu

| Facet IDS | DHCB đọc từ đâu trong Revit |
|---|---|
| `entity` (+ `predefinedType`) | `IfcExportAs` (dạng `IfcWall.SOLIDWALL`), rồi bảng category → lớp IFC |
| `attribute` | `Name`, `Tag` (= Mark, rỗng thì = ElementId — đúng như bộ xuất IFC, §43), `Description`, `ObjectType`, `GlobalId` (**22 ký tự nén** đúng như bộ xuất IFC sinh từ UniqueId, không phải UniqueId 45 ký tự); tên khác thì thử như một tham số cùng tên |
| `property` | tham số `"Pset_Tên.Prop"`, rồi tham số cùng tên ở instance, rồi ở type — đúng thứ tự bộ xuất IFC lấy giá trị. Số có đơn vị theo **đơn vị chuẩn IDS** (dài m, diện tích m², thể tích m³, góc rad) — cùng đơn vị đường IFC đổi về, nên một IDS cho một kết luận |
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
biên `min/max…` so chặt. **Tên lớp IFC** cũng phân biệt hoa thường như IDS 1.0 quy định: phải viết `IFCWALL`;
`IfcWall` không khớp phần tử nào (lint cảnh báo rõ). Trước 2026-09-28 DHCB nâng tên lên chữ hoa cho dễ dãi —
cùng một file cho kết luận khác IfcTester/Solibri, nên đã bỏ.
`cardinality="prohibited"` (có mới là sai) và `"optional"` (không bắt buộc có, nhưng đã có thì phải đúng) đều đọc.

Ở mức **specification** (IDS 1.0, thuộc tính đặt trên `<applicability>`; bản nháp cũ đặt trên `<specification>`
vẫn đọc): `minOccurs="0" maxOccurs="0"` là specification **cấm** — mọi phần tử lọt applicability là một vi phạm
("không có ống nước trên mái"); specification cấm không được có `<requirements>` (có là file sai).
`minOccurs="0"` là tuỳ chọn. Mặc định (`minOccurs="1"`) là **bắt buộc**: không phần tử nào lọt bộ lọc thì
specification **không đạt**. Specification bắt buộc **không có `<requirements>`** là hợp lệ: nghĩa là "phải có
ít nhất một phần tử như vậy". `ifcVersion` **không lọc**: file IFC lược đồ `IFC4` gặp specification khai
`ifcVersion="IFC2X3"` thì vẫn kiểm (như IfcTester và bộ ca buildingSMART), báo cáo chỉ ghi chú lệch lược đồ.
Trước 2026-09-28 specification đó bị bỏ qua — 3 ca `ids/…` của buildingSMART ra "đạt" thay vì "không đạt".

**Gặp thứ chưa hỗ trợ thì từ chối file, không bỏ qua im lặng** — facet lạ, ràng buộc lạ (`totalDigits`,
`whiteSpace`…), `<simpleValue>` rỗng, file không có `<specification>` nào, specification **tuỳ chọn** mà không
có `<requirements>` nào (luôn đạt). Lý do: một quy tắc bị lờ đi vẫn in ra dấu ✓, và người đọc báo cáo không có cách nào biết
là nó chưa từng được kiểm.

## Đường IFC so theo kiểu

Đường IFC (`--verify-ifc … --verify-ids …`, gói bàn giao) không so chuỗi mà so **giá trị có kiểu**, đúng luật mà
bộ ca buildingSMART kiểm:

- **Mọi thực thể** đều là ứng viên, kể cả thứ không có GlobalId (`IfcMaterial`, `IfcTaskTime`,
  `IfcSurfaceStyleRefraction`…). Facet applicability **đầu tiên** quyết tập ứng viên như IfcTester: `property`
  chỉ nhìn `IfcObjectDefinition` (cộng vật liệu/profile từ IFC4), `classification`/`material` chỉ nhìn
  `IfcObjectDefinition`, `entity`/`attribute`/`partOf` nhìn mọi thực thể.
- **attribute** tra theo **bảng lược đồ** IFC2X3/IFC4/IFC4X3 (`src/DhcbTools.Shared.Logic/Ids/ifc-schemas.txt`,
  sinh bằng `tools/ifc-schema/sinh-luoc-do.py` từ ifcopenshell): tên phân biệt hoa thường, tên khai bằng
  restriction khớp mọi thuộc tính hợp tên; thuộc tính inverse/dẫn xuất/`$` là **vắng**, chuỗi rỗng hoặc danh
  sách rỗng là **có mà sai** (tuỳ chọn cũng trượt).
- **Kiểu giá trị**: số thực nhận `42`, `42.`, `4.2e1` (dung sai IDS); số nguyên **không** nhận `42.0`; chuỗi so
  nguyên văn (`'42'` không bằng `42.0`); boolean chỉ `true`/`false`/`1`/`0`; pattern chỉ áp lên chuỗi; tham
  chiếu thực thể, danh sách, select bọc kiểu "có mặt" được nhưng mọi ràng buộc giá trị đều trượt.
- **property**: pset/property khai bằng restriction thì **mọi** cái khớp đều phải thoả; `dataType` phải đúng kiểu
  đo (kể cả quantity: `IfcQuantityLength` là `IFCLENGTHMEASURE`); số có đơn vị **đổi về đơn vị chuẩn IDS**
  (m, m², m³, kg, s…; SI có tiền tố và đơn vị quy đổi theo `ConversionFactor` của file; đơn vị dẫn xuất giữ nguyên
  như IfcTester); list/enumerated/bounded/table: một giá trị khớp là đủ (table chỉ so cột đúng `dataType`);
  complex property, reference property **không hỗ trợ → trượt**; pset của vật liệu/profile
  (`IfcMaterialProperties`, IFC2X3 `IfcExtendedMaterialProperties`) và pset định sẵn (`IfcDoorPanelProperties`…)
  đều tính.
- **classification**: gán thẳng cả một `IfcClassification` cho hệ mà không có mã; tài nguyên nhận phân loại qua
  `IfcExternalReferenceRelationship`; phần tử kế thừa phân loại của kiểu **theo từng hệ** (cùng hệ thì của phần tử
  thắng); `system` khai bằng pattern so đúng pattern (trước đây coi như "mọi hệ").
- **entity** IFC2X3: lớp chưa có trong IFC2X3 (`IFCAIRTERMINAL`…) khớp phần tử có kiểu tương ứng
  (`IfcFlowTerminal` + `IfcAirTerminalType`) — bảng ánh xạ kiểu của buildingSMART.

Đường Revit vẫn so chuỗi như bảng trên (Revit không có kiểu IFC để đọc).

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
- **Tên facet khai bằng `xs:pattern`** (ví dụ "mọi property khớp `Fire.*`"): đường IFC xét mọi thuộc tính/pset/
  property khớp (mục trên); đường Revit không liệt kê được tham số theo mẫu nên facet đó **trượt** thay vì âm thầm
  coi như đạt.
- ✅ Ràng buộc độ dài chuỗi (`length`/`minLength`/`maxLength`), so số theo giá trị, ràng buộc hội, cardinality
  mức specification và lọc `ifcVersion` — thêm 2026-09-23 (`IdsComplianceTests`; lọc `ifcVersion` đã bỏ
  2026-09-28 để khớp chuẩn — xem trên). Đường IFC nay đọc đúng
  `IfcPropertyEnumeratedValue`/`ListValue`/`BoundedValue`/`ComplexProperty` và `RelatingPropertyDefinition`
  dạng tập (IFC4 ADD2); PredefinedType đọc đúng vị trí lược đồ (IfcDoor/IfcWindow ở 10, không "enum đầu tiên
  sau Tag"); Space/Storey/Building/Site không có `Tag`. Số thực trên đường Revit đổi theo loại đại lượng
  (tỉ số giữ nguyên) thay vì nhân 304,8 tất cả — từ 2026-09-28 property theo đơn vị chuẩn IDS (m, m², m³, rad)
  như đường IFC; attribute vẫn theo đơn vị bộ xuất ghi trong file (mm, độ), vì IDS so attribute không đổi đơn vị.
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

| Nhóm | 2026-09-23 | PR #170 | Nay |
|---|---|---|---|
| tolerance | 22/36 | **36/36** | **36/36** |
| material | 23/29 | **29/29** | **29/29** |
| partof | 30/34 | **34/34** | **34/34** |
| entity | 24/33 | 27/33 | **33/33** |
| restriction | 18/25 | 20/25 | **25/25** |
| ids | 7/12 | 9/12 | **12/12** |
| classification | 24/27 | 23/27 | **27/27** |
| attribute | 38/56 | 36/56 | **56/56** |
| property | 54/82 | 57/82 | **82/82** |
| **Tổng** | **240/334** | **271/334** | **334/334** |

`known-gaps.txt` nay rỗng: CI đỏ ngay khi **bất kỳ** ca nào lệch. 63 ca cuối được sửa bằng: bảng thuộc tính theo
lược đồ IFC (attribute, restriction), giá trị có kiểu và đổi đơn vị (property, attribute), mọi pset/property khớp
restriction đều phải thoả (property), phân loại gán thẳng hệ/qua tham chiếu ngoài/đè theo hệ (classification),
bảng ánh xạ kiểu IFC2X3 (entity), không lọc `ifcVersion` và nhận specification bắt buộc không có requirements
(ids, entity), tên lớp phân biệt hoa thường (entity). IfcTester 0.8.5 trên cùng bộ ca: 312/334 — 22 ca nó lệch
(dung sai số, `optional` với `$`, pset vật liệu IFC2X3…) DHCB đều khớp.
