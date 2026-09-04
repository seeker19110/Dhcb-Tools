namespace DhcbTools.Core.AutoCAD.AutoNumbering;

/// <summary>Hướng quét để xác định thứ tự đánh số theo toạ độ.</summary>
public enum NumberingDirection
{
    LeftToRightThenTopToBottom,
    TopToBottomThenLeftToRight,
}

/// <summary>
/// Cấu hình đánh số hàng loạt cho entity trong AutoCAD — tương đương AutoNumberingConfig của Revit.
/// Áp dụng cho Block Reference (Insert): quét theo vị trí InsertionPoint rồi ghi vào Attribute.
/// </summary>
public sealed class AutoNumberingConfig
{
    /// <summary>
    /// Tên Block cần đánh số (tên Block Definition, không phân biệt hoa/thường).
    /// Ví dụ: "DOOR", "COLUMN", "EQUIPMENT".
    /// </summary>
    public required string BlockName { get; init; }

    /// <summary>Tên Attribute trong Block dùng để ghi số (ví dụ "MARK", "NO", "TAG").</summary>
    public string AttributeTag { get; init; } = "MARK";

    /// <summary>Tiền tố trước số thứ tự.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Số bắt đầu.</summary>
    public int StartNumber { get; init; } = 1;

    /// <summary>Bước nhảy.</summary>
    public int Step { get; init; } = 1;

    /// <summary>Số chữ số tối thiểu, ví dụ 3 → "001".</summary>
    public int PadWidth { get; init; } = 0;

    /// <summary>Hướng quét để xác định thứ tự đánh số.</summary>
    public NumberingDirection Direction { get; init; } = NumberingDirection.LeftToRightThenTopToBottom;

    /// <summary>
    /// Dung sai gom hàng/cột (đơn vị bản vẽ, thường là mm): hai block lệch dưới mức này coi như cùng hàng.
    /// Cùng ý nghĩa với <c>rowToleranceMm</c> của AutoNumbering bên Revit — mặc định 300.
    /// </summary>
    public double RowToleranceMm { get; init; } = 300.0;

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
