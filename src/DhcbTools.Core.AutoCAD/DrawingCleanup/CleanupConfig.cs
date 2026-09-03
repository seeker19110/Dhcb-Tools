namespace DhcbTools.Core.AutoCAD.DrawingCleanup;

/// <summary>
/// Cấu hình cho lệnh dọn dẹp drawing — tương đương CleanupConfig của Revit.
/// </summary>
public sealed class CleanupConfig
{
    /// <summary>Xoá layer rỗng (không có entity nào tham chiếu).</summary>
    public bool RemoveEmptyLayers { get; init; } = true;

    /// <summary>Purge block definition không dùng.</summary>
    public bool PurgeUnusedBlocks { get; init; } = true;

    /// <summary>Purge linetype không dùng (giữ lại Continuous, ByLayer, ByBlock).</summary>
    public bool PurgeUnusedLinetypes { get; init; } = true;

    /// <summary>Purge text style không dùng (giữ lại Standard và style của dim style đang dùng).</summary>
    public bool PurgeUnusedTextStyles { get; init; } = false;

    /// <summary>Purge dimension style không dùng (giữ lại Standard).</summary>
    public bool PurgeUnusedDimStyles { get; init; } = false;

    /// <summary>
    /// Purge RegApp (tên ứng dụng đăng ký XData) không còn entity nào mang XData của nó.
    /// Đây là thứ phình DWG âm thầm sau nhiều năm đi qua nhiều add-in.
    /// </summary>
    public bool PurgeRegApps { get; init; } = false;

    /// <summary>Tên layer luôn được giữ lại dù rỗng (không phân biệt hoa/thường, so khớp một phần).</summary>
    public List<string> KeepLayerNameContains { get; init; } = new();

    /// <summary>Chỉ liệt kê, không xoá — kỹ sư xem trước trước khi chạy thật.</summary>
    public bool DryRun { get; init; } = true;
}
