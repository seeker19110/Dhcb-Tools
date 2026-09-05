# Fixture cho bộ kiểm thử chạy trong Revit

File đầu vào cho các lệnh cần đọc file (`GridFromCsv`, `SheetBatchCreate`, `CadLayerMap`, `SpecToConfig`…).
Bộ test trỏ tới đây bằng token `{suiteFolder}/fixtures/...` — token do `RunTestsCommand` cấp, để bộ ca kiểm
không phải viết đường dẫn tuyệt đối của một máy cụ thể.

Số liệu cố tình đặt tên có tiền tố `DHCB-TEST-` để nếu ai đó chạy với `-AllowWrites` trên model thật thì
nhìn tên là biết ngay đâu là rác của bộ test.

## `tuyen-ong.dwg` — fixture nhị phân, sinh lại được

Hai fixture DXF là **văn bản** để đọc và review được ngay trong repo. Nhưng đúng vì thế chúng không chứng
minh được điều kỹ sư thật cần: Revit đọc được một **DWG nhị phân đời mới**. `tuyen-ong.dwg` là DWG **2018**
(`AC1032`, ~18 KB) sinh từ chính `tuyen-ong.dxf` nhưng **dời 20 m theo Y** — nhờ vậy model line sinh ra từ nó
là đường **mới**, không trùng đường của bản DXF, nên con số "2 đường mới" nói được là lệnh đã đọc file này
chứ không phải đọc lại file kia.

Sinh lại (cần máy có AutoCAD; đổi 2026 theo bản đang cài):

```powershell
# 1. dời toạ độ Y của tuyen-ong.dxf thêm 20000 (mã nhóm 20/21) → <temp>	uyen-ong-dwg.dxf
# 2. accoreconsole đọc DXF đó rồi SAVEAS DWG 2018:
$scr = "$env:TEMP\save.scr"
Set-Content $scr "FILEDIA`n0`n_.SAVEAS`n_2018`n`"$PWD	uyen-ong.dwg`"`n_.QUIT`n_Y`n" -Encoding ascii
& 'C:\Program Files\Autodesk\AutoCAD 2026ccoreconsole.exe' /i "$env:TEMP	uyen-ong-dwg.dxf" /s $scr
```

`tuyen-ong-giua.dxf` là bản sao của `tuyen-ong.dxf` chỉ để chạy ca `placement: centered` — một file đã link
thì lần sau bị bỏ qua (đúng tính idempotent), nên hai cách đặt khác nhau cần hai file khác nhau.

