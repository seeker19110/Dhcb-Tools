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

# Kiểm chuỗi băm của một log đã ghi (không cần job, không mở Revit/AutoCAD)
DhcbTools.BatchRunner.exe --verify-log logs\2026-09-04\run-013000.jsonl
```

Kết quả trong `logs/{yyyy-MM-dd}/`: **`run-HHmmss.jsonl`** (mỗi dòng một step; **mỗi lần chạy một file riêng**, không
gộp chung theo ngày như bản `run.jsonl` cũ), `report.html` (bảng file × step, xanh/đỏ, bấm xem chi tiết),
`warnings-summary.md` (khi `--analyze`, xem [`ai-offline.md`](ai-offline.md)).

`--report-only` lấy **file log mới nhất** trong thư mục ngày, và vẫn đọc được `run.jsonl` cũ nên log của các đêm trước
không mất giá trị. Bước AutoCAD dựng script trong thư mục làm việc `acad-steps-HHmmss` (trước là `acad-steps`) nên hai
lượt chạy trong cùng một ngày không giẫm lên nhau.

Mã thoát: `0` mọi step thành công · `1` có step lỗi/bỏ qua · `2` lỗi cấu hình (không đọc được job, không tìm thấy Revit).

## Chuỗi băm của nhật ký (`--verify-log`)

Mỗi dòng trong `run-HHmmss.jsonl` mang thêm hai trường ở cuối:

| Trường | Nghĩa |
|---|---|
| `prevHash` | Băm của **dòng ngay trước** trong cùng file. Dòng đầu tiên mang 64 số 0 |
| `hash` | SHA-256 của **chính dòng đó**, tính trên phần đứng trước trường `hash` |

Sửa một dòng cũ làm gãy chuỗi từ dòng đó trở đi. Kiểm lại bất cứ lúc nào:

```bash
DhcbTools.BatchRunner.exe --verify-log logs\2026-09-04\run-013000.jsonl
```

Mã thoát: `0` nguyên vẹn · `1` chuỗi hỏng (in ra **đúng số thứ tự dòng** hỏng) · `2` không có file. Bốn kết luận
có thể gặp:

| Kết luận | Nghĩa |
|---|---|
| *Chuỗi băm nguyên vẹn* | Mọi dòng khớp và nối liền nhau |
| *Dòng N đã bị sửa* | Băm ghi trong dòng không khớp nội dung của chính dòng đó |
| *Chuỗi đứt tại dòng N* | `prevHash` không khớp băm của dòng trước — có dòng bị chèn, xoá hoặc đảo chỗ. Cũng là kết luận khi người sửa **biết thuật toán** và đã tính lại băm cho riêng dòng họ sửa |
| *Dòng N chưa mang chuỗi băm* | Log ghi bằng bản cài trước tính năng này, hoặc dấu vết đã bị gỡ |

Gắn dấu vết nằm ở `RunLog.Append` — điểm ghi duy nhất của cả batch Revit lẫn AutoCAD — nên không có đường ghi
nào lọt ra ngoài. Trường mới là phần thêm vào cuối dòng JSON, nên `report.html`, `--analyze` và log của các đêm
trước vẫn đọc bình thường.

> **Chuỗi băm chứng minh cái gì và không chứng minh cái gì.** Nó chứng minh **tính toàn vẹn nội bộ**: ai sửa một
> dòng mà không tính lại toàn bộ chuỗi từ đó về sau thì bị phát hiện, và phát hiện ở đúng dòng nào. Nó **không**
> chứng minh log do ai ghi và ghi lúc nào — người có quyền ghi file vẫn dựng lại được cả chuỗi. Theo
> **NĐ 207/2026/NĐ-CP**, nhật ký thi công điện tử cần đủ ba điều kiện: ① dấu thời gian không thể chỉnh sửa ngược ·
> ② cơ chế xác nhận của các bên · ③ sao lưu độc lập. DHCB làm được ①, và tạo điều kiện cho ② (chữ ký số của các
> bên) và ③ (sao lưu của chủ đầu tư). Đừng bán nó như chữ ký số.

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
