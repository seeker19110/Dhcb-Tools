using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace DhcbTools.Core.Health;

/// <summary>
/// Kiểm tra sức khoẻ mô hình Revit và xuất báo cáo HTML.
/// </summary>
public sealed class HealthReportCommand : ICoreCommand<HealthReportConfig>
{
    public string CommandName => "HealthReport";

    public CommandResult Execute(Document document, HealthReportConfig config)
    {
        try
        {
            var metrics = new HealthMetrics();

            // --- a. Warnings ------------------------------------------------
            if (config.CheckWarnings)
            {
                var warnings = document.GetWarnings();
                metrics.WarningCount = warnings.Count;
                foreach (var w in warnings.Take(50))
                    metrics.WarningMessages.Add(w.GetDescriptionText());
            }

            // --- b. Unplaced views ------------------------------------------
            if (config.CheckUnplacedViews)
            {
                var allViews = new FilteredElementCollector(document)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .ToList();

                foreach (var v in allViews)
                {
                    // Check if view is on a sheet by looking for its viewport
                    var vports = new FilteredElementCollector(document)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>()
                        .Where(vp => vp.ViewId == v.Id)
                        .ToList();

                    bool isOnSheet = vports.Count > 0;
                    bool isSheetView = v.ViewType == ViewType.DrawingSheet;
                    bool isSchedule = v.ViewType == ViewType.Schedule;
                    bool isLegend = v.ViewType == ViewType.Legend;

                    if (!isOnSheet && !isSheetView && !isSchedule && !isLegend)
                    {
                        metrics.UnplacedViews.Add($"{v.ViewType}: {v.Name}");
                    }
                }
                metrics.UnplacedViewCount = metrics.UnplacedViews.Count;
            }

            // --- c. Open connectors -----------------------------------------
            if (config.CheckOpenConnectors)
            {
                CollectOpenConnectors(document, metrics);
            }

            // --- d. In-place families ----------------------------------------
            if (config.CheckInPlaceFamilies)
            {
                var inPlaceInstances = new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol != null && fi.Symbol.Family != null
                                 && fi.Symbol.Family.IsInPlace)
                    .ToList();

                metrics.InPlaceFamilyCount = inPlaceInstances.Count;
                foreach (var fi in inPlaceInstances.Take(100))
                    metrics.InPlaceFamilyNames.Add(fi.Name ?? "(unnamed)");
            }

            // --- e. File size -----------------------------------------------
            if (config.CheckFileSizeMb && !string.IsNullOrEmpty(document.PathName))
            {
                try
                {
                    var fi = new FileInfo(document.PathName);
                    if (fi.Exists)
                        metrics.FileSizeMb = fi.Length / 1048576.0;
                }
                catch (System.Exception) { /* ignore */ }
            }

            // --- Generate HTML ----------------------------------------------
            string html = BuildHtml(document, config, metrics);
            string? dir = Path.GetDirectoryName(config.OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(config.OutputPath, html, Encoding.UTF8);

            string summary = $"Health Report: {metrics.WarningCount} cảnh báo, " +
                             $"{metrics.UnplacedViewCount} view chưa đặt, " +
                             $"{metrics.OpenConnectorCount} connector hở, " +
                             $"{metrics.InPlaceFamilyCount} in-place family. " +
                             $"File: {config.OutputPath}";
            return CommandResult.Ok(summary, metrics.WarningCount);
        }
        catch (System.Exception ex)
        {
            return CommandResult.Fail($"HealthReport thất bại: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    private static void CollectOpenConnectors(Document doc, HealthMetrics metrics)
    {
        try
        {
            // Collect all MEP elements that have ConnectorManager
            var mepElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var elem in mepElements)
            {
                ConnectorManager? cm = null;
                try
                {
                    if (elem is MEPCurve curve)
                        cm = curve.ConnectorManager;
                    else if (elem is FamilyInstance fi && fi.MEPModel != null)
                        cm = fi.MEPModel.ConnectorManager;
                }
                catch (System.Exception) { continue; }

                if (cm == null) continue;

                foreach (Connector conn in cm.Connectors)
                {
                    try
                    {
                        if (!conn.IsConnected && conn.ConnectorType != ConnectorType.End)
                        {
                            metrics.OpenConnectorCount++;
                        }
                    }
                    catch (System.Exception) { /* ignore */ }
                }
            }
        }
        catch (System.Exception) { /* ignore connector check failures */ }
    }

    // -------------------------------------------------------------------------
    private static string BuildHtml(Document doc, HealthReportConfig config, HealthMetrics m)
    {
        string projectName = doc.ProjectInformation?.Name ?? doc.Title ?? "Unknown Project";
        string projectNumber = doc.ProjectInformation?.Number ?? "-";
        string filePath = doc.PathName ?? "(not saved)";
        string date = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        double fileSizeWarnMb = config.FileSizeWarnMb;
        string fileSizeColor = m.FileSizeMb <= 0 ? "#888" :
                               m.FileSizeMb > fileSizeWarnMb ? "#e74c3c" :
                               m.FileSizeMb > fileSizeWarnMb * 0.7 ? "#f39c12" : "#27ae60";

        string warnColor = m.WarningCount == 0 ? "#27ae60" : m.WarningCount > 100 ? "#e74c3c" : "#f39c12";
        string unplacedColor = m.UnplacedViewCount == 0 ? "#27ae60" : m.UnplacedViewCount > 20 ? "#e74c3c" : "#f39c12";
        string connColor = m.OpenConnectorCount == 0 ? "#27ae60" : m.OpenConnectorCount > 10 ? "#e74c3c" : "#f39c12";
        string inPlaceColor = m.InPlaceFamilyCount == 0 ? "#27ae60" : m.InPlaceFamilyCount > 5 ? "#e74c3c" : "#f39c12";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"vi\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>DHCB Health Report – {projectName}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; background: #f5f5f5; color: #222; }");
        sb.AppendLine("  .header { background: #1a1a2e; color: #fff; padding: 24px 32px; }");
        sb.AppendLine("  .header h1 { margin: 0 0 4px; font-size: 1.6em; }");
        sb.AppendLine("  .header .meta { font-size: 0.85em; opacity: 0.75; }");
        sb.AppendLine("  .content { max-width: 1100px; margin: 24px auto; padding: 0 16px; }");
        sb.AppendLine("  .cards { display: flex; flex-wrap: wrap; gap: 16px; margin-bottom: 32px; }");
        sb.AppendLine("  .card { flex: 1 1 180px; background: #fff; border-radius: 10px; padding: 20px 24px;");
        sb.AppendLine("          box-shadow: 0 2px 8px rgba(0,0,0,0.08); border-left: 5px solid #ccc; }");
        sb.AppendLine("  .card .icon { font-size: 2em; line-height: 1; }");
        sb.AppendLine("  .card .count { font-size: 2.2em; font-weight: 700; margin: 6px 0 2px; }");
        sb.AppendLine("  .card .label { font-size: 0.85em; color: #666; }");
        sb.AppendLine("  .section { background: #fff; border-radius: 10px; padding: 20px 24px;");
        sb.AppendLine("             box-shadow: 0 2px 8px rgba(0,0,0,0.08); margin-bottom: 20px; }");
        sb.AppendLine("  .section h2 { margin: 0 0 14px; font-size: 1.1em; border-bottom: 1px solid #eee; padding-bottom: 8px; }");
        sb.AppendLine("  .section ul { margin: 0; padding-left: 20px; }");
        sb.AppendLine("  .section li { padding: 3px 0; font-size: 0.9em; }");
        sb.AppendLine("  .badge { display: inline-block; padding: 2px 8px; border-radius: 4px;");
        sb.AppendLine("           font-size: 0.75em; font-weight: 600; color: #fff; margin-left: 6px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine($"  <h1>🏗️ DHCB Health Report</h1>");
        sb.AppendLine($"  <div class=\"meta\">");
        sb.AppendLine($"    <strong>{HtmlEncode(projectName)}</strong> &nbsp;|&nbsp; #{HtmlEncode(projectNumber)} &nbsp;|&nbsp; {date}");
        sb.AppendLine($"    <br>{HtmlEncode(filePath)}");
        sb.AppendLine($"  </div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"content\">");

        // Cards
        sb.AppendLine("<div class=\"cards\">");
        AppendCard(sb, "⚠️", m.WarningCount.ToString(), "Cảnh báo Revit", warnColor);
        AppendCard(sb, "🗂️", m.UnplacedViewCount.ToString(), "View chưa đặt", unplacedColor);
        AppendCard(sb, "🔌", m.OpenConnectorCount.ToString(), "Connector hở", connColor);
        AppendCard(sb, "🧱", m.InPlaceFamilyCount.ToString(), "In-place Family", inPlaceColor);
        if (m.FileSizeMb > 0)
            AppendCard(sb, "💾", $"{m.FileSizeMb:0.0} MB", "Kích thước file", fileSizeColor);
        sb.AppendLine("</div>");

        // Warning list
        if (m.WarningMessages.Count > 0)
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine($"  <h2>⚠️ Cảnh báo ({m.WarningMessages.Count} hiển thị / {m.WarningCount} tổng)</h2>");
            sb.AppendLine("  <ul>");
            foreach (var w in m.WarningMessages)
                sb.AppendLine($"    <li>{HtmlEncode(w)}</li>");
            sb.AppendLine("  </ul>");
            sb.AppendLine("</div>");
        }

        // Unplaced views
        if (m.UnplacedViews.Count > 0)
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine($"  <h2>🗂️ View chưa đặt lên sheet ({m.UnplacedViews.Count})</h2>");
            sb.AppendLine("  <ul>");
            foreach (var v in m.UnplacedViews)
                sb.AppendLine($"    <li>{HtmlEncode(v)}</li>");
            sb.AppendLine("  </ul>");
            sb.AppendLine("</div>");
        }

        // In-place families
        if (m.InPlaceFamilyNames.Count > 0)
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine($"  <h2>🧱 In-place Family ({m.InPlaceFamilyCount})</h2>");
            sb.AppendLine("  <ul>");
            foreach (var n in m.InPlaceFamilyNames)
                sb.AppendLine($"    <li>{HtmlEncode(n)}</li>");
            if (m.InPlaceFamilyCount > m.InPlaceFamilyNames.Count)
                sb.AppendLine($"    <li><em>... và {m.InPlaceFamilyCount - m.InPlaceFamilyNames.Count} khác</em></li>");
            sb.AppendLine("  </ul>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<p style=\"text-align:center;color:#aaa;font-size:0.8em;margin-top:32px\">Generated by DHCB Tools &mdash; Đồng Hành Cùng Bạn</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendCard(StringBuilder sb, string icon, string count, string label, string color)
    {
        sb.AppendLine($"  <div class=\"card\" style=\"border-left-color:{color}\">");
        sb.AppendLine($"    <div class=\"icon\">{icon}</div>");
        sb.AppendLine($"    <div class=\"count\" style=\"color:{color}\">{count}</div>");
        sb.AppendLine($"    <div class=\"label\">{label}</div>");
        sb.AppendLine("  </div>");
    }

    private static string HtmlEncode(string s)
    {
        if (s == null) return string.Empty;
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    // -------------------------------------------------------------------------
    private sealed class HealthMetrics
    {
        public int WarningCount;
        public List<string> WarningMessages = new List<string>();
        public int UnplacedViewCount;
        public List<string> UnplacedViews = new List<string>();
        public int OpenConnectorCount;
        public int InPlaceFamilyCount;
        public List<string> InPlaceFamilyNames = new List<string>();
        public double FileSizeMb;
    }
}
