namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>Cấu hình đếm Block Reference theo tên (và tuỳ chọn nhóm theo giá trị attribute) → CSV BOM.</summary>
public sealed class BlockQuantityConfig
{
    /// <summary>Đường dẫn file CSV đầu ra.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Tag attribute dùng để nhóm thêm (ví dụ nhóm theo "TYPE"). Null/rỗng = không nhóm thêm.</summary>
    public string? GroupByAttribute { get; init; }

    /// <summary>Chỉ đếm block có tên chứa chuỗi này (không phân biệt hoa/thường). Null/rỗng = mọi block.</summary>
    public string? BlockNameContains { get; init; }
}
