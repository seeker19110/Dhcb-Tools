# Xuất BCF gửi tư vấn — `bcfPath`

Đề xuất **B3** trong [`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md) §4: thay việc
**chụp màn hình va chạm dán vào Word** gửi tư vấn. BCF (BIM Collaboration Format) là chuẩn mở của
buildingSMART để trao đổi *vấn đề cần xử lý* giữa các phần mềm — người nhận mở bằng Solibri, BIMcollab,
Navisworks, BIMx hay Revit (qua add-in BCF) là **nhảy thẳng tới đúng phần tử, đúng góc nhìn**.

> **Không phải lệnh mới.** BCF là **một đầu ra thêm** của ba lệnh đã chạy thật, bật bằng trường `bcfPath`.
> Lý do: thêm một lệnh Core thì vướng **nguyên tắc 6** (phải có ca kiểm chạy trong Revit cho lệnh đó), trong
> khi ba lệnh này đã qua vòng chạy thật rồi và đang giữ sẵn đúng dữ liệu cần. Cùng cách đã chọn cho
> `--verify-log` ở mục 11.5 thay vì lệnh `EvidenceVerify`.

## Ba lệnh, ba loại vấn đề

| Lệnh | Mỗi topic là | Góc nhìn |
|---|---|---|
| `ClashDetection` | một va chạm, hai phần tử là component chọn sẵn | camera đặt vào **tâm va chạm**, đứng chéo 45° phía trên |
| `ParameterRuleCheck` | một **phần tử** vi phạm, gộp mọi vi phạm của nó vào phần mô tả | không có camera (vi phạm tham số không có toạ độ) |
| `WarningsExport` | một cảnh báo Revit, phần tử gây lỗi là component | không có camera |

`ParameterRuleCheck` gộp theo phần tử chứ không theo vi phạm là có chủ ý: một cửa thiếu ba tham số là **một
việc phải sửa**, không phải ba việc — người nhận BCF đọc theo phần tử.

```json
{
  "categoriesA": ["Ducts", "Pipes"],
  "categoriesB": ["Structural Framing"],
  "outputPath": "C:/DHCB/clash/2026-09-05.html",
  "bcfPath": "C:/DHCB/clash/2026-09-05.bcf"
}
```

Bỏ trống `bcfPath` thì không có gì thay đổi so với trước.

## Định danh phần tử: IFC GUID, không phải ElementId

BCF chỉ tới phần tử bằng **IFC GUID** 22 ký tự, không bằng `ElementId`. DHCB lấy guid bằng
`ExportUtils.GetExportId` — **đúng guid mà chính bộ xuất IFC của Revit dùng** — nên phần tử trong file BCF
khớp với phần tử trong file IFC đã nộp cho chủ đầu tư. Phần tử của **model liên kết** lấy guid theo document
của chính link đó.

`ElementId` vẫn được ghi kèm vào `AuthoringToolId` của component, để mở lại đúng phần tử trong chính mô hình
đã sinh ra file. Không lấy được guid thì bỏ component đó chứ không bỏ cả vấn đề: một topic không có component
vẫn mở được, chỉ là không tự chọn phần tử.

## Cấu trúc file

BCF **2.1**, đuôi `.bcf` (2.0 dùng `.bcfzip`). File là một zip:

```
bcf.version                     ← VersionId="2.1"
<guid>/markup.bcf               ← Topic: tiêu đề, loại, trạng thái, ngày, tác giả, mô tả, nhãn, bình luận
<guid>/viewpoint.bcfv           ← component chọn sẵn + camera (khi có)
<guid>/snapshot.png             ← ảnh (khi có)
```

Thứ tự thẻ bám đúng XSD 2.1 (`Title` → `Priority` → `Labels` → `CreationDate` → `CreationAuthor` →
`Description`; trong `Components` thì `Selection` trước `Visibility`) vì máy đọc nghiêm ngặt **từ chối cả
file** khi sai thứ tự. Số luôn dấu chấm thập phân, không phụ thuộc culture của máy chạy. Toạ độ camera theo
**mét** như đặc tả yêu cầu.

Tầng thuần [`Shared.Logic/Bcf`](../src/DhcbTools.Shared.Logic/Bcf) có **19 ca test** kiểm đúng cách một máy
đọc BCF sẽ làm: ghi file rồi **mở lại chính file vừa ghi** bằng `ZipArchive` + `XDocument` và đọc ra từng thẻ.
`IfcGuid` có test vòng tròn đi–về trên 200 guid ngẫu nhiên, vì sai một bit là file BCF chỉ vào nhầm phần tử.

## Giới hạn 500 vấn đề một file

Mở 3.000 topic trong Solibri là treo máy người nhận. Quá giới hạn thì file ghi 500 vấn đề đầu và thông báo
nói rõ `500/N`. Muốn đủ thì chia theo tầng hoặc theo cặp category và xuất nhiều file.

Không có vấn đề nào thì **không ghi file rỗng** — thông báo nói "không có vấn đề nào để xuất BCF". Một file
BCF rỗng gửi đi trông y hệt một file hỏng.

## Còn thiếu

- **Chưa chạy thật trong Revit.** Ca kiểm đã có (`revit-mep.json` chốt `filesExist` cho `clash.bcf` vì model
  HVAC có 7 va chạm thật; hai ca còn lại chỉ chốt "bật `bcfPath` không làm hỏng đường cũ" vì model sạch thì
  cố ý không ghi file).
- **Chưa mở lại bằng một máy đọc BCF thật** (Solibri/BIMcollab). Test đọc lại file bằng thư viện XML chứng
  minh cấu trúc đúng đặc tả, không chứng minh phần mềm bên thứ ba chấp nhận nó. Đây là việc phải làm trước
  khi gửi file cho tư vấn thật.
- **Chưa có ảnh chụp** trong topic. `snapshot` của Bridge chụp được view hiện tại, nhưng ảnh cho *từng* va
  chạm cần đặt camera rồi chụp từng cái — đắt và cần `UIDocument`. Bộ ghi đã nhận sẵn `SnapshotPng`, phần
  còn lại là quyết định có đáng thời gian mở view 500 lần hay không.
- Chưa đọc BCF **vào** (nhận vấn đề tư vấn gửi về rồi chỉ tới phần tử trong Revit). Đó là chiều ngược lại và
  là một việc riêng.
