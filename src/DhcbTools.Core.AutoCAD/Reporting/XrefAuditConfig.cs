namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>Cấu hình liệt kê xref, đường dẫn, trạng thái load.</summary>
public sealed class XrefAuditConfig
{
    /// <summary>Đường dẫn file CSV đầu ra. Null/rỗng = chỉ trả kết quả qua Messages, không ghi file.</summary>
    public string? OutputPath { get; init; }
}
