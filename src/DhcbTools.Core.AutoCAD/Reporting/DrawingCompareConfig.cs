namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// Cấu hình so sánh bản vẽ hiện tại với một file DWG khác.
/// Lệnh chạy HAI mức: mức layer (đếm entity theo layer — luôn có) và mức entity theo Handle (chỉ có nghĩa
/// khi bản kia là bản lưu khác của CÙNG bản vẽ; hai file độc lập không chung handle thì mức này tự tắt và
/// lệnh nói rõ trong Messages thay vì liệt kê mọi thứ là thêm/xoá).
/// </summary>
public sealed class DrawingCompareConfig
{
    /// <summary>Đường dẫn file DWG khác để so sánh.</summary>
    public required string OtherPath { get; init; }

    /// <summary>Đường dẫn file báo cáo — .html xuất HTML, còn lại xuất CSV.</summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Ngưỡng coi là "đã di chuyển" khi so theo Handle: tâm bounding box lệch quá giá trị này (đơn vị bản vẽ,
    /// thường là mm) thì entity được báo là đã di chuyển. Mặc định 0 = mọi dịch chuyển đều báo.
    /// </summary>
    public double MoveToleranceMm { get; init; }
}
