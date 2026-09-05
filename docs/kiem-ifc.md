# Kiểm IFC trước khi nộp (mục 11.2)

> Trạng thái: tầng thuần và công cụ dòng lệnh đã xong; xem [`roadmap.md`](roadmap.md) mục 11.2.

## Vì sao có mục này

**NĐ 217/2026/NĐ-CP** bắt chủ đầu tư **cung cấp dữ liệu BIM cho cơ quan chuyên môn**, và mô hình phải
được cập nhật sau hoàn công rồi chuyển cho đơn vị vận hành. Khi file IFC là **thứ nộp đi**, "xuất được"
không còn là đủ — phải **xuất rồi tự đọc lại thấy đúng**.

Lý do kỹ thuật thì cũ hơn luật: bộ xuất IFC của Revit **bỏ phần tử trong im lặng** khi mapping thiếu
category, khi phần tử nằm ngoài view được xuất, hay khi một family không dựng được hình khối — `Document.Export`
không ném ngoại lệ nào cho những chỗ đó. Không đọc lại thì đến lúc bên nhận mở file mới biết thiếu, và lúc
đó hồ sơ đã nộp.

Chính vòng làm mục này đã gặp một dạng nặng hơn: `BatchExport` định dạng `Ifc` **chưa bao giờ tạo ra file
nào** (Revit đòi transaction cho riêng đường IFC), mà lỗi bị gom vào danh sách `Errors` rồi lệnh vẫn báo
thành công. Xem [`bang-chung-test.md`](bang-chung-test.md) §27.

## Chạy

```bash
DhcbTools.BatchRunner --verify-ifc "D:\xuat\toa-a.ifc" --ifc-spec configs\ifc-check.json
```

Bỏ `--ifc-spec` thì dùng **bộ quy tắc mặc định**: có lược đồ, đúng một `IfcProject`, mã định danh không
rỗng không trùng, không tham chiếu gãy. Bộ mặc định **không đoán** dự án cần bao nhiêu bức tường.

| Mã thoát | Nghĩa |
|---|---|
| 0 | Đạt — không có lỗi (có thể còn cảnh báo) |
| 1 | Không đạt — in ra từng lỗi kèm số hiệu thực thể |
| 2 | Không có file IFC, không có file quy tắc, hoặc file quy tắc hỏng |

Đặt cạnh `--verify-log` của mục 11.5 là có ý: **kiểm một file IFC không cần `Document` nào**, nên làm
thành lệnh Core thì vướng nguyên tắc 6 (phải có ca kiểm chạy trong Revit) mà chẳng đổi lại được gì. Ở
dòng lệnh thì chạy được trên CI, trên máy không cài Revit, và chạy lại được trên file đã nộp từ tháng
trước.

## File quy tắc

Mẫu: [`configs/ifc-check.sample.json`](../configs/ifc-check.sample.json).

```json
{
  "schema": "IFC4",
  "requireUniqueGlobalId": true,
  "requireResolvedReferences": true,
  "minEntities": 100,
  "rules": [
    { "type": "IfcProject", "minCount": 1, "maxCount": 1 },
    { "type": "IfcWallStandardCase", "exactCount": 248, "requireName": true,
      "requireProperties": ["Pset_WallCommon.IsExternal"] }
  ]
}
```

| Trường | Nghĩa |
|---|---|
| `schema` | Tên lược đồ bắt buộc (`IFC4`, `IFC2X3`); bỏ trống là không kiểm |
| `requireUniqueGlobalId` | Mã định danh toàn cục không rỗng và không trùng. **Mặc định bật** — bên nhận cập nhật mô hình theo mã này, trùng mã là ghi đè nhầm phần tử |
| `requireResolvedReferences` | Mọi tham chiếu `#N` trỏ tới thực thể có thật. **Mặc định bật** — đây là dấu hiệu bộ xuất bỏ sót phần tử mà vẫn giữ quan hệ trỏ tới nó |
| `minEntities` | Tổng số thực thể tối thiểu — chặn file rỗng mà vẫn báo xuất thành công |
| `rules[].type` | Tên kiểu IFC **đầy đủ** |
| `rules[].minCount` / `maxCount` / `exactCount` | Số lượng. `exactCount` dùng khi đã đếm được số phần tử trong mô hình trước lúc xuất |
| `rules[].requireName` | Bắt buộc có tên không rỗng |
| `rules[].requireProperties` | `Pset_WallCommon.IsExternal` để chỉ đúng bộ, hay chỉ `IsExternal` để chấp nhận bất kỳ bộ nào |
| `rules[].requireClassification` | Bắt buộc có mã phân loại (Uniclass/OmniClass) gán qua `IfcRelAssociatesClassification` |
| `rules[].listLimit` | Kể tên tối đa bao nhiêu phần tử vi phạm rồi nói "và N nữa" (mặc định 10) |

## Ba chỗ dễ hiểu nhầm

1. **Không suy ra lớp con.** `IfcWall` và `IfcWallStandardCase` là **hai quy tắc khác nhau**. Bộ đọc cố
   ý không mang bảng lược đồ EXPRESS: mỗi bản IFC2X3/IFC4/IFC4X3 lại đổi cây kế thừa, mà bảng ấy sai một
   dòng thì quy tắc trượt im lặng — nguy hiểm hơn hẳn việc bắt kỹ sư kể tên đầy đủ. Muốn phủ cả hai thì
   viết hai quy tắc.
2. **Thuộc tính có mặt nhưng bỏ trống vẫn là thiếu**, và được báo thành dòng riêng ("bỏ trống" chứ không
   phải "thiếu"). Bên thẩm tra đọc file không phân biệt được "chưa điền" với "không có".
3. **Đây là quy tắc nội bộ về đầu ra của bộ xuất, không phải yêu cầu của chủ đầu tư.** Yêu cầu của chủ
   đầu tư/tư vấn thẩm tra thì khai bằng **IDS 1.0** (mục 11.1) để bên ngoài kiểm lại bằng IfcTester hay
   Solibri cũng ra cùng kết quả. Đừng dựng lại IDS trong file này.

## Đọc được tới đâu

Tầng thuần nằm ở `Shared.Logic/Ifc` (bộ đọc STEP viết tay, không phụ thuộc thư viện IFC nào nên chạy
được trên CI), 44 ca test ở `IfcTests`.

**Đọc được:** cú pháp STEP đầy đủ (chuỗi có dấu `;` và ngoặc bên trong, chú thích, thực thể xuống nhiều
dòng, giá trị bọc kiểu, danh sách lồng nhau); dãy thoát Unicode `\X2\`/`\X4\`/`\X\`/`\S\` — **tên tiếng
Việt trong file Revit xuất ra đọc đúng dấu**; thuộc tính qua `IfcRelDefinesByProperties`, **kể cả thuộc
tính thừa kế từ kiểu** qua `IfcRelDefinesByType` (giá trị đặt trên phần tử thắng giá trị của kiểu); đại
lượng trong `IfcElementQuantity`; phân loại qua `IfcRelAssociatesClassification`.

**Không đọc:** file IFC nén (`.ifcZIP`), IFC-XML, IFC-JSON. Ba định dạng đó không phải thứ Revit xuất ra
mặc định; thêm khi có nhu cầu thật.

## Đặt vào đêm batch

Job đêm xuất IFC bằng `BatchExport`, rồi bước sau chạy `--verify-ifc` trên chính file vừa xuất. Mã thoát
1 là đêm đó **không có file để nộp** — biết lúc 2 giờ sáng còn hơn biết lúc nộp hồ sơ.

Bằng chứng chạy thật: [`bang-chung-test.md`](bang-chung-test.md) §27 — chạy trên file Revit xuất thật, 925.815 thực thể / 91 MB, kiểm hết trong 5,1 giây.
