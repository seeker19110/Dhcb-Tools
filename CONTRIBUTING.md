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

Hai workflow trong [`.github/workflows/`](.github/workflows/):

**`tests.yml` — chạy mọi push vào `main` và **mọi pull request**.** Ba job:

| Job | Máy | Làm gì |
|---|---|---|
| `logic-tests` | ubuntu-latest | `dotnet restore/build/test` bộ `DhcbTools.Shared.Logic.Tests` (Release), tải kết quả `.trx` lên artifact `test-results` |
| `check-build` | ubuntu-latest, ma trận `2025` / `2024` / `2023` | Build `BatchRunner` + biên dịch Core và cả bốn vỏ (Revit, AutoCAD, AutoCAD core-only) bằng API package NuGet với `UseWPF=false`. Riêng nhánh `2025` còn chạy `py_compile` cho `scripts/*.py` + `tools/autocad-mcp-server/*.py`, `unittest discover` cho gateway panel, và một bước kiểm cú pháp JavaScript trong `panel.html` |
| `build-wpf-windows` | windows-latest, ma trận Revit `2025` / `2024` / `2023` | Build **thật có WPF** vỏ Revit — bật WPF thì SDK bỏ `System.IO` khỏi implicit usings, nên job Linux ở trên không bắt được lỗi đó |

Ma trận ba phiên bản là cố ý: lỗi chỉ xảy ra trên net48 (`Dictionary.GetValueOrDefault`) hoặc chỉ trên
Revit ≤ 2023 (`ElementId.Value`) từng lọt tới tận bước phát hành khi CI chỉ build 2025.

**`release.yml` — CD, chỉ chạy khi đẩy tag `vX.Y.Z` hoặc bấm tay (`workflow_dispatch`).** Trên
windows-latest: build Release thật (có WPF) cho Revit 2023/2024/2025 và AutoCAD 2024/2025 + vỏ core-only,
đóng gói zip kèm `jobs/`, `configs/`, `scripts/`, dựng installer Inno Setup rồi tạo GitHub Release.
Không chạy trên PR nên **không phải chờ nó** khi merge.

## Merge PR — thử auto-merge, không được thì tự theo dõi

Quy ước lấy từ `donghanh` (mục 11 `CLAUDE.md`):

1. **Tạo PR ở trạng thái sẵn sàng** (không để nháp) rồi **thử bật auto-merge (squash) ngay**,
   một lần, không hỏi lại.
2. Auto-merge thất bại nếu repo chưa bật "Allow auto-merge" ở Settings → General → Pull Requests —
   đó không phải lỗi cần chẩn đoán, cứ đi thẳng sang bước tiếp theo.
3. **Chờ check xanh.** Poll trạng thái PR mỗi ~2,5 phút. `tests.yml` chạy trên mọi PR nên luôn có
   check để chờ: **toàn bộ job của `tests.yml` xanh + không xung đột → merge (squash)**. Còn job
   đang chạy → tiếp tục poll. Có job đỏ → dừng, mở log của job đó, sửa và push lại, **không merge**.
4. **Không merge tay để đi tắt khi có check đang đỏ hoặc đang chạy.**
5. Nếu `main` tiến lên gây xung đột (`mergeable_state: dirty`) trong lúc chờ, merge `main` vào
   nhánh, giải xung đột, rồi mới tiếp tục từ bước 3.

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
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -c Release
./scripts/check-build.sh      # biên dịch toàn bộ Core + vỏ bằng API package NuGet (Revit/AutoCAD 2025)
```

Có sửa phần Python (`scripts/`, `tools/autocad-mcp-server/`) thì chạy thêm — CI cũng chạy hai việc này:

```bash
pip install -r requirements-dev.txt          # pytest + pyflakes + fastmcp
python3 -m pytest tools/autocad-mcp-server -q
python3 -m pyflakes scripts/*.py tools/autocad-mcp-server/*.py
```

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
- Lớp AI phải giữ offline: endpoint model chỉ loopback, không thêm SDK cloud, không commit API key.
- Thay đổi ở `DhcbTools.Core`/`DhcbTools.Core.AutoCAD` ảnh hưởng cả Ribbon lẫn HTTP Bridge — kiểm
  tra cả hai đường gọi trước khi merge.

## Tài liệu liên quan

- [`docs/roadmap.md`](docs/roadmap.md) — lộ trình theo giai đoạn.
- [`docs/progress.md`](docs/progress.md) — hiện trạng và danh sách lỗi đã biết.
- [`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md) — khảo sát kỹ thuật.
