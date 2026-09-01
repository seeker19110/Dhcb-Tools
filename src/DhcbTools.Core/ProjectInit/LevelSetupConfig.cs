using System.Collections.Generic;

namespace DhcbTools.Core.ProjectInit;

public sealed class LevelDefinition
{
    /// <summary>Level name, e.g. "Tang 1", "Tang 2".</summary>
    public required string Name { get; init; }

    /// <summary>Elevation in millimetres.</summary>
    public required double ElevationMm { get; init; }

    public bool CreateFloorPlan { get; init; } = true;

    /// <summary>Apply this view template to the auto-created floor plan.</summary>
    public string? ViewTemplateName { get; init; }
}

public sealed class LevelSetupConfig
{
    public required List<LevelDefinition> Levels { get; init; }

    /// <summary>When true the transaction is rolled back - only a report is returned.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Skip if a level with the same name already exists.</summary>
    public bool SkipExisting { get; init; } = true;
}
