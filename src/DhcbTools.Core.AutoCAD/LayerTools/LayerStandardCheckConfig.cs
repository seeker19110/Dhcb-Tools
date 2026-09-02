namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình kiểm tra layer theo bộ quy tắc đặt tên, xuất báo cáo HTML.</summary>
public sealed class LayerStandardCheckConfig
{
    /// <summary>File JSON: mảng object {"pattern": "regex", "description": "..."}.</summary>
    public required string RulesPath { get; init; }

    /// <summary>Đường dẫn file HTML báo cáo.</summary>
    public required string OutputPath { get; init; }
}

/// <summary>Một quy tắc đặt tên layer — layer hợp lệ nếu tên khớp ít nhất một pattern.</summary>
public sealed class LayerNamingRule
{
    public string Pattern { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
