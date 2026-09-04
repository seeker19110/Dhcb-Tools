using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD;

/// <summary>
/// Tiện ích dùng chung cho các lệnh Core AutoCAD. Trước đây <see cref="CollectUsedLayerNames"/> bị chép ở
/// DrawingCleanup lẫn LayerTranslate, còn việc tạo thư mục đầu ra thì mỗi lệnh một kiểu (phần lớn quên).
/// </summary>
internal static class AcadHelpers
{
    /// <summary>
    /// Tên symbol (layer, block, linetype…) có hợp lệ với AutoCAD không. Tên có ký tự cấm
    /// (<c>&lt; &gt; / \ " : ; ? * | , = `</c>) hoặc rỗng khiến <c>LayerTableRecord.Name</c> ném exception
    /// bên trong transaction — phải lọc ra và BÁO trước, không để lệnh sập giữa chừng.
    /// </summary>
    public static bool IsValidSymbolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            SymbolUtilityServices.ValidateSymbolName(name, false);
            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    /// <summary>Tên layer của mọi entity trong mọi Block Table Record (kể cả block definition, paper space).</summary>
    public static HashSet<string> CollectUsedLayerNames(Database database, Transaction transaction)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId entityId in block)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead) is Entity entity)
                {
                    used.Add(entity.Layer);
                }
            }
        }

        return used;
    }

    /// <summary>
    /// Block Table Record không được sửa entity bên trong: block của xref (sửa là đổi file người khác) và
    /// block anonymous (*U…, *D…, hatch/dimension nội bộ do AutoCAD tự quản).
    /// </summary>
    public static bool IsProtectedBlock(BlockTableRecord block)
        => block.IsFromExternalReference || block.IsFromOverlayReference || block.IsDependent || block.IsAnonymous;

    /// <summary>Tạo thư mục cha của file đầu ra nếu chưa có — ghi vào thư mục chưa tồn tại là lỗi hay gặp nhất trong batch.</summary>
    public static void EnsureParentDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Handle của một ObjectId dưới dạng chữ AutoCAD hiển thị (hex hoa) — định danh bền, khác ObjectId chỉ sống trong phiên.</summary>
    public static string HandleOf(ObjectId id) => Shared.Logic.Cad.HandleText.ToText(id.Handle.Value);

    /// <summary>Tên block thật của một Block Reference (tên định nghĩa gốc với dynamic block, không phải *U12).</summary>
    public static string EffectiveBlockName(Transaction transaction, BlockReference blockRef)
        => blockRef.IsDynamicBlock
            ? ((BlockTableRecord)transaction.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name
            : blockRef.Name;
}
