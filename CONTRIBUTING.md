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

Hiện tại chưa có test/CI (xem `docs/progress.md`), nên tối thiểu:

```powershell
dotnet build src/DhcbTools.Revit/DhcbTools.Revit.csproj      -p:RevitVersion=2024
dotnet build src/DhcbTools.AutoCAD/DhcbTools.AutoCAD.csproj  -p:AcadVersion=2024
```

Khi `DhcbTools.Core.Tests` được thêm (Giai đoạn 0 của roadmap), bổ sung `dotnet test` vào danh
sách này và coi là bắt buộc trước khi merge — giống cách donghanh dùng `npm run test:coverage`
làm required check.

## Quy tắc an toàn

- Không commit file cấu hình/credential cá nhân (đường dẫn cài Revit/AutoCAD máy riêng, API key).
- Mọi lệnh sửa mô hình phải giữ `DryRun` mặc định bật và chạy trong một transaction duy nhất
  (xem "Nguyên tắc xuyên suốt" trong [`docs/roadmap.md`](docs/roadmap.md)).
- HTTP Bridge hiện chưa có xác thực (`docs/progress.md`, lỗi #8) — không mở cổng 8765/8766 ra
  ngoài máy cá nhân cho tới khi có token.
- Thay đổi ở `DhcbTools.Core`/`DhcbTools.Core.AutoCAD` ảnh hưởng cả Ribbon lẫn HTTP Bridge — kiểm
  tra cả hai đường gọi trước khi merge.

## Tài liệu liên quan

- [`docs/roadmap.md`](docs/roadmap.md) — lộ trình theo giai đoạn.
- [`docs/progress.md`](docs/progress.md) — hiện trạng và danh sách lỗi đã biết.
- [`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md) — khảo sát kỹ thuật.
