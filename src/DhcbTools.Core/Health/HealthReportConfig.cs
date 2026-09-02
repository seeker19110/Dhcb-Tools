namespace DhcbTools.Core.Health;

/// <summary>Cấu hình tạo báo cáo sức khoẻ mô hình Revit.</summary>
public sealed class HealthReportConfig
{
    /// <summary>Đường dẫn file HTML đầu ra. Bỏ trống → Documents\DHCB_Health_&lt;title&gt;_&lt;thời điểm&gt;.html.</summary>
    public string? OutputPath { get; init; }

    public bool CheckWarnings { get; init; } = true;
    public bool CheckUnplacedViews { get; init; } = true;
    public bool CheckOpenConnectors { get; init; } = true;
    public bool CheckInPlaceFamilies { get; init; } = true;
    public bool CheckFileSizeMb { get; init; } = true;

    /// <summary>Cảnh báo khi file lớn hơn ngưỡng này (MB).</summary>
    public int FileSizeWarnMb { get; init; } = 200;
}
