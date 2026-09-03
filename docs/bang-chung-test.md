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
| `tests/DhcbTools.Shared.Logic.Tests` (xUnit, .NET 8) | 489 | ✅ 489 passed / 0 failed |
| `tools/autocad-mcp-server/test_panel_api.py` (unittest) | 29 | ✅ 29 passed / 0 failed |

```
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -c Release
Passed!  - Failed: 0, Passed: 489, Skipped: 0, Total: 489

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
