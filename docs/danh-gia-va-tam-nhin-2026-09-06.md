# Đánh giá sâu và tầm nhìn phát triển DHCB Tools (2026-09-06)

> Đọc toàn bộ mã nguồn, tài liệu, CI và lịch sử commit tại thời điểm PR #133. Tài liệu này **không** lặp lại
> [`progress.md`](progress.md) (hiện trạng) hay [`roadmap.md`](roadmap.md) (kế hoạch); nó trả lời ba câu:
> *dự án đang thật sự ở đâu, chỗ nào yếu nhất, và 24 tháng tới nên đi đâu*. Phần "tầm nhìn" là đề xuất để quyết,
> không phải việc đã chốt.

## 1. Kết luận ngắn

1. **Về kỹ thuật, dự án đã vượt xa mức "add-in tự viết"**: 43.700 dòng C#, 64 lệnh trên hai nền tảng, kiến trúc
   Core/vỏ tách sạch, 1.232 ca test thuần với cổng phủ 100 % dòng, bộ ca kiểm chạy trong Revit/AutoCAD thật trên
   hai phiên bản, batch đêm có chuỗi băm, IDS/IFC/BCF tự viết không phụ thuộc thư viện. Ít add-in nội địa nào có
   nền như vậy.
2. **Về sản phẩm, dự án chưa có một người dùng thật nào.** Mọi bằng chứng (84 ca 0 trượt, 24 lệnh "dùng tuần")
   đều do chính tác giả tạo ra trên một máy, một dự án A, một buổi đóng vai. Câu hỏi quyết định *"có dùng tiếp
   không"* vẫn trống, và chính roadmap đã thừa nhận điều đó.
3. **Rủi ro lớn nhất không nằm trong mã** mà ở ba chỗ: (a) một người + tốc độ ~130 PR trong 6 ngày, không ai
   khác đọc nổi 7.600 dòng tài liệu để tiếp quản; (b) bề mặt bảo trì 64 lệnh × 3 TFM × 8 phiên bản phần mềm chủ
   mà chưa có doanh thu hay cộng đồng nuôi; (c) chưa có giấy phép, chưa ký DLL, chưa có gói nhập môn cho kỹ sư.
4. **Hướng khác biệt thật sự** không phải "agent cho Revit" (Autodesk và 5 dự án mã mở đang lấp) mà là
   **lớp logic thuần đã tách được khỏi Revit**: kiểm IDS/IFC, chuỗi băm nhật ký, gói bàn giao theo NĐ 207/2026.
   Đó là thứ chạy được **không cần Revit**, tức mở ra tệp người dùng mới (chủ đầu tư, thẩm tra, giám sát) mà
   không đối thủ nào nhắm tới, và luật đang tạo cầu.
5. Đề xuất: **đóng băng số lệnh thêm 6 tháng**, dồn 3 tháng đầu vào việc duy nhất chưa làm được bằng máy —
   đưa cho 5 kỹ sư ở 2 công ty dùng — rồi chọn **một** mũi nhọn (khuyến nghị: gói tuân thủ/hoàn công) làm
   tên gọi của sản phẩm.

## 2. Hiện trạng bằng số

| Chỉ số | Giá trị | Ghi chú |
|---|---:|---|
| Mã nguồn C# (`src/`) | 43.700 dòng | `Shared.Logic` 18.700 · `Core` (Revit) 13.800 · `Core.AutoCAD` 4.400 · hai vỏ 4.100 · `Hosting` 1.700 |
| Test thuần (xUnit) | 1.232 `[Fact]/[Theory]`, 16.700 dòng | Tỉ lệ test/mã ở tầng thuần ≈ 0,9 — rất cao |
| Cổng phủ trên CI | 100 % dòng `Shared.Logic`, 100 % câu lệnh Python | 4 chỗ `ExcludeFromCodeCoverage` có lý do |
| Mã chạm Revit/AutoCAD API **không** qua CI | ≈ 22.000 dòng | Chỉ kiểm bằng 155 ca `tests/suites` trên một máy |
| Lệnh | 49 Revit + 15 AutoCAD | 49/49 và 15/15 đã chạy thật ít nhất một lần |
| Phiên bản hỗ trợ | Revit 2023–2026, AutoCAD 2024–2026 | 2027 build được, chưa chạy thật |
| Tài liệu | 24 file, 7.600 dòng | `bang-chung-test.md` 3.370 dòng, đã tới §64 |
| Tốc độ | PR #133 sau 6 ngày (01–06/9) | Một tác giả, gần như chắc chắn có AI hỗ trợ |
| Người dùng thật | **0** | v1.1.0 và v1.1.1 đã phát hành, chưa giao cho ai |
| Giấy phép / ký DLL | Không có `LICENSE`; `sign-addin.ps1` có nhưng chưa thấy chứng chỉ | SmartScreen sẽ chặn ở máy kỹ sư |
| Lỗi để ngỏ | 6 mục trong "Còn mở" | Đáng chú ý: `ScheduleExport` thành công một phần vẫn `Success=true` |

## 3. Điểm mạnh có bằng chứng

- **Kỷ luật kiến trúc hiếm thấy.** `Document/Database + config → CommandResult`, `dryRun` mặc định, một
  lệnh chạy từ bốn đường (Ribbon, Bridge, batch, AI) qua đúng một điểm `Dispatch`. Bốn bộ test đối chiếu mã với
  mã (`RibbonCoverage`, `CatalogField`, `SuiteCoverage`, `VietnameseMessage`) và hai bộ đối chiếu tài liệu với
  mã (`MaLoi`, `PhanHoiForm`) giữ cho catalog, Ribbon, tài liệu không trôi. Đây là thứ giúp dự án sống sót khi
  đổi người.
- **Văn hoá "lỗi im lặng là lỗi tệ nhất"** được thực thi, không chỉ hô: `E-PRECOND` chặn "0 va chạm" giả,
  `E-PARAM-MISSING` liệt kê tên đã thử, lỗi thiếu family liệt kê 8 family có thật, xem trước biết trước tag
  thiếu. Từng chỗ đều có §bằng chứng đo trên dự án thật A.
- **Tầng thuần là tài sản chuyển được.** Bộ đọc STEP/IFC, IDS 1.0 evaluator (khớp IfcTester 10/10), BCF 2.1,
  HashChain, `PathFinder3D`, `SetoutPlanner` không phụ thuộc gì vào Autodesk. Chúng chạy trên Linux CI hôm nay
  và có thể chạy trong CLI, service, hay Design Automation ngày mai.
- **Đã tự sửa hướng một lần và ghi lại lý do** ("Vì sao đổi hướng", 03/9): dừng mở rộng bề rộng, quay về chiều
  sâu. Mục "Hạ ưu tiên / dừng" có quyết định rõ với lý do. Rất ít dự án cá nhân làm được việc này.
- **Batch đêm đã chạy trên dữ liệu thật** (8/9 file ~700 MB, Task Scheduler Result 0) và lộ ra hai lớp lỗi
  chỉ dữ liệu thật mới lộ (TaskDialog nâng cấp, link chưa nạp). Đó là bằng chứng có giá trị hơn mọi test thuần.

## 4. Điểm yếu và rủi ro, xếp theo mức nguy hiểm

### 4.1 Toàn bộ vòng kiểm chứng là tự-kiểm (nguy hiểm nhất)

Mọi con số đẹp — 84 ca 0 trượt, 24/25 lệnh "dùng tuần", 247 lần chạy trong 3 ngày — đều do một người sinh ra
trên máy của mình. Bộ ca kiểm được viết bởi người viết lệnh nên nó kiểm điều tác giả *nghĩ tới*; vòng đóng vai
6 kỹ sư là một người bấm theo kịch bản mình soạn. Chỉ số roadmap "≥ 5 kỹ sư dùng hằng tuần" chưa nhích khỏi 0.
Hệ quả: không biết lệnh nào trong 64 lệnh thật sự có người cần, nên **mọi ưu tiên của giai đoạn 10/11 đang
được quyết bằng phỏng đoán**, và roadmap cũng nói vậy.

### 4.2 Hệ số xe buýt = 1, cộng với tốc độ làm tài liệu vượt sức đọc của người khác

130 PR trong 6 ngày, `bang-chung-test.md` tới §64, `progress.md` và `roadmap.md` mỗi mục dài 300–600 ký tự
dày đặc tham chiếu chéo §. Tài liệu **nhất quán nội bộ** (có test giữ) nhưng **không đọc nổi từ ngoài**: một
kỹ sư mới cần nhiều ngày chỉ để biết bắt đầu từ đâu, và không có bản "5 trang" nào cho quản lý hay khách hàng.
Bằng chứng kiểm thử nên là *phụ lục tra cứu*, không phải dòng chảy chính; hiện nó là dòng chảy chính.

### 4.3 Bề mặt bảo trì lớn hơn năng lực nuôi

64 lệnh × (net48 / net8 / net10) × 8 phiên bản phần mềm chủ, cộng Python MCP, panel web Hermes, Ollama, Inno
Setup, `.mcpb`. Mỗi năm Autodesk đổi API (ElementId, CreateContainsRule, .NET) và hết hỗ trợ .NET 8 tháng 11 này.
Dự án đã có ma trận CI tốt, nhưng **CI chỉ chứng minh biên dịch được**; 22.000 dòng chạm API chỉ được kiểm trên
một máy có cài Revit. Không có doanh thu, cộng đồng hay đồng tác giả, bề mặt này sẽ mục dần từ mép: nhóm AutoCAD
kiểm chuẩn và panel Hermes đã được đánh dấu "không đầu tư thêm" — đó là dấu hiệu sớm.

### 4.4 Ma sát số một vẫn là tên riêng của dự án

Vòng đóng vai chỉ ra: 4 lệnh giá trị nhất (`SleeveAuto`, `HangerAuto`, `ElevationTag`, `AttributeIncrement`)
đều vấp tên family/tham số/tag. `ParameterDictionary` + `DictionaryLearn` + `FamilyStarter` là câu trả lời đúng
hướng, nhưng là **ba bước** thay vì một, và `FamilyStarter` dựng family "hình giữ chỗ" — kỹ sư MEP thật sẽ không
đặt sleeve giữ chỗ vào mô hình nộp. Chừng nào chưa có thư viện family/template chuẩn đi kèm bản cài, nhóm MEPF
vẫn là "chạy được trong demo".

### 4.5 Ba căng thẳng về định vị chưa được giải

- **Agent hay kỹ sư?** Roadmap đã nhận ra "sản phẩm mang hình dáng agent" và làm form động (9.1). Nhưng phần
  đầu tư lớn nhất còn lại (giai đoạn 10, "hướng khác biệt lớn nhất") lại là agent. Đối thủ nội địa thắng bằng UI +
  đào tạo; Autodesk và mã mở đang lấp agent. DHCB đang đứng giữa hai bên.
- **AI offline hay Claude qua MCP?** README vẫn mở đầu bằng "AI offline, không dữ liệu nào rời máy" trong khi
  roadmap đã hạ thông điệp này và panel AutoCAD gửi bản vẽ ra ngoài. Hai thông điệp trái nhau cùng tồn tại.
- **Tuân thủ pháp luật là tính năng có rủi ro pháp lý.** Giai đoạn 11 bám NĐ 207/2026 và 217/2026 với ghi
  chú "phải đọc bản gốc trên Công báo". Một dấu hoàn công sai một dòng là hồ sơ bị trả. Chưa có ai có chuyên môn
  pháp lý/QLDA rà soát.

### 4.6 Chưa sẵn sàng phân phối

Không có `LICENSE` (mặc định là *all rights reserved*, khách hàng doanh nghiệp sẽ hỏi). DLL chưa ký, SmartScreen
và chính sách IT sẽ chặn ở máy công ty. Token Bridge nằm ở `%APPDATA%` dạng file — đủ cho máy cá nhân, chưa đủ
khi IT hỏi. Không có gói nhập môn: dự án mẫu đi kèm, video 15 phút, một trang cho mỗi vai. Bảy `skills/` là mầm
tốt cho việc này nhưng viết cho agent, không cho người.

### 4.7 Nợ kỹ thuật cụ thể, nhỏ nhưng nên đóng trước khi có người dùng

- ~~`ScheduleExport` thành công một phần vẫn `Success=true` → báo cáo đêm hiện OK giả.~~ ✅ 2026-09-06:
  `CommandResult`/`RunLogEntry` có thêm `PartialSuccess`; `ScheduleExport` bật cờ này khi `0 < done <
  tổng`, và trả `Success=false` khi `done=0` (trước đây báo `Success=true` cả khi xuất được **0** schedule).
  `BatchReport` có ô màu vàng riêng ("Một phần"), `RunLog.ExitCode` trả 1 khi có step một phần — báo cáo
  đêm không còn im lặng gọi "OK" cho việc chưa xong hết. Xem `bang-chung-test.md` §66.
- ~~Batch Revit thoát bằng kill cứng (runner `Process.Kill(true)` sau tối đa 60s chờ), không xin Revit thoát êm.~~
  ✅ 2026-09-06: `BatchStartupHook` nay gọi `UIApplication.PostCommand(ExitRevit)` ngay sau khi ghi
  `batch-done.json`, xin Revit tự thoát ở vòng idle kế tiếp. Runner (`Program.Revit.cs`) giữ nguyên cơ chế
  chờ rồi kill cứng làm lưới an toàn — không đổi hành vi khi lệnh thoát êm thất bại vì lý do gì đó. **Đã
  chạy thật** (`run-in-revit-tests.ps1 -Suite smoke`, Revit 2024, 2026-09-06 22:38): journal ghi chuỗi
  thoát êm chuẩn (`ExitManagedInstance` → `ExitNativeInstance` → `Journal Exit`) 1,4 s sau khi ghi
  `batch-done.json`, console không in dòng cảnh báo phải kill cứng — xem `bang-chung-test.md` §67.
- `RvtFileInfo` đọc phiên bản bằng quét chuỗi 2 MB đầu file — giữ nguyên, chưa sửa: đây là lựa chọn có chủ đích
  (cùng cách RevitBatchProcessor dùng), không phải lỗi đang chờ; chỉ nên thay bằng parser OLE/CFBF đầy đủ nếu
  thực tế gặp file gây nhận sai phiên bản.
- ~~6 khối `catch {}` rỗng còn lại trong `src/`~~ ✅ 2026-09-06: cả 6 nay có bình luận giải thích lý do im
  lặng (đúng quy ước đã áp dụng cho 40+ khối khác từ §61) — xem `bang-chung-test.md` §66.
- `AutoRoute` mức C: đã đo được 1,00× trên 8/9 tuyến, nhưng vẫn phải chuẩn bị điểm bằng `SetoutExport` và bỏ
  Ducts khỏi `obstacleCategories` bằng tay — chưa phải một nút.

## 5. Tầm nhìn 24 tháng — ba chân trời

Nguyên tắc chọn: (1) làm thứ đối thủ **không muốn hoặc không thể** làm, (2) ưu tiên tầng thuần vì nó rẻ để kiểm và
chuyển được, (3) không thêm lệnh khi chưa có người đòi.

### Chân trời 1 (0–3 tháng): từ tự-kiểm sang được-kiểm

Mục tiêu duy nhất: **5 kỹ sư ở 2 công ty dùng 4 tuần, số liệu từ `UsageReport`, không phải từ tác giả.**

- **Đóng băng số lệnh.** Chỉ sửa lỗi và ma sát do người dùng thật báo. ✅ 2026-09-06: catalog đã có hai bậc
  công khai — `CommandDescriptor.Supported` (fluent `.Endorsed()`), đúng 14 lệnh: `WarningsExport`,
  `HealthReport`, `SheetRename`, `RevisionOnSheets`, `BatchExport`, `ClashDetection` (+BCF), `HangerAuto`,
  `SlopePipes`, `IdsValidate`, `ParameterRuleCheck`, `SetoutExport`, `LayerStandardCheck`,
  `AttributeIncrement`, `BlockQuantity`; Ribbon (`App.cs`) tự thêm ghi chú "Bậc thử nghiệm" vào tooltip cho
  lệnh còn lại. Quyết định *có đóng băng thêm lệnh mới hay không* vẫn chờ chốt ở mục 6.
- **Gói nhập môn cho người, không cho agent:** ✅ 2026-09-06: README nay mở đầu bằng mục *Bắt đầu cho kỹ sư*
  (cài → bấm 3 lệnh → thấy kết quả), đẩy Bridge/MCP/AI xuống mục riêng phía sau; bốn trang A4 một vai —
  [`vai-tro-kien-truc.md`](vai-tro-kien-truc.md), [`vai-tro-mep.md`](vai-tro-mep.md),
  [`vai-tro-bim-manager.md`](vai-tro-bim-manager.md), [`vai-tro-autocad.md`](vai-tro-autocad.md) — dẫn từ
  `tong-quan.md`. **Còn thiếu:** dự án mẫu Việt (có sheet, có MEP, có shared parameter thi công) đi kèm bản
  cài — cần dựng thật trong Revit, không sinh được bằng văn bản; và video 15 phút.
- **Sẵn sàng phân phối:** chọn giấy phép (đề xuất mã mở lõi thuần theo MIT/Apache, vỏ và installer giữ quyền
  hoặc cũng mở — quyết ở mục 6), ký DLL bằng chứng chỉ thật, ✅ `CommandResult` có trạng thái *một phần*
  (`PartialSuccess`, 2026-09-06 — xem §4.7).
- **Gom tài liệu:** giữ `bang-chung-test.md` làm phụ lục; ✅ `docs/tong-quan.md` (1 trang, 2026-09-06) là cửa
  vào duy nhất cho người ngoài, README trỏ tới ngay dòng đầu; đặt quy ước "mỗi PR tối đa một mục mới trong
  progress".
- **Chỉ số:** ≥ 5 người dùng có tên, ≥ 3 lệnh được dùng ≥ 3 ngày/tuần bởi ≥ 2 người, ≥ 10 lỗi/ma sát do người
  ngoài báo và đã đóng.

### Chân trời 2 (3–9 tháng): chọn một mũi nhọn và gọi tên sản phẩm bằng nó

Số liệu chân trời 1 quyết, nhưng đây là khuyến nghị nếu phải chọn hôm nay:

**Mũi nhọn: "DHCB kiểm và bàn giao BIM theo NĐ 217/207" — chạy được cả khi không có Revit.**

Lý do: cầu do luật tạo ra (BIM bắt buộc cấp II từ 01/7/2026, nộp dữ liệu BIM, hồ sơ hoàn thành công trình);
Autodesk sẽ không viết cho nghị định Việt Nam; đối thủ nội địa bán UI Revit, không bán cho chủ đầu tư; và DHCB
đã có 70 % phần khó nhất ở tầng thuần (IDS, IFC, HashChain, DossierIndex, HandoverPackage). Việc cần làm:

1. Tách `BatchRunner --verify-ifc/--verify-ids/--verify-log/--dossier` thành **một CLI độc lập** (`dhcb-kiem`),
   chạy trên máy không có Revit, đầu vào là IFC + IDS + thư mục hồ sơ, đầu ra là báo cáo HTML/PDF có thể đóng
   dấu. Đây là sản phẩm cho **chủ đầu tư, thẩm tra, giám sát** — tệp người dùng mới hoàn toàn.
2. Bộ IDS mẫu theo giai đoạn (báo cáo khả thi, thiết kế cơ sở, kỹ thuật, hoàn công) cho công trình dân dụng cấp
   II, có người làm QLDA/BIM manager thật rà soát. Đây là nội dung, không phải mã, và là thứ khách trả tiền.
3. `AsBuiltStamp` chỉ làm **sau khi** có luật sư/QLDA đối chiếu Phụ lục IIb bản gốc; ghi tên người rà soát vào tài
   liệu.
4. Agent (giai đoạn 10) **hạ xuống vai trò enabler**: giữ Bridge/MCP để agent gọi được các lệnh trên, hoàn thiện
   kịch bản "20 warning < 15 phút" như một demo bán hàng, không đầu tư thêm loại query mới trừ khi kịch bản đòi.
   Khi Autodesk mở MCP ghi cho 2027, DHCB không cần thắng ở đây.

Nếu số liệu chân trời 1 nói ngược (kỹ sư dùng MEPF nhiều, không ai quan tâm hồ sơ), mũi nhọn thay thế là
**"MEPF cho dự án Việt"**: thư viện family/template chuẩn đi kèm, `SleeveAuto`/`HangerAuto`/`SlopePipes` một nút,
BOM ra spool (mở lại P3 có điều kiện). Hai mũi nhọn này không làm song song.

### Chân trời 3 (9–24 tháng): từ add-in thành nền tảng

- **Một dòng nền tảng:** Revit 2027 + .NET 10 là đường chính; net48 đóng băng ở bản cuối, chỉ sửa lỗi nghiêm
  trọng. Bỏ bớt ma trận để dành sức.
- **Tầng thuần thành thư viện/dịch vụ:** `Shared.Logic` phát hành NuGet + CLI + tuỳ chọn Design Automation trên
  cloud cho đơn vị không muốn cài; CDE (bắt buộc cấp I) là nơi tích hợp đầu ra bàn giao, không phải thứ DHCB tự
  dựng.
- **Mô hình nuôi dự án:** quyết giữa (a) mã mở toàn bộ + bán đào tạo/hỗ trợ/IDS mẫu như cách đối thủ nội địa
  bán UI + đào tạo, hoặc (b) lõi mở, gói tuân thủ đóng. Không có mô hình thì chân trời 3 không tồn tại.
- **Đồng tác giả:** mục tiêu tối thiểu 1 người thứ hai merge được PR mà không hỏi tác giả, đo bằng việc người đó
  đóng 5 lỗi do người dùng báo.

## 6. Năm quyết định cần chốt ngay

| # | Quyết định | Khuyến nghị |
|---|---|---|
| 1 | Giấy phép | Mã mở `Shared.Logic` + `Shared.Hosting` (Apache-2.0); phần còn lại quyết cùng mô hình nuôi ở chân trời 3, nhưng **phải có một file LICENSE** trước khi giao cho công ty khác |
| 2 | Có đóng băng số lệnh 6 tháng không | Có. Nguyên tắc 6 đã nói vậy; nay thêm: không mở nhánh tính năng mới khi chưa có ≥ 1 người dùng ngoài yêu cầu |
| 3 | Tên gọi sản phẩm | Không phải "add-in 2-trong-1 có AI offline". Đề xuất: *công cụ kiểm, tự động hoá và bàn giao BIM cho dự án Việt* |
| 4 | Ai rà soát phần pháp lý | Một người QLDA/BIM manager có tên, trước khi `AsBuiltStamp` và IDS mẫu ra khỏi nhánh |
| 5 | Ngân sách tài liệu | Mỗi PR tối đa một mục progress mới; bằng chứng test chỉ ghi khi đổi kết luận, không ghi mỗi lượt chạy lại |

## 7. Chỉ số thay cho bảng hiện tại

Bảng "Chỉ số để biết đang đúng hướng" trong roadmap có 5 dòng, 4 dòng đo bằng máy của tác giả. Đề xuất thay:

| Chỉ số | Mốc 3 tháng | Mốc 9 tháng |
|---|---|---|
| Người dùng có tên, dùng ≥ 3 ngày/tuần | 5 ở 2 công ty | 20 ở 5 công ty |
| Lỗi/ma sát do người ngoài báo và đã đóng | 10 | 50 |
| Lệnh trong bậc *hỗ trợ* mà ≥ 2 người dùng hằng tuần | 5 | 10 |
| Hồ sơ bàn giao/IDS chạy trên dự án **không phải của tác giả** | 1 | 5, có 1 được cơ quan thẩm tra chấp nhận |
| Người thứ hai merge được PR | 0 → 1 | 2 |
| Thời gian một kỹ sư mới cài và chạy được 3 lệnh, không hỏi ai | ≤ 30 phút | ≤ 15 phút |

## 8. Việc nên dừng hẳn

- Chạy lại bộ ca kiểm trên cùng model để ghi thêm một § bằng chứng khi kết luận không đổi.
- Thêm loại query cho Bridge/MCP ngoài kịch bản demo đã chốt.
- Hỗ trợ Revit 2023 và AutoCAD 2024 ở bản mới sau khi .NET 8 hết hạn — giữ bản cuối, không phát triển tiếp.
- Panel web AutoCAD + Hermes: đã "không đầu tư thêm"; nên gỡ khỏi README chính để thông điệp dữ liệu nhất quán.
- Viết thêm playbook `skills/` cho agent trước khi có trang A4 cho người.
