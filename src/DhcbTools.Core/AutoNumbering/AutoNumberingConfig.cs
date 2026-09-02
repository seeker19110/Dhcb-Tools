namespace DhcbTools.Core.AutoNumbering;

/// <summary>Hướng sắp xếp để đánh số: theo toạ độ X trước rồi Y, hoặc Y trước rồi X.</summary>
public enum NumberingDirection
{
    LeftToRightThenTopToBottom,
    TopToBottomThenLeftToRight,
}

/// <summary>Cấu hình cho lệnh đánh số hàng loạt (Giai đoạn nền tảng, lệnh #3).</summary>
public sealed class AutoNumberingConfig
{
    /// <summary>Category cần đánh số, ví dụ "Doors", "Rooms", "Duct Fittings".</summary>
    public required string Category { get; init; }

    /// <summary>Tên tham số dùng để ghi số (mặc định là "Mark").</summary>
    public string ParameterName { get; init; } = "Mark";

    /// <summary>Tiền tố trước số thứ tự, ví dụ "D-" cho cửa, "P-" cho phòng.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Số bắt đầu.</summary>
    public int StartNumber { get; init; } = 1;

    /// <summary>Bước nhảy giữa các số.</summary>
    public int Step { get; init; } = 1;

    /// <summary>Số chữ số tối thiểu, ví dụ 3 → "001".</summary>
    public int PadWidth { get; init; } = 0;

    /// <summary>Chỉ đánh số phần tử thuộc Level này (null = tất cả các tầng, đánh số liên tục).</summary>
    public string? LevelName { get; init; }

    /// <summary>
    /// Dung sai gom hàng/cột khi sắp xếp (mm). Hai phần tử lệch nhau trong dung sai này được coi là
    /// cùng một hàng (hoặc cùng một cột), nhờ đó thứ tự trong hàng mới có tác dụng.
    /// </summary>
    public double RowToleranceMm { get; init; } = 300.0;

    /// <summary>Hướng quét để xác định thứ tự đánh số theo vị trí hình học.</summary>
    public NumberingDirection Direction { get; init; } = NumberingDirection.LeftToRightThenTopToBottom;

    /// <summary>Chỉ xem trước, không ghi vào mô hình.</summary>
    public bool DryRun { get; init; } = true;
}
