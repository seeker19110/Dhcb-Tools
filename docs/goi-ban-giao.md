# Gói bàn giao đêm — `handover` trong job batch (mục 11.3)

Một job đêm chạy xong thì trong `outputFolder` có thêm **`ban-giao.html`** (in được, có ô ký) và
**`ban-giao.json`** (bản máy đọc). Gói gom mọi thứ đêm đó sinh ra và nói ba điều mà một tờ giấy bàn giao
phải nói: *có những file nào* (kèm băm SHA-256 từng file), *đã kiểm bằng máy được gì* (chuỗi băm nhật ký, cấu
trúc IFC, IDS), và *ai xác nhận*.

## Vì sao có ô xác nhận

**Điều 11 NĐ 207/2026/NĐ-CP**: hồ sơ điện tử lập theo pháp luật giao dịch điện tử; khi cơ quan có thẩm quyền
yêu cầu thì phải **trích xuất, in ra giấy và được chủ đầu tư xác nhận**. Tờ `ban-giao.html` in ra là bản trích
xuất đó. Băm SHA-256 ở mục 4 của tờ giấy là thứ nối chữ ký với đúng file điện tử: đổi một byte là băm đổi.

## Khai trong job

```json
"outputFolder": "D:/DHCB/ban-giao/{yyyy-MM-dd}",
"handover": {
  "projectName": "Landmark — toà ARC",
  "owner": "Công ty CP Đầu tư Landmark",
  "contractor": "DHCB",
  "idsPath": "D:/DHCB/yeu-cau/chu-dau-tu.ids",
  "ifcSpecPath": null
}
```

| Trường | Nghĩa |
|---|---|
| `enabled` | mặc định `true`; đặt `false` để tắt mà không xoá khối |
| `projectName` / `owner` / `contractor` | in lên đầu trang và ô ký |
| `idsPath` | có thì mọi `.ifc` trong `outputFolder` được kiểm IDS (đường file IFC, mục 11.4), báo cáo `<tên>-ids.html` |
| `ifcSpecPath` | bộ quy tắc cấu trúc IFC cho `--verify-ifc`; rỗng = bộ mặc định |

`handover` đòi `outputFolder` — gói gom file từ đó. Mẫu đầy đủ: [`jobs/ban-giao.sample.json`](../jobs/ban-giao.sample.json):
`HealthReport` → `SheetIndex` → `BatchExport` PDF → `BatchExport` IFC → `IdsValidate`.

## Nội dung `ban-giao.html`

1. **Kiểm tra tự động** — chuỗi băm nhật ký (`--verify-log` trên `run-HHmmss.jsonl` của chính đêm đó), cấu
   trúc từng file IFC (`--verify-ifc`), IDS trên từng IFC (`--verify-ids`), và tổng kết các bước.
2. **Các bước đã chạy** — bảng file × lệnh, kết quả, tóm tắt (từ nhật ký).
3. **Danh mục bản vẽ** — đọc từ CSV có tiêu đề chuẩn do `SheetIndex` ghi: số, tên, revision hiện hành, ngày
   revision, ngày phát hành, người vẽ, người kiểm, số view. Không chạy `SheetIndex` thì mục này nói rõ là trống.
4. **File bàn giao** — mọi `.ifc/.pdf/.dwg/.nwc/.csv/.html/.json/.xlsx` trong `outputFolder` (đệ quy), cỡ và
   SHA-256. Không băm `.rvt`: bản sao mô hình không phải sản phẩm bàn giao.
5. **Xác nhận** — hai ô: đơn vị lập và chủ đầu tư.

Gói dựng **sau** khi job có mã thoát và **không đổi** mã đó: job lỗi vẫn có gói ghi rõ bước lỗi; "kiểm không đạt"
là nội dung của gói, không phải lý do batch báo hỏng.

## `SheetIndex`

Lệnh Core chỉ đọc, nút Ribbon *Hồ sơ & Style → Sheet & revision → Danh mục bản vẽ*. Config:

```json
{ "outputPath": "C:/DHCB/danh-muc-ban-ve.csv", "htmlPath": "C:/DHCB/danh-muc-ban-ve.html", "sheetNumberContains": "", "skipPlaceholders": true }
```

Sheet chưa đặt view nào được nêu tên trong messages; bộ lọc không khớp sheet nào thì `E-PRECOND` (danh mục
rỗng không phải là danh mục).

## Hẹn giờ

```powershell
.\scripts\install-nightly-task.ps1 -Job "D:\DHCB\jobs\ban-giao.json" -RunnerExe "D:\DHCB\bin\DhcbTools.BatchRunner.exe" -LogDir "D:\DHCB\logs" -Time 23:30
```

Bằng chứng chạy thật và lần đầu Task Scheduler tự chạy: [`bang-chung-test.md`](bang-chung-test.md) §43.
