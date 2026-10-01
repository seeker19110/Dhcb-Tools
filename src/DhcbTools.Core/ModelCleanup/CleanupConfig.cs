namespace DhcbTools.Core.ModelCleanup;

/// <summary>Cấu hình cho lệnh dọn dẹp mô hình (Giai đoạn nền tảng, lệnh #2).</summary>
public sealed class CleanupConfig
{
    /// <summary>
    /// Xoá view không đặt trên sheet (loại trừ view template, view hệ thống, sheet, legend, schedule/panel schedule).
    /// View chính chỉ bị xoá khi mọi view phụ thuộc của nó cũng bị xoá — Revit xoá view chính là xoá luôn chúng.
    /// </summary>
    public bool RemoveUnplacedViews { get; init; } = true;

    /// <summary>Xoá sheet chỉ còn khung tên: không view, schedule, ghi chú, ảnh hay đường nét nào đặt lên sheet.</summary>
    public bool RemoveEmptySheets { get; init; } = true;

    /// <summary>Chỉ liệt kê, không xoá — dùng để kỹ sư xem trước danh sách sẽ bị xoá.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Tên view (không phân biệt hoa/thường, so khớp một phần) luôn được giữ lại dù không đặt trên sheet.</summary>
    public List<string> KeepViewNameContains { get; init; } = new();
}
