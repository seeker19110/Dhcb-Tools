# DHCB Revit Tools

Add-in Revit (C#, chạy trên Revit desktop) giúp kỹ sư tự động hoá các tác vụ lặp lại. Xem nghiên cứu đầy đủ và lộ trình tại [`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md).

## Cấu trúc solution

```
Dhcb-Tools.sln
Directory.Build.props        # multi-target: net48 (Revit 2021-2024) / net8.0-windows (Revit 2025+)
src/
├── DhcbTools.Core/           # logic thuần: Document + config → xử lý → CommandResult
│   │                         # KHÔNG TaskDialog, KHÔNG Selection, KHÔNG WPF — chạy được cả
│   │                         # từ Ribbon lẫn từ batch runner sau này mà không viết lại.
│   ├── ICoreCommand.cs
│   ├── CommandResult.cs
│   ├── SilentFailuresPreprocessor.cs
│   ├── ParameterSync/        # Lệnh #1: xuất/nhập tham số qua CSV (Excel mở trực tiếp)
│   ├── ModelCleanup/         # Lệnh #2: dọn view không đặt trên sheet + sheet rỗng
│   └── AutoNumbering/        # Lệnh #3: đánh số hàng loạt theo vị trí hình học
└── DhcbTools.Revit/          # vỏ desktop: Ribbon tab "DHCB Tools", TaskDialog, cửa sổ WPF
    ├── App.cs                # IExternalApplication — tạo Ribbon
    ├── DhcbTools.Revit.addin # manifest nạp add-in vào Revit
    ├── Commands/              # IExternalCommand — vỏ mỏng gọi vào Core
    └── UI/                    # cửa sổ WPF nhập cấu hình (ví dụ đánh số tự động)
```

Ba lệnh đầu tiên đúng theo lộ trình trong tài liệu nghiên cứu (mục "Lộ trình triển khai", bước 1):
**xuất/nhập tham số qua CSV**, **dọn view/sheet thừa**, **đánh số tự động**.

## Build

Yêu cầu Visual Studio 2022 (hoặc `dotnet build`) trên **Windows** — Revit API chỉ chạy trên Windows nên
solution này không build/test được trong môi trường Linux hiện tại của phiên làm việc.

```powershell
dotnet build Dhcb-Tools.sln -p:RevitVersion=2024
```

`RevitVersion` quyết định TargetFramework (xem `Directory.Build.props`) và version của package
`Nice3point.Revit.Api.*` được resolve (các package này đóng gói lại `RevitAPI.dll`/`RevitAPIUI.dll`
theo từng năm Revit, không cần cài Revit trên máy build).

## Triển khai (dev)

Sau khi build, copy hoặc symlink các file sau vào `%ProgramData%\Autodesk\Revit\Addins\<version>\`:

- `DhcbTools.Revit.addin`
- `DhcbTools.Revit.dll` + `DhcbTools.Core.dll` (và dependency đi kèm)

## Trạng thái

Khung solution + 3 lệnh nền tảng đã dựng xong theo lộ trình. Các bước tiếp theo (batch runner,
IUpdater, MEPF, tích hợp AI) nằm trong tài liệu nghiên cứu, mục "Lộ trình triển khai".
