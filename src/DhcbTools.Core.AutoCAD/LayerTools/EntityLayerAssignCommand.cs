using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Chuyển từng entity chỉ đích danh (handle) sang layer đích theo CSV <c>Handle,Layer,Color</c>.
/// <para>
/// Bổ sung cho <see cref="LayerTranslateCommand"/> (đổi CẢ layer): bản vẽ xuất từ Revit thường dồn tag của mọi hệ
/// (DP, GAS, TEAP…) vào một layer IDEN và DIM của mọi hệ vào một layer DIM, nên muốn "hệ nào ra hệ đó" để bóc khối
/// lượng phải tách theo từng đối tượng. Agent/kỹ sư tự phân loại (theo nội dung chữ, ống gần nhất…) rồi đưa CSV vào đây.
/// </para>
/// <para>
/// An toàn: chỉ đụng entity nằm trực tiếp trong Model Space / layout (và attribute của block reference ở đó) — entity
/// bên trong định nghĩa block hay xref thì báo và bỏ qua, vì đổi ở đó là đổi mọi bản chèn. Layer khoá: bỏ qua và báo
/// như các lệnh ghi khác. Handle không có trong bản vẽ: báo, lệnh thành "chưa hoàn tất".
/// </para>
/// </summary>
public sealed class EntityLayerAssignCommand : ICoreCommand<EntityLayerAssignConfig>
{
    public string CommandName => "EntityLayerAssign";

    public CommandResult Execute(Database database, EntityLayerAssignConfig config)
    {
        if (!File.Exists(config.MapCsvPath))
        {
            return CommandResult.Fail($"Không tìm thấy file: \"{config.MapCsvPath}\".");
        }

        var plan = EntityLayerAssignPlanner.Plan(CsvText.ReadRecords(config.MapCsvPath));
        if (plan.Rows.Count == 0)
        {
            var empty = CommandResult.Fail("CSV không có dòng hợp lệ (cần cột Handle,Layer).");
            empty.Messages.AddRange(plan.Notes);
            empty.Errors.AddRange(plan.Conflicts);
            return empty;
        }

        var report = new List<string>(plan.Notes);
        report.AddRange(plan.ColorConflicts);
        var incomplete = plan.Conflicts.Count > 0;
        var changed = 0;
        var unchanged = 0;
        var perTarget = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var transaction = database.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        var lockedLayers = AcadHelpers.LockedLayerIds(database, transaction);
        var lockedSkips = new LockedLayerSkips();

        // Lượt 1 — tạo layer đích còn thiếu (xem trước: chỉ báo).
        var usableTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.Rows.Select(r => r.Layer).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!AcadHelpers.IsValidSymbolName(target))
            {
                report.Add($"Tên layer đích \"{target}\" không hợp lệ với AutoCAD — bỏ qua mọi dòng trỏ tới nó.");
                incomplete = true;
                continue;
            }

            if (layerTable.Has(target))
            {
                usableTargets.Add(target);
                continue;
            }

            if (!config.CreateMissingLayers)
            {
                report.Add($"Layer đích \"{target}\" chưa có và createMissingLayers=false — bỏ qua mọi dòng trỏ tới nó.");
                incomplete = true;
                continue;
            }

            plan.TargetColors.TryGetValue(target, out var aci);
            usableTargets.Add(target);
            if (config.DryRun)
            {
                report.Add($"[Xem trước] Sẽ tạo layer mới: \"{target}\"{(aci > 0 ? $" (màu {aci})" : "")}.");
                continue;
            }

            var layerWrite = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
            var layer = new LayerTableRecord { Name = target };
            if (aci > 0)
            {
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            }

            layerWrite.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
            report.Add($"Đã tạo layer mới: \"{target}\".");
        }

        // Lượt 2 — đổi layer từng entity.
        var missing = new List<string>();
        var insideBlocks = new List<string>();
        foreach (var row in plan.Rows)
        {
            if (!usableTargets.Contains(row.Layer))
            {
                continue;
            }

            if (!TryOpen(database, transaction, row.Handle, out var entity))
            {
                missing.Add(row.HandleText);
                continue;
            }

            if (!IsInLayout(transaction, entity))
            {
                insideBlocks.Add(row.HandleText);
                continue;
            }

            if (string.Equals(entity.Layer, row.Layer, StringComparison.OrdinalIgnoreCase))
            {
                unchanged++;
                continue;
            }

            if (lockedLayers.Contains(entity.LayerId))
            {
                lockedSkips.Add(entity.Layer);
                continue;
            }

            if (!config.DryRun)
            {
                entity.UpgradeOpen();
                entity.Layer = row.Layer;
            }

            changed++;
            perTarget[row.Layer] = perTarget.TryGetValue(row.Layer, out var n) ? n + 1 : 1;
        }

        if (missing.Count > 0)
        {
            incomplete = true;
            report.Add($"{missing.Count} handle không có trong bản vẽ (hoặc đã xoá): {Sample(missing)}.");
        }

        if (insideBlocks.Count > 0)
        {
            incomplete = true;
            report.Add($"{insideBlocks.Count} entity nằm trong định nghĩa block/xref — không đổi (sẽ đổi mọi bản chèn): {Sample(insideBlocks)}.");
        }

        if (lockedSkips.Message("entity") is { } lockedNote)
        {
            report.Add(lockedNote);
        }

        if (unchanged > 0)
        {
            report.Insert(0, $"{unchanged} entity đã ở đúng layer đích — giữ nguyên.");
        }

        foreach (var pair in perTarget)
        {
            report.Add($"→ \"{pair.Key}\": {pair.Value} entity.");
        }

        incomplete |= lockedSkips.Count > 0;

        CommandResult result;
        if (config.DryRun)
        {
            transaction.Abort();
            result = CommandResult.Ok($"[Xem trước] Sẽ chuyển {changed} entity sang layer đích (chưa ghi vào drawing).", changed);
        }
        else
        {
            transaction.Commit();
            result = CommandResult.Ok($"Đã chuyển {changed} entity sang layer đích theo \"{config.MapCsvPath}\".", changed);
        }

        result.Messages.AddRange(report);
        result.Errors.AddRange(plan.Conflicts);
        return result.WithIncompleteWork(incomplete);
    }

    private static bool TryOpen(Database database, Transaction transaction, long handle, out Entity entity)
    {
        entity = null!;
        ObjectId id;
        try
        {
            id = database.GetObjectId(false, new Handle(handle), 0);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        if (id.IsNull || id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity found)
        {
            return false;
        }

        entity = found;
        return true;
    }

    /// <summary>Entity nằm thẳng trong một layout (Model/Paper Space), hoặc là attribute của block reference ở đó.</summary>
    private static bool IsInLayout(Transaction transaction, Entity entity)
    {
        var owner = transaction.GetObject(entity.OwnerId, OpenMode.ForRead);
        if (entity is AttributeReference && owner is BlockReference blockRef)
        {
            owner = transaction.GetObject(blockRef.OwnerId, OpenMode.ForRead);
        }

        return owner is BlockTableRecord block && block.IsLayout;
    }

    private static string Sample(List<string> handles) =>
        string.Join(", ", handles.Take(20)) + (handles.Count > 20 ? $", … (+{handles.Count - 20})" : "");
}
