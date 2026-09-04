namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>
/// Cấu hình gán attribute tăng dần theo mẫu (kiểu Lee Mac BATTE) cho các Block Reference cùng tên,
/// theo thứ tự vị trí hình học.
/// </summary>
public sealed class AttributeIncrementConfig
{
    /// <summary>Tên Block cần đánh số.</summary>
    public required string BlockName { get; init; }

    /// <summary>Tag của attribute sẽ ghi giá trị.</summary>
    public required string AttributeTag { get; init; }

    /// <summary>Mẫu chuỗi, ví dụ "P-{n:000}" — "{n}" hoặc "{n:000}" sẽ được thay bằng số thứ tự.</summary>
    public required string Pattern { get; init; }

    /// <summary>Số bắt đầu.</summary>
    public int StartNumber { get; init; } = 1;

    /// <summary>Dung sai gom hàng (đơn vị bản vẽ, thường là mm) — như AutoNumbering; mặc định 300.</summary>
    public double RowToleranceMm { get; init; } = 300.0;

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
