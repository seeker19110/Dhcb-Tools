namespace DhcbTools.Core.AutoCAD.LayerSync;

/// <summary>
/// Cấu hình cho lệnh xuất layer ra CSV — tương đương ParameterExportConfig của Revit.
/// Mỗi dòng CSV = một layer: tên, màu, linetype, lineweight, plot, description.
/// </summary>
public sealed class LayerExportConfig
{
    /// <summary>Đường dẫn file CSV đầu ra (UTF-8, Excel có thể mở thẳng).</summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Chỉ xuất các layer có tên chứa chuỗi này (không phân biệt hoa/thường).
    /// Null hoặc rỗng = xuất tất cả.
    /// </summary>
    public string? FilterNameContains { get; init; }
}

/// <summary>Cấu hình cho lệnh nhập layer từ CSV — ghi ngược giá trị đã chỉnh sửa vào drawing.</summary>
public sealed class LayerImportConfig
{
    /// <summary>Đường dẫn file CSV đầu vào (đúng định dạng do lệnh xuất tạo ra).</summary>
    public required string InputPath { get; init; }

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Tạo layer mới nếu trong CSV có layer chưa tồn tại trong drawing.</summary>
    public bool CreateMissing { get; init; } = false;
}
