using System.Collections.Generic;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình cho lệnh kiểm tra connector hở trong mô hình MEP.</summary>
public sealed class ConnectorCheckerConfig
{
    /// <summary>Categories to check (empty = all MEP categories).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Report only connectors where domain matches (empty = all).</summary>
    public List<string> Domains { get; init; } = new List<string>();

    /// <summary>
    /// true = GHI một 3D view khoanh vùng phần tử có connector hở vào mô hình. Mặc định false vì đây là lệnh
    /// kiểm tra (catalog khai writesModel:false); chỉ bật khi kỹ sư muốn xem trực quan.
    /// </summary>
    public bool Create3dView { get; init; } = false;

    /// <summary>Xem trước: liệt kê nhưng không tạo view, kể cả khi create3dView = true.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Name of the 3D view to create/reuse.</summary>
    public string ViewName { get; init; } = "DHCB - Open Connectors";
}
