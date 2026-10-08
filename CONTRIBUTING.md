# Đóng góp cho DHCB Tools

Quy ước lấy từ repo [donghanh](https://github.com/seeker19110/donghanh), rút gọn cho quy mô
hiện tại của dự án này (add-in C#, một người làm). Repo **đã có CI** — xem
[CI đang chạy những gì](#ci-đang-chạy-những-gì) — nên mọi PR đều phải chờ check xanh trước khi merge.

## Luồng làm việc

**Idea → Branch → Commit → Pull request → Review → Merge.**

1. Mỗi tính năng hoặc sửa lỗi một nhánh riêng, đặt tên `feat/<slug>`, `fix/<slug>`, hoặc
   `docs/<slug>` (ví dụ `feat/batch-runner`, `fix/parameter-import-double`).
2. **Không push thẳng vào `main`** — kể cả khi làm một mình. Mọi thay đổi đi qua pull request.
3. Commit nhỏ, mỗi commit một thay đổi logic.
4. Mở PR, **chờ toàn bộ check của `tests.yml` xanh** rồi mới merge (chi tiết bên dưới).

## CI đang chạy những gì

Bốn workflow trong [`.github/workflows/`](.github/workflows/) — `tests.yml`, `release.yml` mô tả dưới đây, cộng
`secret-scan.yml` (gitleaks, mọi PR) và `dependency-audit.yml` (`scripts/check-vulnerable.sh`: package NuGet/pip có
lỗ hổng đã biết; chạy khi PR đổi dependency, trên `main` và hằng tuần). `.github/dependabot.yml` mở PR cập nhật
GitHub Actions và pip mỗi tháng. Mô hình an toàn tổng thể: [`SECURITY.md`](SECURITY.md).

**`tests.yml` — chạy mọi push vào `main` và mọi pull request.** Bốn nhóm kiểm tra và một cổng tổng hợp:

| Job | Máy/ma trận | Làm gì |
|---|---|---|
| `logic-tests` | ubuntu-latest | Restore/build/test C# Release; cổng phủ 100% dòng; CLI BatchRunner; đối chiếu BCF và IDS với dữ liệu chuẩn; tải `.trx` lên artifact `test-results` |
| `check-build` | ubuntu-latest; 2022–2027, hai runtime AutoCAD 2025/2026 | Build BatchRunner và Core/vỏ bằng SDK NuGet với `UseWPF=false`; output `headless` tách khỏi build giao diện. Hàng 2025/net8 kiểm Python với phủ 100% câu lệnh, pyflakes, JavaScript panel và cú pháp PowerShell 5.1/7 |
| `build-wpf-windows` | windows-latest; Revit 2022–2027 | Build đầy đủ vỏ Revit có WPF. Hàng 2025 biên dịch/chạy installer Inno Setup 6.7.1 trong thư mục tạm: 64 tổ hợp lựa chọn AutoCAD, runtime, nâng cấp/bỏ chọn/chọn lại và bảo toàn add-in khác |
| `build-autocad-windows` | windows-latest; 8 profile năm/runtime | Build đầy đủ AutoCAD UI có WPF và vỏ Core Console cho mỗi SDK/runtime |
| `quality-gate` | ubuntu-latest | Luôn chạy sau cả bốn nhóm; chỉ xanh khi mọi nhóm xanh. Job lỗi, bị hủy hoặc bị bỏ qua đều chặn cổng |

Revit/AutoCAD 2022–2024 dùng net48; Revit 2025/2026 giữ API baseline net8; Revit 2027 dùng net10.
AutoCAD 2025/2026 có hai profile net8/net10 tùy mức cập nhật host; AutoCAD 2027 dùng net10.
Xem [ma trận và giới hạn nghiệm thu](docs/tuong-thich-2022-2027.md). CI kiểm biên dịch; kiểm host thật
vẫn cần DWG/RVT mẫu và bằng chứng đầu ra.

**`release.yml` — CD, chạy khi đẩy tag `vX.Y.Z` hoặc gọi `workflow_dispatch`.** Nó gọi lại
`tests.yml`, build Windows Release đầy đủ cho 6 năm Revit và 8 profile AutoCAD UI/Core Console,
đóng gói ZIP cùng BatchRunner, dựng installer. Gói có metadata năm/runtime; đường DLL được hỏi MSBuild.
Tag yêu cầu chữ ký hợp lệ và chỉ publish sau khi mọi kiểm tra đạt; chạy tay có thể dựng gói dev.
Workflow này không chạy trên PR nên không phải chờ CD khi merge.

`secret-scan.yml` và `dependency-audit.yml` là các workflow riêng: `quality-gate` tổng hợp `tests.yml`,
không thay chúng. Audit dependency quét cả 8 profile AutoCAD và Revit tương ứng, dùng cùng property
MSBuild cho restore và đọc package để tránh quét nhầm thư mục assets.

## Merge PR — chờ check xanh; auto-merge khi được yêu cầu

Mặc định merge tay sau khi kiểm tra toàn bộ CI. Trước đây `main` chưa có required checks nên
`gh pr merge --auto` từng merge ngay khi CI còn chạy (PR #64, 2026-09-05).

Ruleset của nhánh mặc định phải active, không bypass, yêu cầu PR và cổng ổn định `quality-gate`,
cấm xóa nhánh và force-push. Cổng này gom mọi hàng ma trận của `tests.yml`; đổi ma trận không cần
liệt kê lại từng tên check. Không thấy classic branch protection không có nghĩa nhánh không được bảo vệ:
kiểm cả `GET /repos/{owner}/{repo}/rulesets` và nội dung ruleset. Khi triển khai cổng mới, chỉ cập nhật
required status contexts; giữ các điều kiện nhánh, review, bypass và rule khác.
`gitleaks` vẫn phải xanh trên đúng head SHA. Quy trình merge tay:

1. **Tạo PR ở trạng thái sẵn sàng** (không để nháp).
2. **Chờ check xanh** bằng `gh pr checks <n> --watch --fail-fast` (hoặc poll mỗi ~2,5 phút).
   `tests.yml` chạy trên mọi PR nên luôn có check để chờ.
3. **Toàn bộ job của `tests.yml` xanh + không xung đột → `gh pr merge <n> --squash`.** Còn job đang
   chạy → tiếp tục chờ. Có job đỏ → dừng, mở log của job đó, sửa và push lại, **không merge**.
4. **Không merge khi có check đỏ hoặc đang chạy** trong quy trình tay. Auto-merge chỉ dùng theo ngoại lệ bên dưới.
5. Nếu `main` tiến lên gây xung đột (`mergeable_state: dirty`) trong lúc chờ, merge `main` vào
   nhánh, giải xung đột, rồi mới tiếp tục từ bước 2.

Mặc định dùng quy trình merge tay trên. Khi chủ dự án yêu cầu auto-merge rõ ràng, có thể dùng
`gh pr merge <n> --auto --squash --match-head-commit <SHA>` sau khi:

- Đối chiếu ruleset đang active, không bypass, yêu cầu `quality-gate` cho nhánh đích.
- `gitleaks` xanh trên đúng head SHA trước khi bật; không dựa vào check của commit cũ.
- Rà diff và xác nhận không có check thất bại; nếu dependency-audit chạy thì chờ nó xanh. Các job required còn chạy sẽ do GitHub chờ hoàn tất.

Không dùng `--admin` hoặc bỏ required checks để merge. Push head mới thì rà lại check không bắt buộc
trước khi bật lại auto-merge. Nếu mọi check đã xanh, GitHub có thể merge ngay khi nhận lệnh.
Khi đổi tên job hoặc ma trận CI, đối chiếu lại required checks của ruleset để tránh thiếu cổng kiểm
hoặc chờ một job không còn tồn tại.

## Commit message — Conventional Commits

Dùng tiền tố chuẩn: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `style`, `perf`.

```
feat(mepf): thêm HangerCommand — đặt hanger theo khoảng cách đều
fix(parameter-sync): sửa lỗi round-trip Double theo culture hệ thống
docs: cập nhật roadmap sau khi merge Phase 1+2+3
```

Có `scope` trong ngoặc là tốt nhưng không bắt buộc — dùng tên thư mục/module (`mepf`,
`parameter-sync`, `bridge`), viết chữ thường.

*Chưa có `commitlint`/hook tự động kiểm tra định dạng này (dự án không dùng Node), nên hiện tại
là quy ước tự giác. Xem xét thêm gate ở Giai đoạn 0 nếu thấy cần.*

## Lệnh kiểm tra trước khi mở PR

Chạy trước những gì CI sẽ chạy, để không phải đợi một vòng đỏ (không cần cài Revit/AutoCAD):

```bash
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -c Release \
  --collect:"XPlat Code Coverage" --results-directory ./coverage
python3 scripts/check-coverage.py ./coverage   # cổng phủ 100% dòng — chỉ đúng file:dòng nếu thiếu
./scripts/check-build.sh      # biên dịch toàn bộ Core + vỏ bằng API package NuGet (Revit/AutoCAD 2025)
```

Có sửa phần Python (`scripts/`, `tools/autocad-mcp-server/`) thì chạy thêm — CI cũng chạy hai việc này:

```bash
pip install -r requirements-dev.txt          # pytest + coverage + pyflakes + fastmcp
python3 -m coverage run -m pytest -q         # tools/autocad-mcp-server + tests/python
python3 -m coverage report                   # đỏ nếu phủ < 100% câu lệnh
python3 -m pyflakes scripts/*.py tools/autocad-mcp-server/*.py tests/python/*.py
```

Có đổi dependency (`*.csproj`, `Directory.Build.props`, `requirements*.txt`) thì chạy thêm
`pip install pip-audit && ./scripts/check-vulnerable.sh` — cùng việc `dependency-audit.yml` làm.

**Cả hai tầng đều có ngưỡng phủ 100%**: thêm code mà không thêm test thì CI đỏ. Nhánh thật sự không
chạy được trên CI (mã chỉ có trên Windows, đua giữa hai luồng) thì đánh dấu `[ExcludeFromCodeCoverage]`
/ `# pragma: no cover` **kèm lý do ngay tại chỗ** — xem `docs/dac-ta-kiem-thu.md` §2.0 để biết danh
sách hiện có và những chỗ đã được tiêm seam để test thay vì loại trừ.

Trên Windows có cài Revit/AutoCAD, build thật (kèm WPF) cho phiên bản đang dùng:

```powershell
dotnet build src/DhcbTools.Revit/DhcbTools.Revit.csproj      -p:RevitVersion=2024
dotnet build src/DhcbTools.AutoCAD/DhcbTools.AutoCAD.csproj  -p:RevitVersion=2024 -p:AcadVersion=2024
dotnet build src/DhcbTools.BatchRunner/DhcbTools.BatchRunner.csproj
```

Thêm lệnh Core mới = thêm class + một dòng trong `Shared.Logic/Ai/CommandCatalog.cs` + một `case` trong
`RevitCommandTable`/`AcadCommandTable` (+ nút Ribbon/CommandMethod nếu cần). Test `CommandCatalogTests` sẽ đỏ nếu thiếu.

## Quy tắc an toàn

- Không commit file cấu hình/credential cá nhân (đường dẫn cài Revit/AutoCAD máy riêng, API key).
- Mọi lệnh sửa mô hình phải giữ `DryRun` mặc định bật và chạy trong một transaction duy nhất
  (xem "Nguyên tắc xuyên suốt" trong [`docs/roadmap.md`](docs/roadmap.md)).
- HTTP Bridge yêu cầu token (`%APPDATA%\DHCB\bridge-token.txt`) và chỉ bind 127.0.0.1 — không sửa để bind
  `0.0.0.0`; agent ở máy khác dùng SSH tunnel.
- Bridge từ chối request có header `Origin`/`Sec-Fetch-Site` (trang web trong trình duyệt) — đừng thêm CORS để
  "cho panel gọi thẳng"; panel đi qua gateway `tools/autocad-mcp-server/panel_api.py`.
- Client Python gọi Bridge/Ollama bằng opener `LOOPBACK` (không proxy) và địa chỉ `127.0.0.1`, không dùng
  `urllib.request.urlopen` trực tiếp — nó đi theo proxy hệ thống và đem header `Bearer` ra ngoài. Phía C# cũng vậy:
  request tới loopback đặt `Proxy = null` và `AllowAutoRedirect = false` (xem `OllamaClient.CreateRequest`).
- So chuỗi bí mật (token, chuỗi xác nhận) trong Python bằng `hmac.compare_digest` trên **byte UTF-8**, không trên
  `str` — `str` có ký tự ngoài ASCII làm nó ném `TypeError` thay vì trả `False`.
- Script `.ps1` phải chạy được bằng **Windows PowerShell 5.1** (có sẵn trên mọi máy kỹ sư): không dùng `?.`, `??`,
  `? :`, `&&`, `||` của PowerShell 7. CI kiểm bằng bộ đọc của pwsh (bước *Kiểm script PowerShell* trong `tests.yml`).
- Regex do người dùng/agent nhập luôn có `matchTimeout` — chạy trên luồng UI Revit/AutoCAD, treo là treo cả phần mềm.
- Lệnh mới ghi file ra định dạng mới thì thêm đuôi đó vào `BridgePathPolicy.AllowedExtensions`, và trường đầu ra
  không bắt đầu bằng `output` thì thêm vào danh sách trong `BridgeCommitGuard.IsOutputField` — thiếu là preview
  qua Bridge hỏng (`E-PREVIEW-CHANGED`, như `bcfPath` từng bị).
- Action trong workflow ghim theo commit SHA kèm chú thích phiên bản (`uses: owner/action@<sha> # vX.Y.Z`);
  `actions/checkout` đặt `persist-credentials: false`. Dependabot cập nhật SHA.
- Lớp AI phải giữ offline: endpoint model chỉ loopback, không thêm SDK cloud, không commit API key.
- Thay đổi ở `DhcbTools.Core`/`DhcbTools.Core.AutoCAD` ảnh hưởng cả Ribbon lẫn HTTP Bridge — kiểm
  tra cả hai đường gọi trước khi merge.
- Lệnh ghi: xem trước và chạy thật đi **cùng một vòng** quyết định (cái gì ghi, cái gì bỏ qua), chỉ khác chỗ có ghi
  hay không — để con số xem trước khớp chạy thật. Lệnh AutoCAD mở entity/attribute để ghi thì tra
  `AcadHelpers.LockedLayerIds` và báo bằng `LockedLayerSkips` (mở trên layer khoá ném `eOnLockedLayer`, sập cả lệnh);
  tra theo handle thì xét `ObjectId.IsErased` trước `GetObject`. Nhớ `AttributeReference` không nằm trong
  `BlockTableRecord` — lặp entity thì xét thêm attribute của block reference.
- Panel AutoCAD: route phát token (`/panel`) chỉ mở bằng khoá khởi chạy trong `%LOCALAPPDATA%` — đừng thêm đường
  nào khác trả token hay HTML của panel mà không đòi khoá.
- Thêm script vào `scripts/` thì xếp loại luôn: script người dùng chạy được từ thư mục cài đặt thì thêm vào
  `installer/batchrunner-scripts.txt` (release chỉ chép đúng danh sách này); script repo/CI/ký số thì thêm vào
  `NOT_SHIPPED` trong `tests/python/test_package_scripts.py` — chưa xếp loại là test đỏ.

## Tài liệu liên quan

- [`docs/roadmap.md`](docs/roadmap.md) — lộ trình theo giai đoạn.
- [`docs/progress.md`](docs/progress.md) — hiện trạng và danh sách lỗi đã biết.
- [`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md) — khảo sát kỹ thuật.

## Giấy phép và gói chính thức

Chủ dự án đã chọn Apache-2.0 ngày 2026-10-08. Giữ LICENSE/NOTICE trong các gói ZIP, MCPB và installer.
Phát hành từ tag yêu cầu chữ ký xác minh `Valid`; nếu chưa có chứng chỉ, `sign-release.ps1 -RequireSignature`
sẽ chặn thay vì phát hành gói chưa ký. Build thủ công không dùng tag vẫn có thể tạo gói dev chưa ký.
Không commit PFX, mật khẩu hoặc thay kho chứng chỉ của máy để vượt điều kiện này.

Fixture truy vấn/PDF trong `tools/acceptance/` chỉ dùng cho nghiệm thu trên bản sao, không cài vào bundle.
