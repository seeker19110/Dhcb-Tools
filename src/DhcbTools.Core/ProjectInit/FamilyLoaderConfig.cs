using System.Collections.Generic;

namespace DhcbTools.Core.ProjectInit;

public sealed class FamilyLoaderConfig
{
    /// <summary>Folder containing .rfa files to load.</summary>
    public required string FamilyFolder { get; init; }

    /// <summary>
    /// Only load families matching these names (without .rfa extension).
    /// Empty list = load all files in the folder.
    /// </summary>
    public List<string> FamilyNames { get; init; } = new List<string>();

    /// <summary>If true, overwrite existing family with the same name.</summary>
    public bool OverwriteExisting { get; init; } = false;

    public bool DryRun { get; init; } = true;
}
