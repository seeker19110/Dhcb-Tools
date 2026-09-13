## Tóm tắt

<!-- Thay đổi gì, tại sao, phát hiện thế nào (đặc biệt nếu là fix). -->

-

## Loại thay đổi

- [ ] feat
- [ ] fix
- [ ] refactor
- [ ] docs
- [ ] test
- [ ] chore/ci
- [ ] breaking change

## Đã kiểm tra

<!-- Lệnh đã chạy trước khi mở PR, xem CONTRIBUTING.md#lệnh-kiểm-tra-trước-khi-mở-pr -->

- [ ] `dotnet test ... DhcbTools.Shared.Logic.Tests.csproj` + `scripts/check-coverage.py` (nếu sửa `src/`/`tests/`)
- [ ] `./scripts/check-build.sh` (nếu sửa code build được bằng API package NuGet)
- [ ] `python3 -m coverage run -m pytest -q` + `pyflakes` (nếu sửa `scripts/*.py`, `tools/autocad-mcp-server/`)
- [ ] Chạy thật trên Revit/AutoCAD (nếu sửa hành vi lệnh) — model/version nào:

## Rủi ro / rollback

<!-- Bỏ trống nếu là docs/test thuần. -->
