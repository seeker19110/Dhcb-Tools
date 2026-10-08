# Nghiệm thu tiếp sau PR #184 — 2026-10-08

PR [#184](https://github.com/seeker19110/Dhcb-Tools/pull/184) đã merge vào main tại
`e08882957fafd10bcc57f9924e3663cd4b7e2b12`. 13 check PR và
[CI main](https://github.com/seeker19110/Dhcb-Tools/actions/runs/37748485253) đạt.

## BCF: lỗi extension đã được tìm và sửa

Reader IfcOpenShell `bcf-client 0.9.0` mở được BCF cũ 2.1 với 7 topic nhưng trả `extensions=None`.
Nguyên nhân: writer ghi `extensions.xml` dạng 3.0 và `ExtensionSchema` rỗng trong project 2.1.
Writer hiện dùng `extensions.xsd` với `xs:redefine` và tham chiếu từ `project.bcfp`, kể cả không truyền project.
Các nhãn giữ nguyên khoảng trắng/ký tự XML/tiếng Việt để enumeration khớp markup.

[buildingSMART BCF 2.1](https://github.com/buildingSMART/BCF-XML/tree/92fdcf4bfaa8f0576e7562730f341fc7781e2065)
được ghim theo commit; schema chuẩn được giải cục bộ khi verify.
[Reader độc lập](https://docs.ifcopenshell.org/bcf.html) đọc version, topic, nhãn/stage, ID và camera.

- 5/5 fixture, 6 topic, 21 file XML đạt XSD và reader độc lập.
- Hard/soft/tolerance có nhãn; camera nhìn đúng tâm, direction/up là vector đơn vị và vuông góc.
- File rỗng, project mặc định, topic không camera, camera thẳng đứng đều đọc được.
- 3 ca cố ý phá file (bỏ tham chiếu extension, đổi title, sai hướng camera) đều bị bộ kiểm từ chối.
- C# 2.073/2.073 đạt, coverage 100% dòng. CI có bước xuất fixture và đọc độc lập theo cùng phiên bản đã ghim.

Cách tái chạy: [tools/bcf-acceptance](../../../tools/bcf-acceptance/README.md).
Bằng chứng tại máy: `%USERPROFILE%\DHCB-test-results\bcf-acceptance-20261008-final`
và `bcf-readiness-20261008-final` (TRX/Cobertura).
Đây là nghiệm thu schema/reader; viewer nhận, việc mở model và chọn phần tử trong file/link vẫn chưa được chứng minh.

## Gói đã dựng từ main

Đã dựng tại `%USERPROFILE%\DHCB-deploy\readiness-20261008-e088829`: 8 ZIP
(Revit 2023–2026, AutoCAD 2024–2026, BatchRunner) và installer phát triển.
9 artifact khớp SHA-256; 8/8 ZIP có LICENSE/NOTICE khớp repo; BatchRunner có 8 script trong manifest.
Gói này thuộc commit #184, chưa chứa sửa BCF của lượt tiếp. Không tạo tag/public release.
Manifest chứa commit, phiên bản, danh mục/hash file, SDK và điều kiện host.

## Phần còn thiếu đầu vào

| Việc | Tiến triển mới | Điều kiện còn thiếu |
|---|---|---|
| Revit | Thêm suite readiness 6 ca preview: sinh/chọn/từ chối OPT-id, clash mặc định/phân loại/BCF, khoảng hở sai | Revit.exe hiện không có; suite chưa chạy trong host |
| GUI | Reset runtime rồi thử lại computer-use | Vẫn lỗi `sandboxCwd is not a local file URI` trước thao tác; chưa có ảnh hay bằng chứng Ribbon/Undo mới |
| BCF | XSD/reader độc lập đạt | Viewer công ty và model tương ứng để đối chiếu topic/ID/camera |
| Family/tọa độ | Có checklist nghiệm thu cụ thể | Family/tiêu chuẩn công ty; model, mốc tọa độ đã duyệt và phép đo hiện trường |
| Thí điểm | Công cụ tổng hợp CSV đã sẵn sàng | Người dùng, tác vụ và phản hồi 2/4 tuần thật |
| Ký số | Kiểm chỉ đọc thấy 1 cert có private key, EKU ký mã, còn hạn | Cert tự cấp (`Subject=Issuer`), chưa chứng minh tin cậy trên máy sạch; repo chưa có secret ký tổ chức |

Không đổi kho cert hoặc xuất private key. Không ghi dữ liệu thí điểm giả hoặc đánh dấu ca chưa chạy là đạt.
[Quy trình nghiệm thu còn lại](../../../tools/acceptance/remaining.md) có lệnh chạy suite và tiêu chí hủy/rollback/Undo.
