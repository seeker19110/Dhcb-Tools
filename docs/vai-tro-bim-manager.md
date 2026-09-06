# DHCB Tools cho BIM manager / QLDA (1 trang)

> Dành cho người phụ trách kiểm soát chất lượng mô hình và hồ sơ BIM toàn dự án. Cài đặt và tổng quan chung:
> [`tong-quan.md`](tong-quan.md).

## Ba lệnh nên bấm trước

1. **`HealthReport`** — báo cáo tổng quan tình trạng mô hình (số family, warning, view/sheet chưa dùng…) —
   dùng để kiểm mô hình của các bộ môn trước khi tổng hợp.
2. **`ClashDetection`** — dò va chạm giữa các mô hình liên kết, xuất kèm BCF 2.1 để gửi các bộ môn xử lý.
3. **`IdsValidate`** — kiểm mô hình theo bộ quy tắc IDS 1.0 (buildingSMART) khai theo giai đoạn dự án — xem
   [`kiem-ids.md`](kiem-ids.md).

Cả ba đều ở tab **DHCB Tools** trên Ribbon, nhóm *Kiểm tra & báo cáo*.

## Kiểm soát trên toàn dự án

- **`WarningsExport`** — gom cảnh báo Revit ra bảng để giao từng bộ môn tự xử lý theo tên mình gây ra.
- **`ParameterRuleCheck`** — kiểm tham số bắt buộc (mã hạng mục, LOD, tên hệ…) theo quy tắc khai riêng cho dự án
  trong `configs/parameter-rules`.
- **`RevisionOnSheets`** — đảm bảo mọi sheet phát hành đều có đúng revision, không sót sheet khi cập nhật.
- **`BatchExport`** — xuất hàng loạt PDF/DWG/IFC/NWC theo danh sách đã duyệt, dùng cho các đợt phát hành hồ sơ.

## Batch chạy đêm cho nhiều file

Khi cần kiểm định kỳ nhiều file `.rvt` của dự án (không cần mở tay từng file), dùng `DhcbTools.BatchRunner`
chạy qua Task Scheduler — xem [`batch-runner.md`](batch-runner.md). Kết quả ra báo cáo HTML + log có chuỗi
băm chống sửa (`--verify-log`), phù hợp làm bằng chứng nội bộ.

## Trước khi dùng cho hồ sơ nộp chính thức (NĐ 217/207)

Bộ IDS mẫu và phần kiểm hoàn công đang ở giai đoạn **chưa có người có chuyên môn pháp lý/QLDA rà soát bản
gốc Công báo**. Đọc kỹ [`danh-gia-va-tam-nhin-2026-09-06.md`](danh-gia-va-tam-nhin-2026-09-06.md) §4.5 trước
khi dùng kết quả cho hồ sơ chính thức — công cụ hỗ trợ kiểm, không thay thế người rà soát.

## Gặp lỗi?

Tra mã `E-...` tại [`ma-loi.md`](ma-loi.md).
