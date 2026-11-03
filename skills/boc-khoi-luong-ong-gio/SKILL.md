---
name: boc-khoi-luong-ong-gio
description: Bóc khối lượng ống gió chữ nhật từ bản vẽ ĐHKK/thông gió AutoCAD (DWG xuất từ Revit) ra Excel theo đốt — đốt chuẩn theo nẹp (TDC 1110, C/V 1180), đoạn bù, box bít đầu, co, m² tôn theo 2×(R+C)×L, đánh số từng đốt. Dùng khi người dùng nói "bóc khối lượng ống gió", "xuất khối lượng ống gió", "chia đốt ống gió", "tính m2 tôn ống gió", "duct takeoff".
---

# Bóc khối lượng ống gió

Bản vẽ ĐHKK xuất từ Revit sang DWG không còn đối tượng ống: mỗi đoạn chỉ là **hai Line song song** trên
layer `M-HVAC-DUCT*`, kích thước nằm ở tag chữ (`SEAD - 700x300`), mối nối là block `…Rec_TDC_Union…` /
`…Rec_C_Union…`, co/côn/tê là block mang tên family Revit. Ống thi công chia thành **đốt**: đốt chuẩn dài
tối đa theo nẹp (TDC 1110 mm, nẹp C và bích V 1180 mm), các **đoạn bù**, và **box gió bít đầu** (sản xuất
riêng). Script `scripts/boc-khoi-luong-ong-gio.py` dựng lại đúng cách chia đó và ghi Excel; quy tắc chia
lấy cùng giá trị mặc định với công cụ AutoLISP BKL của tổ bóc khối lượng. Việc của agent là chọn đúng
**vùng mặt bằng**, chạy script, rồi **đối chiếu** kết quả trước khi đưa cho kỹ sư.

Chỉ đọc bản vẽ — không có bước ghi, không cần `confirm`.

## Trước khi làm gì cả

1. Bản vẽ phải đang mở trong AutoCAD và Bridge 8766 đang bật (`DHCB_BRIDGE`). Kiểm nhanh:
   `python scripts/dhcb_agent.py autocad query drawing_info` — ghi lại tên file và đơn vị (phải là mm).
2. Hỏi kỹ sư nếu chưa rõ: **tầng/khu nào**, **tỉ lệ hao hụt** (mặc định 5 %), dự án có quy ước đốt khác
   TDC 1110 / C–V 1180 không.

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

(`--bbox=` có dấu bằng khi số đầu âm.) Script tự đọc `entities`, `text` và `entity_geometry` của mọi block
qua Bridge (mối nối, phụ kiện, cả miệng gió ở layer riêng); `entity_geometry` hay quá 30 giây trên bản vẽ lớn
nên đã chia lô 25 handle và thử lại — để yên cho nó chạy. Bản vẽ không dùng layer `M-HVAC-DUCT` thì truyền
`--layer-prefix`.

Cách script chia đốt (để giải thích khi kỹ sư hỏi):

- cắt tuyến tại **tâm block mối nối**; loại nẹp theo tên block, tuyến không có mối nối lấy theo tuyến cùng
  kích thước → cùng hệ → cạnh lớn (< 2000 TDC, ≥ 2000 V);
- khoảng dài hơn L max quá 80 mm → chia L max từ đầu khoảng; đoạn cuối < 300 mm → cộng đốt trước rồi chia đôi;
  khoảng có chân rẽ/miệng gió bên hông được dài tới 1400 mm;
- khoảng cuối ở **đầu kín** (nắp bịt, hoặc không nối gì) dài hơn L max tới 1400 mm → **box bít đầu**;
- chiều dài làm tròn 10 mm gần nhất như số dim; từ L max − 10 tới L max + 80 tính bằng L max (đốt chuẩn);
- **co**: Revit xuất một co vuông 90° thành **hai nửa block co 45°** nối nhau bằng đoạn chéo ngắn — script gộp
  thành một co, đoạn chéo là cổ co (ghi ở cột "Cổ co", không tính thành ống); góc theo hướng hai tuyến cùng R×C
  nối vào (tên family `Elbow_45` không đáng tin), chỉ một tuyến thì theo tên; R×C và nẹp theo tuyến nối vào,
  m² = 2(R+C) × R·tan(góc/2).

### 3. Đối chiếu — bắt buộc trước khi báo số

Dòng cuối script in: số đốt chuẩn, đoạn bù, box, co, mét ống, m², số phụ kiện, **số tuyến chưa xác định**. Kiểm:

- **So một tuyến với dim trên bản vẽ**: chọn tuyến dài nhất ở trang Chi tiết (cột Tuyến), đối chiếu từng đốt
  với dim — phải khớp kiểu `1400 (box) + 770 + 8×1110`. Lệch là dấu hiệu thiếu block mối nối hoặc sai khung.
- **Tỉ lệ chưa xác định**: mét "chưa xác định" trên 20 % tổng là dấu hiệu sai khung hoặc sai layer — xem lại bước 1.
- **Tiết diện lạ**: tiết diện có rất nhiều đoạn vụn mà kỹ sư không thấy trên bản vẽ (thường gặp: cặp ~100 mm là
  đường viền/cách nhiệt bị ghép nhầm). Báo rõ, đừng tự xoá.
- **Cột Ghi chú** trang Chi tiết: đoạn "có chân rẽ/miệng gió bên hông" dài hơn L max — xem lại vị trí chân rẽ.
- **Trang Co**: co có kích thước "?" là co không nối tuyến nào có tag — kiểm tay. Co "Số nửa block" = 1 mà bản
  vẽ là co vuông thì nửa còn lại thiếu block hoặc nằm ngoài khung — đếm lại trên bản vẽ.
- **So với bản bóc tay** (khi kỹ sư đưa file): so theo MÃ + kích thước + L (co theo góc), cùng phạm vi ống thẳng +
  bịt đầu; bóc tay hay quy chiều dài về số tròn (box 1400, đốt 1180) nên lệch từng dòng nhưng tổng gần khớp.
- Lấy 2–3 handle ở trang Chi tiết, xem lại hình học: `query entity_geometry { "handles": ["2CC1DB"] }`.

### 4. Giao kết quả

Nói tổng mét ống và m² (có và không hao hụt), số đốt chuẩn / đoạn bù / box / co, và **mét chưa xác định** kèm
lý do có thể. File Excel có 7 trang: Tổng hợp (công thức sống theo hệ + kích thước, ô hao hụt S3), Chi tiết
(STT, mã đốt `SEAD-001`, tuyến, nẹp, loại), Đoạn bù & box, Co, Phụ kiện, Chưa xác định, Ghi chú.

File khối lượng của dự án thật **chỉ lưu ở máy** — repo Dhcb-Tools là public, không commit file Excel bóc từ
hồ sơ dự án vào đó.

## Giới hạn cần nói với kỹ sư

- Ống vẽ bằng polyline/spline, ống tròn, và ống nằm trong block không được tính.
- Côn, tê, chân rẽ, van chỉ đếm số lượng (trang Phụ kiện), chưa tính m².
- Đây là bóc từ hình vẽ 2D — số dùng để đối chiếu/ước lượng, không thay bóc theo mô hình Revit khi có.

## Không được làm

- Không chạy không có `--bbox` trên file nhiều khung (mặt cắt, chi tiết) — sẽ cộng trùng.
- Không báo số khi chưa so ít nhất một tuyến với dim trên bản vẽ và chưa đọc dòng "chưa xác định".
- Không sửa hay xoá entity trong bản vẽ để "làm sạch" cho script — chỉ đọc.
- Không đổi R×C, loại nẹp hay loại đốt trong Excel theo phỏng đoán; nghi ngờ thì đánh dấu và hỏi kỹ sư.
- Không commit file khối lượng của dự án thật vào repo public.
