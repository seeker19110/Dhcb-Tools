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
                if (transaction.GetObject(entityId, OpenMode.ForRead) is not Entity entity)
                {
                    continue;
                }

                used.Add(entity.Layer);

                // AttributeReference thuộc BlockReference, KHÔNG nằm trong BlockTableRecord — bỏ sót thì layer chỉ chứa
                // chữ của title block (TEXT-ATT) bị coi là rỗng và DrawingCleanup/LayerTranslate xoá nó.
                if (entity is BlockReference blockRef)
                {
                    foreach (ObjectId attId in blockRef.AttributeCollection)
                    {
                        if (transaction.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef)
                        {
                            used.Add(attRef.Layer);
                        }
                    }
                }
            }
        }

        return used;
    }

    /// <summary>
    /// Như <see cref="CollectUsedLayerNames"/> nhưng tên layer của entity/attribute (ngoài block được bảo vệ, không nằm
    /// trên layer khoá — đúng tập LayerTranslate thật sự đổi) được thay theo <paramref name="map"/> — để xem trước của
    /// LayerTranslate biết layer nguồn nào sẽ RỖNG sau khi đổi.
    /// </summary>
    public static HashSet<string> CollectUsedLayerNamesAfterMap(
        Database database, Transaction transaction, IReadOnlyDictionary<string, string> map, ISet<ObjectId> lockedLayers)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        string After(bool protectedBlock, Entity entity) =>
            !protectedBlock && !lockedLayers.Contains(entity.LayerId) && map.TryGetValue(entity.Layer, out var target)
                ? target
                : entity.Layer;

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            var protectedBlock = IsProtectedBlock(block);
            foreach (ObjectId entityId in block)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead) is not Entity entity)
                {
                    continue;
                }

                used.Add(After(protectedBlock, entity));
                if (entity is BlockReference blockRef)
                {
                    foreach (ObjectId attId in blockRef.AttributeCollection)
                    {
                        if (transaction.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef)
                        {
                            used.Add(After(protectedBlock, attRef));
                        }
                    }
                }
            }
        }

        return used;
    }

    /// <summary>
    /// ObjectId của mọi layer đang khoá. Mở để ghi một entity/attribute trên layer khoá thì AutoCAD ném
    /// <c>eOnLockedLayer</c> và cả lệnh sập — lệnh ghi phải tra tập này rồi bỏ qua + báo (xem <c>LockedLayerSkips</c>).
    /// </summary>
    public static HashSet<ObjectId> LockedLayerIds(Database database, Transaction transaction)
    {
        var locked = new HashSet<ObjectId>();
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId layerId in layerTable)
        {
            if (((LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead)).IsLocked)
            {
                locked.Add(layerId);
            }
        }

        return locked;
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
