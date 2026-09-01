using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.Standards;

/// <summary>Mục 7.8 — LAYTRANS offline: map layer cũ → chuẩn theo CSV.</summary>
public sealed class LayerTranslateConfig
{
    /// <summary>CSV <c>Source,Target,Color,Linetype,Lineweight,Plottable</c>; Source hỗ trợ wildcard * ? và ~ (phủ định).</summary>
    public required string MapCsvPath { get; init; }

    /// <summary>Xoá layer nguồn sau khi chuyển hết entity (nếu không phải layer hệ thống/hiện hành).</summary>
    public bool DeleteEmptySource { get; init; } = true;

    /// <summary>Áp thuộc tính chuẩn (màu, linetype…) lên layer đích kể cả khi đích đã tồn tại.</summary>
    public bool ApplyTargetProperties { get; init; } = true;

    /// <summary>Đổi cả entity nằm trong block definition (mặc định có — như LAYTRANS).</summary>
    public bool IncludeBlockDefinitions { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

public sealed class LayerTranslateCommand : ICoreCommand<LayerTranslateConfig>
{
    public string CommandName => "LayerTranslate";

    public CommandResult Execute(Database database, LayerTranslateConfig config)
    {
        if (!File.Exists(config.MapCsvPath))
        {
            return CommandResult.Fail($"Không tìm thấy \"{config.MapCsvPath}\".");
        }

        var errors = new List<string>();
        var table = LayerMapTable.ParseCsv(File.ReadAllText(config.MapCsvPath, CsvText.Utf8WithBom), errors);
        var result = CommandResult.Ok(string.Empty);
        result.Messages.AddRange(errors);
        if (table.Entries.Count == 0)
        {
            return CommandResult.Fail("Bảng map rỗng.", errors);
        }

        using var tr = database.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)tr.GetObject(database.LayerTableId, OpenMode.ForRead);
        var existing = new List<string>();
        foreach (ObjectId id in layerTable)
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!l.IsDependent) existing.Add(l.Name);
        }

        var unmapped = new List<string>();
        var plan = table.Plan(existing, unmapped);
        result.Messages.AddRange(unmapped.Take(100).Select(u => "Không có trong bảng: " + u));

        // Đếm entity theo layer để báo trước.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!btr.IsLayout && !config.IncludeBlockDefinitions) continue;
            if (btr.IsFromExternalReference) continue;
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity e && plan.ContainsKey(e.Layer))
                {
                    counts[e.Layer] = counts.TryGetValue(e.Layer, out var n) ? n + 1 : 1;
                }
            }
        }

        if (config.DryRun)
        {
            tr.Abort();
            result.Summary = $"[Xem trước] Sẽ chuyển {plan.Count} layer ({counts.Values.Sum()} entity), {unmapped.Count} layer không có trong bảng.";
            result.Messages.AddRange(plan.Select(p => $"{p.Key} → {p.Value.Target} ({(counts.TryGetValue(p.Key, out var n) ? n : 0)} entity)"));
            result.AffectedCount = plan.Count;
            return result;
        }

        layerTable.UpgradeOpen();
        var targetIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan.Values.Distinct())
        {
            if (targetIds.ContainsKey(entry.Target)) continue;
            ObjectId targetId;
            if (layerTable.Has(entry.Target))
            {
                targetId = layerTable[entry.Target];
            }
            else
            {
                var ltr = new LayerTableRecord { Name = entry.Target };
                targetId = layerTable.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                result.Messages.Add($"Tạo layer {entry.Target}.");
            }

            if (config.ApplyTargetProperties)
            {
                ApplyProperties(tr, database, (LayerTableRecord)tr.GetObject(targetId, OpenMode.ForWrite), entry, result);
            }

            targetIds[entry.Target] = targetId;
        }

        var moved = 0;
        foreach (ObjectId btrId in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!btr.IsLayout && !config.IncludeBlockDefinitions) continue;
            if (btr.IsFromExternalReference) continue;
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || !plan.TryGetValue(e.Layer, out var entry)) continue;
                try
                {
                    e.UpgradeOpen();
                    e.LayerId = targetIds[entry.Target];
                    moved++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{e.Handle}: {ex.Message}");
                }
            }
        }

        var deleted = 0;
        if (config.DeleteEmptySource)
        {
            var current = database.Clayer;
            foreach (var source in plan.Keys)
            {
                try
                {
                    var id = layerTable[source];
                    if (id == current || CleanupDecider.IsSystemLayer(source)) continue;
                    var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    ltr.Erase();
                    deleted++;
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Không xoá được layer {source}: {ex.Message}");
                }
            }
        }

        tr.Commit();
        result.Summary = $"Đã chuyển {moved} entity qua {plan.Count} map, xoá {deleted} layer nguồn.";
        result.AffectedCount = moved;
        return result;
    }

    internal static void ApplyProperties(Transaction tr, Database db, LayerTableRecord layer, LayerMapEntry entry, CommandResult result)
    {
        try
        {
            if (!string.IsNullOrEmpty(entry.Color) && short.TryParse(entry.Color, out var aci))
            {
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            }

            if (!string.IsNullOrEmpty(entry.Linetype))
            {
                var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (lt.Has(entry.Linetype!)) layer.LinetypeObjectId = lt[entry.Linetype!];
                else result.Messages.Add($"Linetype \"{entry.Linetype}\" chưa load — bỏ qua cho {layer.Name}.");
            }

            if (!string.IsNullOrEmpty(entry.Lineweight) && Enum.TryParse<LineWeight>(entry.Lineweight!.StartsWith("LineWeight", StringComparison.OrdinalIgnoreCase) ? entry.Lineweight : "LineWeight" + entry.Lineweight, true, out var lw))
            {
                layer.LineWeight = lw;
            }

            if (entry.Plottable.HasValue)
            {
                layer.IsPlottable = entry.Plottable.Value;
            }
        }
        catch (Exception ex)
        {
            result.Messages.Add($"Thuộc tính layer {layer.Name}: {ex.Message}");
        }
    }
}
