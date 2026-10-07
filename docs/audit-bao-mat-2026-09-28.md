# Audit bảo mật, quy trình và công cụ — 2026-09-28

Mốc trước sửa: `f0288b5` (sau PR #169). Phạm vi: bề mặt tấn công cục bộ của add-in (HTTP Bridge, panel
gateway AutoCAD, client Python), chỗ dữ liệu người dùng đi vào regex/script/XML, CI/CD và dependency.
Không lặp lại các mục đã có trong [audit 2026-09-23](audit-nang-cap-2026-09-23.md) — bảng "Việc để lại" ở đó
vẫn còn nguyên giá trị.

Cách làm: đọc mã theo đường dữ liệu từ ngoài vào; quét workflow bằng `actionlint` 1.7.7 và `zizmor` 1.30;
quét dependency bằng `dotnet list package --vulnerable --include-transitive` (ba TFM) và `pip-audit` 2.10.

## Đã sửa

| # | Mức | Chỗ | Vấn đề | Sửa |
|---|---|---|---|---|
| 1 | Cao | `HttpBridgeServer` | Trang web bất kỳ kỹ sư đang mở bắn `fetch("http://127.0.0.1:8765/…", {mode:"no-cors"})` 5 lần là `AuthLockout` khoá Bridge 5 phút — lặp lại mãi chừng nào tab còn mở. Không đọc được dữ liệu, nhưng tắt hẳn đường agent/MCP | `BridgeAuth.IsBrowserRequest`: có `Origin`, hoặc `Sec-Fetch-Site` khác `none` → `403` **trước** bước kiểm token, không tính vào khoá, không chiếm suất in-flight. Gõ URL `/health` vào thanh địa chỉ (`Sec-Fetch-Site: none`) vẫn được |
| 2 | Trung bình | `dhcb_agent.py`, `dhcb_ai.py`, `panel_api.py`, `server.py` | `urllib.request.urlopen` đi theo proxy hệ thống (`HTTP_PROXY`, hoặc ProxyServer trong Internet Settings của Windows) và **không** tự bỏ qua `127.0.0.1` — `<local>` chỉ khớp tên không có dấu chấm. Máy công ty có proxy tĩnh: request tới Bridge ra proxy kèm `Authorization: Bearer <token>` và config lệnh | Opener `LOOPBACK = build_opener(ProxyHandler({}))` cho mọi request loopback. Test dựng server thật + proxy chết để chứng minh (`LoopbackOpenerTests`) |
| 3 | Trung bình | `panel_api.AUTOCAD_URL`, `server.BRIDGE_URL` | `http://localhost:8766` — `localhost` có thể phân giải ra `::1` trước, cổng mà Bridge (chỉ nghe IPv4) không giữ; tiến trình của **tài khoản khác** chiếm `[::1]:8766` nhận được token, vô hiệu ACL của file token | `http://127.0.0.1:8766`, `server.py` dùng chung hằng số của `panel_api` |
| 4 | Trung bình | `NamePattern` (`SheetRename`, `FamilyAudit` đổi tên) | `find` là regex do người dùng/agent đưa vào, chạy trên luồng UI Revit **không trần thời gian**: `(a+)+$` gặp tên dài là treo Revit, không huỷ được | `FindTimeout` 2 s; quá trần → `ArgumentException` (cùng đường với regex sai cú pháp) nói rõ mẫu nào |
| 5 | Thấp | `release.yml` job `version` | Chỉ `inputs.version` được lọc; tên tag thì không, mà git cho phép `$ ( ) ; \|` trong tên ref và `v1.2.3$(…)` vẫn khớp `v*.*.*`. Output được chèn vào pwsh/ISCC ở năm job sau. `github.ref_type` nội suy thẳng vào bash | Lọc chung `[A-Za-z0-9.+-]` cho cả tag lẫn input; dùng `$GITHUB_REF_TYPE` |
| 6 | Thấp | Mọi workflow | Action ghim theo tag di động (`@v4`) — tag bị chiếm là mã lạ chạy trên runner; `actions/checkout` để `GITHUB_TOKEN` lại trong `.git/config` cho mọi bước sau | Ghim commit SHA (đúng SHA của tag `v4` hiện tại — không đổi hành vi) + `persist-credentials: false`; `.github/dependabot.yml` cập nhật SHA hằng tháng |
| 7 | Quy trình | `tests.yml` | CONTRIBUTING và PR template ghi `pyflakes` là bước kiểm, CI không chạy | Thêm vào bước Python của `check-build` (2025) |
| 8 | Quy trình | Dependency | `dependency-review.yml` bị bỏ ở PR #159 (repo chưa bật Dependency graph) → không còn gì báo package có lỗ hổng | `scripts/check-vulnerable.sh` + `dependency-audit.yml`: NuGet ba TFM (net48 / net8 / net10) và `pip-audit`; chạy khi PR đổi dependency, trên `main`, hằng tuần. Không cần tính năng GitHub nào |

Tài liệu đi kèm: [`SECURITY.md`](../SECURITY.md) (mô hình an toàn một trang, cách báo lỗ hổng), README thêm dòng
`403` vào bảng mã của Bridge, CONTRIBUTING thêm bốn quy tắc an toàn và lệnh kiểm dependency.

## Đã soát, không cần sửa

| Chỗ | Vì sao ổn |
|---|---|
| Đọc XML (IDS, `IdsSchemaLint`) | `XDocument.Parse` → `XmlReader` mặc định `DtdProcessing.Prohibit` trên cả net48 lẫn .NET: không XXE, không "billion laughs" |
| Journal Revit (`RevitJournalGen`) | Hằng chuỗi, không chứa dữ liệu người dùng |
| Script AutoCAD (`AcadScriptGen`) | Mọi giá trị qua `Escape` (bỏ `"`, CR, LF) — không chèn được lệnh thứ hai |
| So token | Thời gian hằng số, không phụ thuộc độ dài token client gửi |
| JSON | Newtonsoft.Json 13.0.3, không nơi nào bật `TypeNameHandling` |
| BCF | Chỉ ghi zip, không giải nén — không có zip slip |
| Dependency (2026-09-28) | `check-vulnerable.sh`: 0 package NuGet dính lỗ hổng ở cả ba TFM; `pip-audit`: 0 |
| Workflow sau sửa | `actionlint` sạch; `zizmor --min-severity low`: 0 high/medium (còn gợi ý phong cách `self-repository`) |

## Kiểm chứng

| Kiểm tra | Kết quả |
|---|---|
| `DhcbTools.Shared.Logic.Tests` | 1.839 ca đạt (1.827 trước sửa + 12 ca mới), phủ dòng 100 % |
| `DhcbTools.BatchRunner.Tests` | 21 đạt |
| `scripts/check-build.sh` (Core + bốn vỏ, API NuGet 2025) | 0 lỗi |
| Python: `coverage run` (unittest, như CI) + `pyflakes` | 329 ca chạy (324 đạt, 5 bỏ qua); phủ câu lệnh 100 %; pyflakes sạch |
| `scripts/check-vulnerable.sh` | sạch |

Không chạy trong Revit/AutoCAD thật: thay đổi phía .NET nằm ở `Shared.Logic`/`Shared.Hosting` (netstandard2.0)
và đã có test HTTP thật trên loopback (`HttpBridgeServerGapTests.RequestTuTrangWeb_403KhongTinhVaoKhoa`).

## Vòng 2 — xử lý "Việc để lại" của audit 2026-09-23

Chủ repo yêu cầu sửa hết phần để lại. Nguyên tắc: giữ tương thích ngược (mặc định cũ không đổi, trừ chỗ cũ là
sai chuẩn), và chỉ nhận là "đã sửa" khi kiểm chứng được ở đây — phần phải chạy trong Revit/AutoCAD mới biết
đúng sai thì ghi rõ là chưa làm và vì sao.

| Mục để lại (09-23) | Xử lý |
|---|---|
| Lệnh chỉ đọc nhận `outputPath` tuyệt đối bất kỳ qua Bridge | `BridgePathPolicy`: mọi trường đường dẫn file của lệnh gửi qua Bridge phải mang đuôi thuộc định dạng DHCB đọc/ghi (`.csv .html .json .pdf .dwg .ifc .rvt`…) và không chứa `:` ngoài ổ đĩa → `E-PATH-UNSAFE`. Không chốt thư mục (batch/kỹ sư vẫn xuất ra ổ mạng); chặn đúng thứ nguy hiểm: agent bị prompt injection ghi `.bat`/`.ps1`/`.lnk` vào Startup. Ribbon/batch không bị chốt |
| Bridge luôn khởi động, không có khoá | `settings.json` → `{"bridge": {"enabled": false}}` tắt cả hai Bridge; mặc định vẫn bật; file hỏng thì bật + ghi cảnh báo vào log |
| So giá trị IDS không phân biệt hoa thường; `xs:pattern` XSD (`\i`, `\c`) | Sửa theo **bộ ca chính thức của buildingSMART**: 240 → **271/334**, CI chạy cả bộ và đỏ khi hồi quy ([`kiem-ids.md`](kiem-ids.md#đối-chiếu-bộ-ca-buildingsmart)). Kèm theo: dung sai số IDS 1.0, boolean chữ thường, `.U.` = không có giá trị, nhiều `xs:pattern` là HOẶC, `minOccurs/maxOccurs` đọc từ `<applicability>` (trước đây specification CẤM của file IDS 1.0 thật bị đọc sai), specification bắt buộc không có phần tử = không đạt, `partOf` đọc đúng `<name>`/`<predefinedType>` (trước ghép thành "IFCINVENTORYBUNNY"), vật liệu khớp Category/tên bộ lớp, `USERDEFINED` |
| `--handover` parse cùng file IFC hai lần | Đọc một lần, `IfcChecker.Check(IfcModel, …)` và `IfcIdsModel.From(IfcModel)` dùng chung |
| Dung sai "Mm" phía AutoCAD so trong đơn vị bản vẽ | `DrawingUnits.FromMillimeters` theo `INSUNITS` cho `rowToleranceMm` (AutoNumbering, AttributeIncrement) và `moveToleranceMm` (DrawingCompare). Bản vẽ mm hoặc không khai đơn vị: không đổi |
| AttributeExport chỉ quét model space | Cờ `includePaperSpace` (mặc định `false`); CSV cùng bốn cột, AttributeImport ghi ngược theo Handle nên nhận luôn |
| `AutoNumbering`/`FlowNumbering` mặc định `"Mark"` | Không đổi mặc định; tên `"Mark"` nay tra theo `BuiltInParameter.ALL_MODEL_MARK` khi Revit giao diện ngôn ngữ khác không có tham số tên "Mark" |
| Release không ký DLL/installer | `scripts/sign-release.ps1` + bước ký trong `release.yml` — chạy khi repo có secret `DHCB_SIGN_PFX_BASE64`/`DHCB_SIGN_PFX_PASSWORD`, không có thì bỏ qua như trước. Chỉ ký file `DhcbTools*` |

Phát hiện thêm trong vòng này:

- **ClashDetection kèm `bcfPath` qua Bridge không bao giờ lấy được preview token**: preview ghi file BCF, mà
  `BridgeCommitGuard` chụp `bcfPath` như file *đầu vào* → "file đã đổi trong lúc preview" (`E-PREVIEW-CHANGED`)
  mọi lần. Tái hiện bằng test trên mã cũ, sửa bằng danh sách trường đầu ra chung (`bcfPath`, `legendCsvPath`).
- **Ghi chú §41 của `bang-chung-test.md` nói ngược chuẩn**: cho rằng IDS viết `FALSE` là đúng và IfcTester sai;
  bộ ca buildingSMART coi `FALSE` là invalid. Đã đính chính tại chỗ, fixture đổi sang `false`.

**Chưa làm, và vì sao** — tất cả đều cần chạy trong Revit/AutoCAD để biết đúng sai, không kiểm được ở đây:

| Mục | Lý do |
|---|---|
| `CancellationToken`/tiến độ cho lệnh dài | Đổi chữ ký `ICoreCommand` và mọi vỏ; Bridge 504 đã nói rõ "không huỷ được nữa" |
| `RouteOptionGenerator`/`ClashClassifier` dây vào `AutoRoute`/`ClashDetection` | Tính năng mới trên luồng ghi mô hình — cần bộ ca trong Revit |
| Vòng đời Bridge (batch trong `ApplicationInitialized`, hàng đợi khi Revit modal, revision do transaction tạm) | Hành vi luồng UI Revit, không có giả lập đáng tin |
| ~~TextReplace với mã định dạng MText (`\P`)~~ | **Đã làm 2026-10-02** — `MTextReplace`, xem [audit 2026-10-01, vòng 3](audit-toan-dien-2026-10-01.md#vòng-3--xử-lý-việc-để-lại-2026-10-02-nối-tiếp-pr-175) (mục V3-2) |
| `AuthLockout` khoá toàn cục | Loopback không có định danh client; đường tấn công thật (trang web) đã chặn ở vòng 1 |
| `dhcb_mcp_server` `confirm` boolean | Không phải lỗ: Bridge đòi `previewToken` + `documentId` của lần xem trước; chuỗi xác nhận do cùng model điền không thêm an toàn |
| `IfcStepParser` cấp phát/encoding; tách `AcadQueryHandler`/`SleeveCommand` | Tối ưu/nợ cấu trúc — cần đo trên file thật trước khi đổi |
| ~~IDS: bảng thuộc tính theo lược đồ, kiểu dữ liệu, property khớp nhiều cái (63 ca còn lệch)~~ | **Đã làm 2026-10-07** — 271 → 330/334, 4 ca còn lại là lựa chọn cố ý; xem [kiem-ids.md](kiem-ids.md#đường-ifc-so-giá-trị-có-kiểu-2026-10-07) |

Kiểm chứng vòng 2: `Shared.Logic.Tests` **1.884** ca đạt, phủ dòng 100 %; `BatchRunner.Tests` 21 đạt; Python 100 %
câu lệnh + pyflakes; build Core + bốn vỏ cho Revit/AutoCAD **2024 (net48) và 2026 (net8/net10)** bằng API NuGet;
`actionlint` + `zizmor` sạch; bộ ca buildingSMART 271/334 không hồi quy; `sign-release.ps1` parse bằng pwsh 7 và
chạy nhánh "chưa cấu hình". Revit smoke suite (`revit-smoke.json`) giữ nguyên các chuỗi kỳ vọng — chưa chạy lại
trong Revit.

## Việc cần chủ repo bật

Không làm được từ mã nguồn — cần quyền admin repo trên GitHub.

| Việc | Ở đâu | Vì sao |
|---|---|---|
| Branch protection cho `main`, required checks = 11 job của `tests.yml` + `gitleaks` | Settings → Branches | CONTRIBUTING đang phải dặn "không dùng `--auto`" vì thiếu rule này (PR #64 từng merge khi CI chưa xong) |
| Private vulnerability reporting | Settings → Code security | `SECURITY.md` trỏ vào nút này |
| Dependency graph + Dependabot alerts | Settings → Code security | Bật thì dùng lại được `dependency-review-action` trên PR |
| Chứng chỉ ký mã (PFX) làm secret `DHCB_SIGN_PFX_BASE64` + `DHCB_SIGN_PFX_PASSWORD` | Settings → Secrets and variables → Actions | `release.yml` đã sẵn bước ký; thiếu secret thì gói phát hành chưa ký |
