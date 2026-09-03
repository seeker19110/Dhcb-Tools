# Fixture cho bộ kiểm thử chạy trong Revit

File đầu vào cho các lệnh cần đọc file (`GridFromCsv`, `SheetBatchCreate`, `CadLayerMap`, `SpecToConfig`…).
Bộ test trỏ tới đây bằng token `{suiteFolder}/fixtures/...` — token do `RunTestsCommand` cấp, để bộ ca kiểm
không phải viết đường dẫn tuyệt đối của một máy cụ thể.

Số liệu cố tình đặt tên có tiền tố `DHCB-TEST-` để nếu ai đó chạy với `-AllowWrites` trên model thật thì
nhìn tên là biết ngay đâu là rác của bộ test.
