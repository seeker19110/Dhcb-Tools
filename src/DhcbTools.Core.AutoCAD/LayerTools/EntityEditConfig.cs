using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình lô thao tác hình học (vẽ mới hoặc sửa theo handle) — một giao dịch cho cả lô.</summary>
public sealed class EntityEditConfig
{
    /// <summary>Danh sách thao tác chạy theo thứ tự; op sau trỏ được tới đối tượng op trước tạo bằng <c>"@id"</c>.</summary>
    public List<EntityOperation> Operations { get; init; } = new();

    /// <summary>File JSON chứa mảng thao tác (hoặc <c>{"operations": [...]}</c>) — dùng thay/ghép trước <see cref="Operations"/>.</summary>
    public string? InputPath { get; init; }

    /// <summary>Chỉ xem trước: chạy thử trong giao dịch rồi huỷ, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
