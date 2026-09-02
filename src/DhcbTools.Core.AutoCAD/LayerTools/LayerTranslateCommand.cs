using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Đổi layer của mọi entity từ Source sang Target theo bảng map CSV — tương đương LAYTRANS.
/// Tạo Target nếu chưa tồn tại (copy Color/Linetype/Lineweight/Plottable từ CSV nếu có).
/// Sau khi chuyển, tuỳ chọn xoá layer Source nếu không còn entity nào tham chiếu.
/// </summary>
public sealed class LayerTranslateCommand : ICoreCommand<LayerTranslateConfig>
{
    private sealed record MapRow(string Source, string Target, string? Color, string? Linetype, string? Lineweight, string? Plottable);

    public string CommandName => "LayerTranslate";

    public CommandResult Execute(Database database, LayerTranslateConfig config)
    {
        if (!File.Exists(config.MapCsvPath))
        {
            return CommandResult.Fail($"Không tìm thấy file map: \"{config.MapCsvPath}\".");
        }

        var lines = File.ReadAllLines(config.MapCsvPath);
        if (lines.Length < 2)
        {
            return CommandResult.Fail("File map CSV không có dữ liệu.");
        }

        var rows = new List<MapRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = CsvText.SplitLine(lines[i]);
            if (cells.Count < 2 || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1]))
            {
                continue;
            }

            rows.Add(new MapRow(
                cells[0],
                cells[1],
                cells.Count > 2 && cells[2].Length > 0 ? cells[2] : null,
                cells.Count > 3 && cells[3].Length > 0 ? cells[3] : null,
                cells.Count > 4 && cells[4].Length > 0 ? cells[4] : null,
                cells.Count > 5 && cells[5].Length > 0 ? cells[5] : null));
        }

        if (rows.Count == 0)
        {
            return CommandResult.Fail("File map CSV không có dòng hợp lệ (cần cột Source,Target).");
        }

        var report = new List<string>();
        var changedCount = 0;

        using var transaction = database.TransactionManager.StartTransaction();

        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        // Đảm bảo mọi Target đã tồn tại (tạo mới nếu cần) trước khi đổi entity.
        foreach (var row in rows)
        {
            if (layerTable.Has(row.Target))
            {
                continue;
            }

            if (config.DryRun)
            {
                report.Add($"[Xem trước] Sẽ tạo layer mới: \"{row.Target}\".");
                continue;
            }

            var ltWrite = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
            var newLayer = new LayerTableRecord { Name = row.Target };

            if (row.Color is not null && short.TryParse(row.Color, out var aci))
            {
                newLayer.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            }
            if (row.Plottable is not null && bool.TryParse(row.Plottable, out var plottable))
            {
                newLayer.IsPlottable = plottable;
            }

            ltWrite.Add(newLayer);
            transaction.AddNewlyCreatedDBObject(newLayer, true);
            report.Add($"Đã tạo layer mới: \"{row.Target}\".");
        }

        // Đổi Layer của mọi entity Source → Target, trong mọi Block Table Record.
        var sourceToTarget = rows
            .GroupBy(r => r.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.OrdinalIgnoreCase);

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId entityId in block)
            {
                var entity = transaction.GetObject(entityId, OpenMode.ForRead);
                if (entity is not Entity ent || !sourceToTarget.TryGetValue(ent.Layer, out var target))
                {
                    continue;
                }

                if (config.DryRun)
                {
                    changedCount++;
                    continue;
                }

                ent.UpgradeOpen();
                ent.Layer = target;
                changedCount++;
            }
        }

        var deletedLayers = new List<string>();

        if (config.DeleteEmptySource)
        {
            var stillUsed = CollectUsedLayerNames(database, transaction);

            foreach (var source in sourceToTarget.Keys)
            {
                if (string.Equals(source, "0", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!layerTable.Has(source) || stillUsed.Contains(source))
                {
                    continue;
                }

                if (config.DryRun)
                {
                    report.Add($"[Xem trước] Sẽ xoá layer nguồn rỗng: \"{source}\".");
                    deletedLayers.Add(source);
                    continue;
                }

                var layerId = layerTable[source];
                var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForWrite);
                layer.Erase();
                deletedLayers.Add(source);
            }
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đổi layer của {changedCount} entity, xoá {deletedLayers.Count} layer nguồn rỗng.",
                changedCount);
            preview.Messages.AddRange(report);
            return preview;
        }

        transaction.Commit();

        var result = CommandResult.Ok(
            $"Đã đổi layer của {changedCount} entity theo \"{config.MapCsvPath}\", xoá {deletedLayers.Count} layer nguồn rỗng.",
            changedCount);
        result.Messages.AddRange(report);
        return result;
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
}
