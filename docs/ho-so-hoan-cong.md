# Danh mục hồ sơ hoàn thành công trình — `--dossier` (mục 11.6)

Đối chiếu **danh mục hồ sơ** của dự án với **file có thật** trong một thư mục, rồi báo thiếu mục nào.

```bash
DhcbTools.BatchRunner --dossier "D:/DHCB/ho-so" --dossier-spec "D:/DHCB/configs/ho-so-hoan-cong.json" --dossier-report "D:/DHCB/bao-cao/danh-muc.html"
```

Mã thoát: **0** đủ mục bắt buộc · **1** còn thiếu · **2** không có thư mục/danh mục, hoặc danh mục hỏng.
Có `--dossier-report` thì ghi thêm `.html` (in được, có ô ký) và `.csv` cùng tên. Không cần Revit.

## Ranh giới phải nói trước

DHCB **không phát biểu hộ nội dung Phụ lục VII**. Danh mục nằm ở **file cấu hình của dự án**; công cụ chỉ
đối chiếu với file có thật và đếm. Lý do: Phụ lục VII là văn bản pháp luật — nó đổi theo nghị định, và mỗi
dự án còn thêm bớt theo hợp đồng; viết cứng vào mã nguồn thì không ai sửa được khi luật đổi.

Mẫu: [`configs/ho-so-hoan-cong.sample.json`](../configs/ho-so-hoan-cong.sample.json) — ba nhóm I/II/III theo
**Điều 28 + Phụ lục VII NĐ 207/2026/NĐ-CP**, còn **từng dòng mục là ví dụ cách khai**, đơn vị lập hồ sơ phải
mở bản gốc (Công báo Chính phủ) điền đúng và chịu trách nhiệm. Cùng ranh giới đã đặt cho family dấu hoàn công.

## Khai danh mục

```json
{
  "title": "Danh mục hồ sơ hoàn thành công trình",
  "legalBasis": "Điều 28 và Phụ lục VII Nghị định 207/2026/NĐ-CP",
  "project": "Toà A",
  "groups": [
    { "code": "III", "name": "Quản lý chất lượng thi công", "items": [
      { "code": "III.2", "name": "Biên bản nghiệm thu", "required": true,
        "patterns": ["*bien-ban*nghiem-thu*", "nghiem-thu/*"], "note": "Bản gốc có đủ chữ ký" } ] }
  ]
}
```

| Trường | Nghĩa |
|---|---|
| `patterns` | mẫu tên file kiểu shell (`*`, `?`). Khớp trên **mọi phần đuôi** của đường dẫn tính từ một dấu `/`, nên `nghiem-thu/*` bắt được cả `nghiem-thu/bb01.pdf` lẫn `ho-so/2026/nghiem-thu/bb01.pdf` |
| | **Bỏ dấu và gom dấu ngăn cách**: một mẫu `*khao-sat*` bắt được `khao_sat`, `khao sat`, và `Báo cáo khảo sát địa chất.pdf`. Bắt người khai ba mẫu cho một mục là cách chắc chắn để họ quên một |
| `required` | mặc định `true`. Khai thiếu trường này thì mục vẫn bắt buộc — không lọt êm |
| `note` | in kèm trong báo cáo cho người lập hồ sơ |

Mục **chưa khai `patterns`** thì không file nào nhận nó, và nó luôn bị báo thiếu — cố ý: thà báo thiếu còn
hơn nhận bừa file đầu tiên gặp được rồi in ra dấu "đã có" mà không ai kiểm lại.

## Đọc báo cáo

- Mỗi nhóm một bảng: mục, tài liệu, trạng thái (*có hồ sơ* / **THIẾU** / *chưa có (không bắt buộc)*), file tìm được.
- **File chưa xếp vào mục nào** liệt kê riêng. Không phải lỗi, nhưng người ký cần biết thư mục còn những gì:
  bản nháp, bản trùng, hay một mục danh mục chưa khai mẫu tên file.
- Ô ký cuối trang: Điều 28 giao **chủ đầu tư** tổ chức lập hồ sơ và chịu trách nhiệm về tính chính xác,
  trung thực; mỗi nhà thầu chịu trách nhiệm phần mình lập.

## Còn thiếu của mục 11.6

`AsBuiltStamp` — family mẫu dấu bản vẽ hoàn công theo **Phụ lục IIb** (hai mẫu) và cơ chế điền — **chưa có**:
dựng family phải mở trình soạn family của Revit, không tự động hoá từ đây được. Phần gán dấu lên loạt sheet
và xuất PDF theo danh mục thì `RevisionOnSheets` + `BatchExport` + `SheetIndex` đã làm được.
