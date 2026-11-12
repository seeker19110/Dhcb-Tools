namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình vẽ polyline từ CSV vào Model Space.</summary>
public sealed class PolylineImportConfig
{
    /// <summary>CSV cột: Id,Layer,Width,Color,Closed,Vertices (Vertices = "x y;x y;…").</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Xoá các polyline đang có trong Model Space trên đúng những layer đích của file trước khi vẽ — để chạy lại sau khi
    /// tính lại không bị vẽ chồng hai lần. Chỉ xoá Polyline, không đụng loại đối tượng khác trên layer đó.
    /// </summary>
    public bool ReplaceExisting { get; init; }

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
