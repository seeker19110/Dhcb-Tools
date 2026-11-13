namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình gắn một file DWG làm xref (overlay hoặc attach) vào Model Space.</summary>
public sealed class XrefAttachConfig
{
    /// <summary>Đường dẫn đầy đủ của file DWG cần gắn.</summary>
    public required string Path { get; init; }

    /// <summary>Tên block xref (rỗng = tên file không đuôi). Là tiền tố của layer xref: <c>tên|layer</c>.</summary>
    public string? Name { get; init; }

    /// <summary>true = overlay (không kéo theo khi bản vẽ này bị gắn tiếp — mặc định, như phiên bóc khối lượng); false = attach.</summary>
    public bool Overlay { get; init; } = true;

    /// <summary>Điểm chèn X (đơn vị bản vẽ, mặc định 0).</summary>
    public double X { get; init; }

    /// <summary>Điểm chèn Y (mặc định 0).</summary>
    public double Y { get; init; }

    /// <summary>Điểm chèn Z (mặc định 0).</summary>
    public double Z { get; init; }

    /// <summary>Tỉ lệ đều X=Y=Z (mặc định 1). Lệnh không tự đổi đơn vị — xem cảnh báo INSUNITS trong kết quả.</summary>
    public double? Scale { get; init; }

    /// <summary>Góc xoay (độ, ngược chiều kim đồng hồ, mặc định 0).</summary>
    public double? Rotation { get; init; }

    /// <summary>Layer đặt bản chèn xref (tạo nếu chưa có). Rỗng = layer hiện hành.</summary>
    public string? Layer { get; init; }

    /// <summary>Chỉ xem trước: kiểm file, tên, layer — không gắn.</summary>
    public bool DryRun { get; init; } = true;
}
