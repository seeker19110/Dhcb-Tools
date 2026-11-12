using System.Globalization;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Vẽ polyline vào Model Space theo CSV <c>Id,Layer,Width,Color,Closed,Vertices</c> — kết quả phân tích làm ngoài
/// AutoCAD (tim ống bóc khối lượng, ranh khu vực…) được vẽ lại thành đối tượng thật để kỹ sư nhìn và kiểm.
/// <para>
/// Tạo layer đích còn thiếu (màu ACI tuỳ chọn). <c>replaceExisting</c> xoá polyline cũ trên đúng các layer đích trước
/// khi vẽ để chạy lại không chồng hai lần; layer đích đang khoá thì không xoá, không vẽ, và báo.
/// </para>
/// </summary>
public sealed class PolylineImportCommand : ICoreCommand<PolylineImportConfig>
{
    public string CommandName => "PolylineImport";

    public CommandResult Execute(Database database, PolylineImportConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy file: \"{config.InputPath}\".");
        }

        var plan = PolylineImportPlanner.Plan(CsvText.ReadRecords(config.InputPath));
        if (plan.Rows.Count == 0)
        {
            var empty = CommandResult.Fail("CSV không có dòng polyline hợp lệ (cần Layer và Vertices \"x y;x y;…\").");
            empty.Messages.AddRange(plan.Notes);
            return empty;
        }

        var report = new List<string>(plan.Notes);
        report.AddRange(plan.ColorConflicts);
        var incomplete = plan.Notes.Any(n => n.StartsWith("Bỏ qua", StringComparison.Ordinal));

        using var transaction = database.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

        var usable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.Rows.Select(r => r.Layer).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!AcadHelpers.IsValidSymbolName(target))
            {
                report.Add($"Tên layer \"{target}\" không hợp lệ với AutoCAD — bỏ qua mọi polyline trên layer này.");
                incomplete = true;
                continue;
            }

            if (layerTable.Has(target))
            {
                var record = (LayerTableRecord)transaction.GetObject(layerTable[target], OpenMode.ForRead);
                if (record.IsLocked)
                {
                    locked.Add(target);
                    report.Add($"Layer \"{target}\" đang khoá — không vẽ, không xoá gì trên layer này.");
                    incomplete = true;
                    continue;
                }

                usable.Add(target);
                continue;
            }

            plan.TargetColors.TryGetValue(target, out var aci);
            usable.Add(target);
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

        var erased = 0;
        if (config.ReplaceExisting)
        {
            var polylineClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Polyline));
            foreach (ObjectId id in modelSpace)
            {
                if (!id.ObjectClass.IsDerivedFrom(polylineClass)) continue;
                var existing = (Polyline)transaction.GetObject(id, OpenMode.ForRead);
                if (!usable.Contains(existing.Layer)) continue;
                if (!config.DryRun)
                {
                    existing.UpgradeOpen();
                    existing.Erase();
                }

                erased++;
            }

            report.Add(config.DryRun
                ? $"[Xem trước] Sẽ xoá {erased} polyline cũ trên các layer đích."
                : $"Đã xoá {erased} polyline cũ trên các layer đích.");
        }

        var drawn = 0;
        var perLayer = new SortedDictionary<string, (int Count, double Length)>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in plan.Rows)
        {
            if (!usable.Contains(spec.Layer)) continue;
            if (!config.DryRun)
            {
                if (drawn == 0) modelSpace.UpgradeOpen();
                var polyline = new Polyline(spec.Points.Count);
                for (var i = 0; i < spec.Points.Count; i++)
                {
                    polyline.AddVertexAt(i, new Point2d(spec.Points[i].X, spec.Points[i].Y), 0, spec.Width, spec.Width);
                }

                polyline.Closed = spec.Closed && spec.Points.Count > 2;
                polyline.Layer = spec.Layer;
                polyline.ConstantWidth = spec.Width;
                modelSpace.AppendEntity(polyline);
                transaction.AddNewlyCreatedDBObject(polyline, true);
            }

            drawn++;
            perLayer.TryGetValue(spec.Layer, out var sum);
            perLayer[spec.Layer] = (sum.Count + 1, sum.Length + spec.Length);
        }

        foreach (var pair in perLayer)
        {
            report.Add(string.Format(CultureInfo.InvariantCulture, "→ \"{0}\": {1} polyline, tổng dài {2:0.###}.", pair.Key, pair.Value.Count, pair.Value.Length));
        }

        CommandResult result;
        if (config.DryRun)
        {
            transaction.Abort();
            result = CommandResult.Ok($"[Xem trước] Sẽ vẽ {drawn} polyline (chưa ghi vào drawing).", drawn + erased);
        }
        else
        {
            transaction.Commit();
            result = CommandResult.Ok($"Đã vẽ {drawn} polyline theo \"{config.InputPath}\".", drawn + erased);
        }

        result.Messages.AddRange(report);
        return result.WithIncompleteWork(incomplete || locked.Count > 0);
    }
}
