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
| `tests/DhcbTools.Shared.Logic.Tests` (xUnit, .NET 8) | 345 | ✅ 345 passed / 0 failed |
| `tools/autocad-mcp-server/test_panel_api.py` (unittest) | 29 | ✅ 29 passed / 0 failed |

```
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -c Release
Passed!  - Failed: 0, Passed: 345, Skipped: 0, Total: 345

python -m unittest discover -s tools/autocad-mcp-server -p 'test_*.py'
Ran 29 tests — OK
```

Trong đó `RibbonCoverageTests` (4 test) đối chiếu vỏ Revit với `RevitCommandTable`. Đã kiểm bằng
**mutation**: đổi hỏng một tên lớp trong `App.cs` thì test đỏ ngay (`344 passed, 1 failed`), nên nó
bắt thật chứ không xanh suông.

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

Xem [`bang-chung-test-autocad-live.md`](bang-chung-test-autocad-live.md) — AutoCAD 2026, bản vẽ thật
6.759 entity / 171 layer: `/health`, 6 loại `query`, `LayerExport`, `DrawingCleanup` (dryRun) và
`AutoNumbering` **ghi thật 21/21 block**.

### ⬜ Chưa chạy thật

| Nhóm | Ghi chú |
|---|---|
| **Toàn bộ lệnh Revit (42 lệnh)** | Chưa có vòng kiểm thử nào trên Revit thật — rủi ro lớn nhất còn lại |
| 11 lệnh AutoCAD thêm sau (`AttributeExport/Import`, `TextReplace`, `LayerStandardCheck`, `GridExtract`, `XrefAudit`, `LayerTranslate`, `DrawingCompare`, `BlockQuantity`, `AttributeIncrement`, `CadLayerMap`) | Có mã nguồn, biên dịch xanh, chưa chạy trên AutoCAD thật |
| Batch chạy đêm đầu-cuối | `BatchStartupHook` mới viết, cần một đêm chạy thật trên máy có license |

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

Chưa chạy: R9–R11, R13, R15+ (cần config/CSV riêng).
