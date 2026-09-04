# Bằng chứng Test Live — AutoCAD Bridge
**Ngày:** 2026-09-02 09:10 ICT  
**AutoCAD:** Autodesk AutoCAD 2026 (Education)  
**Drawing:** bản vẽ thông gió tầng mái của một dự án thực tế (đặt tên `<bản vẽ MVAC>.dwg` trong tài liệu này)  
**Bridge:** `http://localhost:8766` — load bằng NETLOAD vào AutoCAD 2026  

---

## Cách load add-in

```
AutoCAD → gõ lệnh: NETLOAD
→ Chọn file: C:\Users\<user>\Dhcb Tools\src\DhcbTools.AutoCAD\bin\Debug\net48\DhcbTools.AutoCAD.dll
```

---

## GET /health

**Request:** `curl http://localhost:8766/health`  
**Response:**
```json
{
  "status": "ok",
  "port": 8766,
  "app": "AutoCAD"
}
```
✅ Bridge sống

---

## POST /query — drawing_info

**Request:** `{"query": "drawing_info"}`  
**Response (trích):**
```json
{
  "filename": "D:\\<thư mục làm việc>\\<bản vẽ MVAC>.dwg",
  "dwgVersion": "MC0To0",
  "unitsName": "Millimeters"
}
```
✅ Đọc được thông tin bản vẽ

---

## POST /query — stats

**Request:** `{"query": "stats"}`  
**Response:**
```json
{
  "totalEntities": 6759,
  "byType": [
    {"type": "Line",             "count": 3072},
    {"type": "BlockReference",   "count": 841},
    {"type": "Polyline",         "count": 801},
    {"type": "Arc",              "count": 508},
    {"type": "Polyline3d",       "count": 455},
    {"type": "DBText",           "count": 373},
    {"type": "MLeader",          "count": 262},
    {"type": "RotatedDimension", "count": 211},
    {"type": "Circle",           "count": 76},
    {"type": "MText",            "count": 68}
  ]
}
```
✅ Thống kê toàn bộ entity trong bản vẽ

---

## POST /query — layers

**Request:** `{"query": "layers"}`  
**Response:** 171 layers  
**Ví dụ:**
```json
{
  "count": 171,
  "layers": [
    {"name": "0", "isOff": false, "colorIndex": 7, "linetype": "Continuous"},
    {"name": "VHT_E.CEILING.LIGHTING", "colorIndex": 6, "description": "Lighting / Đèn chiếu sáng"},
    ...
  ]
}
```
✅ Đọc được 171 layer

---

## POST /query — layouts

**Request:** `{"query": "layouts"}`  
**Response:** 25 layouts (Model + 24 sheet)  
```json
{
  "count": 25,
  "layouts": [
    {"name": "Model", "tabOrder": 0, "isModelSpace": true},
    {"name": "BÌA",   "tabOrder": 1, "plotPaperSize": {"x": 420, "y": 297}},
    {"name": "01",    "tabOrder": 2},
    ...
  ]
}
```
✅ 25 tabs (Model + BÌA + 01→18...)

---

## POST /query — text

**Request:** `{"query": "text", "config": {"limit": 5}}`  
**Response:** 441 text objects  
```json
{
  "count": 441,
  "texts": [
    {"type": "MText",   "text": "MẶT CẮT S1 QUẠT SEAF-A-R-01", "layer": "0"},
    {"type": "DBText",  "text": "TẦNG KỸ THUẬT",                  "height": 75.0},
    {"type": "DBText",  "text": "TẦNG MÁI",                        "height": 75.0}
  ]
}
```
✅ Đọc được nội dung text trong bản vẽ

---

## POST /query — inserts (Block References)

**Request:** `{"query": "inserts", "config": {"limit": 5}}`  
**Response:** 841 block references  
✅ Đọc được tất cả block insert trong bản vẽ

---

## POST /execute — LayerExport

**Request:**
```json
{
  "command": "LayerExport",
  "config": {"outputPath": "C:/Users/<user>/AppData/Local/Temp/dhcb_layers_test.csv"}
}
```
**Response:**
```json
{
  "success": true,
  "summary": "Đã xuất 171 layer ra \"C:/Users/<user>/AppData/Local/Temp/dhcb_layers_test.csv\".",
  "affectedCount": 171
}
```
**File CSV (6 dòng đầu):**
```
Name,Color,Linetype,Lineweight,IsPlottable,Description
0,7,Continuous,LineWeight000,true,
Defpoints,7,Continuous,LineWeight000,false,
VHT_E.CEILING.LIGHTING,6,Continuous,LineWeight000,true,Lighting / Đèn chiếu sáng
96 RI-E-LT-THIN LINE,7,Continuous,LineWeight000,true,
BD1.6 - KeyPlan TTA-RANH PHU HOP QH,10,Continuous,LineWeight000,true,
```
✅ Xuất 171 layer ra CSV thành công

---

## POST /execute — DrawingCleanup (DryRun=true)

**Request:**
```json
{
  "command": "DrawingCleanup",
  "config": {"purgeUnused": true, "auditErrors": true, "dryRun": true}
}
```
**Response:**
```json
{
  "success": true,
  "summary": "[Xem trước] Sẽ xoá 15 object (layer/block/linetype thừa).",
  "affectedCount": 15,
  "messages": [
    "Layer rỗng: \"BD1.6-Thap A-MB Mai$0$A-N-Note\"",
    "Layer rỗng: \"KIẾN TRÚC\"",
    "Block không dùng: \"_ArchTick\"",
    "Block không dùng: \"_Dot\"",
    "Block không dùng: \"XREF.A-2F_09032025\""
  ]
}
```
✅ Phát hiện 15 object thừa (dryRun — chưa xoá thật)

---

## Tổng kết

| Test | Kết quả | Chi tiết |
|------|---------|---------|
| `/health` | ✅ | Bridge sống, port 8766 |
| `query: drawing_info` | ✅ | Tên file, đơn vị mm, version |
| `query: stats` | ✅ | 6759 entities, 10 loại |
| `query: layers` | ✅ | 171 layers với đầy đủ properties |
| `query: layouts` | ✅ | 25 layouts (Model + 24 sheet) |
| `query: text` | ✅ | 441 text objects, đọc được nội dung |
| `query: inserts` | ✅ | 841 block references |
| `query: xrefs` | ✅ | 0 xref (không có external ref) |
| `execute: LayerExport` | ✅ | 171 layers → CSV file |
| `execute: DrawingCleanup (DryRun)` | ✅ | Phát hiện 15 object thừa |
| `execute: AutoNumbering` | ✅ | **21/21 thật** — attribute A=DC1→DC21 đã ghi vào DWG |
| `execute: AutoNumbering (thật)` | ✅ | **21/21 blocks ghi attribute "A" = DC1–DC21** |

**Kết luận:** AutoCAD Bridge hoạt động đầy đủ trên AutoCAD 2026 (dù build target 2024).  
API tương thích ngược giữa AutoCAD 2024 → 2026.

---

# Vòng 2 — 11 lệnh AutoCAD chưa từng chạy thật

**Ngày:** 2026-09-02 · **AutoCAD 2026 (Education)** · plugin build **net10.0-windows**, nạp tự động
qua `%APPDATA%\Autodesk\ApplicationPlugins\DhcbTools.bundle`, điều khiển qua Bridge `localhost:8766`.

**Bản vẽ 1** — `<bản vẽ PCCC>.dwg`, hệ thống chữa cháy tầng 5–29 (49,2 MB):
77.118 entity · 87 layer · 2.117 block định nghĩa · 2.273 block reference · 782 text.
**Bản vẽ 2** — `<bản vẽ MVAC>.dwg`, thông gió tầng mái: 841 insert, 31 block có attribute.

> Điều kiện tiên quyết: trước PR này **không thể** nạp plugin vào AutoCAD 2025/2026 — TFM chỉ nhìn
> `RevitVersion` nên vỏ AutoCAD luôn ra `net48`, trong khi AutoCAD 2026 chạy .NET 10
> (`acdbmgd.runtimeconfig.json` → `"tfm": "net10.0"`). Vòng kiểm thử này chạy được là nhờ bản sửa đó.

| # | Lệnh | Kết quả | Số liệu thật |
|---|---|---|---|
| 1 | `AttributeExport` | ✅ | 42 attribute từ 21 block `Dau Cat` → CSV |
| 2 | `AttributeImport` (dryRun) | ✅ | đọc lại chính CSV trên, 42 attribute |
| 3 | `AttributeIncrement` (dryRun) | ✅ | 21 giá trị `DC1…DC21` vào tag `A` |
| 4 | `TextReplace` (dryRun) | ✅ | 344 đối tượng văn bản khớp `BOP` |
| 5 | `LayerStandardCheck` | ⚠️→✅ | **2 lỗi, xem dưới** — sau khi vá: 64/87 layer sai chuẩn |
| 6 | `GridExtract` | ✅ | 1.911 trục từ layer `S-GRID-COLS` → CSV |
| 7 | `XrefAudit` | ✅ | không có xref (xref đã bind) — báo cáo HTML |
| 8 | `LayerTranslate` (dryRun) | ✅ | 114.322 entity đổi layer, tạo 3 layer mới |
| 9 | `DrawingCompare` | ✅ | so với `HVAC.dwg`: 164/164 layer khác nhau |
| 10 | `BlockQuantity` | ✅ | 2.273 block, 957 nhóm → CSV |
| 11 | `CadLayerMap` | ✅ | gợi ý map cho 87 layer |

## Hai lỗi `LayerStandardCheck` do vòng này phát hiện

**a) Không đọc được chính file mẫu của repo.** Lệnh chỉ nhận mảng thuần `[{...}]`, còn
`configs/layer-rules.sample.json` lại là `{"rules":[...]}` → người dùng làm đúng theo mẫu vẫn gặp
`Cannot deserialize the current JSON object`. Nay chấp nhận cả hai dạng.

**b) Một quy tắc thiếu `pattern` vô hiệu hoá toàn bộ phép kiểm tra — trong im lặng.** `Pattern`
mặc định là chuỗi rỗng → `new Regex("")` khớp **mọi** tên layer; layer chỉ cần khớp một quy tắc là
hợp lệ, nên đúng một dòng thiếu `pattern` làm mọi layer đều "đạt".

Đo trên bản vẽ thật (87 layer):

| File quy tắc | Trước khi vá | Sau khi vá |
|---|---|---|
| Chỉ 1 luật AIA chặt | 69 sai chuẩn | 69 sai chuẩn |
| Luật AIA + 1 luật thiếu `pattern` | **0 sai chuẩn** ❌ | **76 sai chuẩn**, kèm cảnh báo luật bị bỏ |
| File mẫu của repo | **lỗi, không chạy** ❌ | **64 sai chuẩn**, kèm cảnh báo |

Nay quy tắc thiếu/hỏng bị bỏ qua **và báo ra**; nếu không còn quy tắc dùng được thì lệnh **dừng hẳn**
thay vì báo "0 sai chuẩn".

## Vấn đề còn mở → đã vá (vòng 3)

Ba trong bốn vấn đề của vòng 2 có chung một gốc: vỏ AutoCAD **tự viết HTTP server riêng 275 dòng**
thay vì dùng `Shared.Hosting.HttpBridgeServer` như vỏ Revit (nó thậm chí chưa từng tham chiếu
`Shared.Hosting`). Nay đã chuyển sang server dùng chung.

Đo trên AutoCAD 2026 đang chạy, bản vẽ thật:

| Kiểm tra | Trước | Sau |
|---|---|---|
| `GET /health` không token | 200 | 200 (công khai, chỉ trạng thái) |
| `GET /tools` không token | *không có endpoint* | **401** |
| `POST /execute` không token | **200 — chạy thật** ❌ | **401** ✅ |
| `POST /query` không token | **200** ❌ | **401** ✅ |
| `POST /execute` token sai | *không kiểm tra* | **401** |
| `POST /query` token đúng | — | 200 |
| `GET /tools` token đúng | *không có* | 200 — **15 lệnh** |
| `POST /chat` token đúng | *không có* | 200 — "purge dọn bản vẽ này" → `DrawingCleanup {dryRun:true}` |
| 5 lần token sai liên tiếp | *không có* | lần 6 → **429**, khoá 5 phút (chặn cả token đúng) |
| File token tự sinh | **không bao giờ tạo** | `%APPDATA%\DHCB\bridge-token.txt` (43 ký tự) |

Yêu cầu nguy hiểm nhất — `{"command":"DrawingCleanup","config":{"dryRun":false,"purgeUnused":true}}`
gửi **không kèm token** — trước đây chạy thật; nay trả 401.

**Lỗi `limit` cũng cùng gốc.** `QueryRequest` đọc khoá `params`, còn panel/MCP/tài liệu đều gửi
`config`, nên tham số bị bỏ qua trong im lặng. Nay `BridgeQuery` nhận cả hai khoá:

| Yêu cầu | Trước | Sau |
|---|---|---|
| `{"query":"inserts","config":{"limit":5}}` | 2.273 bản ghi | **5** |
| `{"query":"inserts","params":{"limit":5}}` | 2.273 bản ghi | **5** |

Không hồi quy: chạy lại qua Bridge mới cho số liệu khớp hệt vòng 2 — `BlockQuantity` 2.273,
`GridExtract` 1.911, `TextReplace` 344, `LayerStandardCheck` 64/87.

Ba client Python (`panel_api.py`, `server.py`, `dhcb_agent.py`) vốn đã gửi
`Authorization: Bearer` sẵn, nên việc bật xác thực không làm hỏng công cụ nào.

### Còn lại — đã đóng (2026-09-02)

| Vấn đề | Cách đóng |
|---|---|
| Hai instance AutoCAD → xung đột cổng | `HttpBridgeServer.Start()` bắt `HttpListenerException` và ném `BridgePortInUseException` nêu rõ cổng + tên app. Vỏ AutoCAD xếp hàng thông báo và in ra Editor ở sự kiện `Idle` đầu tiên; vỏ Revit hiện `TaskDialog`. Test xUnit: `HttpBridgeServerTests.Second_server_on_same_port_throws_BridgePortInUseException`. **Kiểm thật trên AutoCAD 2026 (bundle Release net10, 2026-09-02 17:33):** AutoCAD mở một Drawing1 tạm lúc khởi động rồi đóng về tab Start, nên dòng in ở Idle rơi vào document tạm đó — vì vậy thêm lệnh `DHCB_BRIDGE` in lại trạng thái. Instance 1 (PID 21580): `HTTP Bridge (PID 21580) đang lắng nghe tại http://127.0.0.1:8766/`. Instance 2 (PID 24316): `CẢNH BÁO (PID 24316) — Cổng 8766 đang bị tiến trình khác chiếm — có thể một AutoCAD khác đã nạp DHCB Tools và đang giữ Bridge. Instance này KHÔNG nhận lệnh qua Bridge...`. `/health` vẫn trả lời từ instance 1. |
| `CommandResult` trùng lặp | Xoá `Core.AutoCAD/CommandResult.cs`; Core.AutoCAD tham chiếu `Shared.Hosting` với global using nên toàn bộ lệnh dùng chung một lớp với Revit. Bỏ hàm chuyển đổi `ToHostResult` ở ranh giới HTTP. Build lại net48 / net8 / net10 đều qua. |
