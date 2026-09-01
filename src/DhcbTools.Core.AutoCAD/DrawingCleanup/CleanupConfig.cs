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

    /// <summary>Purge text style không dùng (giữ Standard và style hiện hành).</summary>
    public bool PurgeUnusedTextStyles { get; init; } = false;

    /// <summary>Purge dimension style không dùng (giữ Standard/ISO-25 và style hiện hành).</summary>
    public bool PurgeUnusedDimStyles { get; init; } = false;

    /// <summary>Purge RegApp không còn xdata tham chiếu (ACAD giữ lại).</summary>
    public bool PurgeRegApps { get; init; } = false;

    /// <summary>Tên layer luôn được giữ lại dù rỗng (không phân biệt hoa/thường, so khớp một phần).</summary>
    public List<string> KeepLayerNameContains { get; init; } = new();

    /// <summary>Chỉ liệt kê, không xoá — kỹ sư xem trước trước khi chạy thật.</summary>
    public bool DryRun { get; init; } = true;
}
