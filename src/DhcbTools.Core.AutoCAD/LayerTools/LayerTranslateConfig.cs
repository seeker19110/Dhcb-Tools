namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Cấu hình map layer cũ → layer chuẩn theo CSV (đổi entity sang layer đích, tuỳ chọn xoá layer nguồn
/// rỗng) — tương đương lệnh LAYTRANS của AutoCAD.
/// </summary>
public sealed class LayerTranslateConfig
{
    /// <summary>CSV cột: Source,Target,Color,Linetype,Lineweight,Plottable (4 cột sau tuỳ chọn).</summary>
    public required string MapCsvPath { get; init; }

    /// <summary>Xoá layer nguồn nếu sau khi chuyển không còn entity nào tham chiếu (trừ layer "0").</summary>
    public bool DeleteEmptySource { get; init; }

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
