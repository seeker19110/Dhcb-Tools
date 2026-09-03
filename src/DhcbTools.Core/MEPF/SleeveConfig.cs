using System.Collections.Generic;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình cho lệnh đặt sleeve/opening tại giao cắt MEP × Tường/Sàn.</summary>
public sealed class SleeveConfig
{
    /// <summary>FamilySymbol name for sleeve/opening family to place.</summary>
    public required string SleeveFamilyName { get; init; }

    /// <summary>Parameter name on sleeve family for width/diameter.</summary>
    public string WidthParamName { get; init; } = "Nominal Width";

    /// <summary>Parameter name on sleeve family for height (for rectangular).</summary>
    public string HeightParamName { get; init; } = "Nominal Height";

    /// <summary>Clearance to add around pipe/duct on each side (mm).</summary>
    public double ClearanceMm { get; init; } = 50;

    /// <summary>Only check these MEP categories (empty = all: Duct, Pipe, CableTray, Conduit).</summary>
    public List<string> MepCategories { get; init; } = new List<string>();

    /// <summary>Only place sleeve on walls/floors with these type names (empty = all).</summary>
    public List<string> HostTypeNames { get; init; } = new List<string>();

    /// <summary>If true, compute and report placements without writing to the model.</summary>
    /// <summary>
    /// Xét cả tường/sàn ở model LIÊN KẾT (mặc định bật). Dự án Việt Nam hầu như luôn tách file MEP và
    /// kiến trúc rồi link vào nhau, nên tắt cái này là lệnh không tìm thấy giao cắt nào mà vẫn báo
    /// thành công.
    /// </summary>
    public bool IncludeLinkedModels { get; init; } = true;

    /// <summary>Chỉ xét link có tên chứa một trong các chuỗi này (rỗng = mọi link đã nạp).</summary>
    public List<string> LinkNameContains { get; init; } = new List<string>();

    public bool DryRun { get; init; } = true;
}
