# Phản hồi 9.4 — vòng đóng vai sáu kỹ sư (2026-09-06)

Chưa có nhóm kỹ sư thật ([`phat-hanh-v1.1.md`](phat-hanh-v1.1.md) chờ người). User bảo làm thay: mỗi vai trong bảng
đề xuất được "ngồi" một phiên, bấm đúng các lệnh *nên thử tuần đầu*, trên dữ liệu đúng của vai đó — dự án thật A
(bản `_upgraded-2024/`, `saveMode: None`, không lưu gì) hoặc model/bản vẽ mẫu khi dự án A không có thứ cần (sheet).
Điền theo đúng [`mau-phan-hoi-9-4.md`](mau-phan-hoi-9-4.md): **Tuần / Bỏ / Chưa**, lý do bắt buộc khi *Bỏ*. Job và
log: `DHCB-test-results/dong-vai-2026-09-06/`. Bằng chứng: [`bang-chung-test.md`](bang-chung-test.md) §57.

**Giới hạn của vòng này, nói trước:** đây là một người đóng sáu vai trong một buổi, không phải sáu người dùng hai tuần.
Cột *Tuần* ở đây nghĩa là "chạy được ngay, ra thứ dùng được, không phải hỏi ai" — chưa phải "đã vào nếp làm việc".
Nó thay được câu hỏi *bấm có chạy không, vướng ở đâu*, không thay được câu *có dùng tiếp không*.

## Vai 1–2 — Kiến trúc DD/CD có sheet

Dữ liệu: Snowdon Architectural (55 sheet) vì dự án A không có sheet; phần không cần sheet chạy trên A ARC L02.

| Lệnh | Tuần | Bỏ | Chưa | Ghi chú |
|---|:--:|:--:|:--:|---|
| `SheetIndex` | ☑ | | | 55 sheet, 52 ms; tự chỉ A602 "chưa có view". Trên A ARC L02: `E-PRECOND` vì không có sheet — đúng, nhưng câu báo bị lặp chữ *"sheet trong mô hình nào trong mô hình"* (đã sửa) |
| `SheetRename` | ☑ | | | Xem trước 44/55, tự chống trùng tên "(2)/(3)" |
| `RevisionOnSheets` | ☑ | | | Xem trước 43/55, tên revision hiện rõ |
| `BatchExport` PDF | ☑ | | | Xem trước liệt kê 55 bản vẽ; chưa in thật trong vòng này |
| `HealthReport` | ☑ | | | 653 ms; A ARC L02: 273 cảnh báo, 18 view chưa đặt |
| `WarningsExport` | ☑ | | | 273 warning gom 5 loại — 217 *identical instances*: việc cần làm đầu tuần |
| `RemoveUnusedViews` | ☑ | | | Xem trước 16 |
| `StylePurge` | ☑ | | | Xem trước 48; báo rõ nhóm không kiểm được trên "Project View" |

## Vai 3–4 — MEP (HVAC + ống), dự án A MEP L02

| Lệnh | Tuần | Bỏ | Chưa | Ghi chú |
|---|:--:|:--:|:--:|---|
| `SleeveAuto` | | ☑ | | `E-CONFIG-MISSING sleeveFamilyName`. Dự án không có family sleeve; `FamilyAudit` 175 family cũng không thấy. **Bỏ vì thiếu family, không phải lỗi lệnh** — nay chạy `FamilyStarter` trước là có `DHCB_Sleeve` (§60) |
| `HangerAuto` | ☑ | | | Với family thật của dự án `REDY_Pipe_ Support (None Insulation)`: 4769 hanger / 4470 phần tử, bỏ qua 108 vị trí đã có. Phải tra tên family qua `FamilyAudit` trước — hai bước |
| `ClashDetection` | ☑ | | | 479 va chạm với link, 2,6 s, BCF 479 topic |
| `SlopePipes` | ☑ | | | Kiểm 2738 ống, 2582 chưa đạt — con số để bàn với thiết kế |
| `AutoRoute` | ☑ | | | Lần đầu trên **dự án thật**: hai đầu duct cách 6 m cùng cao độ 11.400 → **tuyến 2 đoạn 7,9 m = 1,00× Manhattan, 1 rẽ (tối thiểu 1)**, né 1 dầm ở link ARC. Phải lấy điểm bằng `SetoutExport` (Ducts, Internal, mm) trước, và phải bỏ Ducts khỏi `obstacleCategories` — để mặc định thì "điểm nằm trong chướng ngại" (đúng, vì điểm là đầu duct) |
| `SetoutExport` | ☑ | | | 2322 đầu duct, 130 ms — dùng làm nguồn điểm cho AutoRoute |
| `ConnectorChecker` | ☑ | | | 1040 connector hở / 875 phần tử. Lần đầu khai `outputPath` bị `E-CONFIG-UNKNOWN` (lệnh chỉ tạo 3D view, không có CSV) — muốn có CSV để giao việc. **Đã thêm `outputPath` CSV (§58)** |

## Vai 5 — BIM manager, dự án A ARC L01

| Lệnh | Tuần | Bỏ | Chưa | Ghi chú |
|---|:--:|:--:|:--:|---|
| `IdsValidate` | ☑ | | | 4537 phần tử, 3 spec, 0 không đạt, 1 spec không có phần tử — đánh dấu riêng đúng bài học §16 |
| `ParameterRuleCheck` | ☑ | | | 448 giá trị + 6 ngưỡng, 48 vi phạm (47 Doors) |
| `DictionaryLearn` | ☑ | | | 224 tên tham số / 18 category: 3 khoá cần xem, 6 thiếu — đúng việc BIM manager phải quyết |
| `UsageReport` | ☑ | | | 247 lần chạy / 3 ngày / 49 lệnh (log của máy này) — chính là bảng máy làm thay mẫu phản hồi |
| BatchRunner + Task Scheduler | ☑ | | | Task đêm dự án A đã tự chạy 2026-09-05 23:50, Result 0 |

## Vai 6 — AutoCAD 2D (shop drawing), Mechanical Sample

| Lệnh | Tuần | Bỏ | Chưa | Ghi chú |
|---|:--:|:--:|:--:|---|
| `LayerStandardCheck` | ☑ | | | 20 và 70 layer, báo cáo HTML |
| `BlockQuantity` | ☑ | | | 24 block / 5 nhóm ra CSV |
| `AttributeIncrement` | ☑ | | | Ba lượt như người thật: `blockName: "*"` → lỗi; nay lỗi **liệt kê 5 block có trong bản vẽ**. Chọn `AMB006` + tag `TAG` → xem trước nay **cảnh báo 10/10 block không có tag đó, tag có thật: MAKE/MODEL, REF, SERVICE, SIZE, TYPE** (trước: xem trước nói "sẽ gán 10", chạy thật mới lộ). Tag `REF` → ghi thật 10/10, `AttributeExport` đọc lại thấy `P-001…` |
| `DrawingCompare` | ☑ | | | So mức layer 4/4 và 10/10 khác |
| `LayerExport` | ☑ | | | |

**Lỗi thật lộ ra ở vai này:** chạy batch với `--plugin-dll` đường dẫn **tương đối** → accoreconsole *"Unable to load …
assembly"*, mọi `DHCB_RUN` thành *Unknown command*, và runner kết thúc **"0 OK, 0 lỗi"** mã 1 — không một dòng nào
nói NETLOAD hỏng. Sửa: runner tuyệt đối hoá đường dẫn, và khi output có "Unable to load" hoặc file không ghi được
dòng nào vào `run.jsonl` dù có step thì ghi một dòng lỗi `NETLOAD` kèm tên DLL và log.

## Bốn câu hỏi mở

1. **Lệnh nào tiết kiệm nhiều nhất?** `WarningsExport` + `HealthReport` (kiến trúc), `ClashDetection` + `HangerAuto`
   (MEP), `IdsValidate` (BIM manager), `AttributeIncrement` (AutoCAD).
2. **Vướng nhiều nhất ở đâu?** Tên family/block/tag/tham số của **dự án** — bốn lệnh (`SleeveAuto`, `HangerAuto`,
   `AttributeIncrement`, `ElevationTag`) đều đòi biết tên có thật. Hướng đã đi: lỗi và xem trước **liệt kê cái có thật**
   (block, tag từ vòng này; tham số từ `DictionaryLearn`). **`SleeveAuto`/`HangerAuto` nay liệt kê 8 family ứng viên ngay
   trong lỗi (§58)** — không còn phải chạy `FamilyAudit` trước.
3. **Thiếu gì?** ~~CSV cho `ConnectorChecker`~~ (đã có, §58); ~~family sleeve/hanger mẫu~~ — nay `FamilyStarter` dựng và nạp `DHCB_Sleeve`/`DHCB_Hanger` từ template của chính Revit (§60), hình giữ chỗ.
4. **Có dùng tiếp không?** Không trả lời được bằng đóng vai — cần người thật hai tuần.

## Tổng hợp theo lệnh (như hướng dẫn cuối mẫu)

| | Số lệnh |
|---|---:|
| Tuần (chạy được ngay, ra thứ dùng được) | 24 |
| Bỏ, có lý do | 1 (`SleeveAuto` — thiếu family sleeve trong dự án) |
| Lỗi mã tìm thấy và đã sửa trong vòng | 3 (runner AutoCAD im lặng khi NETLOAD hỏng; `SheetIndex` lặp chữ; `AttributeIncrement` xem trước không biết thiếu tag) |
| Cải tiến thông báo | 2 (`BlockNotFound` liệt kê block; xem trước liệt kê tag) |
