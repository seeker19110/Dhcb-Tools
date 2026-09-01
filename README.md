# DHCB Tools — Revit & AutoCAD

Add-in **2-trong-1** (C#) tự động hoá các tác vụ lặp lại cho kỹ sư xây dựng, chạy trực tiếp trên
**Revit desktop** và **AutoCAD desktop**. Xem nghiên cứu đầy đủ và lộ trình tại
[`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md).

## Cấu trúc solution

```
Dhcb-Tools.sln
Directory.Build.props              # multi-target: net48 / net8.0-windows; AcadRoot/RevitVersion
src/
├── DhcbTools.Core/                # Core Revit — logic thuần, KHÔNG TaskDialog/WPF
│   ├── ICoreCommand.cs            # Document + config → CommandResult
│   ├── CommandResult.cs
│   ├── SilentFailuresPreprocessor.cs
│   ├── ParameterSync/             # #1: xuất/nhập tham số qua CSV
│   ├── ModelCleanup/              # #2: dọn view/sheet thừa
│   └── AutoNumbering/             # #3: đánh số hàng loạt theo vị trí hình học
│
├── DhcbTools.Revit/               # Vỏ Revit: Ribbon, TaskDialog, WPF
│   ├── App.cs                     # IExternalApplication
│   ├── DhcbTools.Revit.addin
│   ├── Commands/                  # IExternalCommand — vỏ mỏng gọi Core
│   └── UI/                        # WPF config windows
│
├── DhcbTools.Core.AutoCAD/        # Core AutoCAD — logic thuần, KHÔNG Editor/WPF
│   ├── ICoreCommand.cs            # Database + config → CommandResult
│   ├── CommandResult.cs
│   ├── LayerSync/                 # #1: xuất/nhập layer qua CSV (≈ ParameterSync)
│   ├── DrawingCleanup/            # #2: dọn layer/block/linetype thừa (≈ ModelCleanup)
│   └── AutoNumbering/             # #3: đánh số Block Reference theo attribute tag
│
└── DhcbTools.AutoCAD/             # Vỏ AutoCAD: IExtensionApplication, CommandMethod
    ├── App.cs                     # IExtensionApplication — khởi tạo plugin
    └── Commands/DhcbCommands.cs   # 4 lệnh: DHCB_LAYER_EXPORT/IMPORT, DHCB_CLEANUP, DHCB_AUTONUMBER
```

### Tương đồng giữa hai nền tảng

| Revit                  | AutoCAD                  | Chức năng                          |
|------------------------|--------------------------|------------------------------------|
| `ParameterExport`      | `LayerExport`            | Xuất dữ liệu → CSV                 |
| `ParameterImport`      | `LayerImport`            | Nhập CSV → ghi vào model/drawing   |
| `RemoveUnusedViews`    | `DrawingCleanup`         | Dọn object thừa                    |
| `AutoNumbering`        | `AutoNumbering`          | Đánh số hàng loạt theo toạ độ      |

## Build

Yêu cầu Visual Studio 2022 (hoặc `dotnet build`) trên **Windows**.

```powershell
# Build Revit (2021-2024)
dotnet build Dhcb-Tools.sln -p:RevitVersion=2024

# Build AutoCAD 2024
dotnet build Dhcb-Tools.sln -p:AcadVersion=2024

# Build tất cả cùng lúc (mỗi app một lần)
dotnet build src/DhcbTools.Revit/DhcbTools.Revit.csproj      -p:RevitVersion=2024
dotnet build src/DhcbTools.AutoCAD/DhcbTools.AutoCAD.csproj  -p:AcadVersion=2024
```

**Packages NuGet dùng thay cho DLL local:**
- Revit: `Nice3point.Revit.Api.RevitAPI` — không cần cài Revit trên máy build
- AutoCAD: `AutoCAD.NET` (Autodesk chính thức) — không cần cài AutoCAD trên máy build

## Triển khai (dev)

### Revit
Copy vào `%ProgramData%\Autodesk\Revit\Addins\<version>\`:
- `DhcbTools.Revit.addin`
- `DhcbTools.Revit.dll` + `DhcbTools.Core.dll`

### AutoCAD
Trong AutoCAD, gõ lệnh `NETLOAD` và chọn `DhcbTools.AutoCAD.dll`.  
Hoặc thêm vào `%AppData%\Autodesk\ApplicationPlugins\` để tự động load.

Các lệnh AutoCAD sau khi load:
| Lệnh | Chức năng |
|------|-----------|
| `DHCB_LAYER_EXPORT` | Xuất toàn bộ layer ra CSV |
| `DHCB_LAYER_IMPORT` | Nhập layer từ CSV vào drawing |
| `DHCB_CLEANUP`      | Dọn layer rỗng, block/linetype không dùng |
| `DHCB_AUTONUMBER`   | Đánh số hàng loạt Block Reference |

## Trạng thái

Khung solution 2-trong-1 (Revit + AutoCAD) đã dựng xong. Các bước tiếp theo (HTTP Bridge để
kết nối trực tiếp từ agent AI, batch runner, MEPF, IUpdater) nằm trong tài liệu nghiên cứu.
