# Phát hành v1.1 và nhóm kỹ sư dùng thật (mục 9.4)

**Bản cho kỹ sư cài: `v1.1.1`** — <https://github.com/seeker19110/Dhcb-Tools/releases/tag/v1.1.1>
(v1.1.0 cùng ngày có installer treo khi cài im lặng, §46; mã add-in giống hệt). `release.yml` đóng gói và
đăng lên GitHub Releases. Gói gồm `DhcbTools-Setup-1.1.1.exe`
(Inno Setup, cài add-in Revit 2023–2025, AutoCAD 2024–2026 và BatchRunner vào
`%LOCALAPPDATA%\Programs\DHCB Tools`) và các zip rời cho từng phiên bản.

## Đã đổi so với v1.0.0 (90 commit, 2026-09-02 → 09-06)

- **Chạy thật lần đầu trên dự án thật** (dự án A, 8 file 7–167 MB): batch đêm, nâng cấp bản sao 2024, sửa
  treo TaskDialog, nạp lại link sau SaveAs; ba lệnh MEPF mù model liên kết (Sleeve 0 → 345, Clash 0 → 7,
  Device 0 → 551) đã sửa.
- **Giai đoạn 11 theo NĐ 207/2026**: chuỗi băm nhật ký (`--verify-log`), kiểm IFC trước nộp (`--verify-ifc`),
  kiểm IDS 1.0 trên mô hình Revit **và** trên chính file IFC (khớp IfcTester 10/10), gói bàn giao đêm
  `ban-giao.html` có ô xác nhận chủ đầu tư, lệnh `SheetIndex`.
- **AutoRoute mức D** (chui qua lỗ mở), A* nhanh 400 lần, thất bại biết nói.
- **Từ điển tham số tự học** (`DictionaryLearn`), tiền đề `E-PRECOND`, số liệu sử dụng (`UsageReport`), form
  động, Ribbon AutoCAD, agent khép vòng qua Bridge/MCP.
- Nền .NET 10 cho AutoCAD 2026 / Revit 2027, CI phủ 100% dòng.

## Cài cho kỹ sư

1. Chạy `DhcbTools-Setup-1.1.1.exe` (không cần quyền admin; cài vào hồ sơ người dùng).
2. Mở Revit → hộp "Unsigned Add-In" chọn *Always Load*. Tab **DHCB** xuất hiện.
3. Hướng dẫn kiểm thử tay nằm trong gói: `huong-dan-cai-dat-va-kiem-thu-thu-cong.md`.

## Nhóm kỹ sư đề xuất

Mục tiêu roadmap: **≥ 5 kỹ sư dùng hằng tuần** sau v1.1. Chọn 6 người để trừ hao, theo vai chứ không theo tên:

| # | Vai | Vì sao | Lệnh nên thử tuần đầu |
|---|---|---|---|
| 1–2 | Kiến trúc, đang ở giai đoạn DD/CD có sheet | Duy nhất nhóm có sheet thật → `SheetIndex`, `BatchExport` PDF, gói bàn giao | `SheetRename`, `RevisionOnSheets`, `SheetIndex`, `HealthReport` |
| 3–4 | MEP (HVAC + ống) | Nhóm lệnh sâu nhất và nhiều lỗi đã sửa trên dữ liệu thật | `SleeveAuto`, `HangerAuto`, `AutoRoute`, `ClashDetection`, `SlopePipes` |
| 5 | BIM manager / điều phối | Người ký gói bàn giao, chạy batch đêm, đọc IDS | `IdsValidate`, `ParameterRuleCheck`, `DictionaryLearn`, BatchRunner + Task Scheduler |
| 6 | AutoCAD 2D (shop drawing) | Nhánh AutoCAD chưa có phản hồi người thật | `LayerStandardCheck`, `BlockQuantity`, `AttributeIncrement`, `DrawingCompare` |

Điền tên vào cột "Vai" khi chọn xong; phần còn lại giữ nguyên.

## Thu phản hồi

- Mẫu: [`mau-phan-hoi-9-4.md`](mau-phan-hoi-9-4.md) — mỗi người một bản, tick *tuần / bỏ / chưa* cho từng lệnh,
  bắt buộc ghi lý do khi tick *bỏ*.
- Số liệu máy: `UsageReport` đọc log 30 ngày của từng máy (bản cài kế tiếp trở đi mới có log).
- Mốc: thu sau **2 tuần** và **4 tuần**; tổng hợp theo lệnh (không theo người) như hướng dẫn cuối mẫu.
- Kết quả quyết định giai đoạn 10/11 đi sâu vào đâu; `AsBuiltStamp`/`DossierIndex` (11.6) chỉ làm khi nhóm 1–2 và 5 xác nhận cần.
