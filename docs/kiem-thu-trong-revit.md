# Kiểm thử chạy bên trong Revit và AutoCAD

Giai đoạn 8.3 của [`roadmap.md`](roadmap.md). Giải quyết đúng một lỗ hổng: **toàn bộ `DhcbTools.Core` và
`DhcbTools.Core.AutoCAD` — mọi dòng chạm API của Revit/AutoCAD — không có test tự động nào**, trong khi
mấy trăm test xUnit chỉ phủ `Shared.Logic` thuần. Một bộ test xanh mà không đụng tới phần rủi ro nhất thì
con số đó không nói lên điều gì.

Revit không có chế độ headless chính thức, nhưng batch runner đã mở được Revit không người ngồi máy; AutoCAD
thì có sẵn `accoreconsole`. Bộ test đi đúng hai đường đó: một lệnh Core tên `RunTests` gọi từng lệnh khác qua
`RevitCommandTable` / `AcadCommandTable` trên file mẫu rồi đối chiếu kỳ vọng. **Hai nền tảng dùng chung tầng
đánh giá** `Shared.Logic/Testing` (`TestSuite`, `TestExpectation`, `TestReport`), nên cách viết ca kiểm y hệt
nhau và tầng đó có test riêng trên CI.

## Chạy

Cách nhanh nhất — một lệnh làm trọn vòng (build → cài add-in → dựng job → chạy → in báo cáo):

```powershell
.\scripts\run-in-revit-tests.ps1 -Suite mep        # smoke | mep | plumbing
.\scripts\run-in-autocad-tests.ps1                 # bên AutoCAD, qua accoreconsole
```

Script Revit **chờ tới 120 s cho Revit đóng hẳn** rồi mới bỏ cuộc: Revit khoá DLL add-in khi chạy, và tiến
trình của lượt trước còn sống vài chục giây sau khi batch kết thúc — chạy ba bộ nối đuôi nhau trong một
lệnh vẫn được.

Script AutoCAD không cần đóng AutoCAD: `accoreconsole` là tiến trình riêng, không dùng chung DLL với giao
diện đang mở.

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
| [`revit-smoke.json`](../tests/suites/revit-smoke.json) | Snowdon Towers Sample Architectural | Health, tham số, cảnh báo, family, view/sheet, style, schedule, xuất bản vẽ, toạ độ định vị (cột + giao trục), tiến độ thi công, khởi tạo dự án, kiểm tra, AI offline |
| [`revit-mep.json`](../tests/suites/revit-mep.json) | Snowdon Towers Sample HVAC | Connector, sleeve, cao độ, hanger, chia ống, routing, sizing, BOM, kick, clash, toạ độ định vị thiết bị (`PENZDI`, mm), tiến độ theo hệ (% theo chiều dài) |
| [`revit-plumbing.json`](../tests/suites/revit-plumbing.json) | Snowdon Towers Sample Plumbing | Dốc ống, sizing ống, BOM ống, chia ống, cao độ ống |
| [`autocad-smoke.json`](../tests/suites/autocad-smoke.json) | Data Extraction and Multileaders Sample (kèm AutoCAD) | Đủ **15/15 lệnh AutoCAD**: layer, attribute, text, chuẩn layer, trục, xref, block, so bản vẽ, dọn dẹp, map layer |
| [`revit-write.json`](../tests/suites/revit-write.json) · [`autocad-write.json`](../tests/suites/autocad-write.json) | Bản chép của model/bản vẽ mẫu | **Đường ghi thật** — xem mục dưới |

Các bộ cộng lại phủ **đủ 46/46 lệnh Revit** có ca kiểm (43 đã chạy thật; `SetoutExport`, `ConstructionStatus`, `ProgressReport` thêm 2026-09-05 chờ lượt chạy đầu) — `SuiteCoverageTests` (chạy trên CI, không cần Revit) đỏ ngay
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

Bộ AutoCAD có thêm `{sourceFile}` — đường dẫn đầy đủ của chính bản vẽ đang mở; nhờ nó `DrawingCompare` tự
so bản vẽ với chính nó (phải ra "0 layer khác nhau") mà không cần commit file DWG nào vào repo.

Ca của lệnh cần đầu vào còn có thể **dùng lại kết quả của ca trước**: `ParameterImport` đọc chính file
`{outputFolder}/doors.csv` mà `ParameterExport` vừa ghi, `ApplySizing` đọc `sizing.csv` của
`SizingProposal`, và bên AutoCAD `LayerImport`/`AttributeImport` đọc CSV của `LayerExport`/`AttributeExport`.
Vòng tròn xuất → nhập phải là **không đổi ô nào** (`maxAffected: 0`) — đó là chốt chặn cho lỗi đã sửa ở
PR #29 (`ParameterImport` ghi đè mọi ô vì coi giá trị giống hệt là "đã đổi"), và chính nó bắt lại đúng lỗi
ấy ở `LayerImport` và `AttributeImport` trong vòng chạy AutoCAD đầu tiên.

**Luôn viết ca song sinh cho vòng tròn.** Một mình `maxAffected: 0` vẫn xanh nếu ai đó làm lệnh luôn trả 0.
Bộ AutoCAD vì thế có thêm ca *"nhập CSV đổi đúng một ô"* với `minAffected: 1, maxAffected: 1` — hai ca cạnh
nhau mới chứng minh phép so sánh **phân biệt được**, chứ không chỉ im lặng.

## Đường ghi thật

Mọi ca ở các bộ trên đều chạy **xem trước**. Nhưng phần đáng lo nhất của một lệnh lại nằm ở đoạn sau
`transaction.Commit()` — và một bộ test chỉ xem trước thì không bao giờ chạm tới đó.

Hai bộ `*-write.json` là nơi duy nhất ghi thật. **Ba lớp khoá, phải đủ cả ba mới ghi:**

1. ca phải khai `"allowWrite": true`;
2. người chạy phải bật `-AllowWrites`;
3. script **chép file mẫu sang thư mục kết quả** và chạy trên bản chép — model/bản vẽ gốc kèm
   Revit/AutoCAD nằm trong `Program Files`, hỏng là phải cài lại phần mềm.

```powershell
.\scripts\run-in-revit-tests.ps1  -Suite write -AllowWrites
.\scripts\run-in-autocad-tests.ps1 -Suite write -AllowWrites
```

Thiếu `-AllowWrites` thì script **dừng có thông báo** thay vì chạy bộ `write` ở chế độ xem trước — một
lượt như thế chỉ lặp lại việc bộ smoke đã làm, mà báo cáo vẫn xanh, tức là dối.

### Bản chép mang theo cả model liên kết — và dọn lượt cũ

Bản chép **giữ nguyên tên gốc**, nằm trong `<thư mục kết quả>/ban-chep/`, kèm các model được liên kết
(script dò tên `*.rvt` ngay trong file, chỉ chép những file có thật cạnh model gốc). Không có bước này
thì link lưu theo đường dẫn tương đối không giải được từ vị trí bản chép, và `SleeveAuto` không thấy
tường nào — xem [`bang-chung-test.md`](bang-chung-test.md) §14.

Giá phải trả là dung lượng: một lượt bộ ghi MEP tốn **~320 MB** (sáu model Snowdon). Vì vậy script tự
dọn lượt cũ **của cùng bộ ca kiểm** trước mỗi lần chạy, giữ `-KeepRuns` lượt gần nhất (mặc định 2):

```powershell
.\scripts
un-in-revit-tests.ps1 -Suite write-mep -AllowWrites             # giữ 2 lượt
.\scripts
un-in-revit-tests.ps1 -Suite write-mep -AllowWrites -KeepRuns 5  # giữ 5
.\scripts
un-in-revit-tests.ps1 -Suite write-mep -AllowWrites -KeepRuns 0  # không dọn gì
```

Dọn **trước** khi chạy chứ không phải sau: dọn sau thì lượt vừa chạy cũng nằm trong diện đếm, và nếu
Revit treo thì không bao giờ tới bước dọn.

### Chuỗi tự chứng minh và tự khôi phục

Không so file vàng, không cần model chuẩn bị sẵn. Ca xếp thành chuỗi để **kết quả ca sau chứng minh ca
trước đã ghi thật**:

```
xuất Mark gốc ra CSV
đánh số cửa           GHI THẬT → "Đã đánh số 141/141"
nhập lại CSV gốc      GHI THẬT → "Đã cập nhật 141"   ← 141 này chứng minh bước trên ĐÃ đổi model
nhập lại lần nữa      GHI THẬT → "Đã cập nhật 0"     ← 0 này chứng minh bước trên ĐÃ ghi và idempotent
```

Nếu lệnh chỉ chạy xem trước, giá trị trong model không đổi, và bước "nhập lại CSV gốc" sẽ không có gì để
khôi phục — ca đỏ. Chuỗi cũng **trả model về đúng trạng thái ban đầu**, nên chạy bao nhiêu lần cũng được.

Cặp `ProjectInfo` ghi → ghi lại y hệt (0 trường đổi) chốt thêm một điều mà xem trước không chốt được:
transaction đã **commit thật**, không phải rollback.

`summaryNotContains: ["Xem trước"]` là lớp cuối: nếu một ngày nào đó khoá `dryRun` bị ép nhầm cho cả ca
ghi, ca sẽ đỏ thay vì lặng lẽ xanh.

### Kỳ vọng

| Trường | Ý nghĩa |
|---|---|
| `success` | `CommandResult.Success` (mặc định `true`) |
| `minAffected` / `maxAffected` | Chặn dưới/trên số phần tử bị ảnh hưởng |
| `summaryContains` | Summary phải chứa (không phân biệt hoa thường) |
| `summaryNotContains` | Summary **không** được chứa — dùng cho ca ghi thật: `["Xem trước"]` |
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
