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
| `ParameterImport` ghi tham số **Type** theo từng dòng CSV và so với giá trị HIỆN TẠI: sửa một dòng của type, các dòng sau (vẫn mang giá trị cũ của bản xuất) ghi đè lại — kết quả phụ thuộc thứ tự dòng, summary đếm cả hai lần ghi | Sửa đúng là đổi ngữ nghĩa nhập: cần chốt "sửa một dòng có đổi cả type không". Đề xuất: so với giá trị **trước khi nhập**; nhiều giá trị mới khác nhau cho cùng một type → báo xung đột, không ghi |
| Panel gateway phát token phiên cho mọi tiến trình loopback (tài khoản khác trên máy dùng chung) | Sửa tận gốc cần bí mật do trình khởi chạy trao cho trình duyệt, mà Hermes nhúng panel qua `::preview{file=…}` rồi chuyển hướng — không có kênh nào mang bí mật. Đã ghi giới hạn vào `SECURITY.md` và README của panel |
| Gói BatchRunner chép **mọi** `scripts/*.py`/`*.ps1`, kể cả script quản trị repo (`apply-rulesets.py`, `fix-ruleset.py`, `check-coverage.py`, `sign-addin.ps1`) | Không gây hại; dọn danh sách đóng gói là việc của lần phát hành sau |
| `release.yml` cài Inno Setup bằng `choco install innosetup` không ghim phiên bản | Choco kiểm checksum của gói; ghim phiên bản cần chọn bản đã kiểm trên installer hiện tại |
| `PackageContents.xml` liệt kê cả ba thành phần AutoCAD dù người dùng chỉ chọn một | AutoCAD năm không được cài chỉ báo không nạp được DLL; cần dựng file theo thành phần đã chọn trong `[Code]` của installer |

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
