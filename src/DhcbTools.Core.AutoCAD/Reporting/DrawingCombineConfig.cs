namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// Cấu hình gộp nhiều file DWG vào bản vẽ hiện tại: Model Space của mỗi file thành một block (tên = tên file),
/// tuỳ chọn chép cả layout của từng file thành tab mới "&lt;file&gt; - &lt;layout&gt;" với viewport trỏ đúng chỗ đã đặt.
/// </summary>
public sealed class DrawingCombineConfig
{
    /// <summary>Danh sách file DWG cần gộp (có thể để trống nếu dùng <see cref="InputFolder"/>).</summary>
    public List<string> InputPaths { get; init; } = new();

    /// <summary>Thư mục chứa DWG cần gộp — lấy mọi *.dwg trực tiếp trong thư mục (không đệ quy), theo tên file.</summary>
    public string? InputFolder { get; init; }

    /// <summary><c>Row</c> = xếp thành hàng ngang bên phải nội dung hiện có; <c>Original</c> = giữ nguyên toạ độ gốc.</summary>
    public string Arrangement { get; init; } = "Row";

    /// <summary>Khoảng cách giữa hai bản vẽ khi xếp hàng, mm (đổi sang đơn vị bản vẽ theo INSUNITS).</summary>
    public double GapMm { get; init; } = 10000;

    /// <summary>Chép cả layout (paper space) của từng file thành tab mới.</summary>
    public bool IncludeLayouts { get; init; } = true;

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
