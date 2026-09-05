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

## Hỗ trợ tới đâu

| Facet IDS | DHCB đọc từ đâu trong Revit |
|---|---|
| `entity` (+ `predefinedType`) | `IfcExportAs` (dạng `IfcWall.SOLIDWALL`), rồi bảng category → lớp IFC |
| `attribute` | `Name`, `Tag` (= Mark), `Description`, `ObjectType`, `GlobalId`; tên khác thì thử như một tham số cùng tên |
| `property` | tham số `"Pset_Tên.Prop"`, rồi tham số cùng tên ở instance, rồi ở type — đúng thứ tự bộ xuất IFC lấy giá trị |
| `classification` | `Assembly Code`, `Keynote`, `ClassificationCode` |
| `material` | vật liệu của phần tử, kể cả vật liệu lớp cấu tạo |
| `partOf` | tầng và `System Name` / `System Classification` |

Ràng buộc giá trị: `simpleValue`, `xs:enumeration`, `xs:pattern` (**neo hai đầu** — XSD khớp toàn bộ chuỗi,
không neo thì `AB-01-rác` cũng đạt quy tắc `AB-\d\d`), `minInclusive` / `maxInclusive` / `minExclusive` /
`maxExclusive`. `cardinality="prohibited"` (có mới là sai) và `"optional"` đều đọc.

**Gặp thứ chưa hỗ trợ thì từ chối file, không bỏ qua im lặng** — facet lạ, ràng buộc lạ (`minLength`…), file
không có `<specification>` nào, specification không có `<requirements>` nào. Lý do: một quy tắc bị lờ đi vẫn
in ra dấu ✓, và người đọc báo cáo không có cách nào biết là nó chưa từng được kiểm.

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
> đánh dấu riêng (`0 phần tử — không kiểm được gì`) và đếm riêng trong summary. Con số 0 ở đó nói về bộ lọc
> hoặc về việc mô hình thiếu hẳn nhóm phần tử ấy, chứ không nói về chất lượng mô hình — đúng bài học của §16
> (`ClashDetection` với nhóm category rỗng).

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

- **Đã đối chiếu với IfcTester** trên chính IFC xuất từ Snowdon (§39: khớp cả 3 specification sau khi sửa ánh
  xạ tường kính). Solibri không có trên máy.
- **Tên facet khai bằng `xs:pattern`** (ví dụ "mọi property khớp `Fire.*`") không suy ngược ra tên được, nên
  facet đó **trượt** thay vì âm thầm coi như đạt.
- Ràng buộc độ dài chuỗi (`minLength`/`maxLength`) và `partOf` theo quan hệ IFC đầy đủ chưa hỗ trợ.
- Bảng category → lớp IFC là **bảng rút gọn** cho nhóm hay gặp; family lạ thì khai `IfcExportAs` để chắc chắn.
