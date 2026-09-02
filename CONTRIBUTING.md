# Đóng góp cho DHCB Tools

Quy ước lấy từ repo [donghanh](https://github.com/seeker19110/donghanh), rút gọn cho quy mô
hiện tại của dự án này (add-in C#, một người làm, chưa có CI). Phần nào chưa áp dụng được ngay
thì ghi rõ là việc của [Giai đoạn 0 trong roadmap](docs/roadmap.md#giai-đoạn-0--trả-nợ-kỹ-thuật).

## Luồng làm việc

**Idea → Branch → Commit → Pull request → Review → Merge.**

1. Mỗi tính năng hoặc sửa lỗi một nhánh riêng, đặt tên `feat/<slug>`, `fix/<slug>`, hoặc
   `docs/<slug>` (ví dụ `feat/batch-runner`, `fix/parameter-import-double`).
2. **Không push thẳng vào `main`** — kể cả khi làm một mình. Mọi thay đổi đi qua pull request.
3. Commit nhỏ, mỗi commit một thay đổi logic.
4. Mở PR, để CI (khi đã có — xem Giai đoạn 0) chạy qua trước khi merge.

## Merge PR — thử auto-merge, không được thì tự theo dõi

Quy ước lấy từ `donghanh` (mục 11 `CLAUDE.md`), áp dụng ngay cả khi repo này chưa có CI:

1. **Tạo PR ở trạng thái sẵn sàng** (không để nháp) rồi **thử bật auto-merge (squash) ngay**,
   một lần, không hỏi lại.
2. Auto-merge thường thất bại nếu repo chưa bật "Allow auto-merge" ở Settings → General → Pull
   Requests (đây là tình trạng hiện tại của Dhcb-Tools) — đó không phải lỗi cần chẩn đoán, cứ đi
   thẳng sang bước tiếp theo.
3. **Kiểm trạng thái PR** (`mergeable_state` + danh sách check, nếu có):
   - **Repo chưa có CI** (như hiện tại — không có `.github/workflows`, `get_status` trả
     `total_count: 0`): không có gì để chờ chuyển xanh. `mergeable_state: clean` (không xung
     đột) là đủ điều kiện — merge (squash) ngay, không polling vô ích.
   - **Repo đã có CI** (từ khi Giai đoạn 0 dựng xong): poll trạng thái PR mỗi ~2,5 phút. Mọi
     check bắt buộc xanh + không xung đột → merge (squash) ngay, không chờ người bấm tay. Còn
     check đang chạy → tiếp tục poll. Có check đỏ → dừng, đọc log, sửa và push lại, không merge.
4. **Không merge tay để đi tắt khi có check đang đỏ.** Chỉ merge khi xanh thật hoặc khi xác nhận
   không có CI nào để chờ.
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

Bắt buộc (CI chạy đúng hai việc này, kể cả trên Linux không cài Revit/AutoCAD):

```bash
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj
./scripts/check-build.sh      # biên dịch toàn bộ Core + vỏ bằng API package NuGet (Revit/AutoCAD 2025)
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
