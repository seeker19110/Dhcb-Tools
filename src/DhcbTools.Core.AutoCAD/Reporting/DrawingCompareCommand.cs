using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// So sánh bản vẽ hiện tại với một file DWG khác. Hai mức, chạy cùng lúc:
/// <list type="number">
/// <item>MỨC LAYER: đếm entity theo layer ở hai bên (luôn có, không phụ thuộc handle).</item>
/// <item>MỨC ENTITY theo Handle: chỉ có nghĩa khi hai file cùng gốc (bản kia là bản sao/lưu khác tên của
/// bản này) — khi đó handle được giữ. Entity có ở cả hai bên mà tâm bounding box lệch quá
/// <c>moveToleranceMm</c> → "Đã di chuyển"; chỉ có ở một bên → "Thêm mới"/"Đã xoá". Nếu hai file không
/// chung handle nào thì mức này tự tắt và báo rõ, thay vì liệt kê toàn bộ là thêm/xoá.</item>
/// </list>
/// </summary>
public sealed class DrawingCompareCommand : ICoreCommand<DrawingCompareConfig>
{
    private sealed record EntitySnapshot(string Handle, string Layer, string Type, double Cx, double Cy, double Cz, bool HasExtents);

    public string CommandName => "DrawingCompare";

    public CommandResult Execute(Database database, DrawingCompareConfig config)
    {
        if (!File.Exists(config.OtherPath))
        {
            return CommandResult.Fail($"Không tìm thấy file để so sánh: \"{config.OtherPath}\".");
        }

        if (config.MoveToleranceMm < 0)
        {
            return CommandResult.Fail($"Dung sai di chuyển (moveToleranceMm) không được âm: {config.MoveToleranceMm}.");
        }

        var current = Snapshot(database);

        List<EntitySnapshot> other;
        using (var otherDb = new Database(false, true))
        {
            otherDb.ReadDwgFile(config.OtherPath, FileOpenMode.OpenForReadAndAllShare, true, null);
            other = Snapshot(otherDb);
        }

        var currentCounts = CountByLayer(current);
        var otherCounts = CountByLayer(other);

        var allLayers = new SortedSet<string>(currentCounts.Keys, StringComparer.OrdinalIgnoreCase);
        allLayers.UnionWith(otherCounts.Keys);

        var rows = new List<(string Layer, int CurrentCount, int OtherCount, string Status)>();

        foreach (var layer in allLayers)
        {
            // TryGetValue chứ không GetValueOrDefault: net48 (AutoCAD ≤ 2024) không có extension đó.
            currentCounts.TryGetValue(layer, out var currentCount);
            otherCounts.TryGetValue(layer, out var otherCount);

            string status;
            if (currentCount > 0 && otherCount == 0)
            {
                status = "Chỉ ở bản hiện tại";
            }
            else if (currentCount == 0 && otherCount > 0)
            {
                status = "Chỉ ở bản kia";
            }
            else if (currentCount != otherCount)
            {
                status = "Số lượng khác nhau";
            }
            else
            {
                status = "Giống nhau";
            }

            rows.Add((layer, currentCount, otherCount, status));
        }

        var layerDiff = rows.Count(r => r.Status != "Giống nhau");
        var entityDiff = CompareByHandle(current, other, config.MoveToleranceMm, out var moved, out var added, out var removed, out var handleNote);

        WriteReport(config, rows, entityDiff);

        var summary = handleNote == null
            ? $"So sánh với \"{config.OtherPath}\": {layerDiff}/{rows.Count} layer khác nhau; theo Handle: {moved} di chuyển > {NumericText.Format(config.MoveToleranceMm, 2)}, {added} thêm mới, {removed} đã xoá. Báo cáo: \"{config.OutputPath}\"."
            : $"So sánh mức layer với \"{config.OtherPath}\": {layerDiff}/{rows.Count} layer khác nhau. Báo cáo: \"{config.OutputPath}\".";

        var affected = handleNote == null ? layerDiff + moved + added + removed : layerDiff;
        var result = CommandResult.Ok(summary, affected);

        if (handleNote != null)
        {
            result.Messages.Add(handleNote);
        }
        else
        {
            result.Messages.AddRange(entityDiff.Take(200).Select(d => $"[{d.Status}] {d.Type} handle {d.Handle} — layer \"{d.Layer}\"{d.Detail}"));
            if (entityDiff.Count > 200)
            {
                result.Messages.Add($"… và {entityDiff.Count - 200} khác biệt nữa (xem file báo cáo).");
            }
        }

        return result;
    }

    /// <summary>
    /// Khác biệt từng entity theo Handle. <paramref name="note"/> khác null = không so được theo handle
    /// (hai file không chung handle nào) và mọi tham số ra khác đều là 0.
    /// </summary>
    private static List<(string Handle, string Layer, string Type, string Status, string Detail)> CompareByHandle(
        List<EntitySnapshot> current, List<EntitySnapshot> other, double tolerance,
        out int moved, out int added, out int removed, out string? note)
    {
        moved = 0;
        added = 0;
        removed = 0;
        note = null;
        var diff = new List<(string, string, string, string, string)>();

        var currentByHandle = ToDictionary(current);
        var otherByHandle = ToDictionary(other);

        var shared = currentByHandle.Keys.Count(h => otherByHandle.ContainsKey(h));
        if (shared == 0)
        {
            note = "Hai file không chung Handle nào — không so được từng entity (handle chỉ giữ nguyên giữa các bản lưu của CÙNG một bản vẽ). "
                   + "Báo cáo chỉ gồm phần so sánh mức layer.";
            return diff;
        }

        foreach (var kv in currentByHandle)
        {
            if (!otherByHandle.TryGetValue(kv.Key, out var before))
            {
                added++;
                diff.Add((kv.Key, kv.Value.Layer, kv.Value.Type, "Thêm mới", string.Empty));
                continue;
            }

            if (!kv.Value.HasExtents || !before.HasExtents)
            {
                continue;
            }

            var dx = kv.Value.Cx - before.Cx;
            var dy = kv.Value.Cy - before.Cy;
            var dz = kv.Value.Cz - before.Cz;
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance > tolerance)
            {
                moved++;
                diff.Add((kv.Key, kv.Value.Layer, kv.Value.Type, "Đã di chuyển", $" — lệch {NumericText.Format(distance, 2)}"));
            }
        }

        foreach (var kv in otherByHandle)
        {
            if (!currentByHandle.ContainsKey(kv.Key))
            {
                removed++;
                diff.Add((kv.Key, kv.Value.Layer, kv.Value.Type, "Đã xoá", string.Empty));
            }
        }

        return diff.Select(d => (d.Item1, d.Item2, d.Item3, d.Item4, d.Item5)).ToList();
    }

    private static Dictionary<string, EntitySnapshot> ToDictionary(List<EntitySnapshot> items)
    {
        var map = new Dictionary<string, EntitySnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            map[item.Handle] = item;
        }

        return map;
    }

    private static Dictionary<string, int> CountByLayer(List<EntitySnapshot> items)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            counts[item.Layer] = counts.TryGetValue(item.Layer, out var existing) ? existing + 1 : 1;
        }

        return counts;
    }

    private static List<EntitySnapshot> Snapshot(Database database)
    {
        var list = new List<EntitySnapshot>();

        using var transaction = database.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

        foreach (ObjectId entityId in modelSpace)
        {
            if (transaction.GetObject(entityId, OpenMode.ForRead) is not Entity entity)
            {
                continue;
            }

            var handle = HandleText.ToText(entityId.Handle.Value);
            double cx = 0, cy = 0, cz = 0;
            var hasExtents = false;

            try
            {
                // Entity suy biến (text rỗng, đường dài 0) không có extents — hỏi là ném.
                var ext = entity.GeometricExtents;
                cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                cy = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                cz = (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0;
                hasExtents = true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                hasExtents = false;
            }

            list.Add(new EntitySnapshot(handle, entity.Layer, entity.GetType().Name, cx, cy, cz, hasExtents));
        }

        transaction.Commit();

        return list;
    }

    private static void WriteReport(
        DrawingCompareConfig config,
        List<(string Layer, int CurrentCount, int OtherCount, string Status)> rows,
        List<(string Handle, string Layer, string Type, string Status, string Detail)> entityDiff)
    {
        AcadHelpers.EnsureParentDirectory(config.OutputPath);

        var isHtml = config.OutputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || config.OutputPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

        if (isHtml)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>So sánh bản vẽ</title>")
              .Append("<style>body{font-family:Arial,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}")
              .Append("th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}th{background:#f0f0f0}")
              .Append(".diff{background:#fff3cd}</style></head><body>");
            sb.Append("<h1>So sánh bản vẽ</h1>");
            sb.Append("<h2>Mức layer</h2>");
            sb.Append("<table><tr><th>Layer</th><th>Số lượng (hiện tại)</th><th>Số lượng (bản kia)</th><th>Trạng thái</th></tr>");
            foreach (var row in rows)
            {
                var cssClass = row.Status == "Giống nhau" ? string.Empty : " class=\"diff\"";
                sb.Append($"<tr{cssClass}><td>{HtmlText.Escape(row.Layer)}</td><td>{row.CurrentCount}</td><td>{row.OtherCount}</td><td>{HtmlText.Escape(row.Status)}</td></tr>");
            }
            sb.Append("</table>");

            sb.Append("<h2>Mức entity (theo Handle)</h2>");
            if (entityDiff.Count == 0)
            {
                sb.Append("<p>Không có khác biệt theo Handle (hoặc hai file không chung Handle nào — xem thông báo của lệnh).</p>");
            }
            else
            {
                sb.Append("<table><tr><th>Handle</th><th>Loại</th><th>Layer</th><th>Trạng thái</th><th>Chi tiết</th></tr>");
                foreach (var d in entityDiff)
                {
                    sb.Append($"<tr class=\"diff\"><td>{HtmlText.Escape(d.Handle)}</td><td>{HtmlText.Escape(d.Type)}</td><td>{HtmlText.Escape(d.Layer)}</td><td>{HtmlText.Escape(d.Status)}</td><td>{HtmlText.Escape(d.Detail.Trim(' ', '—'))}</td></tr>");
                }
                sb.Append("</table>");
            }

            sb.Append("</body></html>");
            File.WriteAllText(config.OutputPath, sb.ToString(), Encoding.UTF8);
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("Scope,Layer,CurrentCount,OtherCount,Status");
            foreach (var row in rows)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    "Layer", row.Layer, NumericText.Format(row.CurrentCount), NumericText.Format(row.OtherCount), row.Status,
                })).Append('\n');
            }

            foreach (var d in entityDiff)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    "Entity", d.Layer, d.Handle, d.Type, d.Status + d.Detail,
                })).Append('\n');
            }

            File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);
        }
    }
}
