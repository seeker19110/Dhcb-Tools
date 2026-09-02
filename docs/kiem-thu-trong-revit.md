# Kiểm thử chạy bên trong Revit

Giai đoạn 8.3 của [`roadmap.md`](roadmap.md). Giải quyết đúng một lỗ hổng: **toàn bộ `DhcbTools.Core` — mọi dòng chạm
Revit API — không có test tự động nào**, trong khi 360 test xUnit chỉ phủ `Shared.Logic` thuần. Một bộ test xanh mà
không đụng tới phần rủi ro nhất thì con số đó không nói lên điều gì.

Revit không có chế độ headless chính thức, nhưng batch runner đã mở được Revit không người ngồi máy. Bộ test đi đúng
đường đó: một lệnh Core (`RunTests`) gọi từng lệnh khác qua `RevitCommandTable` trên model mẫu rồi đối chiếu kỳ vọng.

## Chạy

```powershell
DhcbTools.BatchRunner.exe --job jobs\in-revit-tests.json --log-dir D:\DHCB\logs
```

Mã thoát khác 0 khi có ca trượt, nên cắm thẳng vào Task Scheduler hoặc một CI tự dựng. Kết quả:

| File | Dùng để |
|---|---|
| `in-revit-tests.trx` | CI/Visual Studio đọc như mọi bộ test khác |
| `in-revit-tests.md` | Dán vào [`bang-chung-test.md`](bang-chung-test.md); ca trượt xếp lên đầu |

Chạy qua Bridge khi đang mở Revit (nhanh hơn lúc viết ca mới):

```bash
python scripts/dhcb_agent.py revit exec RunTests --config '{"suitePath":"D:/DHCB/tests/suites/revit-smoke.json"}'
```

## Viết một ca kiểm

Bộ ca kiểm là JSON — mẫu ở [`tests/suites/revit-smoke.json`](../tests/suites/revit-smoke.json).

```json
{
  "name": "Xuất tham số ra CSV",
  "command": "ParameterExport",
  "config": { "categories": ["Doors"], "outputPath": "{outputFolder}/doors.csv" },
  "expect": {
    "success": true,
    "minAffected": 1,
    "maxMs": 60000,
    "filesExist": ["{outputFolder}/doors.csv"]
  }
}
```

Token `{outputFolder}`, `{fileName}`, `{yyyy-MM-dd}` giống hệt file job của batch runner.

### Kỳ vọng

| Trường | Ý nghĩa |
|---|---|
| `success` | `CommandResult.Success` (mặc định `true`) |
| `minAffected` / `maxAffected` | Chặn dưới/trên số phần tử bị ảnh hưởng |
| `summaryContains` | Summary phải chứa (không phân biệt hoa thường) |
| `messagesContain` | Ít nhất một dòng `Messages` chứa |
| `neverContains` | **Không** dòng `Messages`/`Errors` nào được chứa — bắt no-op im lặng, ví dụ `"không có tham số"` |
| `noErrors` | `Errors` phải rỗng |
| `maxMs` | Ngưỡng thời gian — lưới bắt hồi quy hiệu năng |
| `filesExist` | File kết quả phải tồn tại sau khi chạy |

**Vì sao khai báo kỳ vọng thay vì so file vàng nguyên vẹn.** `Summary` chứa số đếm phụ thuộc model, nên so từng ký tự
sẽ đỏ hàng loạt mỗi lần đổi model mẫu — rồi người ta sẽ tắt bộ test đi. Kỳ vọng dạng "phải thành công", "ít nhất N",
"có chứa chuỗi này" bắt đúng lỗi thật mà không giòn.

`maxMs` và `neverContains` là hai kỳ vọng đáng viết nhất, vì chúng bắt đúng hai loại lỗi mà giai đoạn 8.1 vừa sửa:

- `SleeveAuto` dựng `FilteredElementCollector` toàn model bên trong vòng lặp → vượt timeout 30 s của Bridge.
- Lệnh báo *thành công* nhưng không làm gì vì thiếu tham số/family, chỉ ghi một dòng trong `Messages`.

## An toàn với model mẫu

Hai lớp khoá, phải mở cả hai thì mới ghi được vào model:

1. Ca kiểm phải khai `"allowWrite": true`.
2. Người chạy phải đặt `"allowWrites": true` trong config của `RunTests`.

Mặc định mọi ca bị ép `dryRun = true`, nên chạy bao nhiêu lần trên cùng model cũng không làm bẩn nó. Job mẫu còn đặt
`saveMode: "None"` để chắc chắn không lưu đè.

Trong lúc chạy, `RunTests` đặt `FailurePolicy.SuppressWarnings`: cảnh báo Revit không hiện hộp thoại (sẽ treo phiên
không người), nhưng **được ghi lại** và đưa vào `Messages` với tiền tố `[Cảnh báo Revit]`, nên `neverContains` soi
được cả cảnh báo.

## Tầng thuần có test riêng

`Shared.Logic/Testing` (đọc bộ ca kiểm, đánh giá kỳ vọng, dựng TRX/Markdown) là phần quyết định một ca đạt hay trượt,
nên chính nó có test trong [`TestingTests.cs`](../tests/DhcbTools.Shared.Logic.Tests/TestingTests.cs) chạy trên CI
Linux — nếu không thì "bộ test xanh" lại là một con số không ai kiểm chứng được.

## Còn thiếu

- Model mẫu mới chỉ có bản kiến trúc (Snowdon Towers) nên các ca MEPF (`SleeveAuto`, `HangerAuto`, `RouteFromLines`,
  `SlopePipes`, `PipeKick`) đang `skip`. Cần một model MEP mẫu — việc của giai đoạn 8.4.
- Mới phủ các lệnh đọc và xem trước. Ca ghi thật (`allowWrite`) cần model dùng một lần rồi bỏ.
