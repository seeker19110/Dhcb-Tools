# Audit toàn diện — 2026-10-01

Mốc trước sửa: `c8891b0` (sau PR #170). Phạm vi: toàn repo — tầng thuần (`Shared.Logic`), hạ tầng Bridge
(`Shared.Hosting`), Core Revit/AutoCAD, hai vỏ, BatchRunner, script Python (client Bridge, MCP server, panel gateway),
script PowerShell, CI/CD, installer và tài liệu. Không lặp lại các mục đã có trong
[audit 2026-09-23](audit-nang-cap-2026-09-23.md) và [audit 2026-09-28](audit-bao-mat-2026-09-28.md).

Cách làm: đọc mã theo đường dữ liệu từ ngoài vào (request Bridge, file job, file IFC/IDS của bên thứ ba, stdin của MCP
server, config lệnh) và theo các đường **xoá/ghi đè** (lệnh dọn dẹp, lưu batch). Mỗi lỗi được **tái hiện trước khi
sửa** — bằng chương trình thật khi chạy được ở đây, hoặc bằng test viết trước và thấy đỏ trên mã cũ. Công cụ: .NET SDK
8.0.131 + 10.0.112, pwsh 7.6.6, `actionlint` 1.7.12, `zizmor` 1.30.1, `pip-audit`. Không có Revit/AutoCAD ở máy audit:
phần chạm API Autodesk chỉ kiểm được bằng biên dịch với API NuGet 2023–2027 — mục "Cần chạy lại trong Revit/AutoCAD"
ghi rõ phần đó.

## Đã sửa

| # | Mức | Chỗ | Vấn đề | Sửa |
|---|---|---|---|---|
| 1 | Cao | `RemoveUnusedViews` (Core Revit) | Lệnh xoá — cả hai cờ bật mặc định, chạy được trong batch đêm — xoá cả thứ **đang nằm trên sheet** theo ba đường: (a) view CHÍNH của mặt bằng chia vùng không có viewport nên bị coi là thừa, mà Revit xoá view chính là xoá luôn mọi view phụ thuộc, kể cả cái đang trên sheet; (b) panel schedule nằm trên sheet bằng `PanelScheduleSheetInstance` chứ không bằng viewport → bị coi là "chưa đặt"; (c) `ViewSheet.GetAllPlacedViews` **không trả schedule** (tài liệu API ghi rõ: *"Schedules on the sheet are not returned by this method"*) → sheet chỉ có bảng thống kê, ghi chú, ảnh bị coi là "rỗng" | Quyết định tách xuống tầng thuần `ViewCleanupPlanner` (có test): view chính chỉ bị xoá khi mọi view phụ thuộc cũng bị xoá. Vỏ Revit: tính "đã đặt" gồm viewport + schedule + panel schedule; loại `PanelSchedule`/`ColumnSchedule` khỏi diện dọn; giữ view đang mở (trước đây làm cả lệnh rollback); chốt chặn thứ hai hỏi chính Revit `GetDependentElements` xem xoá view có kéo theo khung nhìn/bảng nào trên sheet không. Sheet "rỗng" = chỉ còn khung tên. Không chắc thì giữ, và bản xem trước liệt kê view/sheet được giữ kèm lý do |
| 2 | Cao | `scripts/dhcb_mcp_server.py` | MCP server chung (Claude Desktop, gói `.mcpb`) đọc stdin bằng code page ANSI của Windows (Python < 3.15 với stdin là ống). Claude Desktop gửi UTF-8 thô: `Đ` (0x90) và `ờ` (0x9D) không có trong cp1252/cp1258 → `UnicodeDecodeError`, **server chết ngay câu tiếng Việt đầu tiên** ("Đánh số cửa tầng 3"); các dấu khác thành mojibake và đi thẳng vào config lệnh (tiền tố "Tầng" ghi vào mô hình thành "Táº§ng"). Tái hiện: `PYTHONIOENCODING=cp1252` | Đọc byte từ `sys.stdin.buffer`, tự giải mã UTF-8 từng dòng; dòng không phải UTF-8 bị bỏ như dòng JSON hỏng (server sống tiếp) |
| 3 | Trung bình | BatchRunner, đường AutoCAD | `saveOnError` (mặc định `false`) chỉ có tác dụng bên Revit. Script accoreconsole dựng sẵn, dòng `SAVEAS` luôn chạy dù `DHCB_RUN` trước đó lỗi → với `saveMode: "Save"` bản vẽ **gốc** bị ghi đè bằng bản nửa vời (step trước đã ghi, step sau rollback) | `StagedSave` (tầng thuần, có test): khi `saveOnError: false` script lưu vào file tạm cạnh đích (cùng thư mục, giữ nghĩa đường dẫn xref tương đối); runner đọc dòng log của riêng file đó rồi mới thay đích, ngược lại bỏ file tạm và ghi `Save:<mode>` *bỏ qua* kèm lý do. Ghi đè gốc thì giữ `<tên>.bak` như `ISAVEBAK` của AutoCAD. `saveOnError: true` giữ nguyên đường cũ |
| 4 | Trung bình | BatchRunner `Main` | Mã thoát tính từ log mà **bỏ mã của lượt chạy**: Revit sập/bị kill sau 3/10 file thì log chỉ có 3 dòng xanh → mã 0, Task Scheduler không cảnh báo một đêm mới làm được một phần ba; `report.html` cũng toàn xanh | `RunLog.ExitCode(entries, launchCode)` lấy mã nặng hơn; nhánh "Revit không báo hoàn thành" ghi thêm một dòng `Revit` lỗi vào log để báo cáo nói thật |
| 5 | Trung bình | BatchRunner, đường Revit | `pending-job.json` ghi **trước** khi kiểm add-in đã cài; nhánh "chưa cài add-in cho Revit năm X" và "không khởi động được Revit" thoát mà không dọn (còn `Process.Start` ném thì runner sập). Lần sau kỹ sư mở Revit năm khác (có add-in) → Revit âm thầm chạy job của đêm trước rồi **tự đóng giữa phiên làm việc** — đúng cái bẫy mà `BatchStartupHook` đã cố tránh | Kiểm add-in trước, ghi `pending-job.json` ngay trước khi mở Revit; mọi nhánh không mở được Revit gọi `TryDeletePending`; `Win32Exception` thành mã thoát 2 có thông báo |
| 6 | Trung bình | `IfcStepParser` | Đọc danh sách lồng nhau bằng đệ quy không trần: file IFC chỉ toàn `(` (hỏng hoặc cố ý) làm **tràn stack** — `StackOverflowException` không bắt được, BatchRunner chết hẳn (`--verify-ifc`: mã 134 trên Linux, 0xC00000FD trên Windows) và gói bàn giao đêm đó không được dựng. Tái hiện bằng file 400 KB; trên Windows (stack 1 MB) vài KB là đủ | Trần `MaxNesting = 64` tầng (IFC thật sâu nhất 3–4); quá trần là `IfcParseException` — cùng đường "không đọc được file" như mọi IFC hỏng khác. File IDS lồng 100.000 tầng đã thử: không sập, không cần sửa |
| 7 | Trung bình | `scripts/install-nightly-task.ps1` | Dùng `?.` và `??` — toán tử chỉ có ở PowerShell 7, không có `#Requires`. Windows PowerShell 5.1 (bản có sẵn trên mọi máy) báo lỗi cú pháp cho **cả file**: lệnh cài task chạy đêm theo đúng README hỏng ngay | Viết lại bằng cú pháp chung 5.1/7. CI thêm bước *Kiểm script PowerShell*: bộ đọc của pwsh gắn loại token riêng cho đúng các toán tử này, nên kiểm được trên runner Linux — chạy thử trên `main` cũ thì bắt đúng dòng 41 |
| 8 | Trung bình | Task chạy đêm (cùng script) | Action dọn `don-ket-qua.ps1 -Apply` chạy **sau** runner. Task Scheduler chạy các action tuần tự bất kể mã thoát và ghi *Last Run Result* theo action sau cùng → mã 1/2 của batch (cơ chế cảnh báo duy nhất) bị mã của bước dọn che; máy không có thư mục `DHCB-test-results` thì task báo 2 mỗi đêm dù batch xanh | Bước dọn đặt trước, runner là action cuối |
| 9 | Trung bình | `scripts/dhcb_agent.py` `send_background` | Không nhận ra trạng thái `abandoned` của `/progress` (job hết hạn chờ, **không chạy**): client hỏi tiếp 30 phút rồi báo "Lệnh VẪN ĐANG CHẠY" — ngược với sự thật, người dùng không dám gửi lại | Nhánh riêng: trả lỗi ngay, nói rõ lệnh không chạy, gửi lại được |
| 10 | Thấp | `OllamaClient` (lớp AI trong add-in) | `HttpWebRequest` đi theo proxy hệ thống; .NET 8/10 trên Windows **không** tự bỏ qua `127.0.0.1` khi proxy cấu hình tay thiếu `<local>` → prompt (tên layer, thuyết minh, cảnh báo của mô hình) đi ra proxy công ty, trái lời hứa "không dữ liệu nào rời máy". Cùng loại lỗi audit 09-28 đã sửa cho client Python. Kèm theo: tự theo chuyển hướng 307/308 là POST lại nguyên prompt tới đích mới | `CreateRequest`: `Proxy = null`, `AllowAutoRedirect = false` |
| 11 | Thấp | `HttpBridgeServer.ReadBody` | Giải body bằng `HttpListenerRequest.ContentEncoding`: thiếu `charset` thì là `Encoding.Default` — code page ANSI trên .NET Framework (Revit/AutoCAD ≤ 2024). Client gửi `"Mã hiệu"` bằng UTF-8 với `Content-Type: application/json` trơn (curl, PowerShell, Node) thì lệnh nhận `"MÃ£ hiá»‡u"`. Script Python đi kèm chỉ thoát nạn vì `json.dumps` mặc định escape ký tự ngoài ASCII | `BodyEncoding`: theo `charset` nếu có, còn lại UTF-8 (RFC 8259 §8.1) |
| 12 | Thấp | `panel_api.py` (gateway AutoCAD) | `hmac.compare_digest` trên `str` ném `TypeError` khi có ký tự ngoài ASCII: tool MCP `autocad_execute(confirm="đồng ý")` **sập** thay vì báo "cần xác nhận"; header `X-Panel-Token` lạ làm gateway rớt kết nối không trả lời | So trên byte UTF-8 (`_same_secret`) |
| 13 | Thấp | `HealthReport` | `<title>` chèn tên dự án (Project Information của model, có thể nhận từ bên ngoài) **không escape** — thân trang thì có. `</title><script>…` chạy khi trang tự mở sau lệnh | Escape như thân trang |
| 14 | Thấp | `BridgeTokenStore` | Gọi `icacls` bằng tên trần: `CreateProcess` tìm thư mục của Revit.exe/acad.exe và thư mục hiện hành **trước** System32 | Đường dẫn tuyệt đối `%SystemRoot%\System32\icacls.exe` |
| 15 | Thấp | `scripts/run-in-revit-tests.ps1` | Dọn lượt cũ bằng `-like "$Suite-*"`: chạy bộ `write` gom cả `write-mep`/`write-asbuilt`/`write-plumbing` vào, xoá **bằng chứng** của các bộ đó (kèm bản chép model) và đếm sai lượt của chính nó (mô phỏng: xoá 4 thư mục thay vì 1) | Khớp đúng `<bộ>-yyyy-MM-dd_HH-mm-ss` như `don-ket-qua.ps1` |
| 16 | Thấp | BatchRunner, đường AutoCAD | `stopOnError` thì `break` — các file còn lại không có dòng nào trong log, `report.html` không cho biết file nào bị bỏ lại (bên Revit có dòng "Dừng vì stopOnError") | Ghi dòng *bỏ qua* cho từng file còn lại, cùng câu với Revit |
| 17 | Thông tin | Tài liệu, cảnh báo build | README ghi "Revit 17 / AutoCAD 15 truy vấn" — thiếu `document_context` (18/16); câu gợi ý "Hợp lệ: …" phía Revit thiếu `document_context`; `CS0419` (cref mơ hồ) trong `HashChain.cs` là cảnh báo duy nhất của cả solution | Sửa số và câu gợi ý; cref chỉ đúng overload — build Core/vỏ 2023–2027 nay 0 cảnh báo |

Tài liệu đi kèm: `docs/batch-runner.md` (ngữ nghĩa `saveOnError` hai bên, mã thoát, thứ tự action), `README.md`,
`SECURITY.md` (AI không qua proxy, trần độ lồng IFC, giới hạn của panel trên máy nhiều người dùng),
`tools/autocad-mcp-server/README.md`, `CONTRIBUTING.md` (ba quy tắc mới + bước CI mới).

## Đã soát, không cần sửa

| Chỗ | Vì sao ổn |
|---|---|
| `dryRun` mặc định | Đủ 49 lớp config đều `= true`; mọi lệnh `writesModel` (catalog) đều có property `DryRun` — đối chiếu bằng script, vì lệnh thiếu nó sẽ bị `RevitCommandTable` bỏ khoá `dryRun` và **ghi thật** khi được xem trước |
| `BridgeCommitGuard` / `BridgePathPolicy` | Token preview dùng một lần, claim bền vững trước dispatch, chống lặp; mọi đường ghi file trong Core (đã liệt kê bằng grep `File.Write*`/`SaveAs`/`Export`) đi qua trường được chính sách đuôi file kiểm hoặc thư mục do lệnh tự đặt tên |
| `AuthLockout`, `TokensMatch`, `IsBrowserRequest` | Như audit 09-28; không đổi |
| `AcadScriptGen` | Mọi giá trị chèn vào script bỏ `"`, CR, LF; locale lọc ký tự |
| Đi theo tham chiếu `#id` trong IFC/IDS | Mọi vòng đi lên cây (`partOf`, decomposition, containment) có tập đã thăm hoặc trần bước — file có vòng tham chiếu không treo |
| Báo cáo HTML | Mọi `<title>`/ô bảng trong `Shared.Logic` và Core đều escape, trừ chỗ ở mục 13 |
| Workflow | `actionlint` sạch; `zizmor --offline --min-severity low`: 0 high/medium (còn gợi ý phong cách `self-repository` như 09-28) |
| Installer | Cài theo user, không cần admin; kiểm runtime .NET 10 trước khi cài gói AutoCAD 2026 |

## Để lại — cần quyết định hoặc cần phần mềm thật

| Mục | Vì sao để lại |
|---|---|
| ~~`ParameterImport` ghi tham số **Type** theo từng dòng CSV và so với giá trị HIỆN TẠI: sửa một dòng của type, các dòng sau (vẫn mang giá trị cũ của bản xuất) ghi đè lại — kết quả phụ thuộc thứ tự dòng, summary đếm cả hai lần ghi~~ | **Đã sửa ở vòng 2** (mục V2-1), theo đúng đề xuất: so với giá trị trước khi nhập, xung đột thì không ghi |
| ~~Panel gateway phát token phiên cho mọi tiến trình loopback (tài khoản khác trên máy dùng chung)~~ | **Đã sửa ở vòng 2** (mục V2-2): kênh mang bí mật là chính file mà `::preview{file=…}` mở — gateway ghi nó vào `%LOCALAPPDATA%` |
| ~~Gói BatchRunner chép **mọi** `scripts/*.py`/`*.ps1`, kể cả script quản trị repo (`apply-rulesets.py`, `fix-ruleset.py`, `check-coverage.py`, `sign-addin.ps1`)~~ | **Đã sửa ở vòng 3** (mục V3-3) |
| ~~`release.yml` cài Inno Setup bằng `choco install innosetup` không ghim phiên bản~~ | **Đã sửa ở vòng 3** (mục V3-4) — hoá ra không chỉ là vệ sinh: Inno Setup 7 đã ra |
| `PackageContents.xml` liệt kê cả ba thành phần AutoCAD dù người dùng chỉ chọn một | Vẫn để lại — xem bảng "Để lại" của vòng 3 |

## Cần chạy lại trong Revit/AutoCAD trước khi phát hành

Mã của các mục dưới đây đã biên dịch với API NuGet Revit 2023–2027 / AutoCAD 2024–2026, phần quyết định có test ở tầng
thuần, nhưng hành vi trong phần mềm thật chưa chạy ở đây:

- **Mục 1** — bộ `smoke` trong Revit 2024.3 (ca *Xem trước dọn view thừa* chỉ kiểm chữ "Xem trước", không đổi), và một
  lượt xem trước trên model có mặt bằng chia vùng (view phụ thuộc), panel schedule và sheet chỉ có schedule: ba thứ đó
  phải nằm ở phần "Giữ …" của bản xem trước, không ở danh sách xoá. Số "sẽ xoá" trên model mẫu có thể giảm so với các
  lượt trước (93) — đó là mục đích của bản sửa.
- **Mục 3** — bộ `write` của AutoCAD qua accoreconsole với `saveMode: "Save"`: lượt sạch phải thay file gốc và để lại
  `.bak`; thêm một step lỗi cố ý thì file gốc phải nguyên vẹn, log có `Save:Save` *bỏ qua*.
- **Mục 5** — một đêm batch Revit thật (đường mở Revit không đổi, chỉ đổi thứ tự ghi file).

## Kiểm chứng tại máy audit

| Kiểm tra | Kết quả |
|---|---|
| `DhcbTools.Shared.Logic.Tests` | **1.909** ca đạt (1.884 trước sửa + 25 ca mới ở `Audit20261001Tests`), phủ dòng 100 % |
| `DhcbTools.BatchRunner.Tests` | **24** đạt (21 + 3: hai ca không sót `pending-job.json`, một ca IFC lồng 200.000 tầng) |
| Python `coverage run -m pytest` | **340** đạt (gồm 7 ca mới; 5 ca trước đây bị bỏ qua vì thiếu pwsh nay chạy), phủ câu lệnh 100 %, `pyflakes` sạch; chạy kiểu CI (`unittest discover` hai thư mục) cũng xanh |
| Biên dịch Core + bốn vỏ (API NuGet, `UseWPF=false`) | 2023 / 2024 / 2025 / 2026 / 2027: **0 lỗi, 0 cảnh báo** |
| Test mới đỏ trên mã cũ | Python: 6/7 ca mới đỏ (ca còn lại phủ nhánh bỏ dòng không phải UTF-8); CLI: hai ca `pending-job.json` đỏ. Ca IFC lồng sâu không chạy được trên mã cũ — nó làm sập cả test host; lỗi đã tái hiện riêng bằng exe cũ (`--verify-ifc`, mã 134) |
| Bước CI PowerShell mới | Xanh trên nhánh này, đỏ trên `main` cũ đúng `install-nightly-task.ps1:41` (`?.`, `??`) |
| `actionlint` / `zizmor` | Sạch / 0 high-medium |
| `scripts/check-vulnerable.sh` | NuGet ba TFM (2023 / 2025 / 2027): 0 package dính lỗ hổng; `pip-audit`: 0 |

---

## Vòng 2 — cùng ngày, mốc `56b3a3b` (sau PR #173)

Hai việc: (1) sửa tận gốc hai mục "Để lại" đầu tiên ở trên — chủ repo chọn hướng chất lượng cao, tức đổi ngữ nghĩa
`ParameterImport` và thêm bí mật cho panel; (2) quét những phần vòng 1 chưa đọc sâu: `panel.html`, các lệnh **ghi** của
Core AutoCAD (`AttributeImport`, `TextReplace`, `LayerTranslate`, `LayerImport`, đánh số block), `AutoNumbering`/`SheetRename`
bên Revit, form nhập config lệnh, bộ đoán lệnh từ câu nói, đóng gói phát hành. Cùng cách làm: tái hiện trước khi sửa.
Riêng phần chạm API AutoCAD thì không có AutoCAD ở máy audit: kết luận dựa trên ngữ nghĩa API, và đều được liệt kê ở
"Cần chạy lại" bên dưới.

### Đã sửa

| # | Mức | Chỗ | Vấn đề | Sửa |
|---|---|---|---|---|
| V2-1 | Trung bình | `ParameterImport` (Core Revit) | Mục "Để lại" đầu tiên ở trên: tham số type lặp ở mọi dòng cùng type; sửa một dòng thì các dòng sau ghi đè lại giá trị cũ, kết quả phụ thuộc thứ tự dòng, xem trước đếm sai | Hai lượt. Lượt 1 chỉ đọc, so mọi ô với giá trị **trước khi nhập**. `ParameterImportPlanner` (tầng thuần, 7 ca test) gom theo (phần tử ghi thật — instance hay type của nó, tham số): đúng một giá trị mới → ghi **một lần**, kèm ghi chú "áp cho mọi phần tử cùng type"; hai giá trị mới khác nhau → **xung đột**: không ghi, báo dòng nào mang giá trị nào, `PartialSuccess` (Bridge không phát preview token cho lần xem trước có xung đột, nên không commit được tới khi sửa CSV). Lượt 2 ghi đúng các ô kế hoạch chọn |
| V2-2 | Trung bình | Panel gateway AutoCAD | Mục "Để lại" thứ hai: loopback không phân biệt tài khoản Windows — trên máy dùng chung/RDS, tiến trình của tài khoản khác gọi `GET /panel` là có token phiên, rồi điều khiển AutoCAD của bạn qua gateway (gateway gọi Bridge bằng token **của bạn**, đi vòng ACL của `bridge-token.txt`) | **Khoá khởi chạy**: `/panel` đòi `?k=<khoá>` (32 byte, đổi mỗi lần gateway khởi động, so hằng thời gian); thiếu/sai → `403` chữ, không lộ token. Khoá chỉ nằm trong bộ nhớ gateway và file khởi chạy `%LOCALAPPDATA%\DHCB\autocad-panel.html` (ngoài Windows `~/.cache/DHCB`, quyền 600, ghi qua file tạm `O_EXCL`), gateway ghi **sau** khi bind được port và xoá khi tắt; lần chạy bind lỗi không đụng file của gateway đang chạy. Tool `autocad_open_panel` trả preview của file đó, báo rõ khi gateway đang chạy mà thiếu file (bản cũ, tài khoản khác). Không ghi được file mới thì xoá file cũ mang khoá đã hết hạn (`terminate()` không dọn nó), để tool báo rõ thay vì trỏ tới một trang 403. File mở với `O_BINARY` trên Windows (thiếu cờ này CRT đổi dòng hai lần). Trang kèm `Referrer-Policy: no-referrer`. Cách làm giống Jupyter phát token qua file `*-open.html` |
| V2-3 | Trung bình | `AttributeImport` (Core AutoCAD) | AutoCAD cho phép hai attribute **cùng tag** trong một block; `AttributeExport` xuất cả hai dòng cùng (Handle, Tag). Lệnh nhập ghi mọi dòng vào attribute **đầu tiên** khớp tag: nhập lại nguyên file vừa xuất — thao tác "không đổi gì" — là attribute đầu nhận giá trị của attribute sau, im lặng | Tag khớp nhiều attribute → bỏ qua dòng, báo rõ; không đoán |
| V2-4 | Trung bình | `LayerTranslate` (Core AutoCAD) | `AttributeReference` không nằm trong `BlockTableRecord` nên vòng đổi layer bỏ sót: chữ khung tên ở lại layer cũ — đúng thứ mà chuẩn hoá layer cần đổi. Trong khi đó xem trước (`CollectUsedLayerNamesAfterMap`) lại tính cả attribute là đã chuyển → báo "xoá N layer nguồn rỗng", chạy thật xoá ít hơn | Đổi cả attribute (ngoài block xref/anonymous), cùng tập với hàm tính của xem trước |
| V2-5 | Trung bình | Lệnh ghi AutoCAD: `TextReplace`, `AttributeImport`, `AutoNumbering`/`AttributeIncrement`, `LayerTranslate` | Mở để ghi (`ForWrite`/`UpgradeOpen`) một entity hay attribute trên **layer khoá** thì AutoCAD ném `eOnLockedLayer`; không lệnh nào xét khoá, nên một chữ trên layer khung tên bị khoá làm **sập cả lệnh** với thông báo khó hiểu (transaction huỷ, không ghi gì) | Tôn trọng khoá như lệnh FIND của AutoCAD: bỏ qua, và cả xem trước lẫn chạy thật báo số đối tượng bỏ qua theo layer (`LockedLayerSkips`, tầng thuần, có test) — nên con số xem trước khớp chạy thật |
| V2-6 | Thấp | `AttributeImport`; truy vấn theo handle (Bridge `entity_geometry`, UI chọn/zoom) | Đối tượng đã xoá **vẫn** tra ra ObjectId từ handle (còn trong database tới khi lưu và mở lại), và `GetObject` ném `eWasErased`. CSV xuất xong, kỹ sư xoá một block, nhập lại → cả lệnh sập; agent hỏi một handle nó thấy từ lượt trước → cả truy vấn sập | `IsErased` → bỏ qua dòng / báo "không có trong bản vẽ" |
| V2-7 | Thấp | Đánh số (Revit `AutoNumbering`; AutoCAD `BlockNumbering`) | Xem trước chỉ đếm phần tử tìm được: Revit báo "sẽ đánh số 120" rồi chạy thật "40/120" vì phần tử thiếu tham số / tham số chỉ đọc; kết quả `Parameter.Set` bị bỏ qua (trả `false` vẫn đếm là đã đánh số); giá trị đã đúng vẫn ghi lại. Bên AutoCAD, xem trước và chạy thật là hai vòng riêng nên đếm khác nhau | Một danh sách đích dùng chung cho cả hai đường; đã đúng số thì không ghi; `Set` trả `false` thì báo. Bên AutoCAD gộp thành một vòng |
| V2-8 | Thấp | `TextReplace` | (a) Đổi chữ DBText/attribute canh giữa/Fit/Aligned mà không `AdjustAlignment` — chữ lệch khỏi điểm canh (chính `AttributeImport` đã ghi nhận và sửa lỗi này cho nó); (b) chuỗi bị bỏ vì regex chạy quá trần 2 giây được giữ nguyên **im lặng** — xem trước "sẽ thay N" trong khi có chuỗi khớp mà không được thay | `AdjustAlignment` sau khi đổi; báo số chuỗi bị bỏ vì quá giờ |
| V2-9 | Thấp | `LayerTranslate` (có từ trước, cùng loại xem trước ≠ chạy thật) | (a) Chỉ chạy thật mới xét layer **hiện hành**: xem trước báo "sẽ xoá" nó, chạy thật giữ lại — con số "xoá N layer" của hai lượt lệch nhau; (b) màu không phải ACI 1–255 và Plottable sai định dạng chỉ được báo lúc chạy thật | Xét layer hiện hành trước nhánh xem trước; một hàm đọc màu/Plottable dùng chung cho hai lượt, xem trước báo đủ |

### Đã soát, không cần sửa

| Chỗ | Vì sao ổn |
|---|---|
| `panel.html` hiển thị dữ liệu bản vẽ | Mọi chỗ gán `innerHTML` có dữ liệu bản vẽ/AI đều qua `escHtml` (escape cả `"`/`'`); khung chat dùng `textContent`; ô màu kẹp số 0–255; tô màu JSON escape trước rồi mới chèn thẻ `span` cố định. Không có đường XSS để lấy token phiên |
| `LayerImport` | Mở bản ghi layer ở chế độ đọc, chỉ nâng lên ghi khi có ô khác; tên không hợp lệ báo và bỏ qua; không mở entity nên không vướng layer khoá |
| `SheetRename` | Đổi số qua tên tạm hai vòng, khôi phục số gốc khi vòng thật lỗi; tên/số ngoài lô được giữ chỗ chống trùng |
| `CommandFormWindow` (Revit) | Chạy thật chỉ mở sau khi xem trước thành công **và** config không đổi so với lúc xem trước (so snapshot); `dryRun` do cửa sổ điều khiển, người dùng không tự đặt |
| `CommandIntentParser` | Lệnh ghi luôn có `dryRun: true`; xếp hạng ổn định giữa các lần chạy; hai lệnh sát điểm thì hạ độ tin cậy |
| `BridgeCommitGuard` với V2-1 | Xem trước có `Errors`/`PartialSuccess` không được cấp preview token → CSV có xung đột không thể commit qua Bridge, đúng ý "sửa CSV rồi nhập lại" |

### Để lại

| Mục | Vì sao để lại |
|---|---|
| Ba mục đóng gói ở bảng "Để lại" vòng 1 | Hai mục đã sửa ở vòng 3 (V3-3, V3-4); `PackageContents.xml` còn để lại |
| ~~`LayerImport` gặp hai dòng cùng tên layer~~ | **Đã sửa ở vòng 3** (mục V3-1) |

### Cần chạy lại trong AutoCAD/Revit trước khi phát hành

- **V2-3 → V2-9, phần AutoCAD** — trên bản chép của bản vẽ mẫu: khoá một layer có chữ và attribute, rồi chạy
  `TextReplace`, `AttributeImport`, `AutoNumbering` và `LayerTranslate`, mỗi lệnh xem trước rồi chạy thật. Không lệnh nào
  được sập; câu "Bỏ qua N … nằm trên layer đang khoá" phải có ở cả hai lượt, với cùng con số. `LayerTranslate` với một
  layer chỉ chứa attribute: xem trước báo xoá layer đó, chạy thật cũng xoá. Đặt một layer nguồn làm layer hiện hành:
  cả hai lượt đều nói "không xoá … vì đang là layer hiện hành". `AttributeImport`: xoá một block sau khi
  xuất rồi nhập lại → một dòng "đã bị xoá", không sập. Bộ `smoke` AutoCAD phải giữ nguyên kết quả.
- **V2-1, V2-7 (Revit)** — bộ `smoke` và `write` (ca `ParameterImport`, `AutoNumbering` giữ nguyên kỳ vọng); thêm một
  lượt tay: xuất cửa, sửa `Fire Rating` (tham số type) ở **một** dòng → xem trước báo một lần ghi kèm ghi chú "áp cho
  MỌI phần tử cùng type"; sửa thành hai giá trị khác nhau → báo xung đột, không ghi.
- **V2-2** — đã chạy thật ở máy audit (socket, hai tiến trình gateway, Ctrl+C); trên Windows còn cần một lượt mở panel
  từ Hermes để thấy `::preview{file=…}` mở được file trong `%LOCALAPPDATA%`.

### Kiểm chứng tại máy audit

| Kiểm tra | Kết quả |
|---|---|
| `DhcbTools.Shared.Logic.Tests` | **1.919** ca đạt (+7 `ParameterImportPlannerTests`, +3 `LockedLayerSkipsTests`), phủ dòng 100 % |
| `DhcbTools.BatchRunner.Tests` | **24** đạt |
| Python `coverage run -m pytest` | **351** đạt (+11 ca của gateway/tool panel), phủ câu lệnh 100 %, `pyflakes` sạch; chạy kiểu CI cũng xanh. Ba ca của lượt đọc lại sau cùng (cờ `O_BINARY`, xoá file khoá cũ, không xoá được vẫn phục vụ) đỏ trên mã trước đó |
| Test panel đỏ trên mã cũ | Chạy hai ca mới của `GetAuthTests` trên `panel_api.py` cũ (bơm sẵn một `LAUNCH_KEY` giả để ca chạy được): `/panel` trần **được phục vụ kèm token** — ca đỏ đúng câu "panel served without the launch key"; ca có khoá đúng cũng đỏ vì mã cũ so path tuyệt đối. Các biến thể có query (`?k=sai`…) mã cũ vốn trả 403 qua kiểm token — đỏ vì dạng phản hồi khác, không phải lộ token |
| Gateway chạy thật | `GET /panel` → `403 text/plain`, không có token; `GET /panel?k=<khoá trong file>` → `200`, kèm `Referrer-Policy`; file khởi chạy quyền `600`; gateway thứ hai (bind lỗi) không đổi file; Ctrl+C xoá file |
| Biên dịch Core + bốn vỏ (API NuGet, `UseWPF=false`) | 2023 / 2024 / 2025 / 2026 / 2027: **0 lỗi, 0 cảnh báo** (25/25 project) |

---

## Vòng 3 — xử lý việc để lại (2026-10-02, nối tiếp PR #175)

Chủ repo yêu cầu làm tiếp các việc còn lại. Gom từ ba báo cáo (09-23, 09-28, 10-01) — phần làm được và kiểm chứng
được ở máy không có Revit/AutoCAD:

### Đã sửa

| # | Mức | Chỗ | Vấn đề | Sửa |
|---|---|---|---|---|
| V3-1 | Trung bình | `LayerImport` (Core AutoCAD) | (a) Hai dòng cùng layer (tên AutoCAD không phân biệt hoa thường) áp theo thứ tự: dòng sau ghi đè dòng trước; layer chưa có thì xem trước báo "tạo mới" hai lần. (b) Có từ trước, lộ ra khi sửa (a): lúc chạy thật layer vừa tạo còn bị đếm thêm "cập nhật"/"giữ nguyên" — "Đã nhập 2 layer (1 cập nhật, 1 tạo mới)" cho **một** layer, gấp đôi con số xem trước | `LayerImportPlanner` (tầng thuần, 14 ca test) gom trước khi áp: dòng giống hệt nhau áp một lần kèm ghi chú; khác nhau ở bất kỳ ô nào — kể cả một dòng để trống ô mà dòng kia có giá trị — là xung đột: không áp dòng nào của layer đó, báo vào `Errors`, `PartialSuccess` (Bridge không cấp preview token). Layer mới đếm một lần là "tạo mới" ở cả hai lượt; xem trước báo luôn linetype chưa có, lineweight sai như chạy thật |
| V3-2 | Trung bình | `TextReplace` trên MText (Core AutoCAD) — "Chưa làm" của audit 09-28 | Thay thẳng trên `MText.Contents`, chuỗi CÓ mã định dạng: regex `\d+` đổi luôn chữ số trong `\H2.5x;` (chữ đổi cỡ), tìm "PL" trúng `\PLine` và biến mã xuống dòng thành `\X…`, tên font trong `{\fArial;…}` bị thay theo — hỏng định dạng, không báo | `MTextReplace` (tầng thuần, 15 ca test — mỗi ca hỏng chạy kèm cách cũ để thấy cái hỏng): tách `Contents` thành chữ hiển thị (kèm vị trí từng ký tự) và mã định dạng theo bảng mã của AutoCAD; so khớp trên chữ hiển thị (`\P` thành xuống dòng, nên `^`/`$` regex nhiều dòng theo đoạn, `$1` vẫn dùng được). Chỗ khớp nằm gọn trong một đoạn chữ liền thì thay; vắt qua mã, hoặc chứa ký tự do mã sinh ra (xuống dòng, phân số xếp chồng), thì giữ nguyên và báo "cần sửa tay". Chuỗi thay chèn như chữ: `\` `{` `}` được escape, xuống dòng thành `\P` |
| V3-3 | Thấp | `release.yml` — gói BatchRunner, thành phần "scripts" của installer | Chép mọi `scripts/*.py`, `*.ps1`: máy kỹ sư nhận cả script ký số, CI, sửa ruleset, và `run-in-*-tests.ps1`/`dung-family.ps1` vốn cần cả repo nên chạy từ thư mục cài đặt là hỏng | Chỉ chép `installer/batchrunner-scripts.txt` (6 script — đã chạy thử từ một thư mục gói phẳng: `dhcb_mcp_server`/`dhcb_ai` import được `dhcb_agent` cạnh nó); thiếu file nào thì bước dừng. `tests/python/test_package_scripts.py`: script mà tài liệu đi kèm gói hay installer nhắc tới phải có trong gói, script phụ thuộc nhau (`$PSScriptRoot`, `import`) đi cùng nhau, mọi script trong `scripts/` phải được xếp loại — đỏ trên `release.yml` của `main` |
| V3-4 | Trung bình | `release.yml` — bước dựng installer | Inno Setup 7 đã ra (tag `is-7_0_0` 2026-05-14, `is-7_1_0` 2026-08-10 của `jrsoftware/issrc`). `choco install innosetup` không ghim kéo 7.x, cài vào thư mục khác `Inno Setup 6` mà bước đóng gói gọi cứng — lần phát hành tới nhiều khả năng hỏng ở bước installer; `.iss` cũng chưa thử với trình biên dịch 7 | Ghim `--version=6.7.1`: bản 6.x cuối cùng từng là bản **mới nhất** (02→05/2026) nên chắc chắn có trên Chocolatey — 6.7.2 ra cùng ngày 7.0, 6.7.3 ra sau 7.0, bộ cập nhật tự động của Chocolatey có thể đã không đăng. Thiếu `ISCC.exe` thì báo rõ. Test ở V3-3 giữ bản ghim khớp đường dẫn |

### Để lại

| Mục | Vì sao |
|---|---|
| `PackageContents.xml` liệt kê đủ ba năm AutoCAD dù người dùng chỉ chọn một; nâng cấp mà bỏ chọn một năm thì Inno Setup không gỡ DLL cũ của năm đó | Sửa đúng phải đổi installer: `[InstallDelete]` cho thành phần không chọn (`Components: not acad2024`…) **cùng lúc** lọc `PackageContents.xml` theo thành phần đã chọn trong `[Code]` (`CurStepChanged(ssPostInstall)`) — hoặc tách mỗi năm một bundle. Chỉ làm một nửa thì đổi "AutoCAD nạp bản DLL cũ" thành "AutoCAD báo không nạp được DLL". Máy audit không biên dịch được `.iss` (ISCC chỉ chạy trên Windows) và CI của PR không dựng installer, nên không kiểm được — cần một lượt trên Windows |
| Các mục "Chưa làm" khác của audit 09-28 (CancellationToken cho lệnh dài, dây `RouteOptionGenerator`/`ClashClassifier`, vòng đời Bridge, `AuthLockout` theo client, tối ưu `IfcStepParser`, 63 ca IDS còn lệch) | Như 09-28: đổi chữ ký `ICoreCommand`, tính năng mới trên luồng ghi mô hình, hành vi luồng UI Revit, hay việc lớn riêng — đều cần Revit/AutoCAD thật hoặc đo trên file thật |
| Việc cần chủ repo bật (branch protection, private vulnerability reporting, Dependabot, chứng chỉ ký mã) | Cài đặt GitHub của chủ repo — xem audit 09-28 |

### Cần chạy lại trong AutoCAD trước khi phát hành

- **V3-1** — bộ `write` AutoCAD (bốn ca `LayerImport` giữ nguyên kỳ vọng: fixture không có tên trùng). Thêm một lượt
  tay: CSV có hai dòng `A-WALL` giống hệt nhau → một lần cập nhật kèm ghi chú bản lặp; khác màu → báo xung đột, không
  đổi layer; một layer mới có màu → xem trước và chạy thật cùng nói "tạo mới 1".
- **V3-2** — `TextReplace` xem trước trên một MText có `\P`, đổi font giữa từ và chiều cao `\H…;`: regex `\d+` không
  đổi cỡ chữ; "PL" không phá xuống dòng; chuỗi vắt qua chữ in đậm được báo "cần sửa tay".
- **V3-3, V3-4** — một lượt `release.yml` (tag hoặc `workflow_dispatch`): bước cài Inno Setup 6.7.1 thành công, gói
  BatchRunner có đúng 6 script.

### Kiểm chứng tại máy audit

| Kiểm tra | Kết quả |
|---|---|
| `DhcbTools.Shared.Logic.Tests` | **1.948** ca đạt (+14 `LayerImportPlannerTests`, +15 `MTextReplaceTests`), phủ dòng 100 % |
| `DhcbTools.BatchRunner.Tests` | **24** đạt |
| Python `coverage run -m pytest` | **357** đạt (+6 `test_package_scripts`), phủ câu lệnh 100 %, `pyflakes` sạch; chạy kiểu CI cũng xanh. Test đóng gói đỏ trên `release.yml` của `main` (chép cả thư mục, không ghim Inno Setup) |
| Đoạn PowerShell chép script (pwsh 7) | Chép đúng 6 file; thêm một tên không có vào danh sách thì bước dừng với "Cannot find path" |
| Biên dịch Core + bốn vỏ (API NuGet, `UseWPF=false`) | 2023 / 2024 / 2025 / 2026 / 2027: **0 lỗi, 0 cảnh báo** (25/25 project) |
| `actionlint` / `zizmor` | Sạch / không thêm phát hiện (còn một gợi ý `self-repository` như trên `main`) |
