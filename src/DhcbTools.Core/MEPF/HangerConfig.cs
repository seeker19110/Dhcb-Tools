using System.Collections.Generic;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình cho lệnh đặt hanger trên đường ống/duct/cable tray.</summary>
public sealed class HangerConfig
{
    /// <summary>FamilySymbol name for hanger family.</summary>
    public required string HangerFamilyName { get; init; }

    /// <summary>Max spacing between hangers (mm).</summary>
    public double SpacingMm { get; init; } = 3000;

    /// <summary>Offset from element centerline to hanger insertion point (mm, upward).</summary>
    public double OffsetMm { get; init; } = 200;

    /// <summary>Categories to place hangers on (empty = Duct + Pipe + CableTray).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Level name filter (empty = all levels).</summary>
    public string? LevelName { get; init; } = null;

    /// <summary>
    /// Bỏ qua vị trí đã có sẵn một hanger cùng family trong bán kính
    /// <see cref="ExistingToleranceMm"/>. Mặc định bật — chạy lệnh lần hai không được đặt chồng
    /// lên lần một (SleeveAuto đã chống trùng từ đầu, HangerAuto trước đây thì không).
    /// </summary>
    public bool SkipExisting { get; init; } = true;

    /// <summary>Bán kính coi là "đã có hanger ở đây" (mm). Cùng dung sai 100 mm với SleeveAuto.</summary>
    public double ExistingToleranceMm { get; init; } = 100;

    /// <summary>If true, report placements without writing to the model.</summary>
    public bool DryRun { get; init; } = true;
}
