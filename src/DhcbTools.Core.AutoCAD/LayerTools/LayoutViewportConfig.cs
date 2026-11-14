using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình tạo khung bản vẽ (layout giấy) từ layout mẫu và canh viewport theo tâm + tỉ lệ.</summary>
public sealed class LayoutViewportConfig
{
    /// <summary>Danh sách khung: name, template, viewCenter, scale, viewportHandle, title, titleMatch, scaleText, lock.</summary>
    public List<LayoutSheet> Sheets { get; init; } = new();

    /// <summary>File JSON chứa mảng khung (hoặc <c>{"sheets": [...]}</c>) — ghép trước <see cref="Sheets"/>.</summary>
    public string? InputPath { get; init; }

    /// <summary>Layout trùng tên đã có: false = báo lỗi (mặc định), true = xoá rồi tạo lại.</summary>
    public bool ReplaceExisting { get; init; }

    /// <summary>Chỉ xem trước: kiểm mẫu, viewport, MText tiêu đề — không tạo layout.</summary>
    public bool DryRun { get; init; } = true;
}
