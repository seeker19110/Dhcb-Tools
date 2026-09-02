using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace DhcbTools.Core.MEPF;

/// <summary>
/// Quét toàn bộ mô hình, liệt kê mọi connector MEP chưa được kết nối (open connectors).
/// Tuỳ chọn: tạo 3D view tô sáng các phần tử có connector hở.
/// </summary>
public sealed class ConnectorCheckerCommand : ICoreCommand<ConnectorCheckerConfig>
{
    public string CommandName => "ConnectorChecker";

    private const double FtToMm = 304.8;

    public CommandResult Execute(Document document, ConnectorCheckerConfig config)
    {
        // 1. Collect open connectors
        var openConnectors = FindOpenConnectors(document, config);

        // Build report lines
        var reportLines = new List<string>();
        var elementIds = new HashSet<ElementId>();

        foreach (var info in openConnectors)
        {
            var xMm = info.Origin.X * FtToMm;
            var yMm = info.Origin.Y * FtToMm;
            var zMm = info.Origin.Z * FtToMm;
            reportLines.Add(
                $"Element {info.ElementId.Value} at ({xMm:F1},{yMm:F1},{zMm:F1}) mm - {info.Domain}");
            elementIds.Add(info.ElementId);
        }

        if (openConnectors.Count == 0)
        {
            return CommandResult.Ok("Không tìm thấy connector hở nào trong mô hình.", 0);
        }

        // 2. Optionally create/update 3D view
        if (config.Create3dView && elementIds.Count > 0)
        {
            try
            {
                using var tx = new Transaction(document, "DHCB - View connector hở");
                tx.Start();
                tx.SetFailureHandlingOptions(
                    tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor()));

                var view = FindOrCreate3dView(document, config.ViewName);
                if (view != null)
                {
                    view.IsolateElementsTemporary(elementIds.ToList());
                }

                tx.Commit();
            }
            catch (System.Exception ex)
            {
                reportLines.Insert(0, $"[Cảnh báo] Không thể tạo/cập nhật 3D view: {ex.Message}");
            }
        }

        var summary = $"Tìm thấy {openConnectors.Count} connector hở trên {elementIds.Count} phần tử.";
        var result = CommandResult.Ok(summary, openConnectors.Count);
        result.Messages.AddRange(reportLines);
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private sealed class ConnectorInfo
    {
        public required ElementId ElementId { get; set; }
        public required XYZ Origin { get; set; }
        public required string Domain { get; set; }
        public required string Shape { get; set; }
    }

    private List<ConnectorInfo> FindOpenConnectors(Document doc, ConnectorCheckerConfig config)
    {
        var result = new List<ConnectorInfo>();

        var allElements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        bool filterDomain = config.Domains != null && config.Domains.Count > 0;

        foreach (var elem in allElements)
        {
            ConnectorManager? cm = null;

            try
            {
                if (elem is MEPCurve mepCurve)
                {
                    cm = mepCurve.ConnectorManager;
                }
                else if (elem is FamilyInstance fi)
                {
                    var mepModel = fi.MEPModel;
                    if (mepModel != null)
                        cm = mepModel.ConnectorManager;
                }
            }
            catch (System.Exception)
            {
                continue;
            }

            if (cm == null) continue;

            try
            {
                var connectors = cm.Connectors;
                if (connectors == null) continue;

                var iter = connectors.ForwardIterator();
                while (iter.MoveNext())
                {
                    var connector = iter.Current as Connector;
                    if (connector == null) continue;

                    // Skip End-type connectors (terminators, not real open ends)
                    if (connector.ConnectorType == ConnectorType.End) continue;

                    if (connector.IsConnected) continue;

                    var domainStr = connector.Domain.ToString();

                    if (filterDomain)
                    {
                        bool domainMatch = false;
                        foreach (var d in config.Domains!)
                        {
                            if (domainStr.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                domainMatch = true;
                                break;
                            }
                        }
                        if (!domainMatch) continue;
                    }

                    result.Add(new ConnectorInfo
                    {
                        ElementId = elem.Id,
                        Origin = connector.Origin,
                        Domain = domainStr,
                        Shape = connector.Shape.ToString(),
                    });
                }
            }
            catch (System.Exception)
            {
                // Skip elements where connector enumeration fails
            }
        }

        return result;
    }

    private static View3D FindOrCreate3dView(Document doc, string viewName)
    {
        // Try to find existing view
        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(v =>
                !v.IsTemplate &&
                string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));

        if (existing != null) return existing;

        // Find 3D view family type
        var viewFamilyType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

        if (viewFamilyType == null) return null!;

        var view = View3D.CreateIsometric(doc, viewFamilyType.Id);

        // Rename the view
        try
        {
            view.Name = viewName;
        }
        catch (System.Exception)
        {
            // Name conflict — append timestamp
            try { view.Name = viewName + "_" + DateTime.Now.ToString("HHmm"); }
            catch (System.Exception) { }
        }

        return view;
    }
}
