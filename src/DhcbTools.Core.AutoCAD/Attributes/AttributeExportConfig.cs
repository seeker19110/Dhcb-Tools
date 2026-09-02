namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>Cấu hình xuất attribute của Block Reference ra CSV dạng hàng dài.</summary>
public sealed class AttributeExportConfig
{
    /// <summary>Chỉ xuất block có tên này. Rỗng/null = mọi block có attribute.</summary>
    public string? BlockName { get; init; }

    /// <summary>Đường dẫn file CSV đầu ra.</summary>
    public required string OutputPath { get; init; }
}
