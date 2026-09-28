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

## Việc cần chủ repo bật

Không làm được từ mã nguồn — cần quyền admin repo trên GitHub.

| Việc | Ở đâu | Vì sao |
|---|---|---|
| Branch protection cho `main`, required checks = 11 job của `tests.yml` + `gitleaks` | Settings → Branches | CONTRIBUTING đang phải dặn "không dùng `--auto`" vì thiếu rule này (PR #64 từng merge khi CI chưa xong) |
| Private vulnerability reporting | Settings → Code security | `SECURITY.md` trỏ vào nút này |
| Dependency graph + Dependabot alerts | Settings → Code security | Bật thì dùng lại được `dependency-review-action` trên PR |
| Chứng chỉ ký mã (PFX) làm secret cho `release.yml` | Settings → Secrets | Đã ghi ở audit 2026-09-23; DLL/installer hiện không ký |
