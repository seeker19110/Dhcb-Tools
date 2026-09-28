namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>Cấu hình xuất attribute của Block Reference ra CSV dạng hàng dài.</summary>
public sealed class AttributeExportConfig
{
    /// <summary>Chỉ xuất block có tên này. Rỗng/null = mọi block có attribute.</summary>
    public string? BlockName { get; init; }

    /// <summary>Đường dẫn file CSV đầu ra.</summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// <c>true</c> = xuất cả block nằm trong các layout (khung tên, bảng thông tin bản vẽ). Mặc định <c>false</c>:
    /// chỉ Model Space như trước. CSV cùng bốn cột; AttributeImport ghi ngược theo Handle nên nhận được cả dòng
    /// của layout.
    /// </summary>
    public bool IncludePaperSpace { get; init; }
}
