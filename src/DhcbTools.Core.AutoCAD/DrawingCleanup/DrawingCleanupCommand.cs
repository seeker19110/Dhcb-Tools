using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.DrawingCleanup;

/// <summary>
/// Dọn dẹp drawing: layer rỗng, block/linetype/text style/dim style/regapp không dùng —
/// tương đương RemoveUnusedViewsCommand của Revit.
/// <para>
/// Quyết định "có được xoá không" nằm ở <see cref="CleanupDecider"/> (thuần, có test): layer hiện hành,
/// layer/linetype hệ thống, layer của xref ("Xref|Layer") và mọi thứ còn được tham chiếu đều không bị đụng.
/// Trước đây lớp này tự quyết định bằng mã inline nên bỏ sót Defpoints, CLAYER và linetype mà layer
/// definition đang dùng — <c>CleanupDecider</c> có mà không ai gọi.
/// </para>
/// </summary>
public sealed class DrawingCleanupCommand : ICoreCommand<CleanupConfig>
{
    public string CommandName => "DrawingCleanup";

    public CommandResult Execute(Database database, CleanupConfig config)
    {
        var report = new List<string>();
        var toErase = new List<ObjectId>();

        using var transaction = database.TransactionManager.StartTransaction();

        if (config.RemoveEmptyLayers)
        {
            CollectLayers(database, transaction, config, toErase, report);
        }

        if (config.PurgeUnusedBlocks)
        {
            CollectBlocks(database, transaction, toErase, report);
        }

        if (config.PurgeUnusedLinetypes)
        {
            CollectLinetypes(database, transaction, toErase, report);
        }

        if (config.PurgeUnusedTextStyles)
        {
            CollectTextStyles(database, transaction, toErase, report);
        }

        if (config.PurgeUnusedDimStyles)
        {
            CollectDimStyles(database, transaction, toErase, report);
        }

        if (config.PurgeRegApps)
        {
            CollectRegApps(database, transaction, toErase, report);
        }

        if (toErase.Count == 0)
        {
            transaction.Commit();
            return CommandResult.Ok("Không có đối tượng thừa nào cần dọn.");
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok($"[Xem trước] Sẽ xoá {toErase.Count} đối tượng thừa.", toErase.Count);
            preview.Messages.AddRange(report);
            return preview;
        }

        // Xoá thật. Một bản ghi hỏng (đang bị khoá, hoặc vừa bị tham chiếu lại) không được làm đổ cả lượt:
        // ghi lại rồi đi tiếp, để kỹ sư biết chính xác cái nào không xoá được.
        var erased = 0;
        foreach (var id in toErase)
        {
            try
            {
                var record = (SymbolTableRecord)transaction.GetObject(id, OpenMode.ForWrite);
                record.Erase();
                erased++;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                report.Add($"Không xoá được (AutoCAD từ chối): {ex.Message}");
            }
        }

        transaction.Commit();

        var result = CommandResult.Ok($"Đã xoá {erased}/{toErase.Count} đối tượng thừa.", erased);
        result.Messages.AddRange(report);
        return result;
    }

    // ── Layer ────────────────────────────────────────────────────────────────

    private static void CollectLayers(
        Database database, Transaction transaction, CleanupConfig config, List<ObjectId> toErase, List<string> report)
    {
        var usedLayers = CollectUsedLayerNames(database, transaction);
        var currentLayerId = database.Clayer;

        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId layerId in layerTable)
        {
            var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);

            var erase = CleanupDecider.ShouldErase(
                layer.Name,
                isUsed: usedLayers.Contains(layer.Name),
                isCurrent: layerId == currentLayerId,
                isSystem: CleanupDecider.IsSystemLayer(layer.Name),
                keepPatterns: config.KeepLayerNameContains);

            if (erase)
            {
                toErase.Add(layerId);
                report.Add($"Layer rỗng: \"{layer.Name}\"");
            }
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
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                used.Add(entity.Layer);
            }
        }

        return used;
    }

    // ── Block ────────────────────────────────────────────────────────────────

    private static void CollectBlocks(Database database, Transaction transaction, List<ObjectId> toErase, List<string> report)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);

            // Bỏ qua *Model_Space, *Paper_Space, anonymous block và mọi block của xref.
            if (block.IsLayout || block.IsAnonymous || block.Name.StartsWith("*")
                || block.IsFromExternalReference || block.IsFromOverlayReference)
            {
                continue;
            }

            if (!block.IsDynamicBlock && block.GetBlockReferenceIds(true, false).Count == 0)
            {
                toErase.Add(blockId);
                report.Add($"Block không dùng: \"{block.Name}\"");
            }
        }
    }

    // ── Linetype ─────────────────────────────────────────────────────────────

    private static void CollectLinetypes(Database database, Transaction transaction, List<ObjectId> toErase, List<string> report)
    {
        var usedLinetypes = CollectUsedLinetypeIds(database, transaction);
        var currentLinetypeId = database.Celtype;

        var linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
        foreach (ObjectId ltId in linetypeTable)
        {
            var lt = (LinetypeTableRecord)transaction.GetObject(ltId, OpenMode.ForRead);

            var erase = CleanupDecider.ShouldErase(
                lt.Name,
                isUsed: usedLinetypes.Contains(ltId),
                isCurrent: ltId == currentLinetypeId,
                isSystem: CleanupDecider.IsSystemLinetype(lt.Name));

            if (erase)
            {
                toErase.Add(ltId);
                report.Add($"Linetype không dùng: \"{lt.Name}\"");
            }
        }
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
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                if (entity.LinetypeId.IsValid)
                {
                    used.Add(entity.LinetypeId);
                }
            }
        }

        // Linetype gán ở LAYER chứ không ở entity: bỏ qua chỗ này là purge mất nét đứt của cả một layer
        // — lỗi im lặng chỉ lộ ra khi in.
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId layerId in layerTable)
        {
            var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
            if (layer.LinetypeObjectId.IsValid)
            {
                used.Add(layer.LinetypeObjectId);
            }
        }

        return used;
    }

    // ── Text style ───────────────────────────────────────────────────────────

    private static void CollectTextStyles(Database database, Transaction transaction, List<ObjectId> toErase, List<string> report)
    {
        var used = new HashSet<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId entityId in block)
            {
                switch (transaction.GetObject(entityId, OpenMode.ForRead))
                {
                    case DBText text:
                        used.Add(text.TextStyleId);
                        break;
                    case MText mtext:
                        used.Add(mtext.TextStyleId);
                        break;
                    case MLeader mleader:
                        used.Add(mleader.TextStyleId);
                        break;
                    case BlockReference reference:
                        foreach (ObjectId attributeId in reference.AttributeCollection)
                        {
                            if (transaction.GetObject(attributeId, OpenMode.ForRead) is AttributeReference attribute)
                            {
                                used.Add(attribute.TextStyleId);
                            }
                        }
                        break;
                }
            }
        }

        // Dim style tham chiếu text style — purge text style của một dim style còn sống là hỏng dim.
        var dimStyleTable = (DimStyleTable)transaction.GetObject(database.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId dimStyleId in dimStyleTable)
        {
            var dimStyle = (DimStyleTableRecord)transaction.GetObject(dimStyleId, OpenMode.ForRead);
            if (dimStyle.Dimtxsty.IsValid)
            {
                used.Add(dimStyle.Dimtxsty);
            }
        }

        var textStyleTable = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
        foreach (ObjectId styleId in textStyleTable)
        {
            var style = (TextStyleTableRecord)transaction.GetObject(styleId, OpenMode.ForRead);

            // Style hình dạng (SHX shape file) không phải text style hiển thị — không đụng tới.
            if (style.IsShapeFile)
            {
                continue;
            }

            var erase = CleanupDecider.ShouldErase(
                style.Name,
                isUsed: used.Contains(styleId),
                isCurrent: styleId == database.Textstyle,
                isSystem: CleanupDecider.IsSystemTextStyle(style.Name));

            if (erase)
            {
                toErase.Add(styleId);
                report.Add($"Text style không dùng: \"{style.Name}\"");
            }
        }
    }

    // ── Dim style ────────────────────────────────────────────────────────────

    private static void CollectDimStyles(Database database, Transaction transaction, List<ObjectId> toErase, List<string> report)
    {
        var used = new HashSet<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId entityId in block)
            {
                switch (transaction.GetObject(entityId, OpenMode.ForRead))
                {
                    case Dimension dimension:
                        used.Add(dimension.DimensionStyle);
                        break;
                    case Leader leader:
                        used.Add(leader.DimensionStyle);
                        break;
                    case FeatureControlFrame frame:
                        used.Add(frame.DimensionStyle);
                        break;
                }
            }
        }

        var dimStyleTable = (DimStyleTable)transaction.GetObject(database.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId dimStyleId in dimStyleTable)
        {
            var dimStyle = (DimStyleTableRecord)transaction.GetObject(dimStyleId, OpenMode.ForRead);

            var erase = CleanupDecider.ShouldErase(
                dimStyle.Name,
                isUsed: used.Contains(dimStyleId),
                isCurrent: dimStyleId == database.Dimstyle,
                isSystem: CleanupDecider.IsSystemDimStyle(dimStyle.Name));

            if (erase)
            {
                toErase.Add(dimStyleId);
                report.Add($"Dim style không dùng: \"{dimStyle.Name}\"");
            }
        }
    }

    // ── RegApp ───────────────────────────────────────────────────────────────

    private static void CollectRegApps(Database database, Transaction transaction, List<ObjectId> toErase, List<string> report)
    {
        var used = CollectUsedRegAppNames(database, transaction);

        var regAppTable = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
        foreach (ObjectId regAppId in regAppTable)
        {
            var regApp = (RegAppTableRecord)transaction.GetObject(regAppId, OpenMode.ForRead);

            var erase = CleanupDecider.ShouldErase(
                regApp.Name,
                isUsed: used.Contains(regApp.Name),
                isCurrent: false,
                isSystem: CleanupDecider.IsSystemRegApp(regApp.Name));

            if (erase)
            {
                toErase.Add(regAppId);
                report.Add($"RegApp không dùng: \"{regApp.Name}\"");
            }
        }
    }

    /// <summary>
    /// Tên RegApp còn được XData của một đối tượng nào đó dùng. Trong ResultBuffer của XData, mã 1001
    /// (<see cref="DxfCode.ExtendedDataRegAppName"/>) chính là tên ứng dụng.
    /// </summary>
    private static HashSet<string> CollectUsedRegAppNames(Database database, Transaction transaction)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            AddXDataApps(block.XData, used);

            foreach (ObjectId entityId in block)
            {
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                AddXDataApps(entity.XData, used);
            }
        }

        return used;
    }

    private static void AddXDataApps(ResultBuffer? xdata, HashSet<string> used)
    {
        if (xdata == null)
        {
            return;
        }

        using (xdata)
        {
            foreach (var value in xdata)
            {
                if (value.TypeCode == (short)DxfCode.ExtendedDataRegAppName && value.Value is string name)
                {
                    used.Add(name);
                }
            }
        }
    }
}
