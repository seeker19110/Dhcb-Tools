using System.Collections.Generic;

namespace DhcbTools.Core.ProjectInit;

public sealed class ProjectInfoConfig
{
    public string? ProjectNumber { get; init; }
    public string? ProjectName { get; init; }
    public string? ProjectStatus { get; init; }
    public string? ClientName { get; init; }
    public string? BuildingName { get; init; }
    public string? Address { get; init; }
    public string? OrganizationName { get; init; }

    /// <summary>Extra parameters to set by name.</summary>
    public Dictionary<string, string> ExtraParameters { get; init; } = new Dictionary<string, string>();
}
