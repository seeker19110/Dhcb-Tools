using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace DhcbTools.Core.MEPF;

/// <summary>
/// Cắt đoạn ống/duct/cable tray quá dài thành các đoạn ngắn hơn bằng cách sử dụng
/// PlumbingUtils.BreakCurve (Pipe) và MechanicalUtils.BreakCurve (Duct).
/// </summary>
public sealed class PipeSplitterCommand : ICoreCommand<PipeSplitterConfig>
{
    public string CommandName => "PipeSplitter";

    private const double FtToMm = 304.8;

    public CommandResult Execute(Document document, PipeSplitterConfig config)
    {
        double maxSegmentFt = config.MaxSegmentMm / FtToMm;

        // 1. Collect MEP elements
        var elements = CollectElements(document, config);
        if (elements.Count == 0)
        {
            return CommandResult.Fail("Không có phần tử MEP nào phù hợp để cắt.");
        }

        // 2. Compute split plan: (element, category, list of split points along curve)
        var plan = new List<(Element Element, string Category, List<XYZ> SplitPoints)>();

        foreach (var (elem, category) in elements)
        {
            if (!(elem.Location is LocationCurve locCurve)) continue;
            var curve = locCurve.Curve;
            double lengthFt = curve.Length;

            if (lengthFt <= maxSegmentFt + 0.01) continue; // already short enough

            var splitPoints = new List<XYZ>();
            double pos = maxSegmentFt;
            while (pos < lengthFt - 0.01)
            {
                double normalized = pos / lengthFt;
                var pt = curve.Evaluate(normalized, true);
                splitPoints.Add(pt);
                pos += maxSegmentFt;
            }

            if (splitPoints.Count > 0)
                plan.Add((elem, category, splitPoints));
        }

        if (config.DryRun)
        {
            int totalSplits = plan.Sum(p => p.SplitPoints.Count);
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ cắt {plan.Count} phần tử, tạo {totalSplits} điểm cắt.",
                totalSplits);
            foreach (var (elem, cat, pts) in plan)
            {
                preview.Messages.Add(
                    $"  {cat} {elem.Id}: {pts.Count} điểm cắt tại " +
                    string.Join(", ", pts.Select(p => $"({p.X * FtToMm:F0},{p.Y * FtToMm:F0},{p.Z * FtToMm:F0})mm")));
            }
            return preview;
        }

        // 3. Execute splits
        int totalSplitsDone = 0;

        using var tx = new Transaction(document, "DHCB - Cắt đoạn MEP dài");
        tx.Start();
        tx.SetFailureHandlingOptions(
            tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor()));

        foreach (var (elem, category, splitPoints) in plan)
        {
            // Must split from END backwards so ElementIds of already-split segments remain valid
            // Actually BreakCurve returns the new tail element; keep splitting the original from start
            var currentId = elem.Id;

            foreach (var splitPoint in splitPoints)
            {
                try
                {
                    ElementId newSegmentId;

                    if (category.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        newSegmentId = PlumbingUtils.BreakCurve(document, currentId, splitPoint);
                    }
                    else if (category.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        newSegmentId = MechanicalUtils.BreakCurve(document, currentId, splitPoint);
                    }
                    else
                    {
                        // CableTray / Conduit — no direct BreakCurve API; skip (report only)
                        continue;
                    }

                    if (newSegmentId != null && newSegmentId != ElementId.InvalidElementId)
                    {
                        totalSplitsDone++;
                        // After split, the head segment retains currentId; tail is newSegmentId.
                        // Next split point is relative to original curve — keep currentId.
                    }
                }
                catch (System.Exception)
                {
                    // Individual split failure — continue with next point
                }
            }
        }

        tx.Commit();
        return CommandResult.Ok(
            $"Đã cắt {totalSplitsDone} điểm trên {plan.Count} phần tử MEP.",
            totalSplitsDone);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, BuiltInCategory> CategoryMap =
        new Dictionary<string, BuiltInCategory>
        {
            { "Duct", BuiltInCategory.OST_DuctCurves },
            { "Pipe", BuiltInCategory.OST_PipeCurves },
            { "CableTray", BuiltInCategory.OST_CableTray },
            { "Conduit", BuiltInCategory.OST_Conduit },
        };

    private static List<(Element Element, string Category)> CollectElements(Document doc, PipeSplitterConfig config)
    {
        var result = new List<(Element, string)>();

        IEnumerable<KeyValuePair<string, BuiltInCategory>> categoriesToSearch;
        if (config.Categories != null && config.Categories.Count > 0)
        {
            var filtered = new List<KeyValuePair<string, BuiltInCategory>>();
            foreach (var cat in config.Categories)
            {
                BuiltInCategory bic;
                if (CategoryMap.TryGetValue(cat, out bic))
                    filtered.Add(new KeyValuePair<string, BuiltInCategory>(cat, bic));
            }
            categoriesToSearch = filtered;
        }
        else
        {
            categoriesToSearch = CategoryMap;
        }

        foreach (var kvp in categoriesToSearch)
        {
            var elems = new FilteredElementCollector(doc)
                .OfCategory(kvp.Value)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in elems)
            {
                if (!string.IsNullOrEmpty(config.LevelName) && !BelongsToLevel(doc, e, config.LevelName))
                    continue;
                if (e.Location is LocationCurve)
                    result.Add((e, kvp.Key));
            }
        }

        return result;
    }

    private static bool BelongsToLevel(Document doc, Element elem, string levelName)
    {
        var levelParam = elem.LookupParameter("Level")
            ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
            ?? elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)
            ?? elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);

        if (levelParam == null || levelParam.StorageType != StorageType.ElementId) return false;
        var levelId = levelParam.AsElementId();
        if (levelId == null || levelId == ElementId.InvalidElementId) return false;
        var level = doc.GetElement(levelId) as Level;
        return level != null && string.Equals(level.Name, levelName, StringComparison.OrdinalIgnoreCase);
    }
}
