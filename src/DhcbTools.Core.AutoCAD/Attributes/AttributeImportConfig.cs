namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>Cấu hình nhập CSV (đúng định dạng AttributeExport tạo ra) ghi ngược attribute vào block.</summary>
public sealed class AttributeImportConfig
{
    /// <summary>Đường dẫn file CSV đầu vào.</summary>
    public required string InputPath { get; init; }

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
