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
│   ├── App.cs                     # IExternalApplication — khởi động cả Ribbon + HTTP Bridge
│   ├── DhcbTools.Revit.addin
│   ├── Bridge/DhcbHttpBridge.cs   # HttpListener port 8765 — agent AI gọi lệnh Core qua HTTP
│   ├── Bridge/BridgeToken.cs      # token Bearer bắt buộc cho /execute và /query
│   ├── Commands/                  # IExternalCommand — vỏ mỏng gọi Core (kể cả Export/Health/MEPF/ProjectInit)
│   └── UI/                        # WPF config windows
│
├── DhcbTools.Core.AutoCAD/        # Core AutoCAD — logic thuần, KHÔNG Editor/WPF
│   ├── ICoreCommand.cs            # Database + config → CommandResult
│   ├── CommandResult.cs
│   ├── LayerSync/                 # #1: xuất/nhập layer qua CSV (≈ ParameterSync)
│   ├── DrawingCleanup/            # #2: dọn layer/block/linetype thừa (≈ ModelCleanup)
│   ├── AutoNumbering/             # #3: đánh số Block Reference theo attribute tag
│   └── Query/                     # đọc ngữ cảnh drawing qua Bridge (không transaction ghi)
│
└── DhcbTools.AutoCAD/             # Vỏ AutoCAD: IExtensionApplication, CommandMethod
    ├── App.cs                     # IExtensionApplication — khởi tạo plugin + HTTP Bridge
    ├── Bridge/DhcbHttpBridge.cs   # HttpListener port 8766 — agent AI gọi lệnh Core qua HTTP
    └── Commands/DhcbCommands.cs   # 4 lệnh: DHCB_LAYER_EXPORT/IMPORT, DHCB_CLEANUP, DHCB_AUTONUMBER
```

`DhcbTools.Core/Export`, `Health`, `MEPF`, `ProjectInit`, `Query` (Revit) — nhóm lệnh mở rộng từ
khung nền tảng: batch export PDF/DWG/IFC/NWC, health report HTML, sleeve/tag cao độ/hanger/chia
ống/connector checker cho MEPF, khởi tạo dự án (grid/level/family/project info), và query đọc
ngữ cảnh model qua Bridge.

`scripts/dhcb_agent.py` — client Python (không cần dependency ngoài) gọi HTTP Bridge từ terminal,
Hermes, hoặc bất kỳ agent AI nào:

```bash
python scripts/dhcb_agent.py revit Cleanup --dry-run
python scripts/dhcb_agent.py autocad LayerExport --output C:/tmp/layers.csv
```

**Xác thực:** Bridge sinh token ngẫu nhiên lần đầu khởi động và lưu ở
`%APPDATA%\DhcbTools\bridge-token.txt`. Client tự đọc file này; muốn ghi đè thì đặt biến môi
trường `DHCB_BRIDGE_TOKEN`. Mọi request `/execute` và `/query` phải kèm header
`Authorization: Bearer <token>`; riêng `GET /health` vẫn mở để kiểm tra add-in có sống không.

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

**Đã xong**
- Khung solution 2-trong-1 (Revit + AutoCAD), tách Core (logic thuần) khỏi vỏ UI.
- 3 nhóm lệnh nền tảng trên cả hai nền tảng: đồng bộ dữ liệu qua CSV, dọn dẹp, đánh số hàng loạt.
- HTTP Bridge cho agent AI: Revit port 8765, AutoCAD port 8766, kèm client `scripts/dhcb_agent.py`.
- Batch export (PDF/DWG/IFC/NWC), health report, khởi tạo dự án (grid/level/family/project info).
- MEPF phần nền tảng: sleeve tại giao cắt, tag cao độ, hanger, chia ống, connector checker —
  cả 5 lệnh đều có nút Ribbon và gọi được qua HTTP Bridge.
- Giai đoạn 0 (trả nợ kỹ thuật): sửa nhóm lỗi sai âm thầm (CSV locale/BOM, mất cảnh báo, dung sai
  đánh số, cleanup AutoCAD), token xác thực cho Bridge, huỷ việc khi client hết thời gian chờ.

**Đang tới** — test tự động (`DhcbTools.Core.Tests`), tách `DhcbTools.Shared` để bỏ phần trùng lặp
giữa hai Core, batch runner chạy đêm theo lịch, routing MEPF (mức A/B/C), `IUpdater` chạy theo
sự kiện, lớp AI.

Chi tiết:
- [`docs/progress.md`](docs/progress.md) — hiện trạng đầy đủ và danh sách lỗi đã biết.
- [`docs/roadmap.md`](docs/roadmap.md) — lộ trình theo giai đoạn.
- [`docs/nghien-cuu-dhcb-revit-tools.md`](docs/nghien-cuu-dhcb-revit-tools.md) — khảo sát kỹ thuật.
