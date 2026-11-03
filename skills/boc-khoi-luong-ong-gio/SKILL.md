---
name: boc-khoi-luong-ong-gio
description: Bóc khối lượng ống gió chữ nhật từ bản vẽ ĐHKK/thông gió AutoCAD (DWG xuất từ Revit) ra Excel — mét dài, m² tôn theo 2×(R+C)×L, đánh số từng đoạn, đếm phụ kiện. Dùng khi người dùng nói "bóc khối lượng ống gió", "xuất khối lượng ống gió", "tính m2 tôn ống gió", "duct takeoff".
---

# Bóc khối lượng ống gió

Bản vẽ ĐHKK xuất từ Revit sang DWG không còn đối tượng ống: mỗi đoạn chỉ là **hai Line song song** trên
layer `M-HVAC-DUCT*`, kích thước nằm ở tag chữ (`SEAD - 700x300`), phụ kiện là block mang tên family Revit.
Script `scripts/boc-khoi-luong-ong-gio.py` dựng lại ống từ ba thứ đó và ghi Excel. Việc của agent là chọn
đúng **vùng mặt bằng**, chạy script, rồi **đối chiếu** kết quả trước khi đưa cho kỹ sư.

Chỉ đọc bản vẽ — không có bước ghi, không cần `confirm`.

## Trước khi làm gì cả

1. Bản vẽ phải đang mở trong AutoCAD và Bridge 8766 đang bật (`DHCB_BRIDGE`). Kiểm nhanh:
   `python scripts/dhcb_agent.py autocad query drawing_info` — ghi lại tên file và đơn vị (phải là mm).
2. Hỏi kỹ sư nếu chưa rõ: **tầng/khu nào**, có tính **phụ kiện** không, **tỉ lệ hao hụt** (mặc định 5 %).

## Trình tự

### 1. Tìm khung mặt bằng

Một file BVTC thường có cả mặt bằng, mặt cắt, chi tiết lắp đặt (xref ghép, layer `…$0$M-HVAC-DUCT`) và
bảng ký hiệu — các khung đó cũng có layer ống nên **phải khoanh vùng**, không thì cộng trùng.

```
query stats
query text { "limit": 5000 }
```

`stats` cho số entity theo layer; `text` cho toạ độ mọi tag. Khung mặt bằng là cụm có nhiều tag dạng
`SEAD - 700x300`, `RAD - 400x250` nhất. Lấy hộp bao cụm đó, nới mỗi phía vài mét → `--bbox xmin,ymin,xmax,ymax`.
Không chắc thì chụp cho kỹ sư nhìn: `query snapshot`.

### 2. Chạy script

```powershell
python scripts\boc-khoi-luong-ong-gio.py --bbox=-170000,300000,-40000,340000 -o "KL-ong-gio-Tang2.xlsx"
```

(`--bbox=` có dấu bằng khi số đầu âm.) Script tự đọc `entities`, `text`, `entity_geometry` qua Bridge;
`entity_geometry` hay quá 30 giây trên bản vẽ lớn nên đã chia lô 25 handle và thử lại — để yên cho nó chạy.
Bản vẽ không dùng layer `M-HVAC-DUCT` thì truyền `--layer-prefix`.

### 3. Đối chiếu — bắt buộc trước khi báo số

Dòng cuối script in: số đoạn, mét dài, m², số phụ kiện, **số cặp chưa xác định**. Kiểm:

- **Tỉ lệ chưa xác định**: mét "chưa xác định" trên 20 % tổng là dấu hiệu sai khung hoặc sai layer — xem lại bước 1.
- **Tiết diện lạ**: tiết diện có rất nhiều mét mà kỹ sư không thấy trên bản vẽ (thường gặp: cặp ~100 mm là
  đường viền/cách nhiệt bị ghép nhầm). Báo rõ, đừng tự xoá.
- **Cột "Cách tag"** trong trang Chi tiết: đoạn cách tag > 5 m nên kiểm tay.
- Lấy 2–3 handle ở trang Chi tiết, xem lại hình học: `query entity_geometry { "handles": ["2CC1DB"] }`.

### 4. Giao kết quả

Nói tổng mét dài và m² (có và không hao hụt), số phụ kiện, và **mét chưa xác định** kèm lý do có thể. File
Excel có 5 trang: Tổng hợp (công thức sống, đổi ô hao hụt I3), Chi tiết (STT + mã `SEAD-001`…), Phụ kiện,
Chưa xác định, Ghi chú (cách tính và giới hạn).

## Giới hạn cần nói với kỹ sư

- Ống vẽ bằng polyline/spline, ống tròn, và ống nằm trong block không được tính.
- Phụ kiện chỉ đếm số lượng, chưa cộng m² tôn.
- Đây là bóc từ hình vẽ 2D — số dùng để đối chiếu/ước lượng, không thay bóc theo mô hình Revit khi có.

## Không được làm

- Không chạy không có `--bbox` trên file nhiều khung (mặt cắt, chi tiết) — sẽ cộng trùng.
- Không báo số khi chưa đọc dòng "chưa xác định" và chưa kiểm vài handle.
- Không sửa hay xoá entity trong bản vẽ để "làm sạch" cho script — chỉ đọc.
- Không đổi R×C trong Excel theo phỏng đoán; tiết diện nghi ngờ thì đánh dấu và hỏi kỹ sư.
