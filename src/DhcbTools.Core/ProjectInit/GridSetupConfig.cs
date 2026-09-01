using System.Collections.Generic;

namespace DhcbTools.Core.ProjectInit;

public enum GridOrientation { Horizontal, Vertical }

public sealed class GridDefinition
{
    /// <summary>Grid name / bubble label.</summary>
    public required string Name { get; init; }

    /// <summary>X position (mm) for Vertical grids; Y position (mm) for Horizontal grids.</summary>
    public required double PositionMm { get; init; }

    public GridOrientation Orientation { get; init; } = GridOrientation.Vertical;

    /// <summary>Line start offset in mm.</summary>
    public double StartMm { get; init; } = -30000;

    /// <summary>Line end offset in mm.</summary>
    public double EndMm { get; init; } = 30000;
}

public sealed class GridSetupConfig
{
    public required List<GridDefinition> Grids { get; init; }
    public bool DryRun { get; init; } = true;
    public bool SkipExisting { get; init; } = true;
}
