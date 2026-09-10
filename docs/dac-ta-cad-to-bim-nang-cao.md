# Đặc tả chi tiết — CAD → BIM Revit có kiểm soát

- **Trạng thái:** Đề xuất để duyệt trước khi triển khai
- **Ngày:** 2026-09-10
- **Phạm vi:** AutoCAD 2024–2026, Revit 2023–2027, Bridge/MCP DHCB
- **Tài liệu triển khai:** [`dac-ta-trien-khai-cad-to-bim-nang-cao.md`](dac-ta-trien-khai-cad-to-bim-nang-cao.md)

## 1. Mục tiêu

Biến bản vẽ DWG có layer, block, text, DIM, cao độ và thông số kỹ thuật thành một **kế hoạch dựng mô hình Revit có thể kiểm tra**, sau đó mới tạo phần tử thật theo xác nhận của kỹ sư.

Hệ thống không được tuyên bố “hiểu toàn bộ bản vẽ”. Mọi kết luận phải truy ngược được về đối tượng nguồn trong DWG, quy tắc mapping và mức tin cậy. DIM và text là **ràng buộc kiểm chứng**, không tự động trở thành sự thật khi mâu thuẫn với hình học.

### 1.1 Người dùng chính

- Kỹ sư BIM/Revit dựng mô hình từ hồ sơ CAD.
- Kỹ sư MEP dựng tuyến và thiết bị từ mặt bằng CAD.
- BIM Coordinator kiểm tra sai lệch CAD ↔ Revit và duyệt phương án né vật cản.
- BIM Manager quản lý profile mapping theo template và tiêu chuẩn công ty.

### 1.2 Kết quả mong đợi

1. Trích xuất ngữ nghĩa DWG mà không explode hoặc sửa file nguồn.
2. Chuẩn hóa đơn vị, hệ tọa độ, tầng và cao độ trước khi suy luận.
3. Đề xuất cấu kiện/tuyến kèm nguồn, confidence và cảnh báo.
4. Preview đầy đủ trước khi ghi Revit.
5. Chạy lại không nhân đôi; biết phần tử nào sinh từ đối tượng CAD nào.
6. DWG thay đổi thì báo create/update/unchanged/orphaned, không tự xóa.
7. Với tuyến MEP, đưa nhiều phương án né vật cản và giải thích điểm số.
8. Sau khi ghi, tự kiểm geometry, connector, clash và snapshot qua MCP.

## 2. Ngoài phạm vi

Phiên bản đầu **không** làm các việc sau:

- Dựng BIM hoàn chỉnh từ DWG không có quy ước layer/block.
- OCR ảnh scan/PDF hoặc point-cloud-to-BIM.
- Tự chọn family/type không tồn tại trong model hoặc thư viện đã duyệt.
- Tự sửa/xóa phần tử Revit do kỹ sư dựng tay.
- Tự áp phương án confidence thấp.
- Tự resolve mọi clash hoặc thay kỹ sư quyết định đường tuyến.
- Dựng kết cấu tính toán, thiết kế tải hoặc kiểm tiêu chuẩn kỹ thuật chuyên ngành.
- Gọi lệnh native tùy ý của AutoCAD/Revit ngoài `CommandCatalog`.

## 3. Năng lực hiện có được tái sử dụng

Không viết lại các phần đã có:

| Năng lực | Thành phần hiện có | Cách dùng |
|---|---|---|
| Link DWG vào Revit | `CadLink` | Dùng khi cần hiển thị/đối chiếu trong model |
| DWG → model line | `ModelLinesFromCad` | Dùng cho tuyến đã xác định layer và cao độ |
| Trục CAD → CSV | `GridExtract` | Giữ tương thích; nguồn dữ liệu được đưa thêm vào manifest mới |
| CSV → Grid/Level | `GridFromCsv` | Giữ tương thích; luồng mới có thể gọi từ plan |
| Model line → MEP | `RouteFromLines` | Builder tuyến cuối cùng |
| Né vật cản A* 3D | `AutoRoute` + `PathFinder3D` | Mở rộng theo hướng nhiều phương án, không thay mặc định cũ |
| Kiểm va chạm | `ClashDetection`, BCF 2.1 | Kiểm hậu điều kiện và giao việc |
| Đọc model | `parameters_of`, `element_geometry`, `selection`, `snapshot` | Lập config và kiểm sau ghi |
| An toàn Bridge | dry-run, `documentId`, `previewToken`, token Bridge | Bắt buộc giữ nguyên |

## 4. Luồng nghiệp vụ chuẩn

```text
DWG gốc
  ↓ CadSemanticExport (AutoCAD, chỉ đọc)
CAD Semantic Manifest v1 + báo cáo chất lượng
  ↓ CadModelPlan (Revit, chỉ đọc model)
Kế hoạch create/update/skip/review/reject + previewToken
  ↓ kỹ sư duyệt mapping, tọa độ, phạm vi, phương án tuyến
CadModelApply (Revit, ghi một transaction theo lô)
  ↓
ChangedIds + bảng liên kết nguồn↔đích
  ↓
element_geometry / ConnectorChecker / ClashDetection / snapshot
  ↓
CadModelReconcile khi DWG có revision mới
```

### 4.1 Cổng bắt buộc trước khi lập kế hoạch

Lệnh phải dừng nếu thiếu một trong các điều kiện:

1. Không xác định được đơn vị DWG.
2. Chưa xác nhận hệ tọa độ hoặc residual của các mốc kiểm vượt dung sai.
3. Không có mapping profile phù hợp.
4. Family/type đích không tồn tại trong Revit.
5. Level/cao độ mục tiêu không xác định duy nhất.
6. Manifest đã thay đổi sau lần preview.
7. Document Revit đã thay đổi sau preview khi chạy qua Bridge/MCP.

## 5. Hợp đồng dữ liệu

### 5.1 `CadSemanticManifest` phiên bản 1

Một manifest đại diện cho **một snapshot bất biến của một DWG**.

```jsonc
{
  "schemaVersion": "1.0",
  "source": {
    "path": "D:/CAD/MEP-L02.dwg",
    "sha256": "...",
    "dwgVersion": "AC1032",
    "insunits": "Millimeters",
    "extractedAtUtc": "2026-09-10T12:00:00Z"
  },
  "coordinateSystem": {
    "sourceUnit": "mm",
    "origin": { "x": 0, "y": 0, "z": 0 },
    "rotationDeg": 0,
    "controlPoints": []
  },
  "entities": [],
  "dimensions": [],
  "texts": [],
  "blocks": [],
  "levels": [],
  "diagnostics": []
}
```

#### Định danh nguồn

Mỗi đối tượng có:

- `sourceKey = <sourceSha256>:<layout>:<effectiveBlockPath>:<handle>`.
- `handle`: handle AutoCAD dạng hex hoa.
- `layout`: `Model` hoặc tên Paper Space.
- `parentPath`: chuỗi block lồng nhau; giữ transform tích lũy.
- `geometryHash`: hash canonical của hình học sau đổi về mm.

Không dùng `ObjectId` vì chỉ sống trong phiên AutoCAD.

#### Loại entity tối thiểu

- `Line`, `Polyline`, `Arc`, `Circle`.
- `DBText`, `MText`.
- `BlockReference`, kể cả dynamic block bằng effective name.
- `AttributeReference`.
- `Dimension`: linear/aligned/angular/radius/diameter/ordinate khi API cung cấp.
- Xref được ghi nhận như nguồn ngoài; mặc định không đi sâu nếu chưa bật rõ.

Mọi loại chưa hỗ trợ được ghi `diagnostics`, không im lặng bỏ qua.

### 5.2 DIM là ràng buộc, không phải hình học nguồn

Mỗi DIM xuất tối thiểu:

```jsonc
{
  "sourceKey": "...",
  "kind": "Aligned",
  "measurementMm": 3600,
  "displayText": "3600",
  "isTextOverride": false,
  "definitionPointsMm": [],
  "referencedGeometry": [],
  "layer": "A-DIMS",
  "confidence": 1.0
}
```

Quy tắc:

1. `measurementMm` lấy từ API/geometry, không parse chuỗi hiển thị nếu có giá trị đo được.
2. Text override phải gắn cờ; không dùng override làm kích thước hình học.
3. DIM không truy được đối tượng tham chiếu vẫn được xuất nhưng chỉ dùng kiểm tra khoảng cách gần nhất.
4. Khi DIM và geometry lệch quá `dimensionToleranceMm`, candidate chuyển `review`; không tự ép geometry chạy theo DIM.
5. Associative/anonymous block nội bộ của DIM không được coi là block nghiệp vụ.

### 5.3 Mapping profile

Profile là JSON do doanh nghiệp/dự án duyệt, không hard-code tên layer/family trong Core.

```jsonc
{
  "schemaVersion": "1.0",
  "profileId": "office-mep-v1",
  "sourceSelectors": [
    {
      "match": { "layer": "M-DUCT-*", "entityKinds": ["Line", "Polyline"] },
      "target": { "kind": "Duct", "typeName": "Rectangular Duct", "systemType": "Supply Air" },
      "rules": { "widthFrom": "nearestDimension", "heightFrom": "textRegex" }
    }
  ],
  "blockMappings": [
    {
      "effectiveBlockName": "DIFFUSER_*",
      "targetCategory": "Duct Terminal",
      "familyType": "DHCB_Diffuser : 600x600",
      "parameterMap": { "FLOW": "Flow", "MARK": "Mark" }
    }
  ]
}
```

Profile phải validate schema trước khi chạy. Target family/type/parameter được đối chiếu với **model đang mở**; tên không có thật bị từ chối, không cho AI bịa.

### 5.4 `CadModelPlan`

Một plan là snapshot bất biến của:

- hash manifest;
- hash mapping profile;
- `documentId` và revision Revit;
- transform CAD→Revit đã duyệt;
- danh sách candidate và quyết định.

Mỗi candidate có:

```jsonc
{
  "candidateId": "stable-id",
  "sourceKeys": ["..."],
  "targetKind": "Wall",
  "targetType": "Basic Wall: DHCB-Tuong 200",
  "levelName": "Tầng 2",
  "geometryMm": {},
  "parameters": {},
  "confidence": 0.91,
  "status": "ready",
  "action": "create",
  "evidence": ["layer:A-WALL-200", "dim:200", "double-line:200.8"],
  "warnings": []
}
```

`status`:

- `ready`: đủ điều kiện áp.
- `review`: có thể đúng nhưng cần người duyệt.
- `rejected`: thiếu dữ liệu hoặc mâu thuẫn.
- `unsupported`: loại đối tượng chưa hỗ trợ.

`action`: `create`, `update`, `unchanged`, `orphaned`, `skip`. `orphaned` chỉ báo; không tự xóa.

## 6. Thứ tự nguồn và confidence

### 6.1 Thứ tự bằng chứng

Khi mâu thuẫn, mặc định ưu tiên:

1. Mapping người dùng đã khóa cho đúng project/profile.
2. Attribute của block có schema và kiểu dữ liệu hợp lệ.
3. Hình học native cùng transform đã xác nhận.
4. DIM associative không override.
5. Text theo regex/rule cụ thể.
6. Suy luận tên layer/fuzzy/AI.

AI chỉ đề xuất mapping hoặc phân loại. Validator tất định quyết định candidate có hợp lệ để áp hay không.

### 6.2 Ngưỡng mặc định

- `confidence >= 0.85`: có thể `ready` nếu mọi precondition khác đạt.
- `0.65 <= confidence < 0.85`: `review`.
- `< 0.65`: `rejected`.
- Bất kỳ mâu thuẫn hard constraint nào cũng hạ thành `review/rejected` bất kể confidence tổng.

Các ngưỡng là cấu hình profile nhưng không được phép đặt ngoài `[0,1]` hoặc cho candidate thiếu target type trở thành `ready`.

## 7. Phạm vi cấu kiện

### 7.1 Lát A — nền tọa độ, Grid và Level

- Đọc `INSUNITS`, UCS/WCS, base point và mốc do người dùng chọn.
- Trích grid từ line/polyline/xline/ray + text bubble.
- Trích level từ block attribute/text theo regex profile.
- Preview transform bằng ít nhất hai mốc; ba mốc khi có xoay.
- Tái sử dụng `GridFromCsv` hoặc logic thuần hiện có để tạo.

### 7.2 Lát B — cấu kiện kiến trúc/kết cấu cơ bản

- Wall từ centerline hoặc cặp đường song song.
- Structural Column/Generic Model từ block.
- Door/Window từ block nằm trên wall, có hướng và chiều rộng.
- Floor chỉ từ polyline kín, không tự suy từ hatch ở bản đầu.

Ràng buộc:

- Cặp đường song song phải có khoảng cách trong dải thickness của type đã mapping.
- Polyline tạo floor phải kín trong tolerance, không tự nối khoảng hở lớn.
- Door/window không tìm được host wall → `review/rejected`, không tạo family bay tự do.
- Không join/cut tự động ngoài hành vi chuẩn của API trong lát đầu.

### 7.3 Lát C — thiết bị MEP từ block

- Block → family instance theo effective block name.
- Attribute → parameter theo mapping profile.
- Rotation, insertion point, level và offset được giữ sau transform.
- Host-based family chỉ tạo khi tìm được host phù hợp; nếu không thì báo.
- Connector orientation được kiểm sau khi đặt.

### 7.4 Lát D — tuyến MEP từ đường CAD

- Duct/Pipe/CableTray/Conduit từ centerline đã mapping.
- Size lấy theo thứ tự: attribute/schema → DIM hợp lệ → text rule → type default có cảnh báo.
- System/type phải tồn tại trong model.
- Dùng `RouteFromLines` cho dựng và fitting; không nhân đôi thuật toán connector.
- Đường gãy, cycle, nút quá nhiều nhánh hoặc đoạn ngắn hơn fitting phải được báo trước.

### 7.5 Lát E — né vật cản đa phương án

Mở rộng `AutoRoute` theo hướng cộng thêm, giữ tương thích config hiện có:

```jsonc
{
  "alternativeCount": 3,
  "scoring": {
    "lengthWeight": 1.0,
    "turnWeight": 20.0,
    "verticalWeight": 10.0,
    "nearObstacleWeight": 2.0,
    "preferredElevationMm": 3200,
    "preferredElevationWeight": 3.0
  },
  "buildRoute": false
}
```

Mỗi phương án trả:

- polyline 3D;
- tổng chiều dài;
- số lần rẽ, số lần đổi cao độ;
- clearance nhỏ nhất theo mô hình hộp hiện có;
- số vật cản/link đã xét;
- điểm và phân rã từng thành phần;
- lý do không tìm được nếu thất bại.

Không chạy thật nếu chỉ có một phương án nhưng nó vi phạm hard constraint. Mặc định vẫn `buildRoute=false`.

### 7.6 Lát F — reconcile revision DWG

So manifest mới với binding hiện có:

- cùng `sourceKey` + cùng `geometryHash` → `unchanged`;
- cùng nguồn, geometry/spec đổi → `update`;
- nguồn mới → `create`;
- nguồn mất → `orphaned`;
- phần tử Revit đã bị kỹ sư sửa sau lần apply → `conflict`.

Mặc định:

- tạo/update cần preview và xác nhận;
- orphan chỉ báo;
- conflict không ghi đè;
- không bao giờ xóa hàng loạt vì nguồn CAD biến mất.

## 8. Tính lũy đẳng và liên kết nguồn–đích

Mỗi phần tử do DHCB tạo mang Extensible Storage:

- schema version;
- source file fingerprint;
- `sourceKeys`;
- candidate id;
- mapping profile id/hash;
- geometry/spec hash lần apply;
- thời điểm và phiên bản DHCB.

Một `DataStorage` trong model giữ index tổng để tra nhanh. Element-level storage là nguồn chính; index có thể tái dựng nếu mất.

Quy tắc chạy lại:

1. Cùng source + cùng hash → không đổi.
2. Cùng source + hash khác → lập kế hoạch update, không tự apply.
3. Không tìm thấy binding nhưng geometry gần giống phần tử thủ công → `possibleMatch`, không nhận quyền sở hữu.
4. Chỉ phần tử có binding DHCB mới được update tự động.
5. Một transaction thất bại phải rollback toàn bộ lô; không để nửa mô hình đã tạo.

## 9. Giao diện và MCP

### 9.1 Ribbon/form

Wizard gồm sáu bước:

1. Chọn DWG/manifest và mapping profile.
2. Kiểm đơn vị, layout, xref, chất lượng dữ liệu.
3. Chọn/duyệt hệ tọa độ và level.
4. Duyệt mapping layer/block/type/parameter.
5. Duyệt candidate theo `ready/review/rejected` và phương án tuyến.
6. Preview → Apply → kiểm hậu điều kiện.

Không dùng form JSON chung cho toàn bộ workflow này; số quyết định liên thuộc cần wizard chuyên biệt.

### 9.2 MCP

Tool dự kiến:

- AutoCAD: `CadSemanticExport` — chỉ đọc DWG, ghi manifest/report.
- Revit: `CadModelPlan` — chỉ đọc model, tạo plan/preview.
- Revit: `CadModelApply` — ghi model, bắt buộc preview-commit guard.
- Revit: `CadModelReconcile` — mặc định chỉ đọc và lập diff.

MCP phải expose schema từ `CommandCatalog`; không thêm allowlist cứng riêng làm catalog lệch Core.

Vòng chạy bắt buộc:

```text
query document_context/parameters_of
→ CadModelPlan
→ kỹ sư duyệt
→ CadModelApply(confirm + documentId + previewToken)
→ element_geometry/show_elements/snapshot
→ ConnectorChecker/ClashDetection nếu có MEP
```

## 10. Báo cáo

Mỗi lượt plan/apply sinh JSON và HTML có:

- fingerprint nguồn, profile và model;
- đơn vị/transform/mốc kiểm;
- tổng entity theo loại/layer;
- candidate theo target/status/action/confidence;
- mapping và evidence;
- phần tử bị bỏ qua, loại chưa hỗ trợ, mâu thuẫn DIM/geometry;
- kết quả apply, `changedIds`, lỗi transaction;
- kiểm connector/clash hậu điều kiện;
- version DHCB và timestamp.

HTML phải lọc được theo layer, target kind, level, status và confidence.

## 11. Mã lỗi mới

| Mã | Ý nghĩa |
|---|---|
| `E-CAD-UNIT` | Không xác định hoặc không chấp nhận đơn vị DWG |
| `E-CAD-COORD` | Transform/mốc tọa độ chưa đủ hoặc residual vượt ngưỡng |
| `E-CAD-MAPPING` | Mapping profile hỏng hoặc target không tồn tại |
| `E-CAD-AMBIGUOUS` | Nhiều cách hiểu cạnh tranh, không đủ điều kiện tự chọn |
| `E-CAD-DIM-CONFLICT` | DIM mâu thuẫn với geometry vượt tolerance |
| `E-CAD-HOST-MISSING` | Family host-based không tìm được host |
| `E-CAD-STALE-PLAN` | Manifest/profile/model đã đổi sau preview |
| `E-CAD-CONFLICT` | Phần tử Revit đã bị sửa, không được ghi đè tự động |

Mã phải bổ sung vào `docs/ma-loi.md` cùng test đối chiếu hai chiều khi triển khai.

## 12. Tiêu chí nghiệm thu

### 12.1 Chức năng

- Manifest chứa đủ entity hỗ trợ, giữ handle, transform block lồng và đơn vị mm.
- DIM override được phân biệt với measurement thật.
- Target family/type/parameter bịa bị loại trước transaction.
- Preview và apply dùng cùng plan fingerprint; stale plan bị từ chối.
- Chạy apply lần hai trên cùng input tạo `0` phần tử mới.
- Source đổi không làm mất phần tử thủ công hoặc tự xóa orphan.
- Mỗi phần tử tạo ra truy ngược được tới `sourceKeys`.
- Tuyến MEP sau apply không có connector hở mới ngoài danh sách đã duyệt.
- Phương án né vật cản trả score có thể giải thích; mặc định không tự dựng.

### 12.2 Độ chính xác đề xuất

Các ngưỡng dưới đây là **cổng sản phẩm đề xuất**, phải đo lại trên fixture và dự án pilot:

- Grid/control point: sai lệch tối đa 1 mm trên fixture xác định.
- Vị trí block→family: tối đa 2 mm trên fixture.
- Góc đặt family: tối đa 0,1° trên fixture.
- Cao độ/offset: tối đa 2 mm trên fixture.
- Kích thước từ DIM/attribute: đúng giá trị nguồn trong tolerance profile.
- Không candidate `review/rejected` nào được ghi khi chưa có lựa chọn người dùng.

### 12.3 Hiệu năng mục tiêu

Trên DWG 50.000 entity và model Revit 200.000 element:

- Trích manifest: ≤ 60 giây.
- Lập plan một tầng/một discipline: ≤ 120 giây.
- Preview phải hỗ trợ async/progress và không khóa vô hạn.
- Mỗi apply giới hạn theo batch cấu hình; mặc định tối đa 2.000 candidate.

Các con số là mục tiêu nghiệm thu, không phải bằng chứng hiện tại.

## 13. Điều kiện phát hành

Không phát hành tính năng nếu thiếu bất kỳ mục nào:

1. Test thuần cho parser, transform, inference, diff, score và idempotency.
2. AutoCAD host test tạo DWG fixture rồi trích manifest.
3. Revit host test preview → apply → rerun = 0 → reconcile.
4. Test model link có xoay/dịch, DIM override, block động/lồng, family host-based.
5. Test tuyến MEP với model link vật cản và connector hậu điều kiện.
6. Pilot ít nhất hai DWG thật từ hai dự án; lưu precision/review/reject và thời gian kỹ sư sửa.
7. Không còn blocker độc lập trong review; CI và host regression xanh.

## 14. Chỉ số pilot

Không dùng mỗi “lệnh chạy thành công”. Thu thập:

- precision của candidate đã áp;
- tỷ lệ `ready/review/rejected` theo loại;
- số phút sửa thủ công trên 100 candidate;
- tỷ lệ chạy lại không thay đổi;
- số conflict/orphan khi nhận revision mới;
- tỷ lệ tuyến đạt connector/clash gate;
- số lần kỹ sư chọn phương án thứ 2/3 thay vì phương án đầu;
- câu trả lời “có dùng tiếp không”.

Nếu precision thấp, ưu tiên siết mapping/giảm tự động hóa; không hạ confidence để tăng số lượng phần tử tạo.