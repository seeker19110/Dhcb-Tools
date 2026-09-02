using System.Collections.Generic;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình cho lệnh gán cao độ (elevation) vào tham số MEP.</summary>
public sealed class ElevationTagConfig
{
    // Không đặt mặc định cứng "DHCB_*" nữa: shared parameter đó chỉ có trong template của DHCB, dự án
    // khác không có nên lệnh ghi hụt mà vẫn báo thành công. Bỏ trống = tra theo từ điển
    // (%APPDATA%\DHCB\dictionary.json, khoá bottomElevation/topElevation/centreElevation).

    /// <summary>Tên tham số ghi cao độ đáy (mm so với ±0.000). Bỏ trống = tra theo từ điển.</summary>
    public string? BottomElevParamName { get; init; }

    /// <summary>Tên tham số ghi cao độ đỉnh. Bỏ trống = tra theo từ điển.</summary>
    public string? TopElevParamName { get; init; }

    /// <summary>Tên tham số ghi cao độ tim. Bỏ trống = tra theo từ điển.</summary>
    public string? CenterElevParamName { get; init; }

    /// <summary>Categories to process (empty = all MEP linear elements).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Level name to filter (empty = all levels).</summary>
    public string? LevelName { get; init; } = null;

    /// <summary>If true, report changes without writing to the model.</summary>
    public bool DryRun { get; init; } = true;
}
