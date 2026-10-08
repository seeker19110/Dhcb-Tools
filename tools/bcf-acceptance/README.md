# Nghiệm thu BCF 2.1 bằng reader độc lập

Chạy source DHCB để xuất 5 fixture, rồi đối chiếu schema chính thức và reader IfcOpenShell.
Không cần Revit/AutoCAD; không gửi model/BCF lên dịch vụ ngoài. Công cụ này không đi kèm add-in.

```bash
python -m pip install bcf-client==0.9.0 lxml==6.1.3
git init /tmp/bcf-schema
git -C /tmp/bcf-schema fetch --depth 1 https://github.com/buildingSMART/BCF-XML.git 92fdcf4bfaa8f0576e7562730f341fc7781e2065
git -C /tmp/bcf-schema checkout FETCH_HEAD -- Schemas
dotnet run --project tools/bcf-acceptance/DhcbTools.Bcf.Acceptance.csproj -c Release -- ./out/bcf-fixtures
python tools/bcf-acceptance/verify.py ./out/bcf-fixtures /tmp/bcf-schema/Schemas
```

Trên Windows thay thư mục `/tmp` bằng đường dẫn tạm của bạn. Thư mục fixture phải mới/rỗng;
không ghi đè bằng chứng lần trước. CI chạy cùng lệnh, ghim commit schema và phiên bản reader.
`requirements-dev.txt` cũng có hai dependency; Python/runtime add-in không cần chúng.

Fixture gồm hard/soft/tolerance clash, project mặc định, file rỗng, topic không camera và camera nhìn thẳng đứng.
Kiểm version/project/markup/viewpoint theo XSD; markup được kiểm cả với extension XSD do DHCB sinh.
Reader độc lập phải đọc đủ topic, GUID, nhãn, stage, AuthoringToolId, hướng camera và vector up vuông góc.
Có tiếng Việt/ký tự XML/khoảng trắng trong nhãn để bắt mất dữ liệu khi chuyển định dạng.
Schema chuẩn được giải cục bộ; bước verify không cần mạng và không giải nén file vào thư mục model.

BCF **2.1** công bố `extensions.xsd` và đường dẫn trong `project.bcfp`; `extensions.xml` thuộc **3.0**.
Nguồn chuẩn: [buildingSMART 2.1](https://github.com/buildingSMART/BCF-XML/tree/92fdcf4bfaa8f0576e7562730f341fc7781e2065).
Reader: [IfcOpenShell BCF](https://docs.ifcopenshell.org/bcf.html).

Kết quả này chứng minh schema/đọc dữ liệu, **chưa chứng minh** một viewer mở đúng model, chọn được phần tử Revit/link,
hoặc camera đặt đúng lên IFC đã xuất. Cần ứng dụng nhận và model công ty để khép nghiệm thu đó.
