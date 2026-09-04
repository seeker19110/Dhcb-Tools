# Batch runner — chạy đêm không người trực

Giai đoạn 1 của [`roadmap.md`](roadmap.md): biến toàn bộ lệnh Core thành giá trị chạy đêm. Một file job +
một task hẹn giờ là đủ để sáng hôm sau có PDF, health report, log kiểm tra và bản tóm tắt cảnh báo.

## Thành phần

| Thành phần | Ở đâu | Việc |
|---|---|---|
| `DhcbTools.BatchRunner.exe` | `src/DhcbTools.BatchRunner` (net8.0, không tham chiếu Revit/AutoCAD) | Đọc job, mở Revit / accoreconsole, gom log, xuất báo cáo HTML, trả mã thoát |
| `BatchJobRunner` | `src/DhcbTools.Core/Batch` | Bên trong Revit: mở → chạy step qua `RevitCommandTable` → lưu → đóng, ghi log JSONL của lượt chạy |
| Hook trong `App.cs` | `src/DhcbTools.Revit` | Khi Revit khởi động, thấy `%APPDATA%\DHCB\pending-job.json` thì chạy job rồi thoát |
| `DHCB_RUN` | `src/DhcbTools.AutoCAD.Core` (vỏ core-only) | Lệnh không hỏi gì, đọc step JSON, ghi log JSONL — dùng trong script accoreconsole |
| `JobTokens`, `BatchJob`, `RunLog`, `BatchReport`, `AcadScriptGen` | `Shared.Logic/Batch` | Phần thuần, có test |

## File job

Xem [`jobs/nightly.sample.json`](../jobs/nightly.sample.json) (Revit) và
[`jobs/autocad-nightly.sample.json`](../jobs/autocad-nightly.sample.json) (AutoCAD).

- `app`: `revit` (mặc định) hoặc `autocad`.
- `saveMode`: `None` (đóng không lưu) · `Save` · `SaveAs` (lưu bản sao vào `outputFolder`, **mặc định**, không đụng bản gốc).
  ⚠️ **Đổi hành vi:** bên AutoCAD, `Save` nay **lưu đè file gốc thật** bằng `SAVEAS`; trước đây nó chỉ ghi một dòng log
  mà không lưu gì. Job cũ đang để `Save` vì tưởng vô hại thì phải đổi sang `None`/`SaveAs` trước khi chạy lại.
- `saveOnError` (bool, mặc định `false`): batch Revit **không lưu** file có bước lỗi. Đặt `true` nếu muốn giữ lại phần
  đã làm được của file lỗi.
- `dwgVersion` (chuỗi, mặc định `"2018"`): phiên bản DWG cho `SAVEAS` bên AutoCAD.
- `files[]`: `path`, `detachFromCentral`, `worksets` (chỉ mở các workset này), `onlySteps` (lọc step cho riêng file).
- `steps[]`: `command` = đúng `CommandName` của Core (xem `dhcb_agent.py revit tools`), `config` = config của lệnh,
  `skipIfPreviousFailed`.
- Token trong chuỗi config: `{outputFolder}`, `{fileName}`, `{yyyy-MM-dd}`, `{HH-mm}`, và token tự khai báo trong `tokens`.

## Chạy

```powershell
# Revit: add-in DhcbTools.Revit phải được cài cho đúng phiên bản trong job (revitVersion)
DhcbTools.BatchRunner.exe --job jobs\nightly.json --log-dir D:\DHCB\logs --max-minutes 480 --analyze

# AutoCAD: dùng accoreconsole.exe (có sẵn trong mọi bản AutoCAD, không UI)
DhcbTools.BatchRunner.exe --job jobs\autocad-nightly.json --accoreconsole "C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe" --plugin-dll D:\DHCB\bin\DhcbTools.AutoCAD.dll

# Chỉ dựng lại báo cáo + tóm tắt cảnh báo từ log đã có
DhcbTools.BatchRunner.exe --job jobs\nightly.json --report-only --analyze

# Diễn tập: ép mọi step dryRun:true và không lưu
DhcbTools.BatchRunner.exe --job jobs\nightly.json --dry-run
```

Kết quả trong `logs/{yyyy-MM-dd}/`: **`run-HHmmss.jsonl`** (mỗi dòng một step; **mỗi lần chạy một file riêng**, không
gộp chung theo ngày như bản `run.jsonl` cũ), `report.html` (bảng file × step, xanh/đỏ, bấm xem chi tiết),
`warnings-summary.md` (khi `--analyze`, xem [`ai-offline.md`](ai-offline.md)).

`--report-only` lấy **file log mới nhất** trong thư mục ngày, và vẫn đọc được `run.jsonl` cũ nên log của các đêm trước
không mất giá trị. Bước AutoCAD dựng script trong thư mục làm việc `acad-steps-HHmmss` (trước là `acad-steps`) nên hai
lượt chạy trong cùng một ngày không giẫm lên nhau.

Mã thoát: `0` mọi step thành công · `1` có step lỗi/bỏ qua · `2` lỗi cấu hình (không đọc được job, không tìm thấy Revit).

## Luồng Revit

1. Runner ghi `pending-job.json` (đường dẫn job, đường dẫn file log của lượt chạy, max-minutes, dryRun) và một journal
   tối giản tắt hộp thoại lỗi.
2. Runner mở `Revit.exe journal /nosplash` và chờ `batch-done.json`.
3. Add-in, trong `ApplicationInitialized`, thấy pending-job → `BatchJobRunner.Run` → ghi `batch-done.json` → `Environment.Exit`.
   Mọi transaction dùng `SilentFailuresPreprocessor` nên không có hộp thoại treo máy.
4. Quá `--max-minutes`: runner dừng sạch sau file đang chạy; file chưa kịp chạy được ghi `skipped` vào log.

Cũng có nút **AI offline & Batch → Chạy job batch** trên Ribbon để chạy cùng job ngay trong phiên Revit đang mở
(job ở `%APPDATA%\DHCB\configs\revit\batch-job.json`).

## Giai đoạn 7 (học từ RevitBatchProcessor và batch plot)

- **Tự nhận phiên bản Revit theo file:** runner đọc header `.rvt` (`RvtFileInfo`) và mở đúng `Revit <năm>\Revit.exe`;
  nhiều phiên bản khác nhau trong một job → dùng bản cao nhất và cảnh báo. Tắt bằng `--no-autodetect`.
- **Step `PlotPdf` cho AutoCAD:** không phải lệnh Core; runner sinh chuỗi `-PLOT` không hộp thoại vào script accoreconsole
  (`outputPath`, `layout`, `paperSize`, `orientation`, `plotArea`, `plotStyle`). Xem `jobs/autocad-nightly.sample.json`.

- **Vỏ core-only cho accoreconsole (P2):** `DhcbTools.AutoCAD.Core.dll` chỉ tham chiếu AcDbMgd/AcCoreMgd nên NETLOAD được
  trong Core Console mọi phiên bản (vỏ đầy đủ tham chiếu AcMgd có thể bị từ chối). Runner tự ưu tiên DLL này nếu nằm cạnh
  `DhcbTools.BatchRunner.exe`; hoặc chỉ định bằng `--plugin-dll`.

## Hẹn giờ

```powershell
.\scripts\install-nightly-task.ps1 -Job "D:\DHCB\jobs\nightly.json" -RunnerExe "D:\DHCB\bin\DhcbTools.BatchRunner.exe" -LogDir "\\server\dhcb\logs" -Time 23:00 -Analyze
```

Task chạy dưới tài khoản đang đăng nhập (có license). Last Run Result ≠ 0 là tín hiệu cảnh báo.

## Idempotent

Chạy lại cùng job hai lần cho kết quả như nhau khi các lệnh để `dryRun:true` hoặc chỉ đọc (HealthReport, BatchExport,
ConnectorChecker, ClashDetection, RuleCheck). Lệnh ghi (`SleeveAuto`, `HangerAuto`…) chỉ nên bật `dryRun:false` sau khi
đã xem log dryRun của đêm trước.
