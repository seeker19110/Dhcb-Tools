# Nâng cấp vận hành DHCB Tools — 2026-10-08

Mốc bắt đầu `67807b7`, nhánh `codex/production-readiness`. Đợt này hoàn thiện các phần kỹ thuật còn lại
trong [kế hoạch chất lượng](ke-hoach-chat-luong-2026-10-07.md). Source và fixture mới đã được kiểm trên máy
phát triển; bản add-in đang cài từ PR #182 chưa tự động được thay bằng source của đợt này.

## Thay đổi dùng được

| Phần | Hành vi mới | Cách dùng và giới hạn |
|---|---|---|
| PlotPdf | Nhận tỷ lệ số/`1:100`/`1=100` hoặc named page setup; paper layout mặc định 1:1 | [BatchRunner](batch-runner.md); cấu hình sai bị chặn trước khi tạo file, named setup không đi cùng các tùy chọn plot xung đột |
| Console AutoCAD | Giải mã stdout/stderr UTF-16 có/không BOM và UTF-8 | Log Core Console không còn ký tự NUL chen giữa chữ |
| Task đêm | Script có BOM UTF-8 để PowerShell 5.1 đọc tiếng Việt đúng | Bộ đọc 5.1 thật từng báo lỗi chuỗi trong install-nightly-task; đã sửa encoding và thêm gate hồi quy |
| Query AutoCAD | Giới hạn mặc định 2.000, tối đa 10.000; offset sau bộ lọc; dừng ở một record nhìn trước | `layers/blocks/inserts/entities/text` trả `count/offset/limit/hasMore/nextOffset`; không giữ tất cả record để rồi Take |
| MCP AutoCAD | Nhận offset, giới hạn 1–200 theo gateway | JSON không bị cắt giữa chừng; giữ giới hạn nhỏ cho tương tác |
| Bridge | Tiến độ theo stage và hủy hợp tác AutoRoute/ClashDetection | [Giao thức](bridge-preview-commit.md); không ngắt cưỡng bức host, chỉ hủy khi `canCancel=true` |
| AutoRoute | Có thể sinh tối đa ba phương án và chọn OPT-id | Mặc định giữ đường đơn cũ; model line và BuildRoute trong một TransactionGroup; chỉ dựng MEP từ line vừa tạo |
| ClashDetection | Phân loại và khoảng hở ước lượng khi bật `classifyResults` | HTML/BCF ghi rõ **ước lượng hộp bao**; cần kỹ sư duyệt khoảng cách/chiều sâu thật |
| Thí điểm | Tổng hợp CSV thành HTML/JSON | [Hướng dẫn](thi-diem-su-dung.md); tính cả tác vụ thất bại và tiết kiệm âm, không đếm dòng mẫu |
| Phân phối | Apache-2.0 và NOTICE trong ZIP/MCPB/installer | Chủ dự án đã chọn giấy phép; tag release bị chặn khi thiếu chữ ký được xác minh Valid |

Ví dụ query qua client đang có:

```bash
python scripts/dhcb_agent.py autocad query entities --params limit=100 offset=0 entityType=Line
```

Trang sau dùng đúng `nextOffset`, giữ nguyên bộ lọc và bản vẽ. Nếu bản vẽ thay đổi giữa các trang,
hãy bắt đầu lại: đây là phân trang theo thứ tự duyệt database, chưa phải snapshot/cursor bất biến.
`limit=0` ở Bridge dùng mặc định 2.000; MCP yêu cầu 1–200. Cấu hình âm/quá trần bị từ chối.

AutoRoute nhận `generateOptions=true`. Xem `OPT-01/02/03` trong messages, đặt `selectedOptionId`
vào cấu hình đã chọn rồi **preview lại** trước commit; thay cấu hình làm token preview trước không còn hợp lệ.
`dryRun=true` vẫn là mặc định. BuildRoute thất bại sẽ rollback cả group; phần Undo/rollback này cần Revit thật nghiệm thu.

ClashDetection nhận `classifyResults=true`, `requiredClearanceMm=100`, `constructionToleranceMm=5`.
Cặp hộp bao gần nhau có thể được báo là khả năng thiếu khoảng hở ngay cả khi solid không giao nhau.
Không dùng nhãn ước lượng thay cho đo khoảng cách solid hoặc quyết định thi công.
BCF có hướng camera và khoảng nhìn, nhưng chưa xác nhận trong ứng dụng nhận ở đợt này.

## Bằng chứng mới

| Kiểm chứng | Kết quả | Phạm vi |
|---|---|---|
| C# logic/HTTP/path/BCF | 2.072/2.072, phủ 100% dòng ở tầng có cổng | Có ca hủy hợp tác qua HTTP, Seal trước commit, phục hồi scope, lọc trang, PDF prompt, UTF-16/UTF-8 |
| CLI BatchRunner | 68/68 | Cấu hình plot sai/named setup xung đột không tạo thư mục dù dry-run hoặc live |
| Python gateway/scripts | 398 đạt, 12 ca installer được bỏ qua ở WSL; phủ 100% câu lệnh | 174 gateway + 236 scripts gồm 12 ca cần Windows; installer đã chạy riêng trên Windows |
| API build | 25/25 | Core và vỏ Revit/AutoCAD/Core-only 2023–2027; không thay thế chạy thật trong từng host |
| WPF build Windows | 7/7 | Revit 2023–2027 và AutoCAD 2024/2026 |
| Installer Windows | 12/12 | Biên dịch Inno Setup, cài/nâng cấp trong sandbox; LicenseFile và các file giấy phép có trong source gói |
| AutoCAD 2026 engineering | 75/75 | 24 ca host và 51 đối chiếu độc lập entity/attribute/text/layer; bản sao, không lưu model gốc |
| Query trên AutoCAD 2026 | 10/10, fixture 30.000 entity | Default page 2.000 khoảng 84,8 ms; Line offset 17/limit 7 khoảng 0,28 ms; đây là một lượt đo |
| PDF trên AutoCAD 2026 | 3/3 job và đo/render độc lập | Tỷ lệ trực tiếp, named setup, paper layout; DWG gốc/bản sao/fixture giữ hash |
| Chặn phát hành chưa ký | Đạt tình huống thiếu cert | `sign-release.ps1 -RequireSignature` trả lỗi; chưa có chứng thư để nghiệm thu ký hợp lệ |
| Script PowerShell | Bộ đọc 5.1 thật và kiểm cú pháp chung 5.1/7 đạt | Đã phát hiện/sửa BOM của task đêm; không đăng ký hoặc thay task đang có |
| Dependency audit | Không có lỗ hổng đã biết trong lượt kiểm | NuGet 2023/2025/2027 và pip-audit; kết quả theo advisory ở thời điểm chạy |
| CSV thí điểm mẫu | `no-data`, 0 tác vụ thật | 10 dòng mẫu bị loại; không tạo bằng chứng người dùng giả |

PDF được đo bằng pdfplumber và render bằng PDFium, không chỉ kiểm file tồn tại:

| Job | Kết quả |
|---|---|
| Model `plotScale=1=100` | 1 trang A3 ngang 420,158 × 297,039 mm; khung 99,949 × 49,953 mm (mong đợi 100 × 50) |
| Named setup `QA-Model-1-100` | Cùng kích thước khung và giấy như tỷ lệ trực tiếp |
| Layout `QA-A3`, `plotArea=Layout` | 1 trang A3; text nominal 3,5 mm có glyph cao khoảng 3,133 mm ở tỷ lệ 1:1; SHX xuất thành đường nét |

Giá trị chênh nhỏ do plot/vector được lượng tử hóa. Chưa đo bản in vật lý, font/xref/CTB của công ty
hoặc hồ sơ DWG sản xuất. [Bộ fixture tái chạy](../tools/acceptance/README.md) giữ đầy đủ script/log/job/PDF.

Trên máy kiểm chứng, bằng chứng mới nằm dưới `%USERPROFILE%\DHCB-test-results\`:

- `improvements-20261008-final`: TRX logic và Cobertura 100%.
- `improvements-20261008-cli`: TRX CLI.
- `improvements-20261008-host/autocad-engineering-write-2026-10-08_11-41-02-410`: ca host và đối chiếu.
- `query-readiness-20261008-114430-467/queries.json`: cả 10 phản hồi query và thời gian.
- `pdf-readiness-20261008-115432-204`: lần chạy lại bằng script được commit, PDF, measurements.json và ảnh render.

File DWG/binary/log đầy đủ được giữ tại máy, không commit bản vẽ Autodesk vào repo.

## Những phần cần nghiệm thu tiếp

| Việc | Điều kiện khép | Trạng thái thực tế |
|---|---|---|
| GUI AutoCAD/Revit | Ribbon preview lỗi không ghi; sửa config/model làm preview mất hiệu lực; đúp commit không ghi lặp; Undo khôi phục toàn bộ | Automation computer-use không khởi tạo được vì `sandboxCwd is not a local file URI`; chưa có bằng chứng GUI mới |
| AutoRoute trên Revit | Preview/chọn lại OPT-id; BuildRoute chỉ dùng line mới; hủy trước commit không còn line/MEP; BuildRoute lỗi rollback; một Undo phục hồi group | Máy hiện tại không có Revit.exe; API/WPF build và test thuật toán đã đạt, host chưa đạt |
| Clash trên Revit | Kiểm cặp thật/cặp sát/cặp dung sai và link xoay/dịch; hủy scan không xuất report/view; so số liệu bằng đo solid | Ước lượng hộp bao có test report; cần fixture Revit có dữ liệu thật |
| BCF viewer | Mở BCF 2.1 trong viewer được công ty chọn, đúng topic/ID/camera và link | Chưa chạy ứng dụng nhận; camera được bổ sung nhưng chưa nâng nhãn tương tác |
| Family sản xuất | Kỹ sư duyệt family/type/kích thước, connector/load, đặt lại không nhân đôi | Cần bộ family và tiêu chuẩn công ty; FamilyStarter chưa là family đã duyệt |
| Tọa độ công trường | Kiểm hệ tọa độ, đơn vị và một điểm bằng phép đo độc lập | Cần model, mốc và người đo ngoài hiện trường |
| Thí điểm 2/4 tuần | Nhóm kỹ sư nhập tác vụ thật, phản hồi và đo thời gian bao gồm sửa kết quả | Công cụ/hướng dẫn/CSV đã sẵn sàng; chưa có dữ liệu thực |
| Ký số chính thức | Cấp cert/PFX và secret release; verify chữ ký/timestamp trên máy sạch | Chính sách bắt buộc đã có; chưa có chứng thư thật |

Không kết thúc host để hủy lệnh. `202` chỉ là nhận yêu cầu hủy; đợi kết quả cuối của `/progress/{id}`.
Qua điểm Seal, lệnh hoàn tất commit và trả `409` cho yêu cầu hủy mới.
Chỉ cập nhật trạng thái hỗ trợ/triển khai sau khi có bằng chứng host và dữ liệu phù hợp.
