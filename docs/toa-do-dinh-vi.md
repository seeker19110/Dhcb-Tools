# Toạ độ định vị ra máy toàn đạc — `SetoutExport`

Đề xuất **A1** trong [`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md) §4: thay khâu
trắc đạc **đọc bản vẽ rồi gõ tay** toạ độ tim cột / lỗ mở / giá đỡ vào máy toàn đạc. Lệnh **chỉ đọc**, không
mở transaction, chạy được từ Ribbon (*Xuất & Báo cáo → Xuất khác → Toạ độ định vị*), Bridge/MCP, batch đêm
và lớp AI như mọi lệnh khác.

> **Đã chạy thật trong Revit 2024 (2026-09-05, [`bang-chung-test.md`](bang-chung-test.md) §28).** Model kiến trúc:
> **260 điểm** (118 tim cột + 142 giao trục), CSV + DXF có `POINT`/`TEXT` trên layer `DHCB-GRD`, toạ độ Survey thật;
> model HVAC: 545 điểm thiết bị. Lượt đó lộ một lỗi thật (tên điểm cắt đuôi mất phần phân biệt) đã sửa kèm test.
> Nhãn *(thử nghiệm)* đã gỡ 2026-09-06. **Còn phải làm tay:** đối chiếu một điểm bằng Spot Coordinate hoặc máy
> toàn đạc trên model có khai toạ độ chung thật — việc của người trên công trường, không phải của mã.

## Lấy điểm nào

| Phần tử | Điểm lấy | `Kind` |
|---|---|---|
| Có điểm đặt (`LocationPoint`): cột, sleeve/generic model, thiết bị, sprinkler, miệng gió, cửa | **trọng tâm solid** của hình học gốc family (trước join), Z của điểm chèn — `pointMode: "Centre"` (mặc định). Điểm chèn family **không chắc là tim**: họ *Rectangular Column (Off Center)* của Snowdon lệch tới 305 mm (§52). `pointMode: "Insertion"` lấy đúng điểm chèn; lệnh luôn đếm và báo số phần tử hai điểm này lệch nhau > 1 mm | `tim` |
| Có đường đặt (`LocationCurve`): dầm, tường, ống, duct, cột nghiêng | hai đầu (`curvePoints: "Ends"`, mặc định), điểm giữa (`Mid`) hoặc cả ba (`Both`) | `đầu`, `giữa`, `cuối` |
| Không có Location | tâm hộp bao — thông báo đếm riêng để kiểm tay | `tâm hộp bao` |
| Trục (`includeGridIntersections: true`) | giao điểm từng cặp **trục thẳng** cắt nhau trong phạm vi vẽ (dung sai 1 mm); trục cong bị bỏ qua và báo số lượng | `giao trục` |

Mặc định lấy `Structural Columns` + `Columns` — thứ trắc đạc cắm đầu tiên. `elementIds` (ví dụ từ
`query selection`) thay cho `categories` khi cần đúng vài phần tử. Lệnh chỉ đọc **mô hình đang mở**, không
đọc model liên kết.

## Hệ toạ độ

- **`Survey`** (mặc định): toạ độ chung theo điểm khảo sát — `ActiveProjectLocation.GetTotalTransform()`.
  Chiều của transform được **tự kiểm** bằng `GetProjectPosition` tại hai điểm (gốc và điểm cách gốc 1 m)
  thay vì tin vào tài liệu API; không chiều nào khớp thì thông báo cảnh báo rõ.
- **`Internal`**: gốc nội bộ Revit — đúng bằng số `element_geometry` trả về, dùng để agent đối chiếu.

Thông báo luôn có dòng `Site "<tên site>": gốc nội bộ ở E=… N=… Z=… mm, True North xoay …°`. Nếu cả bốn
số đều 0, lệnh nói thêm *"Hệ Survey trùng hệ nội bộ (mô hình chưa khai toạ độ chung)"* — file ra khi đó là
toạ độ Revit, không phải toạ độ khảo sát; đối chiếu với tổ trắc đạc trước khi dùng.

## Định dạng file CSV

Không có "bảng mẫu máy" nào phải bảo trì: kỹ sư gõ **thứ tự cột** đúng như phần mềm máy gọi.

| Chữ | Cột | Ghi chú |
|---|---|---|
| `P` | tên điểm | bắt buộc |
| `N` | Bắc (Y) | bắt buộc |
| `E` | Đông (X) | bắt buộc |
| `Z` | cao độ | |
| `D` | mô tả | theo `descriptionPattern`, mặc định `{Category} {Level}` |
| `C` | mã ngắn | `COL`, `GM`, `ME`, `GRD`… theo category (`SetoutCodes`) |
| `L` | tầng | |
| `I` | ElementId | để truy ngược về mô hình / đối chiếu bằng `element_geometry` |

Mặc định `PNEZD` (Trimble, Leica); nhiều máy Topcon/Sokkia nhận `PENZD`. Sai một chữ là báo lỗi kèm danh sách
hợp lệ, không đoán. Đơn vị `m` (3 số lẻ) hoặc `mm` (0 số lẻ), đổi bằng `decimals`. Số luôn dùng **dấu chấm**
thập phân, không có `-0.000`, dòng kết thúc CRLF. `includeHeader` mặc định bật — tắt nếu phần mềm máy không bỏ
qua được dòng đầu. `utf8Bom` mặc định **tắt**: nhiều phần mềm máy đọc BOM thành ký tự lạ dính vào tên điểm đầu
tiên; chỉ bật khi mở bằng Excel để xem tiếng Việt.

Ví dụ `PNEZD`, mét:

```
Name,N,E,Z,Desc
COL001,1017.250,2003.125,5.400,Structural Columns Level 1
A-1,1000.000,2000.000,5.400,Grids Level 1
```

## Tên điểm

`namePattern` mặc định `{Code}{n:000}` → `COL001`, `ME001`…; bộ đếm **đếm riêng theo mã**, thứ tự tầng →
mã → phần tử (tầng sắp theo số tự nhiên, `Level 2` trước `Level 10`). Token: `{Code}`, `{Category}`,
`{Family}`, `{Type}`, `{Level}`, `{Mark}`, `{Id}`, `{Kind}`, `{n}`; giao trục dùng `gridNamePattern` mặc định
`{Grid}` → `A-1`. Tên được **làm sạch cho máy**: bỏ dấu tiếng Việt, khoảng trắng → `_`, bỏ dấu phẩy/nháy;
rút về `maxNameLength` (mặc định 16 — giới hạn Leica/Trimble); **không bao giờ có hai điểm cùng tên** (trùng
thì thêm `_2`, `_3` và ghi chú trong thông báo).

Tên dài quá giới hạn thì **bỏ bớt ở giữa**, giữ cả đầu lẫn đuôi, đánh dấu `..`:
`Block_35_Left-B.1` → `Block_3..eft-B.1`. Lý do không cắt đuôi: tên điểm gần như luôn là tên ghép —
giao trục là `TrụcA-TrụcB`, mẫu hay dùng là `{Level}-{Grid}` — nên **phần phân biệt nằm ở đuôi**, còn
phần đầu giống nhau ở hàng trăm điểm. Vòng chạy thật trên Snowdon Towers cho ra `Block_35_Left-Bl`,
`Block_35_Left-B.`, `Block_35_Left-X_`: đúng 16 ký tự, đúng là duy nhất, mà trắc đạc không biết đó là
giao trục nào — tên duy nhất mà không đọc được thì cũng chọn nhầm điểm như tên trùng
([`bang-chung-test.md`](bang-chung-test.md) §28).

## DXF điểm

`dxfPath` (tuỳ chọn) ghi DXF ASCII tối thiểu (chỉ section ENTITIES): mỗi điểm một `POINT` trên layer
`DHCB-<mã>` và một `TEXT` tên điểm trên `DHCB-<mã>-TEN`, X = Đông, Y = Bắc, cùng đơn vị với CSV. Dùng cho
phần mềm máy đời cũ chỉ nhập DXF, hoặc để mở trong AutoCAD đối chiếu với mặt bằng.

## Đối chiếu trước khi giao file

Playbook [`skills/xuat-toa-do-dinh-vi/`](../skills/xuat-toa-do-dinh-vi/SKILL.md): xuất → đọc dòng gốc Survey
→ chọn một điểm có `ElementId` → `query element_geometry` (toạ độ nội bộ, mm) hoặc *Spot Coordinate* trong
Revit → khớp mới giao. Bước này là chỗ duy nhất bắt được sai hệ toạ độ.

**Đối chiếu bằng đường độc lập — IFC của Autodesk.** `query element_geometry` và Spot Coordinate đều là đọc lại
cùng một mô hình; thứ độc lập với mã DHCB là **bộ xuất IFC của Autodesk** (Revit → IFC, `BatchExport` với
`formats: ["Ifc"]`): IFCSITE mang gốc Survey và góc True North, IFCGRID mang từng trục, IFCCOLUMN mang thân cột.

```bash
python scripts/doi-chieu-setout-ifc.py C:/DHCB/setout/L1.csv "C:/DHCB/export/Model.ifc" --tol-mm 5
```

Script in gốc IFCSITE (so với dòng *"Site … gốc nội bộ"* trong Messages), khoảng cách từng giao trục và từng tim
cột tới điểm IFC gần nhất, tách riêng cột mà thân IFC **bị cắt bởi join** (nhỏ hơn cỡ danh nghĩa trong tên type —
tâm thân đó không phải tim). Mã thoát 1 khi có điểm lệch quá ngưỡng. Kết quả trên Snowdon (§52): gốc Survey
khớp **từng mm**, 142/142 giao trục lệch ≤ 0,7 mm, 57/118 tim cột ≤ 5 mm; 61 cột còn lại đều thuộc họ *Off
Center* và lệch 52–305 mm — ở đó bộ xuất IFC và hình học Revit **không đồng ý** với nhau về vị trí thân cột, lệnh
đi theo hình học Revit (trọng tâm solid). Với cột lệch tâm, giao file kèm dòng ghi chú của lệnh để trắc đạc biết.

## Config đầy đủ

```json
{
  "categories": ["Structural Columns", "Columns"],
  "elementIds": [],
  "levelName": "Level 1",
  "familyContains": "",
  "typeContains": "",
  "coordinateSystem": "Survey",
  "columns": "PNEZD",
  "unit": "m",
  "decimals": 3,
  "includeHeader": true,
  "namePattern": "{Code}{n:000}",
  "gridNamePattern": "{Grid}",
  "descriptionPattern": "{Category} {Level}",
  "curvePoints": "Ends",
  "pointMode": "Centre",
  "includeGridIntersections": true,
  "maxNameLength": 16,
  "utf8Bom": false,
  "outputPath": "C:/DHCB/setout/L1.csv",
  "dxfPath": "C:/DHCB/setout/L1.dxf"
}
```

Bridge/MCP: `python scripts/dhcb_agent.py revit exec SetoutExport --config '{"outputPath":"C:/DHCB/setout/L1.csv","includeGridIntersections":true}'`.
Batch đêm: một step `SetoutExport` trong file job như mọi lệnh chỉ đọc khác — xem
[`batch-runner.md`](batch-runner.md).

## Mã lỗi

`E-PRECOND` khi bộ lọc không cho ra điểm nào (không ghi file rỗng rồi báo thành công); category hoặc tầng không
có → thông báo kèm danh sách có thật; cột/đơn vị/`curvePoints`/`coordinateSystem` sai → thông báo kèm giá trị
hợp lệ. Bảng mã chung: [`ma-loi.md`](ma-loi.md).

## Còn thiếu

- ~~Chưa chạy thật trong Revit~~ — đã chạy (§28) và **đã đối chiếu độc lập** bằng IFC của Autodesk (§52). Còn
  mở: **họ cột "Off Center"** — hình học Revit và bộ xuất IFC đặt thân cột lệch nhau 52–305 mm; chưa có đường
  thứ ba (máy toàn đạc trên công trường, hay mở family để đọc offset) để phân xử. Cột đối xứng đặt tâm (57/118
  của Snowdon) khớp ≤ 5 mm.
- Chưa đọc phần tử trong model liên kết (kết cấu thường là file link khi mở file MEP).
- Chưa có mẫu riêng cho định dạng nhị phân/GSI của Leica — CSV theo cột và DXF là hai định dạng mọi phần mềm
  máy đều nhập được; làm thêm khi có tổ trắc đạc thật yêu cầu (đúng thứ tự "sau khi có số liệu 9.4").
