namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// Cấu hình so sánh bản vẽ hiện tại với một file DWG khác.
/// GHI CHÚ QUAN TRỌNG: so khớp theo Handle giữa hai file DWG độc lập không đáng tin (handle được
/// AutoCAD cấp lại mỗi khi save-as/insert nên hai file riêng biệt gần như chắc chắn không cùng handle
/// cho "cùng một" entity) — vì vậy bản triển khai này CHỈ so sánh ở MỨC LAYER: đếm số entity theo layer
/// ở mỗi file rồi báo chênh lệch, không phải so từng entity theo handle như đặc tả gốc mô tả.
/// </summary>
public sealed class DrawingCompareConfig
{
    /// <summary>Đường dẫn file DWG khác để so sánh.</summary>
    public required string OtherPath { get; init; }

    /// <summary>Đường dẫn file báo cáo — .html xuất HTML, còn lại xuất CSV.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Giữ lại để tương thích đặc tả — KHÔNG dùng trong bản so sánh mức layer hiện tại.</summary>
    public double MoveToleranceMm { get; init; }
}
