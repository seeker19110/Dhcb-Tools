# Lộ trình phát triển DHCB Tools

Tài liệu này mô tả **kế hoạch phía trước**. Hiện trạng thực tế nằm ở [`progress.md`](progress.md). Đặc tả chi tiết ở
[`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md), kế hoạch kiểm thử ở [`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md), cơ sở kỹ
thuật ở [`nghien-cuu-dhcb-revit-tools.md`](nghien-cuu-dhcb-revit-tools.md), khảo sát thị trường ở
[`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md).

> **Đổi hướng 2026-09-03.** Giai đoạn 0–7 đã cho 57 lệnh có mã nguồn, nhưng mới 4/42 lệnh Revit chạy thật và
> ~4.500 dòng chạm Revit API chưa có test nào. Từ đây **dừng mở rộng số lệnh**, chuyển sang **chiều sâu** ở ba hướng
> mà Autodesk (Revit 2027 MCP chỉ đọc, chỉ 2027; AutoCAD 2027 Assistant) và các dự án revit-mcp mã mở chưa lấp.
> Lý do và bằng chứng: mục [Vì sao đổi hướng](#vì-sao-đổi-hướng) cuối tài liệu.

Ký hiệu: ✅ xong · 🟡 làm dở · ⬜ chưa bắt đầu · 🧪 code xong, chờ kiểm thử trên phần mềm thật.

## Nguyên tắc xuyên suốt

1. **Core không biết UI.** `Document`/`Database` + config → `CommandResult`; một lệnh chạy được từ Ribbon, Bridge, batch, AI.
2. **`DryRun` mặc định bật.** Ribbon luôn chạy xem trước rồi hỏi; Bridge/MCP ép `dryRun:true` trừ khi xác nhận.
3. **Một lệnh = một transaction.** `SilentFailuresPreprocessor` **chỉ dùng cho batch**; lệnh tương tác phải cho kỹ sư thấy cảnh báo.
4. **AI chỉ sinh đề xuất**, whitelist lệnh — xem [`ai-offline.md`](ai-offline.md). Model local là tuỳ chọn, không phải thông điệp chính.
5. **Phần tính được thì test được**: thuật toán xuống `Shared.Logic`, CI xanh trước khi lên máy Revit.
6. **Mới:** **Không thêm lệnh Core khi chưa có test chạy trong Revit cho lệnh đó.** Lệnh chưa qua vòng test thật gắn nhãn *thử nghiệm* trong catalog và Ribbon.
7. **Mới:** **Không có tên tham số, family, shared parameter cứng trong mã.** Mọi tra cứu đi qua lớp từ điển ngoài repo và **báo lỗi rõ khi không có**, không no-op.

---

## Đã xong (giai đoạn 0–7)

| Giai đoạn | Nội dung | Trạng thái |
|---|---|---|
| 0 | Trả nợ kỹ thuật: token Bridge, `Shared.Hosting`, DrawingCleanup an toàn, timeout huỷ lệnh, collector hiệu năng | ✅ |
| 1 | Batch runner chạy đêm (Revit qua add-in + `pending-job`, AutoCAD qua accoreconsole) — [`batch-runner.md`](batch-runner.md) | ✅ 🧪 |
| 2 | Khởi tạo dự án & hồ sơ: ProjectFromTemplate, TransferStandards, GridFromCsv, SheetBatchCreate | ✅ 🧪 |
| 3 | MEPF: sleeve, cao độ, hanger, chia ống, connector, routing A/B, sizing, màu/tên hệ, đánh số dòng chảy | ✅ 🧪 |
| 4 | `ElevationUpdater`, `ParameterRuleCheck`, `ClashDetection` | ✅ 🧪 |
| 5 | Lớp AI: map layer, thuyết minh → config, phân tích warning, ra lệnh tiếng Việt | ✅ |
| 6 | Routing C (`PathFinder3D` + `AutoRoute`), MCP server | ✅ 🧪 |
| 7 | P1 + P2 khoảng trống so với tool thị trường (SheetRename … ViewportCopy; LayerTranslate … AttributeIncrement) | ✅ 🧪 |

**P3 của giai đoạn 7 (BOM ra spool, sizing tổn thất áp, Layer Director) — dừng vô thời hạn.** Mở lại chỉ khi có
phản hồi người dùng thật yêu cầu.

---

## Giai đoạn 8 — Nền móng: biến mã đã biên dịch thành tính năng đã chứng minh 🟡 (tuần 1–2)

Bốn việc này phải xong **trước** mọi hướng mới; bỏ qua thì hướng nào cũng sụp ở lần cài thứ hai.

| # | Việc | Chi tiết | Cỡ |
|---|---|---|---|
| 8.1 ✅ | **Sửa nhóm lỗi im lặng** đã chỉ ra khi rà mã | `SilentFailuresPreprocessor` chỉ gắn ở batch (`BatchJobRunner`), không gắn ở Ribbon/Bridge · bỏ `catch {}` rỗng trong `ParameterRuleCheckCommand` (thu thập chỉ số) và `ParameterImportCommand.IsUnchanged` · `SleeveCommand`: dựng collector Walls/Floors **một lần** ngoài vòng lặp, và báo rõ khi rơi về bbox thay vì solid · gộp 7 hằng số `304.8`/`0.0929` về `RevitCompat` · `SheetRename` hai pha có rollback tên tạm `~DHCB~` khi lỗi · `BatchStartupHook`: xoá `pending-job.json` bằng mọi giá (đổi tên trước, xoá sau) để không chiếm phiên Revit kế tiếp | 3–4 ngày |
| 8.2 ✅ | **Version, installer, log** | `GenerateAssemblyInfo=true` + property `Version` (mặc định `0.9.0-dev`), release.yml truyền `-p:Version=<tag>` vào mọi build; `DhcbVersion` đọc `AssemblyInformationalVersion` nên `GET /health` trả đúng bản · installer Inno Setup ([`installer/dhcb-tools.iss`](../installer/dhcb-tools.iss)) cài theo người dùng, chọn theo phiên bản, AutoCAD thành bundle tự nạp (hết cần `NETLOAD`), có gỡ cài · `DhcbLog` ghi `%APPDATA%\DHCB\logs\<app>-<ngày>.log` (thay `Log = _ => { }`), giữ 30 ngày, có stack trace của mọi lệnh lỗi | ✅ |
| 8.3 ✅ | **Test runner chạy bên trong Revit** | Lệnh Core `RunTests` (nội bộ, không lên Ribbon, không chào ra `/tools`): chạy bộ ca kiểm JSON qua `RevitCommandTable` trên model mẫu, ghi TRX + Markdown, mã thoát khác 0 khi có ca trượt. Kỳ vọng khai báo (`success`/`minAffected`/`summaryContains`/`neverContains`/`maxMs`/`filesExist`) thay vì so file vàng — `maxMs` bắt hồi quy hiệu năng, `neverContains` bắt no-op im lặng. Hai lớp khoá `allowWrite`+`allowWrites` nên mặc định không bao giờ ghi vào model mẫu. Tầng thuần `Shared.Logic/Testing` có test riêng trên CI. Xem [`kiem-thu-trong-revit.md`](kiem-thu-trong-revit.md) | ✅ |
| 8.4 ✅ | **Vòng kiểm thử thật số 1 trọn 42 lệnh** trên Revit 2024.3 + AutoCAD 2026 | **Đã chạy thật 2026-09-03: mã thoát 0, 11 đạt / 0 trượt / 1 bỏ qua trên 12 ca**, Revit tự mở → chạy → tự đóng bằng một lệnh `scripts/run-in-revit-tests.ps1`. Vòng này lộ ra **ba lỗi chặn** mà 448 test thuần không bắt được — quan trọng nhất: Revit khởi động bằng journal **chỉ nạp add-in có `.addin` cùng thư mục với journal**, nên batch chạy đêm chưa từng chạy trọn lần nào. Chi tiết và bằng chứng: [`bang-chung-test.md`](bang-chung-test.md) §7. ⬜ Bộ `-Suite mep` cần model MEP mẫu, chưa chạy. Phần thủ công còn lại theo [`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md) R1–R48, C1–C17, B1–B12; ghi vào [`bang-chung-test.md`](bang-chung-test.md) §6, mỗi lỗi kèm test tái hiện phần thuần | 1 tuần |

**Đầu ra:** phát hành **v1.0** chỉ gồm các lệnh đã qua 8.3/8.4; lệnh còn lại vẫn có trong gói nhưng gắn nhãn *thử nghiệm*.

---

## Giai đoạn 9 — Từ hình dáng agent sang hình dáng kỹ sư 🟡 (tuần 3–4)

32/42 nút Ribbon hiện là runner JSON chung không có form, và file config **không tự sinh** như README nói
(`CommandRunner.LoadConfig` trả `JObject` rỗng). Không kỹ sư nào sửa JSON trong `%APPDATA%` để dùng tool.

| # | Việc | Chi tiết |
|---|---|---|
| 9.1 ✅ | **Form động từ `CommandCatalog`** | `FieldKind` + `FieldSpec` trong catalog, kiểu suy ra từ tên trường (`FieldKindGuess`, thuần, có test cho cả 107 trường thật). `CommandFormWindow` dựng ô nhập theo kiểu: checkbox, ô số theo culture, ô đường dẫn kèm nút chọn file/thư mục, combo lấy từ mô hình đang mở (`ModelChoices`: category, tham số, level, view template, family type). *Xem trước* chạy `dryRun` và hiện `Summary`+`Messages`; nút *Chạy thật* chỉ mở sau khi xem trước thành công. Config tự lưu lại. Ba vỏ MEPF viết tay (SleeveAuto/ElevationTag/ConnectorChecker) nay cũng đi qua form — gỡ luôn `SleeveFamilyName = "M_Generic Model"` gắn cứng. MCP `inputSchema` nhận kiểu JSON đúng (number/boolean/array) thay vì tất cả là string |
| 9.2 ✅ | **Lớp từ điển tham số & family** | `ParameterDictionary` (thuần, có test) đọc `%APPDATA%\DHCB\dictionary.json` — mẫu ở [`configs/dictionary.sample.json`](../configs/dictionary.sample.json). Mỗi khoá logic (`level`, `diameter`, `bottomElevation`…) có danh sách tên đồng nghĩa Anh–Việt; tên trong file đứng **trước** tên dựng sẵn chứ không thay thế, nên dự án dùng thư viện chuẩn vẫn chạy mà không cần file. `RevitCompat.Lookup(element, key, preferred)` là điểm tra tham số duy nhất của Core (instance rồi type). Đã gỡ literal khỏi Core: `"Level"` (5 chỗ), `"Outer Diameter"`/`"Width"`/`"Height"`, `"Department"`/`"Occupancy"`, mặc định `DHCB_*_Elevation` và `M_Generic Model`. **Tra không ra là báo lỗi có mã `E-PARAM-MISSING` kèm danh sách tên đã thử** — `SleeveAuto` báo số phần tử không tra được kích thước, `ElevationTag` trả `Success=false` khi không ghi được phần tử nào (trước đây báo "Đã gán cao độ cho 0/N" như thể bình thường) |
| 9.3 | **Tiếng Việt hoá thông báo** | Mọi `Messages`/`Errors` của Core có tiếng Việt kèm mã lỗi ổn định (ví dụ `E-PARAM-MISSING`) để tra tài liệu và để agent hiểu |
| 9.4 | **Đưa cho một nhóm kỹ sư dùng thật** | Phát hành v1.1, thu phản hồi theo mẫu (lệnh nào dùng hằng tuần, lệnh nào bấm rồi bỏ). Số liệu này quyết định giai đoạn 10/11 đi sâu vào đâu |

---

## Giai đoạn 10 — Agent khép vòng cho Revit 2021–2026 🟡 (tuần 5–8) — **hướng khác biệt lớn nhất**

Autodesk Revit 2027 MCP Server chỉ **đọc** và chỉ chạy trên **2027**; các dự án revit-mcp mã mở có 100+ tool nhưng
không có `dryRun`, token, batch, AutoCAD song hành hay tiếng Việt. DHCB đã có Bridge, token, `ExternalEvent`,
catalog, MCP — thiếu đúng phần làm agent *nhìn, chỉ, kiểm* được kết quả.

| # | Việc | Chi tiết |
|---|---|---|
| 10.1 ✅ | **Mở rộng phía đọc** `RevitQueryHandler` (10 → 17 loại) | Đã có cho Revit: `element_geometry` (hộp bao, đường tâm, **connector kèm tình trạng nối**, host, level — toạ độ trả ra mm), `parameters_of` (tham số của category: tên, `storageType`, chỉ đọc, giá trị mẫu), `schedule_rows` (bảng dạng hàng, không ghi file), `snapshot` (`Document.ExportImage` → PNG base64, nên vẫn ở Core không cần RevitAPIUI); và ở vỏ Revit (`UiQueryHandler`, cần `UIDocument`): `selection` (đọc + **đặt**), `show_elements` (zoom + chọn), `active_view`. Xem [`agent-khep-vong.md`](agent-khep-vong.md). ⬜ Phần AutoCAD tương ứng |
| 10.2 ✅ | **Khép vòng ghi** | `CommandResult.ChangedIds` — ElementId của phần tử vừa tạo/sửa, giới hạn 500 id một lượt để không phình response (`AffectedCount` vẫn là số đầy đủ). Đã gắn cho `SleeveAuto`, `HangerAuto`, `AutoNumbering`, `ElevationTag`, `SheetRename`. Agent nay chạy được vòng: xem trước → chạy → `element_geometry`/`show_elements` trên đúng id vừa đổi → `snapshot` để nhìn |
| 10.3 🟡 | **Playbook nghiệp vụ** cho Claude (thư mục `skills/`) | ✅ 3 playbook đầu trong [`skills/`](../skills/): *kiểm model trước sync*, *đánh số hàng loạt*, *xử lý một nhóm cảnh báo*. Mỗi cái là trình tự `parameters_of` → xem trước → xác nhận → kiểm lại bằng `changedIds` → `show_elements`/`snapshot`, kèm mục **Không được làm**. ⬜ Còn *dựng grid/level từ CAD* và *xuất bộ PDF theo revision* |
| 10.4 ✅ | **Đóng gói `.mcpb`** cho Claude Desktop | ✅ [`tools/mcpb/manifest.json`](../tools/mcpb/manifest.json) + [`scripts/pack-mcpb.ps1`](../scripts/pack-mcpb.ps1) → `dist/dhcb-<app>-<phiên bản>.mcpb`, mở bằng Claude Desktop là xong. Token để trống thì tự đọc `bridge-token.txt`. **Đã đóng gói thật**: 9,1 KB, 4 file, không dependency ngoài. MCP server chịu được khi Revit chưa mở: nhớ danh mục lệnh vào cache nên vẫn liệt kê đủ lệnh kèm ghi chú "Revit chưa mở" thay vì trả danh sách rỗng làm người dùng tưởng gói hỏng |
| 10.5 🟡 | **Bridge chịu tải** | ✅ `timeoutSeconds` theo từng request (`BridgeRequest`), chặn trên 10 phút để một client không giữ hàng đợi Revit vô hạn; `dhcb_agent.send()` và MCP truyền xuống, client tự chờ lâu hơn server 10 s. Hàm chọn timeout tách static nên có test. ⬜ `/progress/<id>` cho lệnh chạy lâu |

**Chỉ số:** agent tự tìm và xử lý **20 warning trên Snowdon Towers** trong một phiên, mỗi bước có ảnh chụp, kỹ sư chỉ bấm xác nhận.

---

## Giai đoạn 11 — Gói tuân thủ BIM theo Nghị định 217/2026/NĐ-CP ⬜ (tuần 9–10, mở tiếp theo phản hồi)

Từ 01/07/2026 công trình **cấp II trở lên** bắt buộc áp dụng BIM từ bước nghiên cứu khả thi; hoàn công phải cập nhật
mô hình theo chuẩn và nộp vào cơ sở dữ liệu quốc gia. Batch runner đêm + `ParameterRuleCheck` + `HealthReport` +
`BatchExport` IFC đã là phần lớn của một "máy kiểm mô hình"; thiếu bộ quy tắc theo chuẩn Việt Nam và báo cáo mà chủ
đầu tư, tư vấn thẩm tra đọc được.

| # | Việc | Chi tiết |
|---|---|---|
| 11.1 | **Checkset theo giai đoạn** (khả thi → thiết kế → thi công → hoàn công) | Tham số bắt buộc theo category, đặt tên file/view/sheet theo BEP, LOD tối thiểu, worksets, link, ngưỡng warning/dung lượng. Quy tắc ở JSON ngoài repo (`configs/checksets/`), mỗi công ty tự chỉnh — phần đánh giá thuần trong `Shared.Logic/Checks`, có test |
| 11.2 | **Kiểm IFC trước nộp** | Mapping export chuẩn, `IfcClassification`, Pset bắt buộc; đọc lại file IFC vừa xuất bằng parser thuần (STEP) trong `Shared.Logic` để kiểm số phần tử, thuộc tính thiếu — có test với file IFC mẫu |
| 11.3 | **Gói bàn giao tự động** | Một job đêm sinh: IFC, PDF, danh mục bản vẽ, báo cáo tuân thủ HTML/PDF có dấu thời gian và version add-in. Chạy trên 2 dự án thật trước khi công bố |
| 11.4 | Quyết định đi sâu IFC (validate theo IDS) hay không | Dựa trên phản hồi chủ đầu tư/thẩm tra sau 11.3 |

---

## Hạ ưu tiên / dừng

| Hạng mục | Quyết định | Lý do |
|---|---|---|
| P3 giai đoạn 7 | Dừng | Thêm lệnh khi 42 lệnh chưa qua test chỉ tăng bề mặt lỗi |
| Nhóm kiểm chuẩn AutoCAD (`LayerStandardCheck`, `DHCB_AI`, panel Hermes) | Giữ nguyên, không đầu tư thêm | AutoCAD 2027 Assistant kiểm chuẩn, chọn theo ngôn ngữ tự nhiên, truy vấn layer/block ngay trong sản phẩm. AutoCAD giữ thế mạnh riêng: accoreconsole batch đêm, `GridExtract` → `GridFromCsv` |
| `AutoRoute` mức C | Thu hẹp thành **đề xuất tuyến** (model line) để kỹ sư duyệt; không dựng thẳng | MagiCAD/eVolve đầu tư nhiều năm; DHCB không cần thắng ở đây |
| Thông điệp "AI offline hoàn toàn" | Bỏ khỏi thông điệp chính | Giữ Ollama như tuỳ chọn; giai đoạn 10 dùng Claude qua MCP mạnh hơn nhiều so với model 8B |
| Routing A/B, sizing | Giữ, gắn nhãn *thử nghiệm* đến khi qua 8.3 | Phụ thuộc family/routing preference từng dự án |

## Nền tảng — .NET 10 ⬜

Microsoft ngừng hỗ trợ .NET 8 ngày 10/11/2026; Autodesk đang preview di trú Revit 2025/2026 lên .NET 10, AutoCAD 2026.1
(package `AutoCAD.NET 25.1.x`) đã ở .NET 10. Khi SDK và phần mềm sẵn: thêm nhánh TFM `net10.0-windows` trong
`Directory.Build.props` (điều kiện `RevitVersion >= 2027` / `AcadVersion >= 2026`), chạy `check-build.sh` với tham số
mới, kiểm `Shared.*` (netstandard2.0) nạp được. Không đổi logic. Làm song song giai đoạn 9–10, không chặn.

## Chỉ số để biết đang đúng hướng

| Chỉ số | Mốc |
|---|---|
| Số lệnh Revit có test chạy trong Revit (8.3) | 42/42 trước v1.0 |
| Số kỹ sư dùng hằng tuần không cần hỏi (9.4) | ≥ 5 sau v1.1 |
| Thời gian agent hoàn thành kịch bản 20 warning (10) | < 15 phút, 0 thao tác tay ngoài xác nhận |
| Số dự án chạy báo cáo tuân thủ đêm (11) | 2 dự án thật trước khi công bố |

---

## Vì sao đổi hướng

Ghi lại để không lặp lại cách làm "bề rộng trước".

**Từ rà mã nguồn (2026-09-03):**
- 32/42 nút Ribbon là `CommandRunner` JSON chung; chỉ 1 form WPF (AutoNumbering). Sản phẩm mang hình dáng agent, không phải kỹ sư.
- `DhcbTools.Core` (~4.500 dòng chạm Revit API) không có test; 345 test xanh chỉ ở `Shared.Logic`.
- Phía đọc Bridge: 10 loại query, không selection/zoom/hình học/schedule/ảnh chụp/tra tham số — agent tả được model nhưng không nhìn, chỉ, kiểm được.
- Literal tiếng Anh và thư viện Mỹ cứng trong Core; trên dự án Việt nhiều lệnh MEPF no-op mà vẫn báo thành công.
- `SilentFailuresPreprocessor` xoá mọi warning cho cả lệnh tương tác; `SleeveCommand` O(n·m); version `0.0.0.0`; không installer; `Log = _ => { }`; bẫy `pending-job.json`.

**Từ thị trường (khảo sát 2026-09-03):**
- Revit 2027 MCP Public Server: chỉ đọc, chỉ 2027, cấu hình từng người dùng. AutoCAD 2027 Assistant: kiểm chuẩn, chọn theo ngôn ngữ tự nhiên, MCP.
- ≥ 5 dự án revit-mcp mã mở (MIT), lớn nhất 124–138 tool, Revit 2023–2027.
- Nghị định 217/2026/NĐ-CP: BIM bắt buộc cấp II trở lên từ 01/07/2026, hoàn công nộp cơ sở dữ liệu quốc gia.
- Đối thủ nội địa (BimSpeed, HQL) bán UI + đào tạo; DHCB không thắng bằng thêm nút.

Nguồn: [Revit Public MCP Server](https://www.autodesk.com/blogs/aec/2026/06/17/revit-public-mcp-server/) ·
[BIM Chapters — MCP tech preview](https://bimchapters.blogspot.com/2026/04/revit-mcp-public-server-tech-preview.html) ·
[AutoCAD 2027](https://www.autodesk.com/blogs/autocad/autocad-2027/) ·
[LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server) ·
[IbrahimFahdah/revit-claude-mcp](https://github.com/IbrahimFahdah/revit-claude-mcp) ·
[Nghị định 217/2026/NĐ-CP](https://luatvietnam.vn/tin-van-ban-moi/tu-01-7-2026-bat-buoc-ap-dung-bim-doi-voi-cong-trinh-xay-dung-moi-tu-cap-ii-tro-len-186-109713-article.html) ·
[BimSpeed](https://www.bimspeed.net/)
