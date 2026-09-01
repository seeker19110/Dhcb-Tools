# Nghiên cứu khả năng tự động hoá cao nhất cho Revit

> **Phạm vi đã chốt**: DHCB Tools chỉ triển khai trên Revit desktop — tức tối đa đến **Cấp 3 (batch máy trạm)**, có thể thêm Cấp 4 (HTTP/MCP bridge) nếu cần sau này. Cấp 5 (cloud DA4R) giữ lại trong tài liệu để tham khảo, không nằm trong kế hoạch.

Tài liệu này xếp hạng các cấp độ tự động hoá Revit từ thấp đến cao nhất — đích đến là **chạy hoàn toàn không cần người, không cần mở Revit desktop** — kèm đánh giá công nghệ, chi phí và kiến trúc đề xuất cho DHCB Tools.

## 1. Thang cấp độ tự động hoá

### Cấp 1 — Lệnh thủ công trong phiên (baseline)
Người dùng bấm nút Ribbon, add-in chạy một tác vụ. Đây là mức của mọi bộ tools thông thường. Tự động hoá = 0 (chỉ tăng tốc thao tác).

### Cấp 2 — Tự động phản ứng theo sự kiện (in-session, không cần bấm nút)
Add-in tự chạy khi có sự kiện trong Revit:
- **`IUpdater` / DynamicModelUpdate**: tự chạy khi phần tử được tạo/sửa/xoá — ví dụ tự điền mã cấu kiện, tự gán workset, tự chặn sửa phần tử đã "khoá". Đây là cơ chế real-time mạnh nhất trong phiên.
- **Application/UIApplication events**: `DocumentOpened`, `DocumentSynchronizedWithCentral`, `DocumentSaving`, `ViewActivated`, `FailuresProcessing` — ví dụ tự chạy model checker mỗi lần sync, tự chặn sync nếu vi phạm quy tắc.
- **`Idling` event + `ExternalEvent`**: cầu nối để tiến trình ngoài (server, hotkey, app khác) ra lệnh cho Revit đang mở.
- **Failure API (`IFailuresPreprocessor`)**: tự nuốt/tự xử lý warning khi chạy batch — bắt buộc phải có cho mọi kịch bản không người ngồi máy.

### Cấp 3 — Batch nhiều file trên máy trạm (semi-headless)
Một lần kích hoạt, xử lý hàng loạt file `.rvt` không cần người can thiệp:
- **Journal file playback**: Revit chạy với `revit.exe /language ENU journal.txt` — mong manh, Autodesk không hỗ trợ chính thức, chỉ dùng bootstrap.
- **RevitBatchProcessor (mã nguồn mở)**: khung chuẩn de-facto để chạy script/lệnh trên danh sách file, hỗ trợ workshared model, retry, log. Có thể nhúng lệnh DHCB Tools vào.
- **Tự viết batch runner**: `IExternalApplication` bắt `ApplicationInitialized`, tự `OpenDocumentFile` → chạy lệnh → `SaveAs`/`SynchronizeWithCentral` → đóng, lặp theo hàng đợi (file config/JSON). Kết hợp **Windows Task Scheduler** để chạy ban đêm: audit, purge, export PDF/IFC, health report toàn bộ dự án theo lịch.
- Giới hạn: vẫn cần máy có license Revit, Revit UI vẫn khởi động (không có headless mode chính thức trên desktop).

### Cấp 4 — Điều khiển từ xa / tích hợp hệ thống
Revit trở thành một "service" trong pipeline dữ liệu:
- **HTTP listener trong add-in** (self-host web server + `ExternalEvent`): web app/Excel/hệ thống ERP gửi lệnh cho Revit đang mở — nền tảng của các sản phẩm kiểu Speckle, Rhino.Inside.
- **MCP server cho Revit + AI agent**: xu hướng 2025 — add-in mở kênh (WebSocket/HTTP) cho LLM agent đọc/ghi mô hình bằng ngôn ngữ tự nhiên (các dự án revit-mcp đã chứng minh khả thi). DHCB có thể xây lớp lệnh an toàn (whitelist tác vụ) cho AI điều khiển.
- **Speckle / Rhino.Inside.Revit**: đồng bộ dữ liệu hai chiều với nền tảng ngoài, chạy Grasshopper trong Revit.

### Cấp 5 — Cloud, không cần Revit desktop (mức cao nhất)
**Autodesk Platform Services (APS) — Design Automation for Revit (DA4R)**:
- Chạy "Revit engine" headless trên cloud của Autodesk: upload file + AppBundle (chính là DLL add-in của bạn, gần như giữ nguyên code) + WorkItem → engine mở file, chạy code, trả kết quả.
- Không cần máy cài Revit, không cần license desktop cho tiến trình chạy; trả phí theo token/giờ engine (cloud credits).
- Làm được: tạo/sửa mô hình, xuất PDF/DWG/IFC, điền dữ liệu từ DB, dựng mô hình từ cấu hình (configurator), kiểm tra hàng trăm file mỗi đêm, tích hợp CI/CD cho BIM.
- Giới hạn quan trọng: **không có UI, không có tương tác người dùng** (mọi `TaskDialog`/selection phải loại bỏ); không truy cập mạng tuỳ ý từ trong engine (chỉ qua input/output đã khai báo); thời gian chạy giới hạn theo WorkItem; workshared model phải xử lý qua cơ chế riêng (detach hoặc Cloud Model API).
- Kết hợp **ACC/BIM 360 API + webhooks**: khi có phiên bản mô hình mới publish lên cloud → webhook kích hoạt DA4R → tự chạy checker/export → gửi báo cáo. Đây là **vòng tự động hoá khép kín 100%, không người, không desktop** — mức cao nhất hiện có.

### Cấp 5b — Không qua Revit engine (đọc/ghi trực tiếp, giới hạn)
- **APS Model Derivative / SVF**: trích xuất geometry + property từ .rvt để xem/phân tích trên web (chỉ đọc).
- Thư viện đọc .rvt trực tiếp (không chính thức) — rủi ro cao, không khuyến nghị ghi.

## 2. So sánh nhanh

| Cấp | Cần người? | Cần Revit desktop? | Độ khó | Chi phí | Phù hợp |
|---|---|---|---|---|---|
| 2 – Event/IUpdater | Có (đang làm việc) | Có | Trung bình | 0 | Ép chuẩn dữ liệu real-time |
| 3 – Batch máy trạm | Không (sau khi hẹn giờ) | Có (1 máy + license) | Trung bình | License 1 máy | Việc đêm: audit, export, purge |
| 4 – Remote/MCP | Tuỳ | Có (đang mở) | Khó | 0–thấp | Tích hợp hệ thống, AI agent |
| 5 – DA4R cloud | Không | **Không** | Khó nhất | Cloud credits | Quy mô lớn, SaaS, pipeline |

## 3. Kiến trúc đề xuất cho DHCB Tools (để đạt mức cao nhất mà không viết lại code)

Nguyên tắc vàng: **tách logic khỏi UI ngay từ đầu**.

```
DhcbTools.Core        # logic thuần: nhận Document + tham số → làm việc → trả report
                      # KHÔNG TaskDialog, KHÔNG Selection, KHÔNG WPF
DhcbTools.Revit       # vỏ desktop: Ribbon, WPF, IUpdater, events → gọi Core
DhcbTools.Batch       # vỏ batch: hàng đợi file + Task Scheduler → gọi Core
DhcbTools.DA          # vỏ cloud: DesignAutomationBridge → gọi Core (AppBundle cho DA4R)
```

Cùng một lệnh (ví dụ "audit + purge + export PDF") chạy được ở cả 4 vỏ. Đầu vào luôn là JSON config thay vì hộp thoại; đầu ra luôn là file report (JSON/Excel) — điều kiện bắt buộc để lên cloud sau này.

## 4. Lộ trình tự động hoá (phạm vi desktop)

1. **Ngay từ giai đoạn 1**: áp kiến trúc Core/vỏ ở trên; mọi lệnh nhận config JSON — vẫn giữ nguyên tắc này vì nó cũng là điều kiện để chạy batch không người ngồi máy.
2. **Cấp 2**: thêm `IFailuresPreprocessor` dùng chung + IUpdater tự điền mã cấu kiện + checker chạy khi sync.
3. **Cấp 3 (đích của dự án)**: batch runner nội bộ (hoặc tích hợp RevitBatchProcessor) + Task Scheduler chạy đêm — audit, purge, export PDF/DWG, health report cho toàn bộ danh sách file.
4. **Cấp 4 (tuỳ chọn về sau)**: HTTP/MCP bridge cho tích hợp AI và hệ thống nội bộ.

Cấp 5 (cloud) không triển khai; `DhcbTools.DA` trong sơ đồ kiến trúc chỉ là chỗ dành sẵn, không cần tạo.
