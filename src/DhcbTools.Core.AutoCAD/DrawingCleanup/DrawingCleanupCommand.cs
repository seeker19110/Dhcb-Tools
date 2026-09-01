using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD.DrawingCleanup;

/// <summary>
/// Dọn dẹp drawing: xoá layer rỗng, purge block/linetype không dùng —
/// tương đương RemoveUnusedViewsCommand của Revit.
/// </summary>
public sealed class DrawingCleanupCommand : ICoreCommand<CleanupConfig>
{
    public string CommandName => "DrawingCleanup";

    public CommandResult Execute(Database database, CleanupConfig config)
    {
        var report = new List<string>();
        var toDeleteLayers = new List<ObjectId>();
        var toDeleteBlocks = new List<ObjectId>();
        var toDeleteLinetypes = new List<ObjectId>();

        using var transaction = database.TransactionManager.StartTransaction();

        // --- Tìm layer rỗng ---
        if (config.RemoveEmptyLayers)
        {
            var usedLayers = CollectUsedLayerNames(database, transaction);

            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId layerId in layerTable)
            {
                var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);

                // Không xoá layer "0" và layer hiện hành (CLAYER): Erase() layer hiện hành ném lỗi
                // và làm hỏng cả transaction (lỗi #6).
                if (layer.Name == "0" || layerId == database.Clayer)
                {
                    continue;
                }

                if (config.KeepLayerNameContains.Any(keep =>
                        layer.Name.IndexOf(keep, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                if (!usedLayers.Contains(layer.Name))
                {
                    toDeleteLayers.Add(layerId);
                    report.Add($"Layer rỗng: \"{layer.Name}\"");
                }
            }
        }

        // --- Tìm block definition không dùng ---
        if (config.PurgeUnusedBlocks)
        {
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId blockId in blockTable)
            {
                var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);

                // Bỏ qua *Model_Space, *Paper_Space và anonymous block
                if (block.IsLayout || block.IsAnonymous || block.Name.StartsWith("*"))
                {
                    continue;
                }

                if (!block.IsDynamicBlock && block.GetBlockReferenceIds(true, false).Count == 0)
                {
                    toDeleteBlocks.Add(blockId);
                    report.Add($"Block không dùng: \"{block.Name}\"");
                }
            }
        }

        // --- Tìm linetype không dùng ---
        if (config.PurgeUnusedLinetypes)
        {
            var reservedLinetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Continuous", "ByLayer", "ByBlock"
            };

            var usedLinetypes = CollectUsedLinetypeIds(database, transaction);

            var linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
            foreach (ObjectId ltId in linetypeTable)
            {
                var lt = (LinetypeTableRecord)transaction.GetObject(ltId, OpenMode.ForRead);
                if (reservedLinetypes.Contains(lt.Name))
                {
                    continue;
                }

                if (!usedLinetypes.Contains(ltId))
                {
                    toDeleteLinetypes.Add(ltId);
                    report.Add($"Linetype không dùng: \"{lt.Name}\"");
                }
            }
        }

        var totalToDelete = toDeleteLayers.Count + toDeleteBlocks.Count + toDeleteLinetypes.Count;

        if (totalToDelete == 0)
        {
            transaction.Commit();
            return CommandResult.Ok("Không có layer/block/linetype thừa cần dọn.");
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ xoá {totalToDelete} object (layer/block/linetype thừa).",
                totalToDelete);
            preview.Messages.AddRange(report);
            return preview;
        }

        // Xoá thật — bọc try/catch từng item: một object không xoá được (đang dùng ngầm, bị khoá)
        // không được phép làm hỏng cả transaction (lỗi #6).
        var skipped = new List<string>();
        var erased = EraseAll(transaction, toDeleteLayers, "Layer", skipped)
                   + EraseAll(transaction, toDeleteBlocks, "Block", skipped)
                   + EraseAll(transaction, toDeleteLinetypes, "Linetype", skipped);

        transaction.Commit();

        var result = CommandResult.Ok(
            $"Đã xoá {erased}/{totalToDelete} object (layer/block/linetype thừa).",
            erased);
        result.Messages.AddRange(report);
        result.Messages.AddRange(skipped);
        return result;
    }

    /// <summary>Xoá từng object, ghi lại object nào không xoá được thay vì để lỗi lan ra transaction.</summary>
    private static int EraseAll(Transaction transaction, List<ObjectId> ids, string kind, List<string> skipped)
    {
        var erased = 0;
        foreach (var id in ids)
        {
            try
            {
                var record = (SymbolTableRecord)transaction.GetObject(id, OpenMode.ForWrite);
                record.Erase();
                erased++;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                skipped.Add($"Không xoá được {kind} (id {id}): {ex.Message}");
            }
        }
        return erased;
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
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                used.Add(entity.Layer);
            }
        }

        return used;
    }

    private static HashSet<ObjectId> CollectUsedLinetypeIds(Database database, Transaction transaction)
    {
        var used = new HashSet<ObjectId>();

        // Linetype mà layer definition đang dùng cũng là "đang dùng", dù không entity nào tham chiếu
        // trực tiếp — bỏ sót chỗ này là xoá nhầm linetype còn sống (lỗi #6).
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId layerId in layerTable)
        {
            var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
            if (layer.LinetypeObjectId.IsValid)
            {
                used.Add(layer.LinetypeObjectId);
            }
        }

        // Linetype mặc định của bản vẽ (CELTYPE).
        if (database.Celtype.IsValid)
        {
            used.Add(database.Celtype);
        }

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId entityId in block)
            {
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                if (entity.LinetypeId.IsValid)
                {
                    used.Add(entity.LinetypeId);
                }
            }
        }

        return used;
    }
}
