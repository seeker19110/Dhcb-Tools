# Kiểm thử chạy bên trong Revit

Giai đoạn 8.3 của [`roadmap.md`](roadmap.md). Giải quyết đúng một lỗ hổng: **toàn bộ `DhcbTools.Core` — mọi dòng chạm
Revit API — không có test tự động nào**, trong khi 360 test xUnit chỉ phủ `Shared.Logic` thuần. Một bộ test xanh mà
không đụng tới phần rủi ro nhất thì con số đó không nói lên điều gì.

Revit không có chế độ headless chính thức, nhưng batch runner đã mở được Revit không người ngồi máy. Bộ test đi đúng
đường đó: một lệnh Core (`RunTests`) gọi từng lệnh khác qua `RevitCommandTable` trên model mẫu rồi đối chiếu kỳ vọng.

## Chạy

Cách nhanh nhất — một lệnh làm trọn vòng (build → cài add-in → dựng job → chạy → in báo cáo):

```powershell
.\scripts\run-in-revit-tests.ps1 -Suite mep
```

Script **dừng ngay nếu Revit đang mở**: Revit khoá DLL add-in khi chạy, và batch runner cần tự mở
Revit của riêng nó. Đóng Revit rồi chạy lại.

Hoặc gọi thẳng batch runner với file job tự viết:

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

Có sẵn hai bộ:

| Bộ | Model mẫu | Phủ |
|---|---|---|
| [`revit-smoke.json`](../tests/suites/revit-smoke.json) | Snowdon Towers Sample Architectural | Health, tham số, cảnh báo, family, view/sheet, style, schedule, xuất bản vẽ, khởi tạo dự án, kiểm tra, AI offline |
| [`revit-mep.json`](../tests/suites/revit-mep.json) | Snowdon Towers Sample HVAC | Connector, sleeve, cao độ, hanger, chia ống, routing, sizing, BOM, kick, clash |
| [`revit-plumbing.json`](../tests/suites/revit-plumbing.json) | Snowdon Towers Sample Plumbing | Dốc ống, sizing ống, BOM ống, chia ống, cao độ ống |

Hai bộ cộng lại phủ **đủ 42/42 lệnh Revit** — `SuiteCoverageTests` (chạy trên CI, không cần Revit) đỏ ngay
khi thêm lệnh mới mà quên ca kiểm, nên con số này không trôi khỏi tài liệu được nữa.

Cả ba model đều đi kèm Revit (`C:\Program Files\Autodesk\Revit 2024\Samples`), nên không cần chuẩn bị gì thêm.

Bộ thứ ba có lý do rõ ràng: model HVAC chỉ có duct, nên trên đó `SlopePipes` và các lệnh về **ống** chỉ chạy
được đường lỗi ("không có ống nào khớp bộ lọc"). Đường thành công của chúng cần model cấp thoát nước.

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

Token `{outputFolder}`, `{fileName}`, `{yyyy-MM-dd}` giống hệt file job của batch runner, cộng thêm
`{suiteFolder}` — thư mục chứa chính file bộ ca kiểm. Lệnh cần file đầu vào (CSV trục/level, CSV sheet,
thuyết minh…) đọc từ [`tests/suites/fixtures/`](../tests/suites/fixtures/) qua token này, thay vì viết
đường dẫn tuyệt đối chỉ đúng trên một máy.

Ca của lệnh cần đầu vào còn có thể **dùng lại kết quả của ca trước**: `ParameterImport` đọc chính file
`{outputFolder}/doors.csv` mà `ParameterExport` vừa ghi, và `ApplySizing` đọc `sizing.csv` của
`SizingProposal`. Vòng tròn xuất → nhập phải là **không đổi ô nào** (`maxAffected: 0`) — đó là chốt chặn
cho lỗi đã sửa ở PR #29, khi `ParameterImport` ghi đè mọi ô vì coi giá trị giống hệt là "đã đổi".

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

## Ký số add-in — bắt buộc để chạy không người trực

Revit hỏi trước khi nạp add-in chưa ký số, bằng hộp thoại **`Security - Unsigned Add-In`**. Ba điều khiến
nó là vấn đề thật chứ không phải phiền toái nhỏ:

1. Hộp thoại **chặn hẳn** việc nạp add-in — không bấm thì add-in không bao giờ chạy.
2. Journal **không tắt được** loại hộp thoại này (`PerformAutomaticActionInErrorDialog` chỉ áp cho hộp
   thoại lỗi), nên batch không người trực đứng chờ tới hết giờ.
3. Revit nhớ lựa chọn *Always Load* theo **chữ ký của file**, nên **mỗi lần build lại là hỏi lại**.

Hệ quả: trước khi ký số, batch chạy đêm không thể chạy tự động được. Vòng kiểm thử thật đầu tiên
(2026-09-03) treo 10 phút rưỡi đúng vì chuyện này, và runner khi đó lại báo nhầm là "chưa cài add-in".

### Ký trên máy dev

```powershell
.\scripts\sign-addin.ps1 -RevitVersion 2024
```

Script tự tạo (hoặc dùng lại) một chứng chỉ **tự ký**, cài vào kho `CurrentUser\Root` và
`CurrentUser\TrustedPublisher` — không cần quyền admin — rồi ký mọi `DhcbTools*.dll` trong thư mục
add-in và kiểm lại bằng `Get-AuthenticodeSignature`.

Chứng chỉ được **dùng lại giữa các lần build**. Tạo mới mỗi lần thì Revit coi là nhà phát hành khác và
lại hỏi — đúng cái đang muốn tránh.

### Giới hạn của chứng chỉ tự ký

| | Chứng chỉ tự ký | Chứng chỉ thương mại (OV/EV) |
|---|---|---|
| Máy đã cài chứng chỉ | Tin cậy | Tin cậy |
| Máy khác | **Vẫn hỏi** | Tin cậy |
| Chi phí | 0 | Có phí, cần xác minh danh tính |

Tự ký giải quyết được máy dev và máy chạy batch của công ty. **Phát hành cho kỹ sư khác thì phải có
chứng chỉ thương mại** — khi có, truyền vào bằng `-PfxPath`:

```powershell
.\scripts\sign-addin.ps1 -PfxPath C:\certs\dhcb.pfx -PfxPassword (Read-Host -AsSecureString)
```

Trong CI, giữ `.pfx` ở GitHub Secrets rồi ký ở bước đóng gói của `release.yml`; **không commit `.pfx`
vào repo**.

### Khi vẫn thấy hộp thoại

`scripts/run-in-revit-tests.ps1` theo dõi song song và báo ngay nếu thấy hộp thoại này, thay vì để
runner ngồi chờ hết giờ. Thấy báo thì: mở Revit bằng tay một lần, chọn *Always Load*, đóng Revit, chạy
lại — hoặc chạy `sign-addin.ps1` để khỏi gặp lại.

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

- Mới phủ các lệnh đọc và xem trước. Ca ghi thật (`allowWrite`) cần một model dùng một lần rồi bỏ.
- `RouteFromLines`, `DevicePlacement`, `AutoRoute`, `PipeKick` chưa có ca: cần model có sẵn model line/phòng
  đúng dạng đầu vào, không có sẵn trong model mẫu của Autodesk.
- Chưa chạy được vòng đầu trên máy thật vì Revit đang mở lúc dựng xong bộ test — chạy
  `scripts/run-in-revit-tests.ps1` khi đóng Revit (giai đoạn 8.4).
