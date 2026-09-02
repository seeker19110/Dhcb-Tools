using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// So sánh bản vẽ hiện tại với một file DWG khác Ở MỨC LAYER — xem ghi chú trong
/// <see cref="DrawingCompareConfig"/> về lý do không so từng entity theo Handle. Với mỗi layer xuất
/// hiện ở ít nhất một trong hai file: đếm số entity ở mỗi bên, đánh dấu "Chỉ ở bản hiện tại",
/// "Chỉ ở bản kia" hoặc "Số lượng khác nhau".
/// </summary>
public sealed class DrawingCompareCommand : ICoreCommand<DrawingCompareConfig>
{
    public string CommandName => "DrawingCompare";

    public CommandResult Execute(Database database, DrawingCompareConfig config)
    {
        if (!File.Exists(config.OtherPath))
        {
            return CommandResult.Fail($"Không tìm thấy file để so sánh: \"{config.OtherPath}\".");
        }

        var currentCounts = CountEntitiesByLayer(database);

        Dictionary<string, int> otherCounts;
        using (var otherDb = new Database(false, true))
        {
            otherDb.ReadDwgFile(config.OtherPath, FileOpenMode.OpenForReadAndAllShare, true, null);
            otherCounts = CountEntitiesByLayer(otherDb);
        }

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

        var diffCount = rows.Count(r => r.Status != "Giống nhau");

        WriteReport(config, rows);

        var result = CommandResult.Ok(
            $"So sánh mức layer với \"{config.OtherPath}\": {diffCount}/{rows.Count} layer khác nhau. Báo cáo: \"{config.OutputPath}\".",
            diffCount);
        result.Messages.Add(
            "Đây là so sánh MỨC LAYER (đếm entity theo layer), không phải so từng entity theo Handle — " +
            "handle không đáng tin cậy để đối chiếu giữa hai file DWG độc lập.");

        return result;
    }

    private static void WriteReport(DrawingCompareConfig config, List<(string Layer, int CurrentCount, int OtherCount, string Status)> rows)
    {
        var isHtml = config.OutputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || config.OutputPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

        if (isHtml)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>So sánh bản vẽ</title>")
              .Append("<style>body{font-family:Arial,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}")
              .Append("th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}th{background:#f0f0f0}")
              .Append(".diff{background:#fff3cd}</style></head><body>");
            sb.Append("<h1>So sánh bản vẽ (mức layer)</h1>");
            sb.Append("<p>So sánh đếm entity theo layer giữa bản hiện tại và file khác — không so từng entity theo Handle.</p>");
            sb.Append("<table><tr><th>Layer</th><th>Số lượng (hiện tại)</th><th>Số lượng (bản kia)</th><th>Trạng thái</th></tr>");
            foreach (var row in rows)
            {
                var cssClass = row.Status == "Giống nhau" ? string.Empty : " class=\"diff\"";
                sb.Append($"<tr{cssClass}><td>{HtmlText.Escape(row.Layer)}</td><td>{row.CurrentCount}</td><td>{row.OtherCount}</td><td>{HtmlText.Escape(row.Status)}</td></tr>");
            }
            sb.Append("</table></body></html>");
            File.WriteAllText(config.OutputPath, sb.ToString(), Encoding.UTF8);
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("Layer,CurrentCount,OtherCount,Status");
            foreach (var row in rows)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    row.Layer, NumericText.Format(row.CurrentCount), NumericText.Format(row.OtherCount), row.Status,
                })).Append('\n');
            }
            File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);
        }
    }

    private static Dictionary<string, int> CountEntitiesByLayer(Database database)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var transaction = database.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

        foreach (ObjectId entityId in modelSpace)
        {
            var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
            counts[entity.Layer] = counts.TryGetValue(entity.Layer, out var existing) ? existing + 1 : 1;
        }

        transaction.Commit();

        return counts;
    }
}
