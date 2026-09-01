# Khảo sát tool nổi tiếng, giới hạn công nghệ và kế hoạch giai đoạn 7

> Ngày khảo sát: 2026-09-01. Nguồn ở cuối tài liệu. Mục đích: đối chiếu DHCB Tools với các tool phổ biến để tìm
> **khoảng trống thật sự có giá trị**, ghi rõ **giới hạn công nghệ** để không hứa điều API không cho phép, rồi
> chốt kế hoạch code theo thứ tự ưu tiên.

## 1. Tool nổi tiếng và điều DHCB học được

### 1.1 Revit

| Tool | Điểm mạnh được người dùng nhắc nhiều | DHCB đã có | Khoảng trống |
|---|---|---|---|
| **pyRevit** (open source) | Sheets panel: copy/move viewport, pin viewport, đổi số/tên sheet hàng loạt; Set Revisions on Sheets; Wipe (view template, filter, tag rỗng, arrowhead không dùng); Pick theo category; Match override | Xuất/nhập CSV, dọn view/sheet, AutoNumbering | **Đổi tên sheet/view theo mẫu**, **gán revision lên nhiều sheet**, **purge style không dùng** (template, filter, pattern, text/dim type) |
| **DiRootsOne** (ProSheets, SheetLink, FamilyReviser, OneFilter, ParaManager) | ProSheets: tự nhận khổ giấy/hướng, đặt tên file theo tham số, **in theo lịch**; SheetLink: Excel ↔ model theo category/schedule; FamilyReviser: đổi tên/dọn family hàng loạt | BatchExport (mẫu tên), ParameterExport/Import, batch runner theo lịch | **Kiểm kê + đổi tên family/type theo quy tắc**, xuất theo **schedule** |
| **Ideate** (BIMLink, Explorer, StyleManager, Sticky, Automation) | Explorer: tìm/lọc/đếm/chọn phần tử + xem warning theo phần tử; StyleManager: phân tích "style đang được ai dùng" rồi xoá/merge an toàn; Automation: chạy nền | Bridge `/query`, HealthReport, batch runner | **Xuất warning ra CSV kèm ElementId** để lọc trong Excel, **phân tích tham chiếu trước khi purge style** |
| **Naviate MEP / Fabrication** | Ống dốc, kick-90, hanger layout, sleeve hàng loạt, spool + BOM | Hanger, Sleeve, PipeSplitter | Ống dốc (P2), BOM theo hệ (P2) |
| **MagiCAD** | Routing thông minh + **tính toán** (sizing, cân bằng, âm) + thư viện sản phẩm | Routing A/B, Sizing đề xuất | Cân bằng/áp suất (ngoài phạm vi — cần dữ liệu sản phẩm) |
| **Victaulic Tools** | Chia ống theo cây khi vẽ, xoay fitting theo góc, BOM spool | PipeSplitter | BOM spool (P2) |
| **eVolve MEP** | Auto-route né va chạm, clash | PathFinder3D (thuần), ClashDetection | Lệnh Core nối PathFinder3D → RouteFromLines (P2) |
| **Autodesk Model Checker** | **Checkset** cấu hình được, báo cáo tuân thủ, zoom tới phần tử, theo dõi dung lượng | HealthReport, ParameterRuleCheck | Checkset "ngưỡng" (số warning, số view, dung lượng, family in-place) — gộp vào RuleCheck |
| **Colour Splasher** (free, rất phổ biến) | Tô màu phần tử theo **giá trị bất kỳ tham số** trong view | SystemColor (chỉ theo hệ) | **Tô màu theo tham số bất kỳ** (tổng quát hoá SystemColor) |
| **Autodesk Assistant + Revit 2027 MCP Server** (Tech Preview) | Hỏi đáp mô hình, sinh view/sheet, MCP **chỉ đọc**, 6 nhóm tool | Bridge + MCP server có cả đọc và ghi (ghi phải xác nhận) | Cờ **read-only** cho MCP để tương đồng Autodesk khi cần; **gom tool theo nhóm** để model nhỏ chọn đúng |
| **RevitBatchProcessor** (open source) | Batch nhiều file, **tự chọn phiên bản Revit theo file**, script Python/Dynamo | BatchRunner theo `revitVersion` cố định | **Tự nhận phiên bản Revit từ header .rvt** rồi mở đúng Revit.exe |

### 1.2 AutoCAD

| Tool | Điểm mạnh | DHCB đã có | Khoảng trống |
|---|---|---|---|
| **Lee Mac** (BatchAttributeEditor, MacAtt, AttModSuite, Layer Director, Steal, Copy2Drawings…) | Sửa attribute nhiều block **nhiều bản vẽ** với **giá trị tăng dần**; trích attribute cả thư mục; đổi layer tự động theo lệnh | AttributeExport/Import, AutoNumbering, batch accoreconsole | Attribute **increment theo mẫu** (`P-{n:000}`), quét **cả thư mục** (đã có qua batch) |
| **CAD Standards / DWS / Batch Standards Checker / Layer Translator (LAYTRANS)** | So bản vẽ với chuẩn (layer, linetype, text/dim style), báo cáo HTML nhiều file; **map layer cũ → layer chuẩn** và lưu mapping | LayerStandardCheck, LayerImport | **LayerTranslate** theo bảng mapping CSV (đổi tên/merge layer, đổi thuộc tính entity), kiểm **text/dim style** |
| **Drawing Compare** (AutoCAD) | Thấy gì thay đổi giữa hai bản | — | **DrawingCompare** offline: so hai DWG theo handle (thêm/xoá/đổi layer/đổi vị trí) → CSV/HTML |
| **Batch plot / publish** | In PDF nhiều bản vẽ | — | **PlotPdf** trong batch accoreconsole bằng `-PLOT` không hộp thoại |
| **Purge sâu** | Purge cả dimstyle/textstyle/regapp/mlinestyle | DrawingCleanup (layer/block/linetype) | Mở rộng cleanup cho **text style, dim style, regapp** |
| **Data extraction / BOM** | Đếm block theo tên/attribute ra bảng | AttributeExport | **BlockQuantity**: đếm theo block + attribute nhóm → CSV |

## 2. Giới hạn công nghệ hiện tại (ràng buộc thiết kế)

| Giới hạn | Nguồn | Hệ quả cho DHCB |
|---|---|---|
| **Revit API không thread-safe**, mọi truy cập (kể cả đọc) phải trên main thread qua `ExternalEvent` | Tammik "never ever thread safe", Revit.Async | Bridge giữ mô hình hàng đợi + `ExternalEvent`; không cho phép truy vấn song song; MCP server chỉ là client HTTP |
| **Không có Revit headless chính thức**; batch local phải qua journal/add-in (RevitBatchProcessor) hoặc **Design Automation** trên cloud | RBP, APS Design Automation | Batch chạy đêm giữ cách add-in + `pending-job`; Design Automation nằm ngoài phạm vi "offline" |
| **.NET 8 hết hỗ trợ 10/11/2026**, Autodesk đang preview di trú Revit 2025/2026 lên **.NET 10** | APS blog | Giữ `Shared.*` ở netstandard2.0; thêm biến `-p:RevitVersion` cho TFM để đổi sang net10.0-windows khi Autodesk phát hành |
| **Revit 2027 MCP Server chỉ đọc** (tech preview), MCP client tích hợp 6 nhóm tool | Autodesk help/blog | DHCB MCP: thêm `--read-only`; ghi luôn qua `confirm:true`; gom tool theo nhóm giống Autodesk để dễ chuyển |
| **accoreconsole**: không hộp thoại, không File Dialog, không Express Tools, NETLOAD chỉ với DLL tham chiếu `acdbmgd`/`accoremgd` (không `acmgd`) | ADN blog, forum | Vỏ AutoCAD hiện tham chiếu `AutoCAD.NET` meta (có acmgd) → **tách `DhcbTools.AutoCAD.Core`** chỉ dùng acdbmgd/accoremgd cho `DHCB_RUN`; plot bằng `-PLOT` dòng lệnh |
| **Local LLM tool-calling yếu với > ~8 tool, mô tả dài, JSON dễ lệch**; Qwen3/Qwen2.5 ổn nhất; **structured outputs** (format = JSON Schema) ép cú pháp hợp lệ; gemma3 không hỗ trợ tool | Ollama docs, benchmark | `OllamaClient` dùng **JSON Schema** thay `format:"json"`; MCP/`/chat` gửi **≤ 8 tool ứng viên** đã lọc bằng heuristic; mặc định `qwen3`; luôn validate lại bằng whitelist |
| Revit API: `CopyElements` không copy được LineStyles/ObjectStyles; IUpdater chạy trong mọi transaction | Đã gặp ở giai đoạn 2/4 | Đã ghi rõ trong Messages; updater tắt mặc định |
| AutoCAD .NET: `Database.ReadDwgFile` cho side database (đọc file khác không mở) — đủ cho Drawing Compare/steal | AutoCAD .NET docs | DrawingCompare/LayerTranslate đọc DWG thứ hai offline |

## 3. Kế hoạch giai đoạn 7 (theo khoảng trống có giá trị nhất)

Nguyên tắc chọn: (a) nhiều người dùng tool khác vì thiếu nó, (b) làm được offline trong giới hạn API, (c) có phần thuần
để test. Mỗi mục đều: Core command + `CommandCatalog` + Ribbon/CommandMethod + test phần thuần + cập nhật tài liệu.

### P1 — làm ngay (đợt này)

| # | Tính năng | Nền tảng | Học từ | Phần thuần (test) |
|---|---|---|---|---|
| 7.1 | `SheetRename`: đổi số/tên sheet và view theo mẫu token (`{Number}`, `{Name}`, `{Level}`, regex tìm/thay, bộ đếm `{n:000}`), xem trước, chống trùng số | Revit | pyRevit Sheets, DiRoots | `NamePattern` |
| 7.2 | `RevisionOnSheets`: gán/bỏ một revision cho nhiều sheet theo lọc số sheet | Revit | pyRevit Set Revisions | — (API mỏng) |
| 7.3 | `StylePurge`: liệt kê style **không được tham chiếu** (view template, filter, line pattern, fill pattern, text/dim type, material) và xoá có xem trước | Revit | Ideate StyleManager, pyRevit Wipe | `UsageDecider` dùng lại `CleanupDecider` |
| 7.4 | `ColorByParameter`: tô màu phần tử trong view theo giá trị một tham số (palette tự sinh, chú giải CSV) | Revit | Colour Splasher | `PaletteGenerator` |
| 7.5 | `FamilyAudit`: kiểm kê family/type (số instance, in-place, không dùng), đổi tên theo mẫu, ghi CSV | Revit | DiRoots FamilyReviser | `NamePattern` |
| 7.6 | `WarningsExport`: warning → CSV (mô tả, mức, ElementId, category) để lọc; gộp đếm theo loại | Revit | Ideate Explorer | — |
| 7.7 | `ModelCheckset` trong `ParameterRuleCheck`: ngưỡng số warning, số view chưa đặt, dung lượng, in-place, link thiếu → cùng báo cáo | Revit | Autodesk Model Checker | `ThresholdRule` |
| 7.8 | `LayerTranslate`: map layer cũ → chuẩn theo CSV (đổi entity, merge/xoá layer cũ, đặt thuộc tính chuẩn) | AutoCAD | LAYTRANS | `LayerMapTable` |
| 7.9 | `DrawingCompare`: so bản vẽ hiện tại với DWG khác (thêm/xoá/đổi layer/đổi vị trí theo handle) → CSV/HTML | AutoCAD | Drawing Compare | `DiffSummary` |
| 7.10 | `BlockQuantity`: đếm block theo tên (+ nhóm theo attribute) → CSV BOM | AutoCAD | Data Extraction | — |
| 7.11 | `AttributeIncrement`: gán giá trị tăng dần theo mẫu cho attribute, thứ tự theo vị trí | AutoCAD | Lee Mac BATTE | `NamePattern` + `NumberingPlanner` |
| 7.12 | DrawingCleanup mở rộng: text style, dim style, regapp không dùng | AutoCAD | Purge | `CleanupDecider` |
| 7.13 | Batch: **tự nhận phiên bản Revit từ header .rvt**, chọn Revit.exe theo file; `PlotPdf` bằng `-PLOT` trong script accoreconsole | Batch | RevitBatchProcessor, batch plot | `RvtFileInfo`, `AcadScriptGen.PlotPdf` |
| 7.14 | AI: `OllamaClient` structured outputs (JSON Schema), gợi ý lệnh bằng model chỉ trong **≤ 8 ứng viên** lọc trước, mặc định `qwen3`; MCP `--read-only`, tool gom nhóm | AI | Ollama, Revit 2027 MCP | `CommandIntentParser.Candidates` |

### P2 — sau khi P1 chạy trên máy thật

- Ống dốc (slope) + kick-90 cho routing A; BOM/spool theo hệ; lệnh Core nối `PathFinder3D` → `RouteFromLines`.
- Vỏ AutoCAD "core-only" (`acdbmgd`/`accoremgd`) để `DHCB_RUN` chắc chắn chạy trong accoreconsole mọi phiên bản.
- Xuất theo **schedule** (SheetLink), copy viewport giữa sheet.
- Design Automation (cloud) là lựa chọn ngoài phạm vi offline; chỉ tài liệu hoá cách cắm `RevitCommandTable` vào `DesignAutomationBridge`.

### Thứ tự code

1. Phần thuần + test: `NamePattern`, `PaletteGenerator`, `LayerMapTable`, `DiffSummary`, `RvtFileInfo`, `AcadScriptGen.PlotPdf`, `ThresholdRule`, structured-output/candidates.
2. Core Revit 7.1–7.7 → `RevitCommandTable` + catalog.
3. Core AutoCAD 7.8–7.12 → `AcadCommandTable` + catalog.
4. Vỏ: Ribbon (panel *Hồ sơ & Style*), CommandMethod, BatchRunner (7.13), MCP/agent (7.14).
5. Tài liệu + kiểm thử thủ công bổ sung vào `dac-ta-kiem-thu.md`.

## Nguồn

- pyRevit: [pyrevitlabs extensions](https://www.pyrevitlabs.io/extensions/), [20 pyRevit features — BIM Pure](https://www.bimpure.com/blog/20-amazing-pyrevit-features-to-save-insane-amounts-of-time), [Top 20 pyRevit tools — RD Studio](https://rdstudio.co/blogs/news/pyrevit-best-20-tools)
- DiRoots: [ProSheets — Autodesk App Store](https://apps.autodesk.com/RVT/en/Detail/Index?appLang=en&id=7448218013676619378&os=Win64), [DiRootsOne](https://diroots.com/revit-plugins/dirootsone/)
- Ideate: [Ideate StyleManager help](https://support.ideatesoftware.com/support/help/ideate-stylemanager), [Most commonly used Revit plugins](https://support.ideatesoftware.com/blog/most-commonly-used-revit-plugins)
- MEP: [MagiCAD routing tools](https://www.magicad.com/using-magicads-routing-tools-to-double-the-productivity-within-the-revit-platform/), [Victaulic Tools for Revit](https://www.victaulic.com/blog/piping-system-design-in-half-the-time-with-victaulic-tools-for-revit/), [eVolve MEP](https://evolvemep.com/blog/how-the-revit-add-on-evolve-simplifies-mep-design-and-coordination), [Naviate MEP](https://www.naviate.com/naviate-for-revit/naviate-mep/), [Automated MEP analysis Revit 2026 — AU](https://www.autodesk.com/autodesk-university/class/Extending-Automated-MEP-Analysis-in-Revit-2026-2025)
- Model Checker: [Autodesk Model Checker for Revit](https://interoperability.autodesk.com/modelchecker.php)
- Autodesk AI/MCP: [Revit Public MCP Server (Tech Preview)](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-WhatsNew/files/GUID-97697CBF-0E11-484E-96E5-4277E3E8D61F.htm), [Usage of Revit 2027 MCP Server](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Usage-of-Revit-2027-MCP-Server.html), [AEC Tech Drop — Revit Public MCP Server](https://www.autodesk.com/blogs/aec/2026/06/17/revit-public-mcp-server/)
- Giới hạn API: [The Revit API is never ever thread safe](https://jeremytammik.github.io/tbc/a/1244_no_multithreading.htm), [Revit.Async](https://github.com/KennanChan/Revit.Async), [Revit 2026/2025 migration to .NET 10 — APS](https://aps.autodesk.com/blog/call-preview-testing-revit-20262025-migration-net-10), [RevitBatchProcessor](https://github.com/bvn-architecture/RevitBatchProcessor), [Getting started with AccoreConsole — ADN](https://blog.autodesk.io/getting-started-with-accoreconsole/), [AccoreConsole guide 2026](https://fdestech.com/resources/accoreconsole-guide-headless-cad-automation/)
- AutoCAD: [Lee Mac Batch Attribute Editor](https://lee-mac.com/batte.html), [Global Attribute Extractor](https://www.lee-mac.com/macatt.html), [CAD Standards tools — Engineering.com](https://www.engineering.com/how-to-use-autocads-cad-standards-tools/), [Enforce standards with DWS — Novedge](https://novedge.com/blogs/design-news/autocad-tip-enforce-autocad-standards-at-scale-with-dws-and-scripts), [AutoCAD 2026 features](https://www.autodesk.com/eu/products/autocad/features)
- Local LLM: [Ollama structured outputs](https://docs.ollama.com/capabilities/structured-outputs), [Ollama tool support](https://ollama.com/blog/tool-support), [Which Ollama models support tool calling](https://www.betterclaw.io/blog/ollama-models-tool-calling-support), [Best Ollama model for tool calling 2026](https://webscraft.org/blog/yaku-model-ollama-obrati-dlya-agenta-z-tool-calling-porivnyannya-i-benchmarki?lang=en), [Structured output reliability in small LMs (arXiv 2605.02363)](https://arxiv.org/pdf/2605.02363)
