using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.DrawingCleanup;

/// <summary>
/// Dọn dẹp drawing: xoá layer rỗng, purge block/linetype không dùng — tương đương RemoveUnusedViewsCommand của Revit.
/// Sửa lỗi #6 (mục 0.4): (1) linetype dùng bởi layer definition được coi là "đang dùng"; (2) không xoá layer hiện hành,
/// layer "0"/"Defpoints", layer của xref, linetype hệ thống; (3) try/catch quanh từng item — một Erase() hỏng
/// không làm hỏng cả transaction. Quyết định xoá đi qua <see cref="CleanupDecider"/> (đã test).
/// </summary>
public sealed class DrawingCleanupCommand : ICoreCommand<CleanupConfig>
{
    public string CommandName => "DrawingCleanup";

    public CommandResult Execute(Database database, CleanupConfig config)
    {
        var report = new List<string>();
        var toDeleteLayers = new List<(ObjectId Id, string Name)>();
        var toDeleteBlocks = new List<(ObjectId Id, string Name)>();
        var toDeleteLinetypes = new List<(ObjectId Id, string Name)>();

        using var transaction = database.TransactionManager.StartTransaction();

        var usedLayers = CollectUsedLayerNames(database, transaction);
        var usedLinetypes = CollectUsedLinetypeIds(database, transaction);
        var currentLayerName = database.Clayer.IsValid ? ((LayerTableRecord)transaction.GetObject(database.Clayer, OpenMode.ForRead)).Name : "0";

        if (config.RemoveEmptyLayers)
        {
            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId layerId in layerTable)
            {
                var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
                var isCurrent = string.Equals(layer.Name, currentLayerName, StringComparison.OrdinalIgnoreCase) || layerId == database.Clayer;
                var isUsed = usedLayers.Contains(layer.Name) || layer.IsDependent;

                if (CleanupDecider.ShouldErase(layer.Name, isUsed, isCurrent, CleanupDecider.IsSystemLayer(layer.Name), config.KeepLayerNameContains))
                {
                    toDeleteLayers.Add((layerId, layer.Name));
                    report.Add($"Layer rỗng: \"{layer.Name}\"");
                }
                else if (isCurrent && !isUsed && !CleanupDecider.IsSystemLayer(layer.Name))
                {
                    report.Add($"Giữ layer hiện hành \"{layer.Name}\" dù rỗng.");
                }
            }
        }

        if (config.PurgeUnusedBlocks)
        {
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId blockId in blockTable)
            {
                var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
                if (block.IsLayout || block.IsAnonymous || block.Name.StartsWith("*", StringComparison.Ordinal) || block.IsFromExternalReference || block.IsDependent)
                {
                    continue;
                }

                var refs = block.GetBlockReferenceIds(true, false).Count;
                var anonRefs = block.IsDynamicBlock ? block.GetAnonymousBlockIds().Count : 0;
                if (refs == 0 && anonRefs == 0)
                {
                    toDeleteBlocks.Add((blockId, block.Name));
                    report.Add($"Block không dùng: \"{block.Name}\"");
                }
            }
        }

        if (config.PurgeUnusedLinetypes)
        {
            var linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
            foreach (ObjectId ltId in linetypeTable)
            {
                var lt = (LinetypeTableRecord)transaction.GetObject(ltId, OpenMode.ForRead);
                var isCurrent = ltId == database.Celtype;
                var isUsed = usedLinetypes.Contains(ltId) || lt.IsDependent;
                if (CleanupDecider.ShouldErase(lt.Name, isUsed, isCurrent, CleanupDecider.IsSystemLinetype(lt.Name)))
                {
                    toDeleteLinetypes.Add((ltId, lt.Name));
                    report.Add($"Linetype không dùng: \"{lt.Name}\"");
                }
            }
        }

        var toDeleteStyles = new List<(ObjectId Id, string Name)>();
        if (config.PurgeUnusedTextStyles)
        {
            var used = new HashSet<ObjectId> { database.Textstyle };
            ForEachEntity(database, transaction, e =>
            {
                switch (e)
                {
                    case AttributeDefinition ad: used.Add(ad.TextStyleId); break; // kế thừa DBText — xét trước
                    case DBText t: used.Add(t.TextStyleId); break;
                    case MText m: used.Add(m.TextStyleId); break;
                }
            });
            foreach (var d in EnumerateDimStyles(database, transaction)) { used.Add(d.Dimtxsty); }
            var tst = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in tst)
            {
                var ts = (TextStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                if (ts.IsShapeFile) continue;
                var isSystem = ts.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase) || ts.Name.Equals("Annotative", StringComparison.OrdinalIgnoreCase);
                if (CleanupDecider.ShouldErase(ts.Name, used.Contains(id) || ts.IsDependent, id == database.Textstyle, isSystem))
                {
                    toDeleteStyles.Add((id, "TextStyle " + ts.Name));
                    report.Add($"Text style không dùng: \"{ts.Name}\"");
                }
            }
        }

        if (config.PurgeUnusedDimStyles)
        {
            var used = new HashSet<ObjectId> { database.Dimstyle };
            ForEachEntity(database, transaction, e =>
            {
                if (e is Dimension dim) used.Add(dim.DimensionStyle);
                if (e is Leader ld) used.Add(ld.DimensionStyle);
            });
            var dst = (DimStyleTable)transaction.GetObject(database.DimStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in dst)
            {
                var ds = (DimStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                var isSystem = ds.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase) || ds.Name.Equals("ISO-25", StringComparison.OrdinalIgnoreCase) || ds.Name.StartsWith("*", StringComparison.Ordinal);
                if (CleanupDecider.ShouldErase(ds.Name, used.Contains(id) || ds.IsDependent, id == database.Dimstyle, isSystem))
                {
                    toDeleteStyles.Add((id, "DimStyle " + ds.Name));
                    report.Add($"Dim style không dùng: \"{ds.Name}\"");
                }
            }
        }

        if (config.PurgeRegApps)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ACAD", "ACAD_PSEXT", "ACAD_DSTYLE_DIMJAG", "ACAD_MLEADERVER" };
            ForEachEntity(database, transaction, e =>
            {
                var xd = e.XData;
                if (xd == null) return;
                foreach (var tv in xd) { if (tv.TypeCode == 1001 && tv.Value is string app) used.Add(app); }
            });
            var rat = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
            foreach (ObjectId id in rat)
            {
                var ra = (RegAppTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                if (CleanupDecider.ShouldErase(ra.Name, used.Contains(ra.Name) || ra.IsDependent, false, ra.Name.StartsWith("ACAD", StringComparison.OrdinalIgnoreCase)))
                {
                    toDeleteStyles.Add((id, "RegApp " + ra.Name));
                    report.Add($"RegApp không dùng: \"{ra.Name}\"");
                }
            }
        }

        var totalToDelete = toDeleteLayers.Count + toDeleteBlocks.Count + toDeleteLinetypes.Count + toDeleteStyles.Count;
        if (totalToDelete == 0)
        {
            transaction.Abort();
            var none = CommandResult.Ok("Không có layer/block/linetype thừa cần dọn.");
            none.Messages.AddRange(report);
            return none;
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok($"[Xem trước] Sẽ xoá {totalToDelete} object (layer/block/linetype thừa).", totalToDelete);
            preview.Messages.AddRange(report);
            return preview;
        }

        var result = CommandResult.Ok(string.Empty);
        result.Messages.AddRange(report);
        var deleted = 0;

        // Block trước (giải phóng layer/linetype mà block đang giữ), rồi layer, rồi linetype.
        deleted += EraseEach(transaction, toDeleteBlocks, "Block", result);
        deleted += EraseEach(transaction, toDeleteLayers, "Layer", result);
        deleted += EraseEach(transaction, toDeleteLinetypes, "Linetype", result);
        deleted += EraseEach(transaction, toDeleteStyles, "Style", result);

        transaction.Commit();
        result.Summary = $"Đã xoá {deleted}/{totalToDelete} object (layer/block/linetype thừa).";
        result.AffectedCount = deleted;
        return result;
    }

    private static int EraseEach(Transaction tr, List<(ObjectId Id, string Name)> items, string kind, CommandResult result)
    {
        var count = 0;
        foreach (var (id, name) in items)
        {
            try
            {
                var obj = tr.GetObject(id, OpenMode.ForWrite);
                obj.Erase();
                count++;
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Không xoá được {kind} \"{name}\": {ex.Message}");
            }
        }
        return count;
    }

    private static void ForEachEntity(Database database, Transaction transaction, Action<Entity> action)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId entityId in block)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead) is Entity entity)
                {
                    action(entity);
                }
            }
        }
    }

    private static IEnumerable<DimStyleTableRecord> EnumerateDimStyles(Database database, Transaction transaction)
    {
        var dst = (DimStyleTable)transaction.GetObject(database.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in dst)
        {
            yield return (DimStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
        }
    }

    private static HashSet<string> CollectUsedLayerNames(Database database, Transaction transaction)
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

        // Layer đang dùng bởi viewport freeze list hay layer state không tính là "rỗng" — giữ nguyên qua IsDependent ở trên.
        return used;
    }

    private static HashSet<ObjectId> CollectUsedLinetypeIds(Database database, Transaction transaction)
    {
        var used = new HashSet<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId entityId in block)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead) is Entity entity && entity.LinetypeId.IsValid)
                {
                    used.Add(entity.LinetypeId);
                }
            }
        }

        // Lỗi #6: linetype chỉ được layer definition dùng cũng là "đang dùng".
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId layerId in layerTable)
        {
            var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
            if (layer.LinetypeObjectId.IsValid)
            {
                used.Add(layer.LinetypeObjectId);
            }
        }

        used.Add(database.Celtype);
        used.Add(database.ContinuousLinetype);
        used.Add(database.ByLayerLinetype);
        used.Add(database.ByBlockLinetype);
        return used;
    }
}
