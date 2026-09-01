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

### 2.6 Test còn thiếu, cần thêm khi làm §0.3 của đặc tả tính năng

Một test đối chiếu: mọi `ICoreCommand.CommandName` đều có một `case` trong `DispatchCommand` của
Bridge và một nút Ribbon (hoặc được đánh dấu rõ là "chỉ dùng trong batch"). Đây là cách duy nhất để
lỗi #11 (Hanger/PipeSplitter viết xong mà không gọi được từ đâu) không tái diễn.

### 2.7 Test cần thêm cho batch runner (§1 đặc tả tính năng)

`JobTokens.Expand` (thay `{outputFolder}`, `{fileName}`, `{yyyy-MM-dd}`), phân giải thứ tự step,
xử lý file lỗi giữa lô, và ghi/đọc log JSONL — tất cả đều thuần, phải có test trước khi runner chạy
đêm thật.

## 3. Ngưỡng chất lượng

- Mọi thuật toán trong `Shared.Logic` phải có test cho: đường đi thường, biên (0, âm, rỗng, `null`),
  và **đúng cái lỗi cũ đã gây ra** — mỗi lỗi trong `progress.md` khi sửa phải kèm một test tái hiện.
- Test không được phụ thuộc culture máy chạy: chỗ nào liên quan tới số/ngày thì tự đặt culture.
- Test không đọc/ghi ngoài thư mục tạm.

## 4. Kịch bản thủ công (phần cần Revit/AutoCAD thật)

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
