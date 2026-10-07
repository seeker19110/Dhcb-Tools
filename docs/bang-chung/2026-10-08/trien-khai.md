# Nghiệm thu triển khai DHCB Tools — 2026-10-08

## Bản đã cài và bằng chứng

[PR #182](https://github.com/seeker19110/Dhcb-Tools/pull/182) merge vào main tại
`2cac2316f6b6248a5a8ffcc45bec45aa862aff6b`; phiên bản `0.9.0-dev`, gói phát triển chưa ký.
[CI main](https://github.com/seeker19110/Dhcb-Tools/actions/runs/37662972611) đạt;
12 check PR đạt. Không tạo public release hoặc tag mới.

Phạm vi cài: bundle AutoCAD 2026 (.NET 10), BatchRunner và 7 script người dùng.
Máy có AutoCAD 2026 Core Console `R25.1.179.0.0`; không có Revit.exe.

| Đối chiếu bản đã cài | Kết quả |
|---|---|
| Smoke Core Console | 18/18 đạt |
| Ghi thật Core Console | 12/12 đạt |
| Engineering write + đối chiếu CSV độc lập | 24/24 đạt |
| Layer/INSERT khóa, MText, partial/no-op và CSV lỗi | 51/51 đạt |
| PDF: preview, thành công, khổ giấy sai, CAD step lỗi, SaveAs bị chặn | 5/5 đạt; PDF/DWG cũ giữ nguyên khi lỗi; temp được dọn |
| Doctor offline | 0 lỗi; 5 cảnh báo do 3 cấu hình tùy chọn và 2 Bridge offline |
| Receipt sau cài | 27 file có hash khớp gói |
| Trạng thái ngay sau cài, trước host postflight | 132 file và 1 task đêm giữ nguyên; không đổi token/config/Revit manifest |

Gói và báo cáo thô nằm ngoài repo tại
`C:\Users\liend\DHCB-deploy\quality-20261008-2cac231`:
`deployment-report.json`, `deployment-report.txt`, `manifest.json`, `SHA256SUMS.txt`,
`installation-receipt.json`, `state-comparison.json` và thư mục `postflight`.
Manifest gói có SHA-256 `f2068ff9e68c960631e5943634ba0fd259e4a04924cd59a79ba789c73956c24e`.
Backup trước cài: `C:\Users\liend\DHCB-deploy\backups\20261008-005906-464`.
Các đường dẫn này là bằng chứng tại máy triển khai, không phải file có sẵn sau clone repo.

## Kiểm nội dung PDF đã xuất

PDF thật `postflight/pdf/live/drawing.pdf` có 77.397 byte, SHA-256
`88ea2aa454767892ff7c619abb24f42f6d1e0d385b210d88adf797eb30a81563`.
PDFium render được ở 144 dpi (2.382 × 1.684 pixel); pypdf/pdfplumber đọc được cấu trúc,
văn bản và hình học. Kiểm ảnh render thấy đủ hai mặt bằng LEVEL 1/LEVEL 2, trục,
tường/cửa/cầu thang và chú giải, không có trang trắng hoặc nét bị cắt ở mép giấy.

- Một trang A3 ngang: 420,158 × 297,039 mm, MediaBox = CropBox, rotation 0.
- Lề vùng mực khoảng 16–17 mm; 1.081 ký tự không phải khoảng trắng đều nằm trong trang.
- 2.111 đường, 680 đường cong; hình vector và đơn sắc đúng cấu hình `monochrome.ctb`.
- AutoCAD ghi trùng key metadata `/PageMode`; pypdf cảnh báo nhưng vẫn đọc được,
  PDFium render bình thường. Chưa kiểm PDF/A/PDF/X.

**Giới hạn quan sát được:** job Fit toàn Model vào A3 làm chữ rất nhỏ:
trung vị 2,345 pt, nhỏ nhất 0,535 pt; 982/1.081 ký tự dưới 3 pt. Không duyệt mẫu này
làm hồ sơ in sản xuất. Cần layout, khổ giấy và tỷ lệ của dự án để chú giải/kích thước đọc được.
Kiểm này xác nhận xuất/render/khung trang, không xác nhận thiết kế hoặc tỷ lệ hồ sơ.

Ảnh, JSON và script kiểm chỉ đọc nằm trong `postflight/pdf/visual-review` của gói tại máy.
Bốn file PDF ở các ca preview/lỗi là sentinel 22 byte được cố ý giữ nguyên;
không coi chúng là PDF sinh ra để render. DWG/PDF gốc không bị sửa trong vòng kiểm này.

## Kiểm bộ cài nâng cấp có phạm vi

Bổ sung `/PRESERVEUNSELECTED=1`: cập nhật nhóm được chọn, giữ add-in các năm không chọn
và khối manifest AutoCAD cũ (kể cả thiết lập không tự nạp). Cách bỏ chọn để gỡ ở chế độ mặc định
vẫn giữ nguyên. Nâng cấp BatchRunner không ghi đè job/config đang có; chỉ chép mẫu còn thiếu.
Manifest cũ hỏng hoặc không xác định được duy nhất module bị từ chối trước thao tác ghi/xóa;
cấu trúc XML hợp lệ nhưng ngoài dạng được nhận diện cũng bị từ chối, cần chuẩn hóa trước.

**12/12 test Windows Inno Setup 6.7.1 đạt**, bao gồm 8 tổ hợp AutoCAD,
nâng cấp/bỏ chọn/chọn lại, giữ Revit theo byte, UTF-8/BOM/thiết lập manifest cũ,
manifest hỏng/DTD/module gộp, runtime sai và giữ job/config đã sửa.
DOM giữ đúng module đang hoạt động khi XML có nháy đơn/đổi khoảng trắng hoặc comment giả;
thay các khối từ năm 2026 xuống 2024 để comment được giữ không chen vào lần thay sau.
Các test chuyển mọi đích cài sang profile tạm, không ghi registry uninstall và không thay bản cài thật.
Log tại máy: `C:\Users\liend\DHCB-deploy\installer-sandbox-20261008.log`.

Python trên WSL: **389 đạt + 115 subtest, 12 installer skip** vì cần Windows;
12 ca này đã chạy riêng và đạt trên Windows. Coverage **100% / 1.776 câu lệnh**,
pyflakes sạch. Không đổi DLL ứng dụng; số liệu CAD phía trên thuộc DLL đã cài từ PR #182.

## Phần còn cần nghiệm thu và điều kiện đạt

Lượt kiểm GUI hiện tại bị chặn trước thao tác: runtime `node_repl` không nhận cwd WSL;
không có ảnh Ribbon/form để ghi nhận đạt. AutoCAD Core Console không chứng minh đường UI.
Các vòng Revit trong [lịch sử](../../bang-chung-test.md) không chứng minh DLL mới chạy trong Revit.

Dùng bản chép fixture, không dùng model đang sản xuất; ghi phiên bản host, SHA gói,
config đã loại thông tin riêng, ảnh kết quả và hash model trước/sau.

| Phần | Bước kiểm | Điều kiện đạt | Trạng thái |
|---|---|---|---|
| Ribbon/form | Chạy TextReplace/AttributeImport: preview → từ chối, rồi preview → duyệt; lặp ghi | Từ chối không sửa; số preview khớp ghi; lặp là no-op; không lỗi form | Chưa kiểm GUI |
| Snapshot UI | Sau preview, sửa CSV hoặc bản vẽ rồi duyệt | Chặn chạy thật và yêu cầu preview mới; không ghi nội dung đã thay đổi | Chưa kiểm GUI |
| Kết quả một phần | CSV thiếu handle/tag, hoặc INSERT cha khóa | Preview báo partial; UI không cho ghi từ preview chưa hoàn tất; batch không lưu mặc định | Core Console đạt; GUI chưa kiểm |
| Undo/Redo | Ghi TextReplace/AttributeImport trên bản sao rồi Undo/Redo | Một lần Undo phục hồi toàn bộ lệnh; Redo khôi phục đúng; lần hủy không tạo transaction thừa | Chưa kiểm GUI |
| Bridge trong host | Doctor online, tools/query; giữ dialog UI rồi xếp job nền, hủy job chưa nhận | Token không lộ; hủy không sửa model; kết quả job đã chạy không bị đổi; queue đầy trả 429 | Logic/HTTP test đạt; host UI chưa kiểm |
| Revit | Smoke/read/write trên phiên bản công bố, preview/Undo/transaction và batch | Số suite/đối chiếu đầu ra đạt; không lưu model gốc khi thử; không hộp thoại treo batch | Máy hiện tại thiếu host |
| PDF sản xuất | Chọn đúng layout/khổ giấy/tỷ lệ, đọc kích thước và chữ từ PDF, đối chiếu DWG | Đúng tỷ lệ mong muốn, kích thước/chữ đọc được, không mất nét/xref/font; mỗi layout cần kiểm riêng | Cần bản vẽ và tiêu chuẩn in dự án |
| Family/tọa độ/BCF | Dùng family công ty, đo Spot Coordinate/điểm thực địa, mở BCF bằng ứng dụng nhận | Kích thước/connector và tọa độ khớp; topic/viewpoint BCF mở đúng | Cần dữ liệu, host và người nghiệm thu |
| Thí điểm | 5–10 lệnh trên công việc thật, đo làm tay/tool và phản hồi 2/4 tuần | Có người, dữ liệu và phản hồi thật theo mẫu đo | Chưa thu phản hồi |
| Phát hành chính thức | Chọn LICENSE và cấp chứng chỉ ký | Quyền sử dụng rõ, chữ ký xác minh được, gói đúng phiên bản công bố | Chưa có tài nguyên/quyết định |

Cách chạy lại fixture: từ repo trên Windows dùng
`./scripts/run-autocad-fixture-tests.ps1`;
helper tạo bản chép và đối chiếu CSV/hash. Xem thêm
[kiểm thử thủ công](../../huong-dan-cai-dat-va-kiem-thu-thu-cong.md),
[doctor](../../chan-doan-windows.md) và [thí điểm](../../thi-diem-su-dung.md).
