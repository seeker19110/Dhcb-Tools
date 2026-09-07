# Audit và nâng cấp ngày 2026-09-07

Mốc mã nguồn trước sửa: `52baa87`. Người dùng yêu cầu triển khai sau audit và lưu kết quả.
Các thay đổi được lưu trong cây làm việc; chưa phát hành hoặc cài đè add-in lên phần mềm đang dùng.

## Đánh giá và quyết định

Dự án có kiến trúc logic thuần / hosting / API Autodesk / UI, bộ test đáng kể và các bộ ca chạy trong
phần mềm thật. Hướng ưu tiên là độ tin cậy của quy trình kiểm tra, chuẩn hóa và báo cáo cho kỹ sư.
Số test xanh không thay thế bằng chứng sử dụng thực tế hoặc kiểm thử UI/host.

## Thay đổi đã triển khai

| Phát hiện | Thay đổi | Bằng chứng hiện có |
|---|---|---|
| MCP biến chuỗi `"false"` thành xác nhận chạy thật | Kiểm tra boolean đúng kiểu; chuỗi, số, null, list và object bị từ chối trước khi gửi lệnh | Test hồi quy xác nhận không gọi đường gửi khi đầu vào sai |
| `--read-only` cho qua lệnh không có trong catalog | Chỉ cho tên lệnh thuộc danh mục đọc; từ chối lệnh chưa xác minh, kể cả khi dùng cache | Test catalog cache cũ thiếu lệnh ghi mới |
| Đổi cấu hình sau preview vẫn chạy được | Sửa trường nhập vô hiệu hóa preview; trước khi chạy đối chiếu cấu hình và SHA-256 nội dung các trường file | Test thay file cùng độ dài và mtime; build WPF đạt |
| Bridge thực thi trên model đang mở khác | `document_context` trả định danh phiên; `documentId` được kiểm trên luồng UI ngay trước dispatch | Test cùng phiên, khác phiên, thiếu id, kiểu dryRun sai; build cả hai host đạt |
| Release không phụ thuộc kết quả test cùng commit | `tests.yml` có `workflow_call`; `publish` phụ thuộc job `verify` gọi workflow cùng repo/ref | Đọc YAML bằng parser, kiểm workflow_call và mọi liên kết needs đạt; lần chạy GitHub Actions mới còn phải xác nhận |
| Gói AutoCAD 2026 không phân biệt runtime | Công bố hỗ trợ Update 1.2+; installer yêu cầu thư mục AutoCAD và kiểm runtime `net10.0` trước cài | Đã đọc runtimeconfig .NET 10 của máy này; chưa biên dịch/thử installer mới |
| Dependency API dùng wildcard | Cố định Revit API, AutoCAD.NET và System.Drawing.Common theo các gói hiện đã restore tại máy | Build các nhánh .NET Framework, .NET 8 và .NET 10 |
| Tài liệu hiện trạng bị trôi | Sửa trạng thái routing, mô tả BatchRunner net10, chú thích số liệu lịch sử; thêm hướng dẫn Bridge và bảng mã lỗi | Bộ kiểm tài liệu C# đạt |

Nguồn phạm vi AutoCAD: [Autodesk — Managed .NET Compatibility](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm):
AutoCAD 2026 đến Update 1.1 dùng .NET 8; Update 1.2 trở lên dùng .NET 10.
Không đổi nghĩa các ghi nhận lịch sử gọi máy là “2026.1”; cần ghi build chính xác khi kiểm lại host.

## Sử dụng Bridge sau nâng cấp

Cập nhật DLL và script client cùng nhau. Client cũ gửi ghi thật thiếu định danh sẽ bị từ chối.

1. `POST /query` với `{"query":"document_context"}` trả `documentId` và tên model.
2. Xem trước với `documentId` đó ở cấp ngoài của payload `/execute`.
3. Sau khi người dùng xác nhận, gửi lại cùng `documentId` và config đã kiểm, đặt `dryRun:false`.
4. Nếu nhận `E-DOCUMENT-CHANGED`, kiểm tra model và xem trước lại; không tự thay id rồi gửi lại lệnh ghi.

```json
{
  "command": "AutoNumbering",
  "documentId": "id-cua-phien-da-xem-truoc",
  "config": {"category": "Doors", "parameterName": "Mark", "dryRun": false}
}
```

Client `dhcb_agent.request` (gồm sync, background và raw), panel và server MCP AutoCAD tự lấy context
ngay trước khi gửi `dryRun:false` nếu chưa có id. Đây là bảo vệ việc chuyển model giữa gửi request và
thực thi. Để ràng buộc cả khoảng thời gian từ preview đến commit, caller HTTP phải giữ và gửi rõ cùng id.
Bridge không coi boolean xác nhận do AI gửi là bằng chứng con người đã duyệt.

Installer mặc định kiểm thư mục `%ProgramFiles%\Autodesk\AutoCAD 2026`; người dùng có thể chọn nơi khác.
Cài im lặng ở vị trí khác dùng `/ACAD2026DIR="D:\Autodesk\AutoCAD 2026"`.
Không tìm thấy `acad.exe`, không đọc được runtimeconfig hoặc runtime chưa là net10 thì dừng trước cài.

## Kiểm chứng tại máy

| Kiểm tra | Kết quả |
|---|---|
| Shared.Logic + Shared.Hosting | 1.660 đạt, không bỏ qua; cổng phủ dòng 100% |
| BatchRunner CLI | 21 đạt |
| Python | 313 đạt và 19 subtest đạt; cổng phủ câu lệnh 100% |
| Revit WPF | Build Release 2023/2024/2025/2026/2027 đạt, 0 warning, 0 error |
| AutoCAD UI | Build Release 2024/2025/2026 đạt, 0 warning, 0 error |
| AutoCAD core-only | Build Release 2026 đạt, 0 warning, 0 error |

Coverage C# của lượt này ở `out/audit-2026-09-07/coverage/` (không commit artifact sinh ra).
Thư viện đo phủ Python được đặt riêng trong `out/audit-2026-09-07/python-deps/`, không cài vào Python toàn máy.

## Các giới hạn phải nghiệm thu tiếp

- Chưa chạy tương tác UI/Bridge của bản sửa này trên Revit và AutoCAD thật. Test mới kiểm logic bảo vệ;
  build kiểm tính tương thích biên dịch, không chứng minh hành vi của wrapper Document trong host.
- Máy không có Inno Setup compiler. Phần Pascal Script mới chưa được ISCC biên dịch hoặc thử wizard/silent;
  release vẫn bị chặn nếu bước đóng gói installer lỗi. Đã thử tải Inno Setup 6.7.3 qua liên kết GitHub
  chính thức bằng PowerShell và curl nhưng kết nối timeout; đường tải jrsoftware trả về trang HTML.
  Không thực thi file tải không phải executable. Nguồn: [Inno Setup Downloads](https://jrsoftware.org/isdl.php).
- Preview chụp các file được khai báo bằng trường `FilePath`, không chụp toàn bộ nội dung thư mục family,
  linked model hoặc trạng thái thay đổi bên ngoài form. Chưa có transaction chung bao trùm preview và commit.
- Chưa có khóa idempotency bền vững cho client gửi lại sau đứt kết nối. Quy tắc hiện hành vẫn là
  dùng `/progress/<id>` khi server đã nhận lệnh, không tự retry lệnh ghi.
- Đã cố định dependency trực tiếp nêu trên; chưa khóa toàn bộ dependency bắc cầu hoặc SDK bằng lockfile.
- Việc sinh toàn bộ bảng hỗ trợ từ một manifest có cấu trúc là hạng mục tiếp theo; chưa thay thế mọi bảng lịch sử.

## Kế hoạch phát triển đã lưu

| Ưu tiên | Việc cần làm | Điều kiện nghiệm thu |
|---|---|---|
| Trước phát hành | Chạy ca UI đổi config sau preview, sửa CSV ngoài form, đóng/mở lại model, đổi tab khi queue còn chờ | Không có ghi sai phạm vi/model; id ổn định trong cùng phiên và đổi sau mở lại |
| Trước phát hành | Biên dịch installer và chạy wizard/silent trên AutoCAD 2026 runtime 8/10, đường dẫn tùy chỉnh | Runtime 8 bị chặn, runtime 10 cài được, silent không treo |
| Trước mở rộng AI ghi model | Lưu kế hoạch preview phía server, ràng buộc config/model, khóa request chống gửi lặp | Mạng đứt hoặc gửi lặp không tạo thay đổi hai lần; xác nhận hết hạn khi đầu vào đổi |
| Thí điểm 9.4 | 5–8 kỹ sư, 2 dự án, 2 tuần; chọn 3 quy trình kiểm chuẩn/chuẩn hóa/batch | Đo thời gian tiết kiệm, tỷ lệ tự hoàn thành, lỗi phát sinh và số người dùng lại tuần hai |
| Sau thí điểm | Preset theo dự án, bảng thay đổi trước/sau, chọn/zoom phần tử lỗi | Kỹ sư hoàn thành quy trình từ đầu đến cuối không cần sửa JSON |
| Theo nhu cầu đã đo | Benchmark model lớn, thêm phiên bản host, mở rộng MEP/setout/hồ sơ hoàn công | Số liệu hiệu năng và bằng chứng nghiệm thu đúng từng phạm vi hỗ trợ |

Dùng `UsageReport` và `docs/mau-phan-hoi-9-4.md` sẵn có cho vòng thí điểm. Không đánh dấu thí điểm
hoàn thành bằng mô phỏng người dùng hoặc unit test. Chưa liên hệ kỹ sư hoặc phát hành bản mới trong lượt này.
