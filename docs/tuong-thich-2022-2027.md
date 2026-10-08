# Tương thích Revit và AutoCAD 2022–2027

Cập nhật 2026-10-08. Ma trận dưới đây mô tả SDK, build và gói cài được triển khai trong mã nguồn.
Kết quả chạy host được ghi riêng: một DLL build được chưa chứng nhận hành vi trên tất cả phiên bản.

## Ma trận build và gói

| Phần mềm | Năm/mức cập nhật | Profile build | SDK/API ghim |
|---|---|---|---|
| Revit | 2022 | net48 | 2022.1.80 |
| Revit | 2023 | net48 | 2023.1.90 |
| Revit | 2024 | net48 | 2024.3.60 |
| Revit | 2025 | net8 | 2025.4.60 |
| Revit | 2026 | net8 | 2026.4.10 |
| Revit | 2027 | net10 | 2027.2.0 |
| AutoCAD | 2022 | net48 | 24.1.51000 |
| AutoCAD | 2023 | net48 | 24.2.0 |
| AutoCAD | 2024 | net48 | 24.3.0 |
| AutoCAD | 2025 đến 2025.1.3 | net8 (mặc định) | 25.0.1; Core/Model 25.0.0 |
| AutoCAD | 2025.1.4 trở lên | net10 | 25.0.2 |
| AutoCAD | 2026 đến 2026.1.1 | net8 | 25.1.0 |
| AutoCAD | 2026.1.2 trở lên | net10 (mặc định) | 25.1.1 |
| AutoCAD | 2027 | net10 | 26.0.0 |

Revit 2025.5/2026.5 chuyển runtime sang .NET 10. Gói DHCB cho 2025/2026 giữ API baseline net8:
Autodesk cho biết phần lớn add-in net8 tiếp tục dùng được, nhưng đây là kỳ vọng tương thích cần kiểm
trên host, chưa phải bằng chứng nghiệm thu DHCB trên bản cập nhật đó.
AutoCAD 2025/2026 chọn runtime theo file `acdbmgd.runtimeconfig.json` trên máy, thay vì suy luận từ năm.
Nếu cập nhật Autodesk làm đổi runtime, chạy doctor và cài lại profile phù hợp.

Nguồn chính thức: [AutoCAD Managed .NET compatibility](https://help.autodesk.com/cloudhelp/2027/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm),
[AutoCAD 2025 runtime theo mức cập nhật](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm),
[AutoCAD 2026 runtime theo mức cập nhật](https://help.autodesk.com/cloudhelp/2026/CSY/OARX-DevGuide-Managed/files/GUID-450FD531-B6F6-4BAE-9A8C-8230AAC48CB4.htm),
[Revit 2027 chuyển .NET 10](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-8D7A4715-EAF8-4BD1-BE78-061F900D0BCE),
[phản hồi Autodesk về Revit 2025.5/2026.5](https://forums.autodesk.com/t5/revit-api-forum/revit-2025-2026-migration-to-net-10-compatibility-and-deployment/m-p/14120353/highlight/true).
Bảng compatibility theo năm của Autodesk cần đọc cùng tài liệu cập nhật, vì các bản vá có thể đổi runtime.

## Cách build và kiểm gói

```powershell
# Mỗi năm được restore/build vào thư mục riêng
 dotnet build src\DhcbTools.Revit\DhcbTools.Revit.csproj -c Release -p:RevitVersion=2022
 dotnet build src\DhcbTools.AutoCAD\DhcbTools.AutoCAD.csproj -c Release -p:AcadVersion=2022
# AutoCAD 2026 trước Update 1.2
 dotnet build src\DhcbTools.AutoCAD.Core\DhcbTools.AutoCAD.Core.csproj -c Release -p:AcadVersion=2026 -p:AcadRuntime=net8
# AutoCAD 2027
 dotnet build src\DhcbTools.AutoCAD\DhcbTools.AutoCAD.csproj -c Release -p:AcadVersion=2027
# Kiểm cài đặt chỉ đọc
 python scripts\dhcb_doctor.py --app autocad --year 2022 --offline
 python scripts\dhcb_doctor.py --app revit --year 2027 --offline
```

Output: `bin/<năm>/<profile>/<cấu hình>/<TFM>/`; restore: `obj/<năm>/<profile>/`.
Build kiểm tra `UseWPF=false` thêm nhánh `headless/`, tránh ghi đè bản có giao diện.
Installer giữ bundle tại `Contents/<năm>/`, chọn DLL net8/net10 theo host cho 2025/2026.
Gói có `dhcb-host-profile.json` để chẩn đoán năm/runtime; không sửa file này để ép nhận gói sai.
ZIP tự cài phải chọn đúng năm và runtime. SDK Autodesk là tham chiếu build, không được chép thành
runtime dependency cạnh plugin rồi che DLL của host.

## Điểm yếu đã sửa

| Điểm yếu | Hậu quả | Cách sửa |
|---|---|---|
| AutoCAD đời cũ đều dùng SDK 2024 | DLL có thể build xanh nhưng không nạp/chạy trên 2022/2023 | Ghim SDK theo năm, chặn SDK override sai |
| Chỉ chọn runtime theo năm | Gói không nạp sau cập nhật host hoặc trên RTM | Hai profile cho AutoCAD 2025/2026; bộ cài đọc runtime thực tế |
| Build chung output/restore | Dùng nhầm DLL năm khác, đặc biệt khi `-SkipBuild` | Tách cả `bin` và `obj`; script hỏi MSBuild đường output |
| Tên kiểm tra CI phụ thuộc hàng ma trận | Đổi ma trận có thể làm ruleset yêu cầu tên không còn tồn tại | Cổng `quality-gate` ổn định gom cả bốn nhóm kiểm tra; script cấu hình ruleset dùng tên cổng |
| Bundle/release/batch thiếu năm | Có source nhưng không cài hoặc tự tìm host được | Bổ sung 2022–2027 đồng bộ |
| Batch chỉ kiểm TFM | SDK 2024 net48 vẫn bị chọn cho host 2022 | Đọc metadata tham chiếu Autodesk, từ chối API mới hơn/cross-major |
| ID Revit 2022/2023 bị ép từ long sang int không kiểm giới hạn | CSV ID quá lớn có thể trỏ sang phần tử khác | Kiểm biên 32-bit trước khi parse/tạo ElementId |
| Hình học quá lớn/vô hạn phủ hàng triệu ô không gian | Kiểm clash có thể hết bộ nhớ | Giới hạn 4.096 ô/hộp; hộp lớn dùng danh sách dự phòng, truy vấn lớn kiểm giao chính xác tuyến tính |
| Doctor không nhận diện gói/host Revit | Báo cáo xanh không nói đủ tình trạng cài đặt | Kiểm năm, DLL, manifest, runtime và profile; có đường dẫn host tùy chọn |
| Timeout/ngắt kết nối Bridge thiếu xử lý | CLI sập hoặc gây hiểu nhầm về lệnh ghi | Chẩn đoán lỗi vận chuyển, giữ ID progress; ưu tiên lỗi rõ ràng trước dữ liệu thành công lồng bên trong |
| Đóng gói thiếu deps Core Console hoặc bỏ chọn scripts không có hiệu lực | NETLOAD thiếu metadata; cài component ngoài lựa chọn | Gói kèm deps của cả hai shell; scripts chỉ thuộc component riêng |
| Quét dependency đọc profile mặc định sau restore năm khác | Quét nhầm assets hoặc bỏ sót runtime mới | Dùng cùng property MSBuild cho restore/list, quét cả 8 profile |

## Nghiệm thu và hướng nâng chất lượng tiếp

Máy hiện tại chạy được AutoCAD 2026, không có Revit.exe. Có thư mục AutoCAD 2022 nhưng thiếu
`acad.exe`/`accoreconsole.exe`, nên không thể nghiệm thu host 2022 tại máy này. Bằng chứng Revit 2024/2026 trong
các tài liệu trước là lịch sử, không xác nhận toàn bộ thay đổi của lượt này.
Kết quả tại máy của lượt này:

| Kiểm tra | Kết quả |
|---|---|
| Build Windows đầy đủ | 22/22 cấu hình: 6 Revit, 8 AutoCAD UI, 8 AutoCAD Core Console; Debug và Release đều 0 lỗi/0 cảnh báo |
| Build bỏ WPF | Kiểm thêm hai đầu 2022/2027 của cả hai host; output tách riêng |
| Logic/hosting C# | 2.094 ca đạt; phủ dòng 100% trong phạm vi cổng CI |
| CLI BatchRunner | 82 ca đạt |
| Python | 436 ca + 223 kiểm tra phụ đạt; 16 ca cần môi trường khác bị skip; phủ dòng 100% trong phạm vi cấu hình CI |
| AutoCAD 2026 Core Console | 105/105 ca: smoke 18, write 12, engineering 24, locked 51 |
| Kiểm độc lập AutoCAD | Giá trị attribute, định dạng DBText/MText, layer và hash DWG gốc/bản chép đạt |
| Truy vấn AutoCAD 2026 | 10/10 ca trên 30.000 entity trong database riêng; DWG gốc/bản sao không đổi hash |
| Bộ cài Windows | 16 ca tự động đạt, gồm 64 tổ hợp lựa chọn; kiểm nâng cấp, bỏ chọn, runtime và lựa chọn mặc định trong thư mục tạm |

Các kết quả AutoCAD ở `C:\Users\liend\DHCB-test-results\compatibility-2022-2027\`:
`autocad-smoke-2026-10-08_22-02-43`, `autocad-write-2026-10-08_22-03-04`,
`autocad-engineering-write-2026-10-08_22-03-58-824`.
Test installer dùng dữ liệu và thư mục tạm, không thay bản cài đang dùng.
Bộ cài dev dựng từ source: `dist/DhcbTools-Setup-0.9.0-dev-20261008.exe` (Windows x64).
Gói để dùng thử; không phải chứng nhận nghiệm thu Revit. Workflow mới cần ruleset yêu cầu
`quality-gate`; khi triển khai chỉ chuyển tên required status checks, giữ chính sách review,
điều kiện nhánh và các rule bảo vệ khác. Auto-merge phải chờ gitleaks và dependency-audit
(nếu chạy) đạt trên đúng head SHA.
Cổng phủ dòng không bao gồm toàn bộ API/GUI host. Các ca thực thi PowerShell/installer cũng được
chạy riêng trên môi trường phù hợp thay cho những ca bị skip ở Linux.

Ưu tiên tiếp theo theo giá trị công việc:

1. Chạy bộ smoke và write trên bản chép DWG/RVT cho từng năm/runtime; kiểm GUI/Ribbon, PDF, chữ tiếng Việt,
   transaction rollback, family và worksharing. Chỉ nâng mức hỗ trợ sau khi có log, file đầu ra và phiên bản host.
2. Lưu bộ dữ liệu nghiệm thu cố định theo ngành kiến trúc/MEP/kết cấu để so kết quả sau mỗi thay SDK.
3. Giữ family gốc ở phiên bản Revit thấp nhất cần dùng, sinh bản nâng cấp theo năm; không hứa hạ cấp RFA/RVT.
4. Đo thí điểm với kỹ sư thật: thời gian, số lần sửa tay, sai số và lỗi, dùng `dhcb_pilot.py` đã có.
5. Dồn cải tiến vào báo cáo chất lượng và bàn giao CAD–BIM có số đo; danh mục đã có nhiều lệnh,
   giá trị tiếp theo nằm ở độ tin cậy và việc người dùng hoàn thành công việc.
