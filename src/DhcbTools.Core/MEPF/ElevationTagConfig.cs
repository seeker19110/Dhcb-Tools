using System.Collections.Generic;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình cho lệnh gán cao độ (elevation) vào tham số MEP.</summary>
public sealed class ElevationTagConfig
{
    /// <summary>Parameter name to write bottom-of-element elevation (mm from ±0.000).</summary>
    public string BottomElevParamName { get; init; } = "DHCB_Bottom_Elevation";

    /// <summary>Parameter name for top elevation.</summary>
    public string TopElevParamName { get; init; } = "DHCB_Top_Elevation";

    /// <summary>Parameter name for centerline elevation.</summary>
    public string CenterElevParamName { get; init; } = "DHCB_Center_Elevation";

    /// <summary>Categories to process (empty = all MEP linear elements).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Level name to filter (empty = all levels).</summary>
    public string LevelName { get; init; } = null;

    /// <summary>If true, report changes without writing to the model.</summary>
    public bool DryRun { get; init; } = true;
}
