using System.Collections.Generic;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình cho lệnh kiểm tra connector hở trong mô hình MEP.</summary>
public sealed class ConnectorCheckerConfig
{
    /// <summary>Categories to check (empty = all MEP categories).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Report only connectors where domain matches (empty = all).</summary>
    public List<string> Domains { get; init; } = new List<string>();

    /// <summary>Create 3D View isolating open connectors.</summary>
    public bool Create3dView { get; init; } = true;

    /// <summary>Name of the 3D view to create/reuse.</summary>
    public string ViewName { get; init; } = "DHCB - Open Connectors";
}
