# Bằng chứng Test Live — AutoCAD Bridge
**Ngày:** 2026-09-02 09:10 ICT  
**AutoCAD:** Autodesk AutoCAD 2026 (Education)  
**Drawing:** SDG.MEP.MVAC.013.R0-BVTC hệ thống thông gió tầng mái - Tháp A.dwg  
**Bridge:** `http://localhost:8766` — load bằng NETLOAD vào AutoCAD 2026  

---

## Cách load add-in

```
AutoCAD → gõ lệnh: NETLOAD
→ Chọn file: C:\Users\liend\Dhcb Tools\src\DhcbTools.AutoCAD\bin\Debug\net48\DhcbTools.AutoCAD.dll
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
  "filename": "D:\\Z-Nháp\\SDG.MEP.MVAC.013.R0-BVTC hệ thống thông gió tầng mái - Tháp A.dwg",
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
  "config": {"outputPath": "C:/Users/liend/AppData/Local/Temp/dhcb_layers_test.csv"}
}
```
**Response:**
```json
{
  "success": true,
  "summary": "Đã xuất 171 layer ra \"C:/Users/liend/AppData/Local/Temp/dhcb_layers_test.csv\".",
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
