# Lộ trình phát triển DHCB Tools

Tài liệu này mô tả **kế hoạch phía trước**. Hiện trạng thực tế nằm ở [`progress.md`](progress.md). Đặc tả chi tiết ở
[`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md), kế hoạch kiểm thử ở [`dac-ta-kiem-thu.md`](dac-ta-kiem-thu.md), cơ sở kỹ
thuật ở [`nghien-cuu-dhcb-revit-tools.md`](nghien-cuu-dhcb-revit-tools.md), khảo sát thị trường ở
[`nghien-cuu-tool-thi-truong-va-ke-hoach.md`](nghien-cuu-tool-thi-truong-va-ke-hoach.md), khảo sát theo **chặng
công việc** (thiết kế → BIM → shop → thi công → hoàn công) ở
[`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md).

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
| 8.4 ✅ | **Vòng kiểm thử thật trọn 42 lệnh** trên Revit 2024.3 + AutoCAD 2026.1 | **Đạt 2026-09-03: 52 ca đạt / 0 trượt / 1 bỏ qua trên 53 ca, ba bộ ca kiểm ba model mẫu (kiến trúc 27/28 · HVAC 17/17 · cấp thoát nước 8/8), phủ đủ 42/42 lệnh Revit** — Revit tự mở → chạy → tự đóng bằng một lệnh. Vòng này lộ ra **7 lỗi runtime + 4 chỗ lệch tài liệu↔mã nguồn**, tất cả đã sửa kèm test chốt chặn (`SuiteCoverageTests` giữ cho con số 42/42 không trôi). Batch **AutoCAD** cũng chạy trọn lần đầu qua accoreconsole, lộ ra lỗi `DHCB_RUN` ba tham số trên một dòng. Bằng chứng: [`bang-chung-test.md`](bang-chung-test.md) §8 và §9. ✅ **AutoCAD cũng đã đủ 15/15 lệnh có ca kiểm tự động** qua `accoreconsole` (18/18 ca, §10) — vòng đó lộ thêm 2 lỗi ghi đè im lặng ở `LayerImport`/`AttributeImport`. ✅ **Đường ghi thật** cũng đã có (12/12 ca, §11): ba lớp khoá + chạy trên bản chép, chuỗi tự chứng minh đã commit và tự khôi phục. ✅ **Đường ghi cho nhóm lệnh tạo phần tử mới** cũng đã có (§12): `LevelSetup`/`SheetBatchCreate` chốt bằng tính idempotent (2 → 0), `HangerAuto` 1120 → 0 sau khi được bổ sung chống trùng. Vòng này lộ ra một lỗi chặn thật: cảnh báo Revit **lúc mở model** nằm ngoài mọi transaction nên batch treo ở hộp thoại — sửa bằng `Application.FailuresProcessing` cho cả phiên batch. ✅ **Bài học lặp lại ở đêm batch trên dự án thật (§20):** `FailuresProcessing` chỉ bắt *cảnh báo*, không bắt **TaskDialog** — hộp thoại "nâng cấp phiên bản" khi mở file Revit cũ treo batch 43 phút. Phải đăng ký **`UIApplication.DialogBoxShowing`** song song với `FailuresProcessing` cho cả phiên batch; hai loại hộp thoại này là hai đường khác nhau, chặn một cái không chặn cái kia. ⬜ Còn: phần kiểm tay R1–R48, C1–C17, B1–B12 | 1 tuần |

**Đầu ra:** phát hành **v1.0** chỉ gồm các lệnh đã qua 8.3/8.4; lệnh còn lại vẫn có trong gói nhưng gắn nhãn *thử nghiệm*.

---

## Giai đoạn 9 — Từ hình dáng agent sang hình dáng kỹ sư 🟡 (tuần 3–4)

32/42 nút Ribbon hiện là runner JSON chung không có form, và file config **không tự sinh** như README nói
(`CommandRunner.LoadConfig` trả `JObject` rỗng). Không kỹ sư nào sửa JSON trong `%APPDATA%` để dùng tool.

| # | Việc | Chi tiết |
|---|---|---|
| 9.1 ✅ | **Form động từ `CommandCatalog`** | `FieldKind` + `FieldSpec` trong catalog, kiểu suy ra từ tên trường (`FieldKindGuess`, thuần, có test cho cả 107 trường thật). `CommandFormWindow` dựng ô nhập theo kiểu: checkbox, ô số theo culture, ô đường dẫn kèm nút chọn file/thư mục, combo lấy từ mô hình đang mở (`ModelChoices`: category, tham số, level, view template, family type). *Xem trước* chạy `dryRun` và hiện `Summary`+`Messages`; nút *Chạy thật* chỉ mở sau khi xem trước thành công. Config tự lưu lại. Ba vỏ MEPF viết tay (SleeveAuto/ElevationTag/ConnectorChecker) nay cũng đi qua form — gỡ luôn `SleeveFamilyName = "M_Generic Model"` gắn cứng. MCP `inputSchema` nhận kiểu JSON đúng (number/boolean/array) thay vì tất cả là string |
| 9.2 ✅ | **Lớp từ điển tham số & family** | `ParameterDictionary` (thuần, có test) đọc `%APPDATA%\DHCB\dictionary.json` — mẫu ở [`configs/dictionary.sample.json`](../configs/dictionary.sample.json). Mỗi khoá logic (`level`, `diameter`, `bottomElevation`…) có danh sách tên đồng nghĩa Anh–Việt; tên trong file đứng **trước** tên dựng sẵn chứ không thay thế, nên dự án dùng thư viện chuẩn vẫn chạy mà không cần file. `RevitCompat.Lookup(element, key, preferred)` là điểm tra tham số duy nhất của Core (instance rồi type). Đã gỡ literal khỏi Core: `"Level"` (5 chỗ), `"Outer Diameter"`/`"Width"`/`"Height"`, `"Department"`/`"Occupancy"`, mặc định `DHCB_*_Elevation` và `M_Generic Model`. ✅ **Không phải gõ tay từ điển**: lệnh `DictionaryLearn` (`Core/Ai/DictionaryLearnCommand`, tầng thuần `Ai/DictionarySuggester` có test) soi tên tham số CÓ THẬT trong mô hình đang mở, đối chiếu từng khoá logic, và đề xuất/ghi vào `dictionary.json` — `dryRun` mặc định bật, khi ghi thì **trộn** (tên kỹ sư đã khai giữ nguyên, bản cũ lưu `.bak`). Ràng buộc như `CadLayerMap`: chỉ đề xuất tên có thật; tham số rỗng toàn dự án và sai kiểu bị hạ điểm; không có ứng viên đủ giống thì báo *không thấy* chứ không đề xuất bừa. Lý do làm: §21 đo được đúng ma sát này trên dự án thật. **Tra không ra là báo lỗi có mã `E-PARAM-MISSING` kèm danh sách tên đã thử** — `SleeveAuto` báo số phần tử không tra được kích thước, `ElevationTag` trả `Success=false` khi không ghi được phần tử nào (trước đây báo "Đã gán cao độ cho 0/N" như thể bình thường) |
| 9.3 ✅ | **Tiếng Việt hoá thông báo** | ✅ Đã dọn các chuỗi tiếng Anh còn sót mà vòng chạy thật 2026-09-03 in ra tận báo cáo: `[Dry Run] Would create 2 level(s).`, `[Skip]`, `[Create]`, `Family folder not found`, `Error: `, `[DryRun]` của BatchExport. `VietnameseMessageTests` (thuần, đọc mã nguồn Core) chốt chặn theo danh sách mẫu đã thấy tận mắt. ✅ Thêm mã lỗi `E-CONFIG-MISSING` (thiếu trường bắt buộc) cạnh `E-PARAM-MISSING`/`E-PATH-MISSING` sẵn có. ✅ **Bảng mã lỗi** gom vào [`ma-loi.md`](ma-loi.md) — 4 mã (`E-CONFIG-MISSING`, `E-PATH-MISSING`, `E-PARAM-MISSING`, `E-PARAM-READONLY`), mỗi mã kèm nghĩa/khi nào gặp/cách xử lý; `MaLoiTests` đối chiếu tài liệu với mã nguồn **cả hai chiều** nên bảng không trôi. ✅ **Rà hết**: nhóm MEPF không còn chuỗi tiếng Anh nào; vòng rà cuối bắt thêm 7 chỗ kỹ sư nhìn thấy hằng ngày mà danh sách mẫu cũ không thấy — 6 **tên transaction** hiện trong danh sách Undo của Revit (2 cái viết không dấu, 4 cái tiếng Anh) và **tiêu đề báo cáo HTML**. `TenTransaction_PhaiCoDauTiengViet` kiểm theo hướng ngược lại (phải CÓ dấu) nên bắt được cả chữ Việt không dấu. Giữ nguyên `"Shared Levels and Grids"` — đó là tên workset chuẩn của Revit, đổi là tạo workset lệch chuẩn |
| 9.4 🟡 | **Đưa cho một nhóm kỹ sư dùng thật** | Phát hành v1.1, thu phản hồi theo mẫu (lệnh nào dùng hằng tuần, lệnh nào bấm rồi bỏ). Số liệu này quyết định giai đoạn 10/11 đi sâu vào đâu. ✅ **Mẫu thu phản hồi**: [`mau-phan-hoi-9-4.md`](mau-phan-hoi-9-4.md) — bảng tick *tuần / bỏ / chưa* cho đủ 42 lệnh Revit + 15 lệnh AutoCAD, cột lý do bắt buộc khi tick *bỏ*, bốn câu hỏi mở, và cách tổng hợp theo lệnh chứ không theo người; `PhanHoiFormTests` đối chiếu hai chiều với `CommandCatalog` nên mẫu không trôi khi thêm/bớt lệnh. ⬜ Phát hành v1.1 và chọn nhóm kỹ sư |

---

## Giai đoạn 10 — Agent khép vòng cho Revit 2021–2026 🟡 (tuần 5–8) — **hướng khác biệt lớn nhất**

Autodesk Revit 2027 MCP Server chỉ **đọc** và chỉ chạy trên **2027**; các dự án revit-mcp mã mở có 100+ tool nhưng
không có `dryRun`, token, batch, AutoCAD song hành hay tiếng Việt. DHCB đã có Bridge, token, `ExternalEvent`,
catalog, MCP — thiếu đúng phần làm agent *nhìn, chỉ, kiểm* được kết quả.

| # | Việc | Chi tiết |
|---|---|---|
| 10.1 ✅ | **Mở rộng phía đọc** `RevitQueryHandler` (10 → 17 loại) | Đã có cho Revit: `element_geometry` (hộp bao, đường tâm, **connector kèm tình trạng nối**, host, level — toạ độ trả ra mm), `parameters_of` (tham số của category: tên, `storageType`, chỉ đọc, giá trị mẫu), `schedule_rows` (bảng dạng hàng, không ghi file), `snapshot` (`Document.ExportImage` → PNG base64, nên vẫn ở Core không cần RevitAPIUI); và ở vỏ Revit (`UiQueryHandler`, cần `UIDocument`): `selection` (đọc + **đặt**), `show_elements` (zoom + chọn), `active_view`. Xem [`agent-khep-vong.md`](agent-khep-vong.md). ✅ **Phần AutoCAD tương ứng**: `entity_geometry` + `attributes_of` ở Core (chỉ cần `Database`, nên `accoreconsole` vẫn chạy được), `selection` + `show_entities` + `active_layout` ở vỏ (cần `Editor`). Định danh là **handle** hex — `HandleText` (Shared.Logic, 16 test) nhận mọi dạng viết; handle sai luôn báo trong `notFound`. ✅ **`snapshot` phía AutoCAD (2026-09-05)**: vỏ render model space vào thiết bị off-screen của GraphicsSystem (`live`, đúng cỡ `imageWidth`, không đụng khung nhìn), Core trả ảnh xem trước trong DWG (`thumbnail`, chạy được trong accoreconsole); mỗi kết quả ghi rõ `source`. Chạy thật trên AutoCAD 2026.1: live 1200×900 / 22 KB, kẹp cỡ 99999 → 4000×3000, thumbnail 256×171 — [`bang-chung-test.md`](bang-chung-test.md) §25. Nhận định cũ "không có đường nào sạch" sai: `PNGOUT` là ngõ cụt, GraphicsSystem thì không |
| 10.2 ✅ | **Khép vòng ghi** | `CommandResult.ChangedIds` — ElementId của phần tử vừa tạo/sửa, giới hạn 500 id một lượt để không phình response (`AffectedCount` vẫn là số đầy đủ). Đã gắn cho `SleeveAuto`, `HangerAuto`, `AutoNumbering`, `ElevationTag`, `SheetRename`. Agent nay chạy được vòng: xem trước → chạy → `element_geometry`/`show_elements` trên đúng id vừa đổi → `snapshot` để nhìn |
| 10.3 ✅ | **Playbook nghiệp vụ** cho Claude (thư mục `skills/`) | ✅ 3 playbook đầu trong [`skills/`](../skills/): *kiểm model trước sync*, *đánh số hàng loạt*, *xử lý một nhóm cảnh báo*. Mỗi cái là trình tự `parameters_of` → xem trước → xác nhận → kiểm lại bằng `changedIds` → `show_elements`/`snapshot`, kèm mục **Không được làm**. ✅ Đủ 5 playbook: thêm *dựng grid/level từ CAD* (hai chặng AutoCAD → CSV → Revit, chốt offset gốc toạ độ trước khi ghi) và *xuất bộ PDF theo revision* (gán revision đúng nhóm sheet → xem trước → xuất → **đối chiếu số file thật** trong thư mục). `PlaybookTests` chốt: mọi tên lệnh/truy vấn nhắc trong playbook phải có thật, và mỗi playbook phải có mục *Không được làm* |
| 10.4 ✅ | **Đóng gói `.mcpb`** cho Claude Desktop | ✅ [`tools/mcpb/manifest.json`](../tools/mcpb/manifest.json) + [`scripts/pack-mcpb.ps1`](../scripts/pack-mcpb.ps1) → `dist/dhcb-<app>-<phiên bản>.mcpb`, mở bằng Claude Desktop là xong. Token để trống thì tự đọc `bridge-token.txt`. **Đã đóng gói thật**: 9,1 KB, 4 file, không dependency ngoài. MCP server chịu được khi Revit chưa mở: nhớ danh mục lệnh vào cache nên vẫn liệt kê đủ lệnh kèm ghi chú "Revit chưa mở" thay vì trả danh sách rỗng làm người dùng tưởng gói hỏng |
| 10.5 ✅ | **Bridge chịu tải** | ✅ `timeoutSeconds` theo từng request (`BridgeRequest`), chặn trên 10 phút để một client không giữ hàng đợi Revit vô hạn; `dhcb_agent.send()` và MCP truyền xuống, client tự chờ lâu hơn server 10 s. Hàm chọn timeout tách static nên có test. ✅ `/progress/<id>`: `POST /execute` kèm `"async": true` trả ngay `202 {id}`, client hỏi trạng thái bằng id — kết quả không đi theo kết nối nữa. Sổ lệnh nền có hạn (30 phút / 50 mục, không bao giờ bỏ lệnh đang chạy), phần thuần có test; `dhcb_agent.py --background` tự hỏi vòng |

**Chỉ số:** agent tự tìm và xử lý **20 warning trên Snowdon Towers** trong một phiên, mỗi bước có ảnh chụp, kỹ sư chỉ bấm xác nhận.

---

## Giai đoạn 11 — Gói tuân thủ BIM và hồ sơ hoàn công theo NĐ 217/2026 + NĐ 207/2026 ⬜ (tuần 9–10, mở tiếp theo phản hồi)

> ✅ **Đã viết lại theo văn bản mới (2026-09-04).** Bản trước căn cứ vào **NĐ 06/2021/NĐ-CP**, nghị định
> đó đã bị **NĐ 207/2026/NĐ-CP thay thế từ 01/7/2026**. Mục này giữ đúng ba đề xuất **C1/C2/C3** của
> [`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md) §2 và §4 đợt C — hai tài liệu
> không được phát biểu khác nhau về cùng một điều luật.

**Hai nghị định cùng hiệu lực 01/7/2026 chi phối giai đoạn này:**

| Văn bản | Chỗ chạm vào DHCB |
|---|---|
| **NĐ 217/2026/NĐ-CP** — quản lý hoạt động xây dựng (thay NĐ 175/2024) | BIM bắt buộc với công trình mới **từ cấp II trở lên**, từ bước báo cáo nghiên cứu khả thi. Chủ đầu tư phải **cung cấp dữ liệu BIM cho cơ quan chuyên môn**; mô hình phải **cập nhật sau hoàn công** rồi chuyển cho đơn vị vận hành. CDE chỉ bắt buộc với **cấp I** — hạ tầng, không phải add-in |
| **NĐ 207/2026/NĐ-CP** ngày 15/6/2026 — quản lý chất lượng, thi công xây dựng và bảo trì công trình, quy định chi tiết **Luật Xây dựng số 135/2025/QH15** (thay **NĐ 06/2021**), **11 phụ lục** | Bốn địa chỉ phải bám khi code — bảng ngay dưới |

**Bốn địa chỉ trong NĐ 207/2026 mà giai đoạn 11 phải bám:**

| Nội dung | Vị trí | Điều DHCB phải làm đúng |
|---|---|---|
| **Nhật ký thi công xây dựng** và **bản vẽ hoàn công** | **Phụ lục II** — nghĩa vụ của *nhà thầu thi công xây dựng* (Điều 15) | Nhật ký ghi **đồng thời với sự kiện, không ghi bù**. Nhật ký **điện tử** được chấp nhận khi có đủ ba điều kiện: ① dấu thời gian **không thể chỉnh sửa ngược** · ② **cơ chế xác nhận của các bên** · ③ **sao lưu độc lập** |
| **Mẫu dấu bản vẽ hoàn công** | **Phụ lục IIb** — **hai mẫu**: Mẫu 1 cho hợp đồng thường; **Mẫu 2** cho hợp đồng **thầu chính/thầu phụ, EPC, chìa khoá trao tay** | Dấu gồm tên nhà thầu thi công · dòng "BẢN VẼ HOÀN CÔNG" · ngày tháng năm · người lập · chỉ huy trưởng công trình hoặc giám đốc dự án · tư vấn giám sát trưởng (Mẫu 2 tách thêm dòng của **tổng thầu**). Nếu kích thước thực tế **không vượt dung sai** thì photocopy bản vẽ thi công rồi các bên đóng dấu, ký xác nhận; vẽ lại thì khung tên phải tương tự mẫu ở Phụ lục IIb |
| **Hồ sơ hoàn thành công trình** | **Điều 28 + Phụ lục VII** — **chủ đầu tư** tổ chức lập, chịu trách nhiệm về tính chính xác và trung thực; mỗi nhà thầu chịu trách nhiệm phần mình lập | Danh mục ba nhóm: ① chuẩn bị đầu tư xây dựng và hợp đồng · ② khảo sát, thiết kế xây dựng · ③ quản lý chất lượng thi công xây dựng. Điều 28 **tách khỏi** điều kiện đưa công trình vào khai thác, sử dụng (Điều 29) — hai nhóm điều kiện song song, không còn tuần tự như NĐ 06/2021 |
| **Hồ sơ điện tử** | **Điều 11** | Lập theo pháp luật về **giao dịch điện tử**. Khi cơ quan nhà nước có thẩm quyền yêu cầu, hồ sơ điện tử phải **trích xuất, in ra giấy và được chủ đầu tư xác nhận** — ràng buộc trực tiếp lên đầu ra của 11.3 |

> ⚠️ **Trước khi code từng mục, mở lại bản gốc trên Công báo Chính phủ.** Số hiệu điều và phụ lục ở trên
> đã đối chiếu chéo nhiều nguồn, nhưng **nội dung từng dòng của Phụ lục IIb và Phụ lục VII phải đọc từ văn
> bản gốc** rồi mới dựng family dấu và danh mục hồ sơ. Đây là lộ trình, không phải bản trích lục pháp lý.

Batch runner đêm + `ParameterRuleCheck` + `HealthReport` + `BatchExport` IFC đã là phần lớn của một "máy kiểm
mô hình". Ba thứ còn thiếu, và cả ba đến từ luật chứ không từ ý thích: bộ quy tắc **đọc được bằng máy** mà bên
ngoài kiểm lại cũng ra cùng kết quả, đường **hồ sơ hoàn công** (dấu + danh mục), và **bằng chứng không sửa
ngược** cho nhật ký điện tử.

| # | Việc | Chi tiết |
|---|---|---|
| 11.1 | **Checkset theo giai đoạn** (khả thi → thiết kế → thi công → hoàn công), nền là **IDS** chứ không phải JSON tự nghĩ | Đọc file **IDS 1.0** (buildingSMART, chuẩn chính thức từ 01/6/2024): chủ đầu tư/tư vấn thẩm tra khai yêu cầu **một lần**, kiểm được bằng cả DHCB lẫn IfcTester/Solibri và nhận **cùng một kết quả** — đúng điều IDS được lập ra để bảo đảm. Kiểm **thẳng trên mô hình Revit**, không vòng qua IFC: kỹ sư sửa ngay tại chỗ. Phần đánh giá thuần ở `Shared.Logic/Ids` (`IdsSpec` + `IdsEvaluator`, 6 loại facet), có test. `ParameterRuleCheck` và `configs/checksets/` **giữ nguyên** cho quy tắc nội bộ công ty mà IDS không mô tả: đặt tên file/view/sheet theo BEP, worksets, link, ngưỡng warning/dung lượng. = đề xuất **C3** |
| 11.2 ✅ | **Kiểm IFC trước nộp** | ✅ 2026-09-05. Căn cứ: NĐ 217/2026 bắt **nộp dữ liệu BIM** cho cơ quan chuyên môn, nên "xuất được" chưa đủ — phải "xuất rồi tự đọc lại thấy đúng". Tầng thuần `Shared.Logic/Ifc`: bộ đọc STEP (ISO 10303-21) **viết tay, không phụ thuộc thư viện IFC nào** nên chạy trên CI; đọc được chuỗi có dấu `;` bên trong, chú thích, giá trị bọc kiểu, và **dãy thoát Unicode `\X2\`/`\X4\`/`\X\`/`\S\`** — không giải mã thì tên tiếng Việt trong file Revit xuất ra thành rác và mọi so khớp tên đều trượt. `IfcModel` dựng sẵn hai quan hệ ngược mà mọi quy tắc đều cần (Pset qua `IfcRelDefinesByProperties` **kể cả thừa kế từ kiểu**, phân loại qua `IfcRelAssociatesClassification`) — một lượt duyệt O(n) thay vì quét lại toàn bộ quan hệ cho từng phần tử. `IfcChecker` + `IfcCheckSpec`: lược đồ, số lượng theo kiểu, thuộc tính/tên/phân loại bắt buộc, mã định danh không rỗng không trùng, tham chiếu không gãy. 44 ca test. **Không làm thành lệnh Core** — kiểm một file IFC không cần `Document` nào, mà thêm lệnh Core thì vướng **nguyên tắc 6**; đổi lại là **`DhcbTools.BatchRunner --verify-ifc <file> [--ifc-spec <json>]`** (0 đạt · 1 có lỗi · 2 không có file/quy tắc hỏng), đúng hình dáng `--verify-log` của 11.5. Tài liệu: [`kiem-ifc.md`](kiem-ifc.md), mẫu [`configs/ifc-check.sample.json`](../configs/ifc-check.sample.json). ✅ **Chạy thật** trên file Revit 2024.3 xuất ra (**925.815 thực thể / 91 MB, kiểm hết 5,1 giây**) — vòng này lộ ra **hai lỗi**: (a) `BatchExport` định dạng `Ifc` **chưa bao giờ tạo ra file nào** vì Revit đòi transaction cho riêng đường IFC, mà ngoại lệ bị gom vào `Errors` rồi vẫn trả `Ok` (no-op im lặng núp sau danh sách lỗi không ai đọc) — sửa bằng transaction + `RollBack`, ca kiểm nay có `noErrors: true`; (b) chính bộ kiểm **báo nhầm 106 "mã định danh trùng"** vì tên thuộc tính `TreadLengthAtInnerSide` dài đúng 22 ký tự — thiếu ràng buộc ký tự đầu chỉ chở 2 bit nên nằm trong `0`–`3`. Bằng chứng: [`bang-chung-test.md`](bang-chung-test.md) §27. ⚠️ **Ranh giới phải nói rõ**: đây là quy tắc **nội bộ về đầu ra của bộ xuất**, không phải yêu cầu của chủ đầu tư — yêu cầu của chủ đầu tư/thẩm tra khai bằng **IDS** (11.1). Bộ đọc **không suy ra lớp con** (`IfcWall` ≠ `IfcWallStandardCase`): mang bảng lược đồ EXPRESS vào thì mỗi bản IFC lại phải bảo trì một bảng, sai một dòng là quy tắc trượt im lặng |
| 11.3 | **Gói bàn giao tự động** | Một job đêm sinh: IFC, PDF, danh mục bản vẽ, báo cáo tuân thủ HTML/PDF có dấu thời gian và version add-in. **Thêm theo Điều 11 NĐ 207/2026:** mọi đầu ra điện tử phải **trích xuất được ra PDF/giấy** kèm chỗ **chủ đầu tư xác nhận** — không được chỉ tồn tại dưới dạng dữ liệu trong máy. Chạy trên 2 dự án thật trước khi công bố |
| 11.4 | Quyết định **mức sâu của kiểm IDS** | 11.1 đã chốt IDS là nền nên câu hỏi còn lại hẹp hơn bản cũ: có mở `IdsValidate` sang kiểm **trên file IFC** (đối chiếu kết quả với IfcTester để chứng minh cùng kết luận) hay dừng ở kiểm trên mô hình Revit. Dựa trên phản hồi chủ đầu tư/thẩm tra sau 11.3 |
| 11.5 ✅ | **Chuỗi băm cho nhật ký điện tử** (đề xuất **C1**) | ✅ Mỗi dòng `run-HHmmss.jsonl` nay mang `prevHash` (băm dòng trước) và `hash` = SHA-256 của **chính chuỗi ký tự đã ghi ra file**, tính đến trước trường `hash` — băm trên byte đã ghi chứ không serialize lại, nên kiểm không phụ thuộc thư viện JSON. `Shared.Logic/Evidence/HashChain` **thuần tuyệt đối**, 24 ca test. Gắn dấu vết đặt ở `RunLog.Append` — **điểm ghi duy nhất** của cả batch Revit lẫn AutoCAD — nên phủ hết mọi đường ghi mà không sửa chỗ gọi nào; trường mới thêm ở cuối dòng nên `report.html`/`--analyze`/log đêm cũ vẫn đọc được. ✅ Kiểm bằng **`DhcbTools.BatchRunner --verify-log <file>`** (0 nguyên vẹn · 1 hỏng, in **đúng số dòng** · 2 không có file). **Không làm thành lệnh Core** như tên `EvidenceVerify` ban đầu gợi ý: kiểm log không cần `Document` nào, mà thêm lệnh Core thì vướng **nguyên tắc 6** (phải có ca kiểm chạy trong Revit) — đổi lại được thứ chạy trên CI. Bằng chứng: [`bang-chung-test.md`](bang-chung-test.md) §23 (bốn cách sửa log, bốn lần bị bắt, kể cả ca kẻ sửa **biết thuật toán** và tính lại băm cho chính dòng vừa sửa) và **§24** — chạy thật trên **log đêm batch 30 dòng / 351 KB / dòng dài nhất 123.357 ký tự**, và trên log AutoCAD nối qua **4 tiến trình `accoreconsole` riêng**. ⚠️ **Phải nói thật trong tài liệu sản phẩm:** chuỗi băm chứng minh **tính toàn vẹn nội bộ** của log, *không* thay chữ ký số hay dấu thời gian của một CA. Nó phủ **điều kiện ①** trong ba điều kiện của NĐ 207/2026, và tạo điều kiện cho ② (xác nhận của các bên — cần chữ ký số) và ③ (sao lưu độc lập — cần hạ tầng của chủ đầu tư); hứa quá là tự tạo rủi ro cho khách hàng. ⬜ Còn: chỉ số 30 ngày (theo định nghĩa phải chờ 30 ngày) và log của **dự án thật** như §20 |
| 11.6 | **`AsBuiltStamp` + `DossierIndex`** — dấu hoàn công và danh mục hồ sơ | DHCB cung cấp **family mẫu dấu theo Phụ lục IIb — dựng cả hai mẫu** — cùng cơ chế điền (tên nhà thầu, ngày, người ký, số hợp đồng lấy từ config); **doanh nghiệp chịu trách nhiệm nội dung**. Phần tự động: gán dấu lên loạt sheet, đặt revision "Hoàn công", xuất PDF theo danh mục — `RevisionOnSheets` + `BatchExport` đã làm được một nửa. `DossierIndex` (`Shared.Logic/AsBuilt`) sinh danh mục theo **Phụ lục VII** rồi **đối chiếu với file thật trong thư mục** và báo thiếu mục nào — cùng cách playbook xuất PDF theo revision đã dùng. = đề xuất **C2** |

**Thứ tự bên trong giai đoạn 11** (theo [`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md) §5):
**11.5 làm được ngay** — thuần gần hết, không phụ thuộc template hay thư viện của dự án, giá trị không phụ thuộc
kết quả 9.4; ✅ **đã làm 2026-09-04**. **11.1 rồi 11.6 chờ số liệu 9.4/`UsageReport`**: mẫu dấu và danh mục hồ sơ phụ thuộc thói quen từng
công ty; làm trước khi biết kỹ sư thật cần gì là lặp lại đúng sai lầm "bề rộng trước" mà mục cuối tài liệu này đã
ghi lại. Ràng buộc không đổi: **nguyên tắc 6** — không thêm lệnh Core khi chưa có ca kiểm chạy trong Revit.

**Chỉ số:** báo cáo tuân thủ đêm chạy trên **2 dự án thật** trước khi công bố; `BatchRunner --verify-log` mã thoát 0 trên
log thật của một đêm batch, **kiểm lại sau 30 ngày**.

### Chặng thi công — đợt A/B của [`nghien-cuu-chuoi-den-hoan-cong.md`](nghien-cuu-chuoi-den-hoan-cong.md) §5 🟡

Ba việc "làm ngay, không cần chờ số liệu 9.4" vì chỉ đọc hoặc chỉ ghi tham số, tầng thuần chiếm phần lớn, và mở
thêm **tệp người dùng khác** (trắc đạc, chỉ huy trưởng) cho chính vòng 9.4:

| # | Việc | Trạng thái |
|---|---|---|
| A1 | **`SetoutExport`** — toạ độ định vị (tim cột, tâm thiết bị/sleeve, giao trục) ra CSV theo thứ tự cột máy toàn đạc (`PNEZD`/`PENZD`…) + DXF điểm, hệ Survey tự kiểm chiều transform, tên điểm ≤ 16 ký tự không trùng | ✅ mã nguồn 2026-09-05: tầng thuần `Shared.Logic/Setout` + `Geometry/GridIntersections` (50 ca test), lệnh Core, Ribbon, 4 ca kiểm trong `revit-smoke`/`revit-mep`, playbook `skills/xuat-toa-do-dinh-vi`. 🧪 **Chưa chạy thật trong Revit** — giữ nhãn *thử nghiệm* theo nguyên tắc 6. Tài liệu: [`toa-do-dinh-vi.md`](toa-do-dinh-vi.md) |
| B1 | **`ConstructionStatus`** + **`ProgressReport`** — trạng thái lắp đặt/nghiệm thu từ CSV hiện trường (từ vựng Việt/Anh, có hay không dấu đều nhận), % theo **số lượng và chiều dài**, gộp theo tầng/hệ/category, luỹ kế theo tuần → HTML + CSV | ✅ mã nguồn 2026-09-05: tầng thuần `Shared.Logic/Progress` (41 ca test), hai lệnh Core, nút Ribbon, 6 ca kiểm, playbook `skills/theo-doi-tien-do-thi-cong`. 🧪 **Chưa chạy thật**; đường ghi của `ConstructionStatus` chưa có ca kiểm tự động (mã cấu kiện là ElementId của đúng file đang mở nên không viết sẵn vào fixture được). Tài liệu: [`tien-do-thi-cong.md`](tien-do-thi-cong.md) |
| B3 | **BCF 2.1 cho `ClashDetection`** — thêm `bcfPath`: mỗi va chạm một topic (tiêu đề, mô tả, nhãn "trong file"/"với model liên kết", camera phối cảnh nhìn vào tâm, hai phần tử liên quan) | ✅ mã nguồn 2026-09-05: tầng thuần `Shared.Logic/Bcf` (21 ca test, đọc lại chính zip vừa ghi), nối vào lệnh đã chạy thật nên **không thêm lệnh Core mới**; GUID topic sinh từ `key` va chạm nên xuất lại không đẻ vấn đề mới. 🧪 Chờ một lượt `revit-mep` để chốt file mở được trong Navisworks/Solibri |
| C4 | **`ModelLinesFromCad`** — DWG đã link/import → model line cho `RouteFromLines`: lọc layer (wildcard), bỏ đoạn rác và đường vẽ chồng, nối đoạn thẳng hàng nhưng không xuyên ngã ba, ép về một cao độ | ✅ mã nguồn 2026-09-05: tầng thuần `Shared.Logic/Cad/CadCurveFilter` (23 ca test), lệnh Core, nút Ribbon, 2 ca kiểm trong `revit-mep`; chạy lại không sinh bản sao. 🧪 **Chưa chạy thật trong Revit** — giữ nhãn *thử nghiệm* theo nguyên tắc 6. Đặc tả: [`dac-ta-tinh-nang.md`](dac-ta-tinh-nang.md) §3.0 |

---

## Hạ ưu tiên / dừng

| Hạng mục | Quyết định | Lý do |
|---|---|---|
| P3 giai đoạn 7 | Dừng | Thêm lệnh khi 42 lệnh chưa qua test chỉ tăng bề mặt lỗi |
| Nhóm kiểm chuẩn AutoCAD (`LayerStandardCheck`, `DHCB_AI`, panel Hermes) | Giữ nguyên, không đầu tư thêm | AutoCAD 2027 Assistant kiểm chuẩn, chọn theo ngôn ngữ tự nhiên, truy vấn layer/block ngay trong sản phẩm. AutoCAD giữ thế mạnh riêng: accoreconsole batch đêm, `GridExtract` → `GridFromCsv` |
| `AutoRoute` mức C | Thu hẹp thành **đề xuất tuyến** (model line) để kỹ sư duyệt; không dựng thẳng | MagiCAD/eVolve đầu tư nhiều năm; DHCB không cần thắng ở đây |
| Thông điệp "AI offline hoàn toàn" | Bỏ khỏi thông điệp chính | Giữ Ollama như tuỳ chọn; giai đoạn 10 dùng Claude qua MCP mạnh hơn nhiều so với model 8B |
| Routing A/B, sizing | Giữ, gắn nhãn *thử nghiệm* đến khi qua 8.3 | Phụ thuộc family/routing preference từng dự án |

## Nền tảng — .NET 10 🟡

Microsoft ngừng hỗ trợ .NET 8 ngày **10/11/2026**. AutoCAD 2026.1 (package `AutoCAD.NET 25.1.x`) đã ở .NET 10;
Revit 2027 cũng vậy — gói `Nice3point.Revit.Api.RevitAPI` **2027.2.0** đã có trên NuGet, tức Revit 2027 đã phát
hành chứ không còn "preview".

| Việc | Trạng thái |
|---|---|
| Nhánh TFM `net10.0-windows` cho **AutoCAD ≥ 2026** | ✅ có từ trước trong `Directory.Build.props`, nhưng **chưa từng có CI** — chỉ build được trên máy có cài AutoCAD 2026 |
| Nhánh TFM `net10.0-windows` cho **Revit ≥ 2027** | ✅ 2026-09-05. Bản cũ điều kiện `>= 2025 → net8` **không có cận trên**, nên `-p:RevitVersion=2027` ra net8.0-windows sai âm thầm; nay `2025–2026 → net8`, `≥ 2027 → net10` |
| `check-build.sh` chạy được đường .NET 10 | ✅ `REVIT_VERSION=2027 ACAD_VERSION=2027 ./scripts/check-build.sh` xanh; header ghi bảng phiên bản → TFM |
| CI phủ .NET 10 | ✅ `tests.yml`: ma trận `check-build` **2023–2027**, `build-wpf-windows` **2023–2027** (2027 là bản WPF đầu tiên trên net10 — đúng chỗ SDK đổi implicit usings khi bật WPF), cài cả SDK 8 lẫn 10 |
| `release.yml` không đóng gói nhầm thư mục | ✅ bỏ hai chỗ hardcode `-ge 2025 → net8`; TFM nay **hỏi MSBuild** (`-getProperty:TargetFramework`) — một nguồn sự thật trong `Directory.Build.props` |
| Phát hành **AutoCAD 2026** (net10) | ✅ 2026-09-05: ma trận `build-autocad` thêm 2026, installer có component `acad2026` → bundle `Contents6`, `PackageContents.xml` thêm khối `SeriesMin/Max R25.1`. Làm được vì AutoCAD 2026.1 **đã chạy thật** (§24 batch 18/18 + 12/12, §25 snapshot) — khác Revit 2026/2027 vẫn chưa |
| `Shared.*` (netstandard2.0) nạp được trong net10 | ✅ ở mức biên dịch/liên kết: `deps.json` của vỏ Revit net10 tham chiếu đủ `Shared.Logic`/`Shared.Hosting`; AutoCAD 2026.1 thật đã NETLOAD và chạy 18/18 + 12/12 qua accoreconsole ([`bang-chung-test.md`](bang-chung-test.md) §24) |
| Chạy thật trên **Revit 2026/2027** | ⬜ máy chỉ có Revit 2024.3. Vì thế `release.yml` **chưa** đóng gói Revit 2026/2027 — không phát hành thứ chưa chạy. Phía AutoCAD thì đã chạy thật nên đã phát hành 2026 |

Không đổi logic. Không chặn giai đoạn 9–11.

## Chỉ số để biết đang đúng hướng

| Chỉ số | Mốc |
|---|---|
| Số lệnh Revit có test chạy trong Revit (8.3) | 42/42 trước v1.0 |
| Số kỹ sư dùng hằng tuần không cần hỏi (9.4) | ≥ 5 sau v1.1 |
| Thời gian agent hoàn thành kịch bản 20 warning (10) | < 15 phút, 0 thao tác tay ngoài xác nhận |
| Số dự án chạy báo cáo tuân thủ đêm (11.3) | 2 dự án thật trước khi công bố |
| Chuỗi băm nhật ký batch kiểm lại được sau 30 ngày (11.5) | `BatchRunner --verify-log` mã thoát 0 trên log thật của một đêm batch |

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
- Nghị định 207/2026/NĐ-CP (cùng hiệu lực 01/07/2026, thay NĐ 06/2021): nhật ký thi công điện tử được chấp nhận **có điều kiện**, hồ sơ hoàn thành công trình ở Điều 28 + Phụ lục VII, mẫu dấu bản vẽ hoàn công ở Phụ lục IIb. Đây là chỗ luật đi trước sản phẩm — xem giai đoạn 11.
- Đối thủ nội địa (BimSpeed, HQL) bán UI + đào tạo; DHCB không thắng bằng thêm nút.

Nguồn: [Revit Public MCP Server](https://www.autodesk.com/blogs/aec/2026/06/17/revit-public-mcp-server/) ·
[BIM Chapters — MCP tech preview](https://bimchapters.blogspot.com/2026/04/revit-mcp-public-server-tech-preview.html) ·
[AutoCAD 2027](https://www.autodesk.com/blogs/autocad/autocad-2027/) ·
[LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server) ·
[IbrahimFahdah/revit-claude-mcp](https://github.com/IbrahimFahdah/revit-claude-mcp) ·
[Nghị định 217/2026/NĐ-CP](https://luatvietnam.vn/tin-van-ban-moi/tu-01-7-2026-bat-buoc-ap-dung-bim-doi-voi-cong-trinh-xay-dung-moi-tu-cap-ii-tro-len-186-109713-article.html) ·
[Nghị định 207/2026/NĐ-CP — Công báo Chính phủ](https://congbao.chinhphu.vn/van-ban/nghi-dinh-so-207-2026-nd-cp-469769.htm) ·
[Toàn bộ 11 phụ lục NĐ 207/2026](https://thuvienphapluat.vn/chinh-sach-phap-luat-moi/vn/ho-tro-phap-luat/chinh-sach-moi/114944/toan-bo-phu-luc-nghi-dinh-207-2026-nd-cp-quan-ly-cong-trinh-xay-dung-tu-01-7-2026) ·
[BimSpeed](https://www.bimspeed.net/)
