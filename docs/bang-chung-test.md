# DHCB Tools — Bằng chứng Build & Test

**Ngày:** 2026-09-02 · **Repo:** https://github.com/seeker19110/Dhcb-Tools
**Nhánh:** `fix/toan-bo-danh-gia` ([PR #21](https://github.com/seeker19110/Dhcb-Tools/pull/21))

> ⚠️ **Đính chính bản trước.** Bản ghi ngày 09:00 ICT báo *"28/28 unit test PASS"* dựa trên project
> `src/DhcbTools.Tests`. Project đó **không hề tham chiếu `DhcbTools.Shared.Logic`** — nó khai báo lại
> `UnitConverter`, `FileNameFormatter`… ngay trong file test rồi kiểm chính bản sao đó:
>
> ```csharp
> // Các class dưới đây mirror đúng logic trong Core để test isolated.
> static class UnitConverter { public static double MmToFeet(double mm) => mm / MmPerFoot; }
> ```
>
> 28 test ấy xanh kể cả khi mã nguồn thật hỏng hoàn toàn, và CI cũng chưa bao giờ chạy tới chúng
> (`tests.yml` chạy `tests/DhcbTools.Shared.Logic.Tests`, không nằm trong solution cũ). Project đã bị xoá;
> phần logic tương ứng đã có test thật trong `Shared.Logic.Tests`. Con số dưới đây đo trên mã nguồn thật.

---

## 1. Test tự động

| Bộ test | Số lượng | Kết quả |
|---|---|---|
| `tests/DhcbTools.Shared.Logic.Tests` (xUnit, .NET 8) | 569 | ✅ 569 passed / 0 failed |
| `tools/autocad-mcp-server/test_panel_api.py` (unittest) | 29 | ✅ 29 passed / 0 failed |

```
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -c Release
Passed!  - Failed: 0, Passed: 569, Skipped: 0, Total: 569

python -m unittest discover -s tools/autocad-mcp-server -p 'test_*.py'
Ran 29 tests — OK
```

Bốn bộ trong số này không kiểm logic mà **đối chiếu mã nguồn với mã nguồn**, nên bắt được lớp lỗi
"tài liệu nói một đằng, mã làm một nẻo" ngay trên CI Linux, không cần Revit/AutoCAD:

| Bộ | Đối chiếu |
|---|---|
| `RibbonCoverageTests` | Vỏ Revit ↔ `RevitCommandTable` (mọi lệnh có đường vào từ Ribbon) |
| `CatalogFieldTests` | `CommandCatalog` ↔ property của lớp `*Config` thật, và "lệnh ghi thì phải có `DryRun`" |
| `SuiteCoverageTests` | 42/42 lệnh Revit **và 15/15 lệnh AutoCAD** có ít nhất một ca kiểm chạy thật |
| `VietnameseMessageTests` | Không còn mẫu thông báo tiếng Anh trong Core |

`RibbonCoverageTests` đã kiểm bằng **mutation**: đổi hỏng một tên lớp trong `App.cs` thì test đỏ ngay,
nên nó bắt thật chứ không xanh suông. `CatalogFieldTests` thì chứng minh bằng chính lần đầu chạy —
nó tìm ra ngay 5 chỗ lệch có thật (xem §8).

---

## 2. Build — đúng bộ phiên bản mà `release.yml` đóng gói

Tất cả đều là build **sạch** (`--no-incremental`). Điều này quan trọng: build incremental bỏ qua
project đã dựng nên **giấu mất warning** — chính vì thế lần đo trước báo nhầm "0 warning".

| Cấu hình | TFM | Kết quả |
|---|---|---|
| Solution, Revit+AutoCAD 2025 | net8.0-windows | ✅ 0 error, 0 warning |
| Solution, Revit+AutoCAD 2024 | net48 | ✅ 0 error, 0 warning |
| `DhcbTools.Revit`, Revit 2023 | net48 | ✅ 0 error, 0 warning |
| `DhcbTools.Revit`, Revit 2024 | net48 | ✅ 0 error, 0 warning |
| `DhcbTools.Revit`, Revit 2025 | net8.0-windows | ✅ 0 error, 0 warning |
| `DhcbTools.AutoCAD`, AutoCAD 2024 | net48 | ✅ 0 error, 0 warning |
| `DhcbTools.AutoCAD`, AutoCAD 2025 | net8.0-windows | ✅ 0 error, 0 warning |

Solution nay gồm **9/9 project thật** (bản trước chỉ khai báo 5/10 và trỏ nhầm sang project test giả).

### Ba lỗi chặn phát hành, chỉ lộ ra khi build đủ phiên bản

CI cũ chỉ build nhánh 2025 (net8), nên ba lỗi dưới đây lọt qua — dù `release.yml` vẫn đóng gói 2023/2024:

| Lỗi | Ảnh hưởng |
|---|---|
| `DrawingCompareCommand` dùng `Dictionary.GetValueOrDefault` — không có trên net48 | Toàn bộ nhánh **AutoCAD/Revit ≤ 2024 không build được** |
| `ConnectorCheckerCommand` gọi thẳng `ElementId.Value` thay vì `RevitCompat.IdValue` | **Revit ≤ 2023 không build được** |
| `ParameterImportCommand` tách nhánh bằng `#if NET8_0_WINDOWS` — symbol **không bao giờ được định nghĩa** (TFM `net8.0-windows` sinh ra `NET8_0_WINDOWS7_0`) | Nhánh `long` luôn thắng → **Revit ≤ 2023 không build được** |

Đã sửa cả ba, và `tests.yml` nay chạy ma trận **2025 + 2024 + 2023** để không tái phát.

---

## 3. Gateway panel AutoCAD — kiểm chứng chạy thật

Chạy `panel_api.py` trên cổng riêng rồi gọi thật bằng `curl`:

| Yêu cầu | Mong đợi | Thực tế |
|---|---|---|
| `GET /alive` không token | 200, không mang dữ liệu | ✅ 200 `{"panelApi": "ok"}` |
| `GET /health` không token | 403 | ✅ 403 |
| `GET /health` có token | 200 | ✅ 200 |
| `GET /panel` không token | 200 (nơi phát token) | ✅ 200 |
| `POST /execute` lệnh ngoài whitelist | từ chối | ✅ `{"ok": false, "error": "command không hợp lệ"}` |

Và kiểm chứng lớp AI:

```
hermes argv    : ['hermes', '--ignore-rules', '-t', '', '-z', ...]
toolsets empty : True      ← model không duyệt web / không chạy lệnh / không đọc file
no web toolset : True
planner fenced : True      ← dữ liệu DWG nằm trong khối <du_lieu>
```

---

## 4. Phạm vi — cái gì đã kiểm, cái gì chưa

### ✅ Kiểm bằng test tự động (không cần Revit/AutoCAD)

Toàn bộ `Shared.Logic`: đánh số, đặt tên file, CSV, HTML, xác thực Bridge, batch/RunLog, hình học
lưới trục, MEP (route graph, device pattern, duct/pipe sizing, path finder 3D), rule checker,
lớp AI offline; cộng phủ Ribbon và gateway panel.

### ✅ Đã chạy thật trên AutoCAD

**15/15 lệnh** có ca kiểm tự động chạy qua `accoreconsole` — §10. Trước đó, kiểm tay trên bản vẽ thật
6.759 entity / 171 layer: `/health`, 6 loại `query`, `LayerExport`, `DrawingCleanup` (dryRun) và
`AutoNumbering` **ghi thật 21/21 block** — [`bang-chung-test-autocad-live.md`](bang-chung-test-autocad-live.md).

### ⬜ Chưa chạy thật

| Nhóm | Ghi chú |
|---|---|
| Lệnh **tạo phần tử mới** ở đường ghi thật | `SleeveAuto`, `HangerAuto`, `LevelSetup`, `SheetBatchCreate`… mới chạy xem trước; đường ghi (§11) hiện phủ 4 lệnh có phép nghịch đảo để tự khôi phục |
| Batch chạy đêm đầu-cuối trên dự án thật | Đã chạy được cả hai nhánh (Revit §7–§8, AutoCAD §9) trên model/bản vẽ mẫu; còn thiếu một đêm thật trên dự án thật |

Quy trình kiểm thử tay: [`huong-dan-cai-dat-va-kiem-thu-thu-cong.md`](huong-dan-cai-dat-va-kiem-thu-thu-cong.md).

---

## 5. Vì sao bản trước sai

Ba cơ chế khiến bằng chứng cũ đẹp hơn thực tế — ghi lại để không lặp lại:

1. **Test trên bản sao.** `src/DhcbTools.Tests` kiểm logic chép tay trong chính file test, nên xanh
   bất kể mã nguồn thật ra sao.
2. **Build incremental giấu warning.** Project đã dựng bị bỏ qua, warning không hiện lại. Mọi con số
   trong tài liệu này đều đo bằng `--no-incremental`.
3. **CI chỉ phủ một phiên bản.** Chỉ build 2025 nên mọi lỗi riêng của net48 / Revit ≤ 2023 đều lọt.

Nguyên tắc từ nay: **con số nào vào tài liệu này thì phải kèm lệnh tái lập được**, và test phải tham
chiếu mã nguồn thật chứ không kiểm bản sao.

---

## 6. Revit 2024 thật — vòng kiểm thử đầu tiên (2026-09-02 23:18 ICT)

Máy: Windows 11, Revit 2024.3, add-in build `-c Release -p:RevitVersion=2024` (net48) copy vào
`%APPDATA%\Autodesk\Revit\Addins\2024`. Model: `Snowdon Towers Sample Architectural.rvt` (mẫu kèm Revit).
Kịch bản theo `huong-dan-cai-dat-va-kiem-thu-thu-cong.md` §5.1.

| # | Việc | Kết quả |
|---|---|---|
| R1 | Tab DHCB Tools, 6 panel, không hộp thoại lỗi | ✅ (Revit hỏi "Unsigned Add-In" → Always Load) |
| R2 | `bridge-token.txt` 43 ký tự | ✅ |
| R3 | `dhcb_agent.py revit tools` liệt kê 42 lệnh | ❌ → ✅ sau sửa: console Windows cp1252 vỡ ký tự `○/✎` (`UnicodeEncodeError`); script nay ép stdout/stderr UTF-8 |
| R4 | `GET /health` 200, chỉ status/app/version | ✅ (`version` đang là `0.0.0.0` — chưa đặt AssemblyVersion) |
| R5 | `POST /execute` không token → 401 | ✅ |
| R6 | 5 lần sai token rồi token đúng → 429 `locked` | ✅ (mở khoá sau ~4 phút) |
| R7 | `query document_info` | ✅ title, projectNumber, warningCount 34, linkCount 6 |
| R8 | Chỉ bind 127.0.0.1 | ✅ `netstat`: `127.0.0.1:8765 LISTENING` |
| R14 | HealthReport từ Ribbon | ✅ HTML 11.9 KB trong Documents, mở trình duyệt |
| — | HealthReport qua Bridge (`exec HealthReport`) | ❌ → ✅ sau sửa: config JSON không có `outputPath` → `required` không chặn được null qua Newtonsoft → `ArgumentNullException: path`. Core nay tự đặt `Documents\DHCB_Health_<title>_<time>.html` |
| R12 | RemoveUnusedViews xem trước qua Bridge | ✅ liệt kê 90 view/sheet, không ghi (dry-run mặc định) |

### 6.1 Lệnh nền tảng R9–R13 (cùng máy, cùng model, qua Bridge `dhcb_agent.py`)

| # | Lệnh | Kết quả |
|---|---|---|
| R9 | ParameterExport Doors · Mark/Level/Width | ✅ 142 phần tử, CSV UTF-8 BOM, số dấu chấm. Ghi nhận: Width xuất theo đơn vị nội bộ (feet) kiểu `3.0000000000000004` — chưa quy đổi mm |
| R10 | ParameterImport sửa 3 ô Mark | ❌ → ✅ sau sửa: bản cũ xem trước báo "cập nhật 277 giá trị" dù CSV chỉ đổi 3 ô (ghi đè lại mọi ô, kể cả tham số Type dùng chung). Core nay bỏ qua ô trùng giá trị; sau sửa xem trước = 3, ghi thật = 3, xuất lại đối chiếu đúng 3 phần tử đổi; cột Level chỉ đọc được báo trong Messages |
| R11 | AutoNumbering Doors · Mark · D- · pad 3 | ✅ 141/142 đánh D-001…D-141 trái→phải; 1 cửa (id 1447958) bị bỏ qua — cần xem lý do (không có Location/Level?) |
| R12 | RemoveUnusedViews xem trước → thật → Ctrl+Z | ✅ xem trước 90, xoá 90, Ctrl+Z trong Revit trả lại đủ 90. Ghi nhận: sau khi xoá, xem trước lần 2 còn 1 ứng viên phát sinh (sheet vừa rỗng) |
| R13 | BatchExport PDF+DWG mẫu `{SheetNumber}-{SheetName}` | ❌ → ✅ sau sửa: bản cũ gọi Export cả lô nên Revit tự đặt `Sheet-Cover.pdf` và `<Dự án>-Sheet - G000 - Cover.dwg`, bỏ qua mẫu tên, hai sheet trùng tên ghi đè nhau, DWG tách mỗi view thành xref riêng. Core nay xuất từng sheet (`Combine=true`+`FileName`, `MergedViews=true`, `MakeUnique`) → `S000-Cover Sheet.pdf` / `.dwg` |

Chưa chạy: R15+ (giai đoạn 7 P1) và MEPF.

---

## 7. Batch trong Revit — vòng chạy tự động đầu tiên (2026-09-03 09:51 ICT)

Máy: Windows 11, Revit 2024.3. Chạy bằng một lệnh, **không ai đụng vào máy**:

```powershell
.\scripts\run-in-revit-tests.ps1 -Suite smoke -RevitVersion 2024
```

Revit tự mở → chạy 12 ca → tự đóng → sinh `report.html`, `in-revit-tests.md`, `in-revit-tests.trx`.

**Kết quả: mã thoát 0 — 11 đạt / 0 trượt / 1 bỏ qua trên 12 ca.**

| Ca | Lệnh | Kết quả | ms |
|---|---|---|---:|
| Đọc thông tin mô hình | `HealthReport` | ✅ | 1827 |
| Xuất tham số ra CSV | `ParameterExport` | ✅ 142 phần tử, 2 tham số | 37 |
| Xuất cảnh báo ra CSV | `WarningsExport` | ✅ 34 warning, 10 loại | 9 |
| Kiểm kê family | `FamilyAudit` | ✅ 286 family | 64 |
| Xem trước dọn view thừa | `RemoveUnusedViews` | ✅ sẽ xoá 90 | 28 |
| Xem trước đánh số cửa | `AutoNumbering` | ✅ 141 cửa | 33 |
| Xem trước đổi tên sheet | `SheetRename` | ✅ 16/55 sheet | 72 |
| Tô màu theo tham số | `ColorByParameter` | ✅ báo lỗi rõ (batch không có view đang mở) | 17 |
| Xem trước purge style | `StylePurge` | ✅ 105 style thừa | 144 |
| Xuất schedule | `ScheduleExport` | ✅ 36/36 schedule | 6453 |
| Kiểm connector hở | `ConnectorChecker` | ✅ | 40 |
| Sleeve — hiệu năng | `SleeveAuto` | ⭐ bỏ qua (model kiến trúc không có MEP) | 0 |

### Ba lỗi chặn, chỉ lộ ra khi chạy thật

Trước vòng này, **batch chạy đêm chưa từng chạy trọn một lần nào** dù đã đánh dấu xong từ giai đoạn 1.
Không lỗi nào trong ba lỗi dưới đây bị 448 test thuần bắt được.

| # | Lỗi | Cách phát hiện |
|---|---|---|
| 1 | Journal có `Jrn.Directive "DocSymbol", "[]"` — cần document đang mở để bind, mà lúc khởi động chưa có. Revit ghi *"no DocumentStorage available to bind"*, coi journal sai nhịp và **dừng playback ở dòng 6**, rồi treo ở một hộp thoại 10 phút rưỡi | Đọc journal Revit ghi ra |
| 2 | Runner luôn báo "chưa cài add-in" dù add-in đã cài đúng | Đi tìm nhầm hướng mất thời gian |
| 3 | **Revit khởi động bằng journal chỉ nạp add-in có `.addin` cùng thư mục với journal** (Autodesk cố ý, để add-in lạ không xen vào khi chạy kiểm thử hồi quy). Add-in bị bỏ qua **im lặng**: không lỗi, không hộp thoại, Revit ngồi im tới hết giờ | Đếm add-in trong journal: phiên tương tác 48, phiên journal 38 — không add-in bên thứ ba nào |

Lỗi 3 là nguyên nhân gốc. Cách tách biến: chạy `Revit.exe /nosplash` không journal → add-in nạp được;
thêm journal → không. Đã thử ký số DLL trước đó — **không giải quyết được** lỗi này (nhưng vẫn cần cho
việc phát hành, xem [`kiem-thu-trong-revit.md`](kiem-thu-trong-revit.md)).

### Hai vấn đề nhỏ phát hiện kèm

- `ColorByParameter` trong batch không có view đang mở nên báo lỗi. Đây là hành vi **đúng** sau các bản
  sửa ở giai đoạn 8.1 — trước đó lệnh loại này âm thầm không làm gì mà vẫn báo thành công. Bộ ca kiểm nay
  chốt đúng hành vi này.
- Báo cáo hiện ra tiếng Việt vỡ trên console. File trên đĩa **đúng UTF-8**; chỉ `Get-Content` của Windows
  PowerShell 5.1 mặc định đọc cp1252. Cùng họ với lỗi `dhcb_agent.py` ở §6.

### Còn lại

Bộ `revit-mep.json` đã chạy — xem §8.

---

## 8. Phủ đủ 42/42 lệnh Revit (2026-09-03 11:00 ICT)

Máy: Windows 11, Revit 2024.3. Ba bộ ca kiểm, ba model mẫu kèm Revit, chạy nối tiếp bằng một vòng lặp
`run-in-revit-tests.ps1`, **không ai đụng vào máy**:

| Bộ | Model mẫu | Kết quả | Mã thoát |
|---|---|---|---:|
| `revit-smoke.json` | Snowdon Towers Sample Architectural | **27 đạt / 0 trượt / 1 bỏ qua** trên 28 ca | 0 |
| `revit-mep.json` | Snowdon Towers Sample HVAC | **17 đạt / 0 trượt** trên 17 ca | 0 |
| `revit-plumbing.json` | Snowdon Towers Sample Plumbing | **8 đạt / 0 trượt** trên 8 ca | 0 |

Cộng lại **52 ca đạt, 0 trượt**, phủ **42/42 lệnh Revit** — chỉ số "42/42 trước v1.0" của
[`roadmap.md`](roadmap.md) nay đạt. `SuiteCoverageTests` (chạy trên CI, không cần Revit) giữ cho con số
này không trôi: thêm lệnh mà quên ca kiểm là CI đỏ.

Vài số đo đáng ghi: `HealthReport` 1.958 ms · `ScheduleExport` 36/36 schedule 6.644 ms ·
`ParameterRuleCheck` 1.484 giá trị + 6 ngưỡng 194 ms · `ClashDetection` 93 va chạm 543 ms ·
`SlopePipes` kiểm 1.794 ống 47 ms · `SystemBom` 6.348 phần tử 897 ms · `SleeveAuto` quét
1.053 phần tử MEP × tường/sàn 223 ms (ngưỡng 30 s).

### Bảy lỗi, không lỗi nào bị 481 test thuần bắt được

| # | Lỗi | Hậu quả thật | Đã sửa |
|---|---|---|---|
| 1 | `RunTestsCommand` thay token trên **chuỗi JSON đã serialize** rồi mới parse. Token `{suiteFolder}` trả về `C:\Users\…`, mà `\U` không phải escape JSON hợp lệ | Cả lượt chạy chết ngay ca đầu với `Bad JSON escape sequence: \U`, 27 ca còn lại không chạy lần nào | `JobTokens.ExpandIn` đi theo từng giá trị của cây JSON (dùng chung với batch runner); 4 test |
| 2 | Khối chuẩn bị config nằm **ngoài** `try` của từng ca | Một ca config hỏng giết cả lượt thay vì trượt một mình | Đưa vào trong `try` |
| 3 | `ProjectInfoConfig` **không có** `DryRun` trong khi catalog vẫn chào trường đó | Hai lớp khoá "bộ test không bao giờ ghi vào model mẫu" vô hiệu **im lặng** với riêng lệnh này — Newtonsoft không tìm thấy property nào để gán | Thêm `DryRun` (mặc định bật) + `CatalogFieldTests.LenhGhiCuaRevit_DeuCoDryRun` chốt cho cả nhóm lệnh ghi |
| 4 | `SystemColorConfig.Colors` là `required` nhưng Newtonsoft dựng object bằng reflection nên đi vòng qua `required` của compiler | Gọi thiếu `colors` → `NullReferenceException` trần trụi ném ra Bridge/agent | `RequiredConfig` kiểm sau khi deserialize, trả `E-CONFIG-MISSING: thiếu trường bắt buộc … "colors"`; bảng dispatch đổi `ConfigException` thành `CommandResult.Fail`; 4 test |
| 5 | `SleeveCommand` và `HangerCommand` mỗi lớp có một bản sao `FindFamilySymbol` **không khớp nhau**: bản của Sleeve không nhận tên family | Truyền tên family (đúng như tên trường `sleeveFamilyName` và ví dụ `M_Generic Model` trong tài liệu) thì `SleeveAuto` **không bao giờ** tra ra — cùng một tên, Hanger chạy được còn Sleeve báo lỗi | Gộp về `RevitCompat.FindFamilySymbol` (type / family / "Family: Type"). Sau khi sửa, SleeveAuto chạy trọn vòng quét: 15 ms → 223 ms |
| 6 | `PipeSplitter` chỉ nhận tên category **số ít** và **phân biệt hoa thường**; tên lạ bị bỏ **im lặng** | `categories: ["Pipes"]` trên model có 1.794 ống → "không có phần tử MEP nào phù hợp để cắt", không phân biệt được với model rỗng thật | Bảng dùng chung `RevitCompat.MepCurveCategories` (số ít + số nhiều, không phân biệt hoa thường) và **báo tên không nhận ra**. Sau khi sửa: cắt 143 phần tử, 189 điểm cắt |
| 7 | `HangerCommand` gặp tên category lạ thì âm thầm rơi về **toàn bộ** category mặc định | Gõ sai một tên → lệnh chạy sai phạm vi mà vẫn báo thành công | Dùng chung `ResolveMepCategories`, trả tên sai ra ngoài |

Lỗi 3, 5, 6, 7 cùng một họ với nhóm đã dọn ở giai đoạn 8.1: **không phải crash, mà là báo thành công
trong khi không làm đúng việc**. Đây cũng là lý do bộ ca kiểm nay có hẳn nhóm ca *"báo lỗi rõ khi…"* —
đường lỗi được chốt chặn ngang với đường thành công.

### Bốn lỗi lệch tài liệu ↔ mã nguồn, tìm bằng test đối chiếu (không cần Revit)

| # | Lệch | Hậu quả |
|---|---|---|
| 1 | `CommandCatalog` khai `FamilyLoader.familyPaths`, config thật là `familyFolder`/`familyNames` | Form động (giai đoạn 9.1) dựng một ô nhập **không dây vào đâu cả**; MCP chào một trường mà lệnh bỏ qua |
| 2 | `SizingProposal.maxVelocityMs` — config thật có `maxDuctVelocityMs`/`maxPipeVelocityMs` | Như trên |
| 3 | `DrawingCleanup` khai ba trường `purgeUnusedTextStyles`/`purgeUnusedDimStyles`/`purgeRegApps` mà `CleanupConfig` không có, và lệnh không hề purge ba thứ đó (mục 7.12 chưa từng viết) | `jobs/autocad-nightly.sample.json` **đang dùng** hai trường này — job đêm chạy mỗi tối mà không purge gì |
| 4 | `RibbonCoverageTests` miễn trừ `ProjectInfo` với lý do "có vỏ riêng", nhưng vỏ đó **không tồn tại** | Test báo "phủ đủ 42/42" trong khi Ribbon chỉ với tới 41 lệnh |

Cả bốn đều được chốt bằng test đọc thẳng mã nguồn (`CatalogFieldTests`, `RibbonCoverageTests`), chạy trên
CI Linux, không cần Revit/AutoCAD.

### Còn lại sau §8

- **AutoCAD chưa có bộ ca kiểm tự động** tương đương bên Revit. `DrawingCleanup` (gồm phần purge sâu mới
  viết) đã chạy thật qua `accoreconsole` — xem mục ngay dưới — nhưng 15 lệnh AutoCAD chưa được phủ theo
  kiểu khai báo kỳ vọng như bên Revit. Đây là khoảng trống lớn nhất còn lại.
- `SleeveAuto` chạy trọn vòng quét nhưng ra 0 sleeve trên model HVAC mẫu (model không có family Generic
  Model nào; ca kiểm mượn family `HeatRecoveryUnit` để đi hết đường nặng). Cần một model có family sleeve
  thật để chốt số lượng đặt được.
- `AutoRoute` trên model kiến trúc mẫu chạm giới hạn 400.000 ô sau 3,3 s và **báo rõ lý do** — đúng hành
  vi mong muốn, nhưng chưa có ca nào chứng minh đường tìm-ra-tuyến trên model thật.

---

## 9. Batch AutoCAD — vòng chạy thật đầu tiên (2026-09-03 11:04 ICT)

Máy: Windows 11, **AutoCAD 2026.1** (R25.1.179 → .NET 10). Hai bản vẽ mẫu kèm AutoCAD
(`Data Extraction and Multileaders Sample.dwg`, `Floor Plan Sample.dwg`), chạy bằng `BatchRunner` →
`accoreconsole` → `NETLOAD DhcbTools.AutoCAD.Core.dll` → `DHCB_RUN`, không mở giao diện AutoCAD.

**Lỗi chặn — batch AutoCAD chưa từng chạy được lần nào.** `AcadScriptGen` sinh
`DHCB_RUN "step.json" "run.jsonl" "a.dwg"` trên **một dòng**, trong khi `DHCB_RUN` hỏi ba prompt riêng.
Script AutoCAD coi mỗi **dòng** là một lần Enter, nên cả ba tham số bị nuốt vào prompt đầu tiên:

```
' The filename, directory name, or volume label syntax is incorrect. :
  'C:\Users\...\"C:\...\001-00-LayerExport.json" "C:\...\run.jsonl" "C:\...\mau.dwg"'
```

Đúng một họ với lỗi journal của Revit ở §7: **giai đoạn 1 đánh dấu xong từ lâu cho cả hai nền tảng,
nhưng chưa nền tảng nào chạy trọn một lần.** Đáng chú ý là hàm `AcadScriptGen.PlotPdf` ngay bên cạnh đã
viết đúng ("mỗi tham số một dòng") — chỉ dòng `DHCB_RUN` sai. Đã sửa, có test chốt chặn thứ tự bốn dòng
và đường dẫn có dấu cách.

**Kết quả sau khi sửa (mã thoát 0):**

| Bản vẽ | Lệnh | Kết quả | ms |
|---|---|---|---:|
| `mau.dwg` | `LayerExport` | ✅ 70 layer → CSV | 53 |
| `mau.dwg` | `DrawingCleanup` (xem trước, purge sâu) | ✅ 10 đối tượng thừa | 30 |
| `floorplan.dwg` | `LayerExport` | ✅ 29 layer → CSV | 41 |
| `floorplan.dwg` | `DrawingCleanup` (xem trước, purge sâu) | ✅ 3 đối tượng thừa | 29 |

Đây cũng là lần đầu **purge sâu (text style / dim style / regapp)** chạy thật — mục 7.12 của khảo sát
thị trường, trước đó `CommandCatalog` chào ba trường `purgeUnusedTextStyles`/`purgeUnusedDimStyles`/
`purgeRegApps` mà `CleanupConfig` không có và lệnh không hề làm; `jobs/autocad-nightly.sample.json` vẫn
đang truyền hai trong ba trường đó mỗi đêm. Rác tìm được là rác thật của add-in cũ: `AVE_FINISH`,
`AVE_GLOBAL`, `RAK`, `CONTENT*` (Content Explorer), `AFM10`/`AFM50` (Autodesk Fabrication).

**Một quyết định phải đổi sau khi nhìn số liệu thật:** vòng đầu còn đề nghị xoá `AcadAnnoAV`,
`AcadAnnoPO`, `AcadAnnotativeDecomposition` — dữ liệu nội bộ của tính năng annotative. Về lý thuyết
purge được (không entity nào mang XData của chúng), nhưng cái đáng dọn là rác bên thứ ba, không phải vài
byte của Autodesk. `CleanupDecider.IsSystemRegApp` nay giữ lại mọi tên bắt đầu bằng `ACAD`/`AcDb`;
sau khi sửa, danh sách của `mau.dwg` còn 10 mục.

**Còn lại:** `DrawingCleanup` mới chạy ở chế độ **xem trước**; chưa có lượt xoá thật trên bản vẽ có
xref/annotative để chốt phần `Erase()`.

---

## 10. Phủ đủ 15/15 lệnh AutoCAD (2026-09-03 11:32 ICT)

Khoảng trống lớn nhất sau §8 — "AutoCAD chưa có bộ ca kiểm tự động" — nay đã lấp. Cơ chế đối xứng hoàn
toàn với bên Revit: lệnh Core `RunTests` của `Core.AutoCAD` chạy qua `accoreconsole`, dùng **chung tầng
đánh giá** `Shared.Logic/Testing` (`TestSuite`, `TestExpectation`, `TestReport`) với Revit.

```powershell
.\scripts\run-in-autocad-tests.ps1
```

| Bộ | Bản vẽ mẫu | Kết quả | Mã thoát |
|---|---|---|---:|
| [`autocad-smoke.json`](../tests/suites/autocad-smoke.json) | Data Extraction and Multileaders Sample (kèm AutoCAD 2026) | **18 đạt / 0 trượt trên 18 ca**, phủ **15/15 lệnh** | 0 |

`SuiteCoverageTests` nay đòi cả hai nền tảng: thêm một lệnh AutoCAD mà quên ca kiểm là CI đỏ, y như bên Revit.

### Hai lỗi, cùng một họ với lỗi đã sửa bên Revit

Vòng chạy đầu tiên ra 14/17. Ba ca trượt: một là kỳ vọng sai của chính bộ test (bản vẽ cơ khí không có
layer `AXIS`, nên `GridExtract` **đúng** khi báo lỗi), hai còn lại là lỗi thật:

| Lệnh | Lỗi | Hậu quả |
|---|---|---|
| `LayerImport` | Mở **mọi** layer trong CSV ở chế độ ghi rồi gán lại y nguyên giá trị cũ | Nhập lại chính file vừa xuất vẫn báo "cập nhật 70 layer" — không phân biệt được với việc kỹ sư sửa thật 70 layer, và làm bẩn drawing (dirty flag, một mục undo) mà không đổi gì |
| `AttributeImport` | Như trên, với 50 attribute; và nhánh xem trước **đánh rơi toàn bộ `Messages`** đã gom | Kỹ sư xem trước không thấy ô nào sẽ đổi |

Đây đúng là lỗi đã sửa cho `ParameterImport` bên Revit ở PR #29 — cùng một hình dạng, ở hai chỗ khác chưa
ai soi tới. Phát hiện được là nhờ **ca vòng tròn**: xuất ra CSV rồi nhập lại chính file đó, kỳ vọng
`maxAffected: 0`.

Sửa kèm hai thứ lộ ra khi đọc lại `LayerImport`: nó **bỏ qua hoàn toàn cột Linetype và Lineweight** dù
header CSV và tài liệu đều nói có (sửa nét đứt trong Excel rồi nhập lại thì không có gì xảy ra, lệnh vẫn
báo thành công), và nó tự viết bộ tách CSV riêng thay vì dùng `CsvText` đã có test, đọc file không theo
UTF-8 BOM như lúc xuất.

### Ca song sinh — vì sao `maxAffected: 0` một mình là chưa đủ

Một mình ca vòng tròn vẫn xanh nếu ai đó làm `LayerImport` **luôn** trả 0. Bộ AutoCAD vì thế có thêm ca
*"nhập CSV đổi đúng một ô"* (`minAffected: 1, maxAffected: 1`) với fixture chép từ chính bản vẽ mẫu, đổi
màu một layer. Hai ca cạnh nhau mới chứng minh phép so sánh **phân biệt được**:

```
Nhập lại chính CSV vừa xuất        → [Xem trước] Sẽ cập nhật 0 layer
Nhập CSV đổi đúng một ô            → [Xem trước] Sẽ cập nhật 1 layer
```

### Còn lại sau §10

- `GridExtract` và `TextReplace` mới chốt được đường lỗi/không-khớp: bản vẽ mẫu kèm AutoCAD không có layer
  trục và không có chuỗi cần thay. Đường thành công cần một bản vẽ dự án thật.
- `DrawingCompare` mới so bản vẽ với chính nó; chưa có cặp bản vẽ khác nhau thật để chốt số liệu khác biệt.

---

## 11. Đường ghi thật (2026-09-03 12:07 ICT)

Tới hết §10, **mọi ca kiểm đều chạy ở chế độ xem trước**. Nhưng phần đáng lo nhất của một lệnh nằm ở đoạn
sau `transaction.Commit()`, mà một bộ test chỉ xem trước thì không bao giờ chạm tới. Hai bộ `*-write.json`
lấp chỗ đó.

**Ba lớp khoá, phải đủ cả ba mới ghi:** ca khai `"allowWrite": true` · người chạy bật `-AllowWrites` ·
script **chép file mẫu sang thư mục kết quả và chạy trên bản chép** (model/bản vẽ gốc nằm trong
`Program Files`, hỏng là phải cài lại phần mềm). Thiếu `-AllowWrites` thì script dừng có thông báo, thay
vì chạy bộ `write` ở chế độ xem trước rồi báo xanh — một lượt như thế là dối.

| Bộ | File chạy | Kết quả | Mã thoát |
|---|---|---|---:|
| [`revit-write.json`](../tests/suites/revit-write.json) | Bản chép Snowdon Towers Architectural | **7 đạt / 0 trượt trên 7 ca** | 0 |
| [`autocad-write.json`](../tests/suites/autocad-write.json) | Bản chép Data Extraction and Multileaders Sample | **5 đạt / 0 trượt trên 5 ca** | 0 |

### Chuỗi tự chứng minh — không so file vàng

Ca xếp thành chuỗi để **kết quả ca sau chứng minh ca trước đã ghi thật**, và chuỗi tự trả file về trạng
thái ban đầu nên chạy lại bao nhiêu lần cũng được:

```
Xuất Mark cửa — bản gốc            → Đã xuất 142 phần tử
Đánh số cửa — GHI THẬT             → Đã đánh số 141/141 phần tử "Doors"      (902 ms)
Nhập lại CSV gốc — GHI THẬT        → Đã cập nhật 141 giá trị tham số         (373 ms)
Nhập lại lần nữa                   → Đã cập nhật 0 giá trị tham số           (3 ms)
Ghi thông tin dự án — GHI THẬT     → Đã ghi 2 trường
Ghi lại y hệt                      → Đã ghi 0 trường
```

- Con số **141** ở bước khôi phục là bằng chứng bước đánh số **đã đổi model thật**: nếu nó chỉ xem trước,
  Mark trong model không đổi và bước này sẽ không có gì để khôi phục.
- Con số **0** ở bước kế là bằng chứng bước khôi phục đã ghi, và đường ghi **idempotent**.
- Cặp `ProjectInfo` 2 → 0 chốt thêm điều mà xem trước không chốt được: transaction đã **commit thật**,
  không phải rollback.

Bên AutoCAD cùng một hình dạng, dùng `LayerImport` với fixture đổi đúng một ô: `1 → 0 → 1 (khôi phục) → 0`.

### Kỳ vọng mới: `summaryNotContains`

Thêm vào tầng đánh giá (`Shared.Logic/Testing`, có test thuần) để ca ghi chốt được **"đây không phải bản
xem trước"**. Nếu một ngày khoá `dryRun` bị ép nhầm cho cả ca ghi, ca sẽ đỏ thay vì lặng lẽ xanh — đúng
loại "test xanh mà không kiểm gì" mà cả bộ này sinh ra để tránh.

### Còn lại sau §11

- Đường ghi mới phủ 4 lệnh (`AutoNumbering`, `ParameterImport`, `ProjectInfo`, `LayerImport`) — là những
  lệnh có sẵn phép nghịch đảo để tự khôi phục. Các lệnh **tạo phần tử mới** (`SleeveAuto`, `HangerAuto`,
  `LevelSetup`, `SheetBatchCreate`…) chưa có ca ghi thật vì chưa có cách xoá lại gọn gàng trong cùng phiên.
- Vẫn chưa có một đêm batch chạy trên **dự án thật** thay vì file mẫu.

---

## 12. Đường ghi cho nhóm lệnh **tạo phần tử mới** (2026-09-03 12:45 ICT)

§11 chỉ phủ được những lệnh có **phép nghịch đảo** (xuất ra rồi nhập lại). Nhóm lệnh tạo phần tử mới không
có phép đó: tạo xong không xoá lại được gọn gàng trong cùng phiên. Chỗ này giải bằng **tính idempotent**
thay vì khôi phục — chạy lệnh hai lần, lần hai phải tạo **0** cái và nói rõ "đã có":

```
Tạo tầng — GHI THẬT              → Đã tạo 2 tầng. [Tạo] DHCB-WRITE-L1 @ 24000 · [Tạo] DHCB-WRITE-L2 @ 27600   (67 ms)
Tạo lại y hệt                    → Đã tạo 0 tầng. [Bỏ qua, đã có] DHCB-WRITE-L1 · [Bỏ qua, đã có] DHCB-WRITE-L2 (10 ms)
Tạo sheet hàng loạt — GHI THẬT   → Đã tạo 2/2 sheet, đặt 0 view                                                (375 ms)
Tạo lại bộ sheet đó              → Đã tạo 0/0 sheet — "DHCB-TEST-01 đã tồn tại"                                (13 ms)
```

Con số **0** ở mỗi bước hai chính là bằng chứng bước một đã **commit thật**: nếu transaction bị rollback
thì tầng/sheet không tồn tại, và bước hai lại tạo ra đúng 2 cái nữa. Đây cũng là lý do đủ để không cần
dọn: bản chép bị bẩn sau lượt chạy là chấp nhận được vì nó nằm trong thư mục kết quả.

**Kết quả:** [`revit-write.json`](../tests/suites/revit-write.json) — **11 đạt / 0 trượt trên 11 ca**
(7 ca của §11 + 4 ca mới), mã thoát 0, trên bản chép Snowdon Towers Architectural, Revit 2024.3.

### `HangerAuto` phải sửa trước khi kiểm được

Bộ ghi cho MEP chạy trên model HVAC ([`revit-write-mep.json`](../tests/suites/revit-write-mep.json), gọi
bằng `-Suite write-mep`) lộ ra một lỗi nghiệp vụ thật, không phải lỗi test: **`SleeveAuto` chống trùng từ
đầu, còn `HangerAuto` thì không** — chạy lệnh lần hai là hanger chồng đúng lên hanger cũ, và người dùng
không có cách nào biết ngoài việc tự đếm.

Sửa: `HangerAuto` nay bỏ qua vị trí đã có hanger cùng family trong bán kính `existingToleranceMm`
(mặc định 100 mm, cùng dung sai với `SleeveAuto`), tắt được bằng `skipExisting: false`. Phép so khoảng
cách tách ra `MepLayout.IsNearAny` ở `Shared.Logic` để hai lệnh dùng chung và có test thuần (6 ca).
Lệnh còn tính cả những vị trí vừa lên kế hoạch trong chính lượt chạy, nên hai đoạn ống nối nhau không
sinh hai hanger chồng ở chỗ nối.

### Bộ ghi cho MEP: `HangerAuto` 1120 → 0

[`revit-write-mep.json`](../tests/suites/revit-write-mep.json), bản chép Snowdon Towers HVAC,
**4 đạt / 0 trượt trên 4 ca**:

```
Đặt hanger — GHI THẬT        → Đã đặt 1120 hanger trên 1053 phần tử MEP                    (30,5 s)
Đặt hanger lần hai           → Đã đặt 0. Bỏ qua, đã có hanger: 1120 vị trí                 (51 ms)
```

**1120 → 0** là bằng chứng cùng dạng với cặp `ProjectInfo` 2 → 0 ở §11, nhưng cho nhóm lệnh tạo phần tử:
transaction bị rollback thì model không có hanger nào và lần hai lại đặt đúng 1120 cái nữa.

Hai ca `SleeveAuto` trong cùng bộ **chưa chứng minh được gì**: trên model mẫu lệnh không tìm thấy giao cắt
MEP × tường/sàn nào nên cả hai lần đều "đã đặt 0 sleeve" — con số 0 ở lần hai không nói lên điều gì. Giữ
lại vì chúng vẫn chạy trọn nhánh ghi mà không ném, nhưng ghi rõ là **nợ**: cần một model có giao cắt thật
(hoặc fixture dựng sẵn) mới chốt được. Thử chạy bộ này trên model Plumbing thì lệnh dừng ngay ở bước tra
family — model đó không có `HeatRecoveryUnit`.

### Lỗi chặn mà chỉ đường ghi trên model MEP mới lộ ra: batch treo ở hộp thoại cảnh báo

Lượt chạy `write-mep` đầu tiên **treo 15 phút không chạy nổi một ca nào**, không log, không báo cáo.
Journal của phiên đó chỉ ra chỗ đứng im:

```
' 6:< Error dialog summary
' 6:< Warning: Space is not in a properly enclosed region - 67 times
Jrn.Data "Error dialog" , "0 failures, 0 errors, 67 warnings"
'C 12:56:54.355; 6:< ADialog::doModal start
```

Revit tính lại Space **lúc mở model** HVAC và bật hộp thoại cảnh báo — nằm **ngoài mọi transaction của
lệnh DHCB**, nên `SilentFailuresPreprocessor` gắn theo từng transaction không bao giờ chạm tới. Batch
đứng chờ người bấm nút, đến hết `--max-minutes` mới chết. Model kiến trúc không có cảnh báo lúc mở, nên
7 vòng chạy trước đó không hề lộ ra chuyện này.

Sửa: `BatchStartupHook` đăng ký `Application.FailuresProcessing` cho **cả phiên batch** (gỡ ở `finally`),
dùng chính `SilentFailuresPreprocessor` với `FailurePolicy.Silent` — cảnh báo bị nuốt nhưng vẫn ghi vào
`CoreContext.SuppressedWarnings` để báo cáo thấy được. Đây là chỗ duy nhất phủ được transaction không
phải của mình.

### Lưới mới: ca ghi phải chốt "không phải xem trước"

`SuiteCoverageTests.CaGhiThat_PhaiChotKhongPhaiXemTruoc` — mọi ca khai `allowWrite` bắt buộc có
`summaryNotContains: ["Xem trước"]`. Thiếu lưới này thì một ngày khoá `dryRun` bị ép nhầm cho cả ca ghi,
cả bộ write sẽ xanh trong khi không ghi gì.

---

## 13. Lệnh chạy nền và `GET /progress/<id>` (2026-09-03 13:35 ICT)

Giai đoạn 10.5 phần còn lại. Trước đây mọi lệnh qua Bridge đều giữ một kết nối HTTP suốt thời gian chạy;
với `HangerAuto` 26,6 s (§12) hay `AutoRoute` trên hộp lớn, kết nối đứt giữa chừng là **mất kết quả của
một việc đã chạy xong** — Revit đã ghi vào model nhưng client không còn cách nào biết.

Nay `POST /execute` nhận thêm `"async": true`: server trả ngay `202` kèm `id`, lệnh chạy tiếp trên luồng
UI, client hỏi `GET /progress/<id>` tới khi xong. Kết quả nằm ở server theo id nên hỏi lại bao nhiêu lần
cũng được (giữ 30 phút hoặc 50 lệnh gần nhất; **không bao giờ** bỏ lệnh đang chạy).

Chạy thật trên Revit 2024.3, model Snowdon Towers Architectural:

```
POST /execute {"command":"AutoNumbering","async":true,…}
  → 202 { "id": "b81fc5256cac", "status": "running", "progressUrl": "/progress/b81fc5256cac" }

GET /progress/b81fc5256cac
  → { "status": "done", "elapsedMs": 94,
      "result": { "success": true, "affectedCount": 141,
                  "summary": "[Xem trước] Sẽ đánh số 141 phần tử \"Doors\"…" } }

GET /progress/b81fc5256cac   (hỏi lại hai lần nữa)
  → kết quả y hệt, elapsedMs vẫn 94 — kết quả không đi theo kết nối
```

`GET /progress/<id>` sai id → **404**; không token → **401** (kiểm chứng bằng curl trên máy thật, cùng
lượt chạy). Client `dhcb_agent.py --background` tự hỏi vòng và in tiến độ:

```
$ python scripts/dhcb_agent.py revit exec HealthReport --config '{"outputPath":"…"}' --background
  … đang chạy 0 s
  … đang chạy 2 s
✓ Health Report: 34 cảnh báo, 91 view chưa đặt, 0 connector hở, 2 in-place family.
```

`result` có **đúng hình dạng** của `/execute` đồng bộ (kể cả `changedIds` của giai đoạn 10.2) — đường
đồng bộ nay cũng dựng payload bằng chính hàm đó, nên agent không phải viết hai đường đọc.

Phần thuần (`BridgeJob`, `BridgeJobStore`) có 7 test, đường HTTP có 3 test chạy trên server thật trong
tiến trình test — gồm cả ca "lệnh còn đang chạy thì `/progress` phải trả `running`".

---

## 14. `SleeveAuto` không nhìn thấy model liên kết — 0 → 345 (2026-09-03 14:40 ICT)

Hai ca `SleeveAuto` ở §12 "xanh" mà không chứng minh được gì: lệnh báo *"Đã đặt 0 sleeve"* trên cả hai
lượt. Ghi là nợ với giả định "model mẫu không có giao cắt". **Giả định đó sai.**

Model Snowdon Towers HVAC **không có tường/sàn nào của riêng nó** — tường nằm ở model kiến trúc **liên
kết**. `SleeveCommand` chỉ dựng `FilteredElementCollector` trên `document`, nên không thấy gì để giao
cắt, trả về 0 và **báo thành công**. Đây đúng là cách hồ sơ Việt Nam được tổ chức (file MEP link file
kiến trúc), nghĩa là lệnh gần như **chưa từng dùng được trên dự án thật** mà vẫn luôn xanh.

### Sửa

- `includeLinkedModels` (mặc định **bật**) + `linkNameContains`: quét tường/sàn của mọi
  `RevitLinkInstance` đã nạp, kèm `GetTotalTransform()` để đưa về toạ độ file chủ. Link xoay thì hộp bao
  dựng lại từ **tám đỉnh**, không chỉ hai điểm min/max.
- Host nằm trong link thì **không host được** (Revit không cho tạo family instance bám mặt phần tử của
  link) — đặt tự do tại điểm giao và nói rõ trong Summary, thay vì lặng lẽ đặt kiểu khác.
- `ElementIntersectsSolidFilter` chỉ áp cho host **cùng file**: nó so trong một document, đưa phần tử
  của link vào là sai kết quả.
- Số 0 không còn trơ trọi: Summary nói luôn *"Không có tường/sàn nào để xét, kể cả trong model liên
  kết — kiểm lại link đã nạp chưa"* hoặc *"Đã xét N tường/sàn trong file + M từ model liên kết trên K
  phần tử MEP nhưng không có giao cắt nào"*. Báo cáo batch chỉ in Summary, nên lời giải thích nằm trong
  `Messages` là lời giải thích không ai đọc.

### Đo trên model thật (Revit 2024.3, Snowdon Towers HVAC + link kiến trúc)

| | Trước | Sau khi đọc link | Sau khi tối ưu |
|---|---:|---:|---:|
| Sleeve tìm được | **0** | **345** | **345** |
| Thời gian | 0,2 s | **49,8 s** ❌ | **1,2 s** ✅ |

49,8 s là ngưỡng chết: Bridge mặc định chờ 30 s. Nguyên nhân lộ ra ngay từ vòng đo — `get_BoundingBox`
gọi lại cho **từng** ứng viên host bên trong vòng lặp **từng** phần tử MEP (1.053 MEP × hàng nghìn
tường/sàn của link). Nay hộp bao tính **một lần** lúc dựng ứng viên, ở toạ độ file chủ: **nhanh 41 lần**,
kết quả không đổi.

Bộ `revit-mep.json`: **17 đạt / 0 trượt trên 17 ca**, ca sleeve nay chốt `minAffected: 1` (chống hồi quy
"0 sleeve vì không đọc link") và `maxMs: 10000` (chống hồi quy hiệu năng).

### Đường ghi của `SleeveAuto` — trả nợ luôn trong cùng ngày

Bộ ghi chạy trên **bản chép**, mà link của Snowdon lưu theo đường dẫn tương đối nên cạnh bản chép không
có file kiến trúc — Revit không giải được link. Sửa ở `run-in-revit-tests.ps1`: bản chép nay **giữ nguyên
tên gốc**, nằm trong thư mục `ban-chep/` riêng, và script **chép luôn các model được liên kết** (dò tên
`*.rvt` ngay trong file, chỉ chép những file có thật cạnh model gốc — sáu file Snowdon, 313 MB).

Với bước đó, đường ghi thật của `SleeveAuto` chạy được lần đầu tiên:

```
Đặt sleeve — GHI THẬT      → Đã đặt 334 sleeve. Trong đó 334 cái bám tường/sàn
                             của model liên kết nên đặt tự do.               (5,9 s)
Đặt sleeve lần hai         → Đã đặt 0 sleeve. Bỏ qua, đã có sleeve: 552 vị trí. (2,1 s)
```

**334 → 0** là bằng chứng lần một đã **commit thật**, cùng dạng với `HangerAuto` 1120 → 0.

Bộ `revit-write-mep.json` sau khi đổi: **4 đạt / 0 trượt trên 4 ca** (hanger 1120 → 0, sleeve 334 → 0).

Giá phải trả: nạp sáu model liên kết làm vòng chạy chậm hẳn (`HangerAuto` 26 s → 63 s), nên **chỉ bộ ghi
mới chép link**, không áp cho bộ xem trước.

### Một lỗi diễn đạt lộ ra ngay ở vòng đó

Lần hai báo *"…không có giao cắt nào (thường do lệch cao độ hoặc hostTypeNames lọc quá chặt)"* — **sai**.
Giao cắt còn nguyên; chỉ là đã có sleeve ở đó rồi. Thông báo tự tin mà sai còn tệ hơn không có thông báo:
người đọc sẽ đi tìm một nguyên nhân không tồn tại. Nay lệnh đếm số vị trí bỏ qua và nói đúng —
*"Bỏ qua, đã có sleeve: N vị trí."*, giống `HangerAuto`. Ca kiểm chốt cả hai chiều: phải chứa
`"Bỏ qua, đã có sleeve"` **và không được chứa** `"không có giao cắt nào"`.

---

## 15. Quét hồi quy sau 10 PR trong một ngày (2026-09-03 16:50 ICT)

Ngày 03/09 có mười PR vào `main`, trong đó ba cái đụng vào đường chạy chung: `SleeveCommand` (đọc model
liên kết), `BatchStartupHook` (nuốt cảnh báo cả phiên), `HttpBridgeServer` (lệnh chạy nền). Con số
"42/42 lệnh Revit có test chạy thật" chỉ có giá trị nếu **đo lại** sau đợt đó, không phải trích lại từ
lần đo cũ — nên chạy hết mọi bộ ca kiểm trên `main` sau khi merge PR cuối.

| Bộ | Model / bản vẽ | Kết quả |
|---|---|---|
| Revit `smoke` | Snowdon Architectural | **27 đạt / 0 trượt / 1 bỏ qua** trên 28 |
| Revit `mep` | Snowdon HVAC (+ link kiến trúc) | **17 / 0** trên 17 |
| Revit `plumbing` | Snowdon Plumbing | **8 / 0** trên 8 |
| Revit `write` (ghi thật) | bản chép Architectural | **11 / 0** trên 11 |
| Revit `write-mep` (ghi thật) | bản chép HVAC + 6 link | **4 / 0** trên 4 |
| AutoCAD `smoke` (accoreconsole) | bản vẽ mẫu | **18 / 0** trên 18 |
| AutoCAD `write` (ghi thật) | bản chép bản vẽ mẫu | **5 / 0** trên 5 |

**90 ca đạt / 0 trượt / 1 bỏ qua trên 91 ca**, phủ 42/42 lệnh Revit và 15/15 lệnh AutoCAD.

Ca bỏ qua là `SleeveAuto` trong bộ `smoke` — model kiến trúc không có hệ MEP, ca thật của nó nằm ở bộ
`mep`; lý do ghi trong `skipReason` và `SuiteCoverageTests` chốt rằng mọi ca bỏ qua đều phải có lý do.

---

## 16. Cùng lỗi ở `ClashDetection` — 0 → 7 va chạm (2026-09-03 18:25 ICT)

Sau §14, câu hỏi tự nhiên là: **lệnh nào khác cũng mù model liên kết?** Rà `FilteredElementCollector`
trên category kiến trúc/kết cấu tìm ra ngay `ClashDetection` — và đây là ca nặng hơn `SleeveAuto`:

> Một báo cáo va chạm nói *"không có va chạm"* là kết luận người ta **tin và làm theo**.

Bằng chứng có sẵn trong chính báo cáo cũ: ca *"Dò va chạm nội bộ"* (`Ducts × Structural Framing`) trên
model HVAC báo **0 va chạm** và xanh, vì dầm nằm ở model kết cấu liên kết.

### Sửa

- Nhóm B lấy thêm từ mọi `RevitLinkInstance` đã nạp. **Category phải tra trong chính link** — id
  category là của từng document, dùng id của file chủ là không khớp gì cả.
- Lọc tinh vẫn là **solid × solid**, không rơi về hộp bao: đưa solid của A về toạ độ link bằng
  `SolidUtils.CreateTransformed(solid, transform.Inverse)` rồi lọc bằng `ElementIntersectsSolidFilter`
  ngay trong document của link (`ElementIntersectsElementFilter` chỉ so trong một document).
- Hộp bao của phần tử link dựng từ **tám đỉnh** — link xoay thì lấy hai điểm min/max qua phép biến đổi
  là sai.
- `0 va chạm` nay luôn kèm cơ sở: xét bao nhiêu phần tử, từ file hay từ link, hoặc "không có phần tử
  nhóm B nào để xét — kiểm lại link đã nạp chưa".

### Đo trên model thật (Snowdon HVAC + link kết cấu)

| | Trước | Sau |
|---|---:|---:|
| Va chạm tìm được | **0** | **7** (đều với model liên kết) |
| Thời gian | 31 ms | 1.023 ms |

Ca kiểm nay chốt `minAffected: 1` và `maxMs: 60000`. Bộ `mep`: **17/17**; bộ `smoke` (clash `Walls ×
Doors` cùng document) vẫn **27/0/1** — nhánh cùng-file không hồi quy.

### Rà nốt: còn lệnh nào mù model liên kết?

| Lệnh | Kết luận |
|---|---|
| `SleeveAuto` | ✅ đã sửa (§14) |
| `ClashDetection` | ✅ đã sửa (§16) |
| `AutoRoute` | ⬜ **còn mù** — quét vật cản (`Walls`, `Floors`, `StructuralFraming`, `StructuralColumns`) chỉ trong file đang mở, nên tuyến đề xuất có thể xuyên qua dầm/tường bên link. Đang gắn nhãn *thử nghiệm* theo roadmap nên chưa sửa vội, nhưng phải sửa trước khi bỏ nhãn đó |
| `DevicePlacement` | ⬜ **còn mù** — đọc `OST_Rooms` trong file đang mở; hồ sơ tách file thì Room nằm ở model kiến trúc. Trên file MEP thuần, lệnh sẽ không thấy phòng nào |
| `ConnectorChecker`, `ParameterRuleCheck`, `HealthReport` | Không cần: chúng chỉ xét phần tử của chính file, đúng ý nghĩa |

Hai lệnh còn mù đã ghi vào [`progress.md`](progress.md) mục *Còn mở* — sửa theo cùng khuôn
(`includeLinkedModels` + biến đổi toạ độ + nói rõ nguồn), nhưng cần model mẫu có Room/vật cản bên link
để chốt bằng số thật, không sửa mò.

---

## 17. `DevicePlacement` — 0 phòng → 44 phòng, và cái bẫy tên family (2026-09-03 18:40 ICT)

Ở §16 tôi ghi `DevicePlacement` là "còn mù nhưng chưa có model để đo". **Nhận định đó sai**: Snowdon
HVAC link thẳng model kiến trúc, mà Room nằm đúng bên đó — vậy là có model để đo, chỉ là ca kiểm cũ
dừng quá sớm để chạm tới. Ca cũ dùng family không tồn tại (`DHCB-KHONG-CO-FAMILY`) nên lệnh thoát ngay
ở bước tra family; phần quét phòng **chưa từng chạy lần nào**.

### Hai lỗi, không phải một

**1. Mù model liên kết.** Room chỉ được quét trong document đang mở, nên trên file MEP thuần lệnh dừng ở
*"Không có phòng nào khớp bộ lọc"* — câu đó **đổ lỗi cho bộ lọc** trong khi vấn đề là chỗ tìm. Nay:

- quét Room từ mọi `RevitLinkInstance` đã nạp (`includeLinkedModels`, mặc định bật);
- **biên phòng đưa về toạ độ file chủ** — bỏ bước này thì thiết bị rơi lệch đúng bằng độ lệch gốc của
  link, sai kiểu khó phát hiện hơn nhiều so với không chạy;
- câu báo lỗi phân biệt ba tình huống: không có phòng ở đâu cả · có phòng nhưng bộ lọc loại hết ·
  `includeLinkedModels` đang tắt.

**2. Hai lệnh cùng sản phẩm hiểu tên family khác nhau.** `HangerAuto`/`SleeveAuto` dùng
`FindFamilySymbol` nên nhận **tên family**; `DevicePlacement` dùng `FindType` vốn chỉ nhận **tên type**
hoặc `Family: Type`. Cùng chuỗi `"HeatRecoveryUnit"`: chỗ chạy, chỗ báo "không tìm thấy (đã load chưa?)".
Nay `FindType` thử theo thứ tự *đúng tên type → `Family: Type` → chứa trong tên type → **tên family***,
và lệnh nói rõ đã chọn type nào — tra theo tên family có thể ra nhiều type, người dùng phải thấy cái
thực sự được dùng.

Lỗi thứ hai lộ ra vì ca kiểm mới **trượt**. Nếu sửa ca cho khớp mã thay vì hỏi vì sao, cái bẫy vẫn còn
nguyên cho người dùng.

### Đo trên model thật (Snowdon HVAC + link kiến trúc)

| | Trước | Sau |
|---|---:|---:|
| Phòng tìm được | **0** (lệnh thoát) | **44** |
| Thiết bị lên kế hoạch | — | **551** |
| Thời gian | — | 267 ms |

Bộ `mep`: **18/18**.

### Còn lại

`AutoRoute` vẫn mù vật cản bên link (tuyến đề xuất có thể xuyên dầm/tường của model liên kết). Nó đang
mang nhãn *thử nghiệm* theo roadmap; phải sửa trước khi bỏ nhãn đó.

---

## 18. `AutoRoute` — 30 → 546 vật cản, và giới hạn thật của bộ tìm đường (2026-09-03 18:50 ICT)

Lệnh cuối cùng trong họ "mù model liên kết". Vật cản (dầm, cột, tường, sàn) nằm ở model kết cấu/kiến
trúc liên kết, nên A* chạy trong một không gian gần như trống và **luôn tìm được tuyến** — tuyến xuyên
thẳng qua dầm.

### Sửa

Cùng khuôn với §14/§16/§17, thêm một chi tiết riêng: **hộp tìm kiếm phải đưa về toạ độ link trước khi
lọc** (`BoundingBoxIntersectsFilter` chạy trong document của link), rồi hộp bao từng vật cản đưa ngược
về toạ độ file chủ bằng cả tám đỉnh.

Số vật cản nay nằm trong **Summary** chứ không chỉ `Messages`: báo cáo batch chỉ in Summary, mà "tuyến
đẹp" tìm trong không gian trống là kết quả vô nghĩa **trông y hệt** kết quả tốt. Khi không có vật cản
nào, lệnh nói thẳng *"tuyến này chỉ là đường nối hai điểm"*.

### Đo trên model thật (Snowdon HVAC + link kiến trúc/kết cấu)

| | Trước | Sau |
|---|---:|---:|
| Vật cản trong hộp tìm kiếm | **30** (chỉ ống/duct của chính file) | **546** = 30 trong file + **516 từ link** |

### Và ngay lập tức lộ ra giới hạn thật của bộ tìm đường

Có vật cản thật thì bài toán khác hẳn:

| Bước lưới | Kết quả | Node | Thời gian |
|---|---|---:|---:|
| 100 mm (mặc định) | chạm trần 400.000 ô, không ra tuyến | 400.001 | 17,9 s |
| 500 mm | *"Không có đường đi trong hộp tìm kiếm"* | 4.818 | 0,3 s |

Bộ tìm đường **phân biệt đúng** hai tình huống (`Điểm đầu/cuối nằm trong chướng ngại` là một lý do
riêng), nên kết quả trên nghĩa là: hai điểm tự do nhưng bị bao kín trong hộp tìm kiếm — sàn và tường
của model liên kết chặn hết, đúng như thực tế một toà nhà.

**Không "chữa" bằng cách dò toạ độ may mắn cho ra tuyến đẹp.** Ca kiểm chỉ chốt thứ đo được: có vật cản
đến từ link, không ném, thời gian có trần. Chất lượng tuyến — tránh đúng chỗ, cao độ hợp lý — phải người
có nghề nhìn.

### Đối chiếu trước/sau trên bộ `smoke` — bản vá không làm hỏng gì

| | Trước bản vá | Sau |
|---|---|---|
| Vật cản xét tới | 153 | **561** (153 trong file + 408 từ link) |
| Kết quả | chạm trần 400.000 ô | chạm trần 400.000 ô |
| Thời gian | 6,1 s | 18,5 s |

Ca đó **chưa bao giờ tìm được tuyến**, cả trước lẫn sau — giới hạn có sẵn của bộ tìm đường với bước
100 mm, không phải hệ quả của bản vá. Bản vá chỉ làm nó xét đúng số vật cản, và chậm hơn ba lần.

Đây cũng là số liệu để giữ nhãn *thử nghiệm* của `AutoRoute` trong roadmap: giờ có lý do đo được thay vì
cảm tính. Muốn dùng thật thì cần chọn điểm đầu/cuối trong cùng không gian trần kỹ thuật và cho đủ
`searchMarginMm`, hoặc chấp nhận bước lưới thô.

## 19. Bộ tìm đường `AutoRoute` — 4049 ms → 10 ms, và thất bại biết nói (2026-09-03 20:10 ICT)

§18 chốt được số vật cản (30 → 546) nhưng để lại ba thứ chưa động tới, cả ba đều là lỗi thuần logic —
không cần Revit để thấy:

| # | Lỗi | Hậu quả đo được ở §18 |
|---|---|---|
| 1 | `Blocked()` quét tuyến tính cả danh sách hộp cho **từng ô** | 546 hộp × 400.000 ô × 6 hướng × 2 lượt ≈ 2,6 tỉ phép thử = 17,9 s |
| 2 | Heuristic Manhattan **bỏ qua `TurnPenalty = 20`** | Ước lượng thấp hơn chi phí thật cả chục lần → A* thoái hoá gần thành Dijkstra → chạm trần 400.000 ô |
| 3 | Thất bại chỉ nói *"Không có đường đi"* | Không phân biệt **bị kết cấu bịt kín** với **hết ngân sách tìm kiếm** — hai thứ chữa khác hẳn nhau |

### Sửa

1. **Raster hoá chướng ngại** vào lưới bit một lần trước khi chạy (`OccupancyGrid`): chi phí tỉ lệ với
   *thể tích vật cản* thay vì *số ô × số vật cản*, tra ô bị chặn còn O(1). Điều kiện chặn giữ **nguyên
   định nghĩa cũ** — tâm ô nằm trong hộp đã nới `clearance` — nên không có tuyến nào đổi nghĩa.
2. **Heuristic cộng phạt rẽ**: Manhattan + (số trục còn phải đi − 1) × `TurnPenalty`, cộng thêm một lần rẽ
   nếu hướng đang đi không trùng chiều với trục còn lại. Vẫn là **chặn dưới** của chi phí thật nên A* giữ
   nguyên tính tối ưu — ca kiểm cũ vẫn ra đúng 2 lần rẽ.
3. **Chẩn đoán bằng flood-fill** khi thất bại: nói thẳng điểm đầu ra tới bao nhiêu ô trống, và hai điểm
   **có nối thông nhau không**. Thêm `MaxCells` (mặc định 16 triệu ô): hộp quá lớn so với bước lưới thì
   **từ chối ngay** thay vì chạy 18 giây rồi mới báo thua.

### Đo — chạy đúng bản cũ cạnh bản mới trên cùng dữ liệu

550 vật cản đặt ngẫu nhiên (đúng quy mô §18), lưới 100 mm, hộp 10×10×3 m, khoảng hở 100 mm:

| | Trước | Sau |
|---|---:|---:|
| Thời gian | **4.049 ms** | **10 ms** |
| Ô mở rộng | 58.720 | **5.783** |
| Tuyến | tìm được | tìm được (cùng kết quả) |

Ca hành lang một tường, lưới 100 mm: 15.465 → **9.884** ô, 14 → 7 ms, **cùng 2 lần rẽ** — heuristic mạnh
hơn nhưng không đánh đổi chất lượng tuyến.

### Ca kiểm mới — [`PathFinder3DGridTests.cs`](../tests/DhcbTools.Shared.Logic.Tests/PathFinder3DGridTests.cs)

7 ca, trong đó ca quan trọng nhất là **đối chiếu vét cạn**: 40 lượt với hộp đặt ngẫu nhiên, trải lại mọi
ô giữa hai đỉnh polyline và khẳng định không ô nào rơi vào vật cản đã nới khoảng hở — raster hoá mà lệch
một ô so với cách cũ thì tuyến xuyên dầm. Còn lại: heuristic không quét cả lưới, tuyến vẫn tối ưu, bịt kín
báo đúng là bịt kín, hết ngân sách báo đúng là hết ngân sách, lưới quá lớn từ chối dưới 1 giây, và quy mô
550 vật cản xong trong vài giây.

Bộ `Shared.Logic`: **569 đạt / 0 trượt** (562 → 569), cả bộ chạy trong 0,2 s. `check-build.sh` xanh.

### Cái này vẫn KHÔNG chứng minh

Chất lượng tuyến trên model thật — tránh đúng chỗ, cao độ hợp lý — vẫn phải người có nghề nhìn, đúng như
§18 đã nói. Bản vá này chỉ gỡ ba thứ **đo được là sai**: chậm, mù hướng, và câm khi thua. Nhãn *thử
nghiệm* của `AutoRoute` giữ nguyên cho tới khi chạy lại trên Snowdon HVAC.

### Chạy lại trên model thật sau bản vá (2026-09-03 19:14 ICT)

`run-in-revit-tests.ps1 -Suite mep` trên `Snowdon Towers Sample HVAC.rvt` + link kiến trúc/kết cấu:
**19 đạt / 0 trượt / 0 bỏ qua trên 19 ca**. Ca `AutoRoute` chạy ở bước lưới 500 mm (đúng cấu hình ca kiểm
từ §18 — bước 100 mm mặc định đã chạm trần nên ca kiểm dùng 500):

| | §18 (trước vá) | Sau vá |
|---|---|---|
| Vật cản | 546 = 30 trong file + 516 từ link | **546** — không đổi, đúng như phải thế |
| Thời gian | 0,3 s | **82 ms** |
| Ô mở rộng | 4.818 | 4.965 |
| Kết luận | *"Không có đường đi trong hộp tìm kiếm"* | *"điểm đầu chỉ ra tới **782 / 12.025 ô**"* — nói rõ là bị bịt kín |

**Số ô mở rộng KHÔNG giảm, và đó là kết quả đúng.** Khi tuyến không tồn tại, A* buộc phải vét cạn cả
khoang trống chứa điểm đầu — heuristic dù mạnh tới đâu cũng không rút ngắn được việc chứng minh "không có
đường". Con số tự nó khớp: 782 ô trống × 6 hướng = 4.692 trạng thái, cộng vài mục cũ trong hàng đợi ra
4.965. Cái giảm được là **thời gian cho mỗi ô** (raster hoá): 0,3 s → 82 ms, nhanh 3,7 lần.

Và đây là lần đầu §18 được **xác nhận bằng số** thay vì suy đoán: giả thuyết "hai điểm tự do nhưng bị sàn
và tường của model liên kết bao kín" nay có con số 782/12.025 ô đứng sau — flood-fill đi hết khoang chứa
điểm đầu và không chạm tới điểm cuối.

Bộ `mep` cũng lên **19 ca** (§14 còn 17), tất cả xanh — bản vá không làm hỏng lệnh MEP nào khác.

**Chưa đo lại ở bước 100 mm** trên model thật (số 17,9 s của §18). Ca kiểm cố định 500 mm nên vòng này
không chạm tới; muốn có con số đó phải thêm một ca riêng.
