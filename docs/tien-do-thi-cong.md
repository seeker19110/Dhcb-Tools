# Trạng thái thi công và báo cáo tiến độ — `ConstructionStatus` + `ProgressReport`

Đề xuất **B1** trong [`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md) §4 (đợt B, chặng
thi công): thay việc **nhập trạng thái lắp đặt/nghiệm thu vào một file Excel rời** sống song song với mô hình,
rồi **vẽ tay biểu đồ tiến độ**. Hai lệnh chạy được từ Ribbon (*Xuất & Báo cáo → Tiến độ thi công*), Bridge/MCP,
batch đêm và lớp AI như mọi lệnh khác.

> **Trạng thái: thử nghiệm.** Tầng thuần có 41 ca test trên CI, hai lệnh biên dịch xanh với API Revit 2023–2027,
> có 6 ca kiểm trong `tests/suites/` — nhưng **chưa chạy thật trong Revit**. Riêng **đường ghi** của
> `ConstructionStatus` (ghi trạng thái vào phần tử có thật) chưa có ca kiểm nào chạy được trên model mẫu, vì mã
> cấu kiện trong CSV là `ElementId` của đúng file đang mở nên không viết sẵn được vào fixture. Đó là việc đầu
> tiên khi có máy cài Revit.

## Hai lệnh, hai chiều

| Lệnh | Chiều | Việc |
|---|---|---|
| `ConstructionStatus` | ghi mô hình | Đọc CSV hiện trường (mã cấu kiện → trạng thái, ngày, người xác nhận, ghi chú) và ghi vào tham số của phần tử. `dryRun` mặc định bật |
| `ProgressReport` | chỉ đọc | Gộp trạng thái theo tầng / hệ / category → HTML + CSV, kèm luỹ kế theo tuần |

Trạng thái sống **trong mô hình**, nên `ColorByParameter` tô màu được theo chính tham số đó, `snapshot` chụp
được, `ParameterExport` xuất ra được, và `ClashDetection`/`SystemBom` nhìn cùng một sự thật.

## Từ vựng trạng thái

Bốn mức, có **thứ hạng** (mức sau bao hàm mức trước):

| Tên chuẩn ghi vào mô hình | Cũng nhận |
|---|---|
| `Chưa lắp` | Chưa lắp đặt, Chưa thi công, Not started, Not installed, Pending |
| `Đang lắp` | Đang lắp đặt, Đang thi công, In progress, Installing, WIP |
| `Đã lắp` | Đã lắp đặt, Đã thi công, Lắp xong, Installed, Complete, Completed, Done |
| `Đã nghiệm thu` | Nghiệm thu, Đã bàn giao, Accepted, Approved, Handover, Signed off |

Không phân biệt hoa thường, **có dấu hay không dấu đều được** (`da lap`, `ĐÃ LẮP`, `da-nghiem-thu`) — file CSV
này do người gõ tay ngoài công trường. Nhưng chữ không nhận ra thì **báo đúng số dòng kèm danh sách hợp lệ**,
không đoán và không bỏ qua im lặng: một dòng bị nuốt là một cấu kiện biến mất khỏi báo cáo mà không ai biết.

Ô trạng thái **để trống** là "chưa ai ghi nhận" — khác hẳn "đã ghi nhận là chưa lắp", và hai thứ này được đếm
riêng suốt cả tầng thuần lẫn báo cáo.

## Ba quy tắc của phần trăm

Đây là phần dễ nói dối nhất của một báo cáo tiến độ, nên cả ba đều được chốt bằng test:

1. **Mẫu số là toàn bộ cấu kiện trong phạm vi**, kể cả cái chưa ai ghi nhận. Chưa nhập thì chưa lắp; không
   được lặng lẽ bỏ chúng ra khỏi mẫu số cho phần trăm đẹp lên. Báo cáo in thẳng số lượng ấy ra.
2. **"Đang lắp" không có trọng số.** Một cấu kiện đang lắp không phải nửa cái ống, nên nó chỉ được đếm ở cột
   của chính nó, không cộng nửa vào phần trăm hoàn thành.
3. **Phần đã lắp mà không có ngày được đếm riêng.** Nó không vẽ được lên trục thời gian, nên đường luỹ kế theo
   tuần thấp hơn tổng ở bảng trên — báo cáo nói rõ chênh lệch đó thay vì để người đọc tự phát hiện.

Phần trăm tính hai cách và **thường khác nhau**: theo **số lượng** cấu kiện, và theo **chiều dài** với
ống/duct/tray (`CURVE_ELEM_LENGTH`). Một tuyến trục 90 m đã lắp không bằng ba đoạn nhánh 5 m — bảng Excel gõ
tay không phân biệt được điều này. Nhóm không có cấu kiện dạng đường thì cột chiều dài hiện `—` chứ không hiện
`0 %`: "không đo được" và "0 %" là hai chuyện khác nhau.

## Luỹ kế theo tuần

Tuần bắt đầu **thứ Hai**, gọi theo ngày đầu tuần (`31/08/2026`) chứ không theo "tuần số mấy" — số tuần ISO là
chỗ mỗi phần mềm đếm một kiểu. Tuần không có gì mới vẫn xuất hiện trong bảng để đường luỹ kế liền mạch.

## Tham số, không có tên cứng trong mã

Theo **nguyên tắc 7** của [`roadmap.md`](roadmap.md), ba khoá từ điển mới — `constructionStatus`,
`constructionDate`, `constructionBy` — tra qua `%APPDATA%\DHCB\dictionary.json`
(mẫu: [`configs/dictionary.sample.json`](../configs/dictionary.sample.json)), hoặc đặt thẳng tên trong config
(`statusParameter`, `dateParameter`, `personParameter`). Tên dựng sẵn chỉ là phỏng đoán hay gặp
(`DHCB_Trang_Thai`, `Trạng thái thi công`, `Construction Status`, `Status`…); `DictionaryLearn` soi tên **có
thật** của dự án và ghi lên đầu danh sách.

Tra không ra thì cả hai lệnh **dừng bằng `E-PARAM-MISSING`** kèm danh sách tên đã thử. Với `ProgressReport` điều
này quan trọng hơn vẻ ngoài: một báo cáo 0 % cho mọi nhóm vì tra sai tên tham số trông y hệt một công trường
chưa khởi công.

## Ba đường chặn no-op im lặng

| Tình huống | Mã | Vì sao chặn |
|---|---|---|
| CSV không có dòng nào đọc được | `E-PRECOND` | "0 phần tử cập nhật" khi file hỏng nói về file, không nói về công trường |
| Không mã nào trong CSV khớp phần tử của mô hình đang mở | `E-PRECOND` | File của mô hình khác — `ElementId` chỉ có nghĩa trong đúng file sinh ra nó |
| Không phần tử nào **mang** tham số trạng thái | `E-PARAM-MISSING` | Chưa gắn shared parameter, không phải "chưa lắp gì" |

Thêm một lớp nữa ở `ConstructionStatus`: **lùi trạng thái bị chặn mặc định**. Đã nghiệm thu mà CSV ghi đang lắp
gần như luôn là nhập đè một file cũ, và nó xoá mất một mốc nghiệm thu đã ghi nhận. Muốn sửa thật thì đặt
`allowDowngrade: true`; lệnh liệt kê từng dòng bị chặn kèm giá trị hiện tại.

## Config

```json
{
  "inputPath": "C:/DHCB/hien-truong/2026-09-05.csv",
  "statusParameter": "DHCB_Trang_Thai",
  "dateParameter": "DHCB_Ngay_Lap",
  "personParameter": "DHCB_Nguoi_Xac_Nhan",
  "noteParameter": "",
  "dateFormat": "dd/MM/yyyy",
  "allowDowngrade": false,
  "dryRun": true
}
```

```json
{
  "categories": [],
  "groupBy": "Level",
  "statusParameter": "DHCB_Trang_Thai",
  "dateParameter": "DHCB_Ngay_Lap",
  "levelName": "",
  "systemContains": "",
  "outputPath": "C:/DHCB/tien-do/2026-09-05.html",
  "csvPath": "C:/DHCB/tien-do/2026-09-05.csv"
}
```

`categories` rỗng = nhóm MEP và thiết bị mặc định (ống, duct, tray, conduit, thiết bị cơ/điện/nước, sprinkler,
miệng gió). `groupBy` nhận `Level`, `System`, `Category`.

## CSV hiện trường

```
ElementId,Trạng thái,Ngày,Người xác nhận,Ghi chú
123456,Đã lắp,03/09/2026,Nguyễn Văn A,lắp trước khi đóng trần
123457,Đã nghiệm thu,2026-09-04,Trần B,
```

Tiêu đề nhận nhiều cách viết (`ElementId`/`Id`/`Mã cấu kiện`, `TrangThai`/`Trạng thái`/`Status`, `Ngày`/`Date`,
`Người xác nhận`/`By`, `Ghi chú`/`Note`); chỉ hai cột đầu là bắt buộc. Ngày viết **ngày trước tháng**
(`03/09/2026`) hoặc dạng ISO (`2026-09-03`). Mã trùng nhau thì lấy dòng sau cùng và nói ra.

Lấy danh sách mã cấu kiện để phát cho hiện trường bằng `ParameterExport` trên chính file đang mở — cột đầu của
file đó là `ElementId`.

## Còn thiếu

- **Chưa chạy thật trong Revit**, và đường ghi của `ConstructionStatus` chưa có ca kiểm tự động (mã cấu kiện
  phụ thuộc file). Việc đầu tiên khi có máy: gắn một shared parameter trạng thái vào model mẫu, xuất
  `ParameterExport` lấy id thật, ghi trạng thái cho vài phần tử, rồi chạy `ProgressReport`.
- Chưa đọc phần tử trong **model liên kết** — tiến độ tính trên mô hình đang mở.
- Chưa có đường ngược lại (xuất mẫu CSV trống theo phạm vi để hiện trường điền). `ParameterExport` làm gần đủ;
  làm thêm khi có ban chỉ huy thật yêu cầu, đúng thứ tự "sau khi có số liệu 9.4".
