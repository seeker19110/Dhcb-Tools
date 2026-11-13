using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình đặt block (từ bản vẽ hiện tại hoặc thư viện DWG) vào Model Space.</summary>
public sealed class BlockInsertConfig
{
    /// <summary>Tên block cần đặt.</summary>
    public required string BlockName { get; init; }

    /// <summary>
    /// Nguồn định nghĩa khi bản vẽ chưa có block (hoặc khi <see cref="Redefine"/>): một file DWG thư viện chứa block
    /// tên đó, một DWG mà cả file là ký hiệu, hoặc một thư mục có <c>&lt;BlockName&gt;.dwg</c>.
    /// </summary>
    public string? LibraryPath { get; init; }

    /// <summary>Nạp lại định nghĩa từ thư viện dù bản vẽ đã có block cùng tên (cập nhật ký hiệu).</summary>
    public bool Redefine { get; init; }

    /// <summary>Danh sách vị trí đặt: position, scale, rotation (độ), layer, attributes.</summary>
    public List<BlockInsertion> Insertions { get; init; } = new();

    /// <summary>CSV <c>X,Y[,Z],Rotation,Scale,Layer,&lt;TAG&gt;…</c> — ghép sau <see cref="Insertions"/>.</summary>
    public string? InputPath { get; init; }

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
