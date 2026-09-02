using System.Collections.Generic;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình cho lệnh cắt đoạn ống/duct/cable tray quá dài.</summary>
public sealed class PipeSplitterConfig
{
    /// <summary>Max segment length before splitting (mm); default 6000mm = 6m.</summary>
    public double MaxSegmentMm { get; init; } = 6000;

    /// <summary>Categories to split: "Pipe", "Duct", "CableTray", "Conduit".</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Coupling family name to insert at split points (optional — leave null to just split).</summary>
    public string? CouplingFamilyName { get; init; } = null;

    /// <summary>Level name filter (empty = all levels).</summary>
    public string? LevelName { get; init; } = null;

    /// <summary>If true, report splits without writing to the model.</summary>
    public bool DryRun { get; init; } = true;
}
