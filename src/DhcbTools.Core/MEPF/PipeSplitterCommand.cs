using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Mep;
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

    public CommandResult Execute(Document document, PipeSplitterConfig config)
    {
        if (config.MaxSegmentMm <= 0)
        {
            return CommandResult.Fail("MaxSegmentMm phải lớn hơn 0.");
        }

        double maxSegmentFt = MepLayout.MmToFeet(config.MaxSegmentMm);

        // 1. Collect MEP elements
        var unknownCategories = new List<string>();
        var elements = CollectElements(document, config, unknownCategories);
        if (unknownCategories.Count > 0)
        {
            // Tên category không nhận ra phải báo, không được bỏ im lặng rồi kết luận "model rỗng".
            return CommandResult.Fail(RevitCompat.UnknownMepCategories(unknownCategories));
        }

        if (elements.Count == 0)
        {
            return CommandResult.Fail("Không có phần tử MEP nào phù hợp để cắt.");
        }

        // 2. Compute split plan: (element, category, list of split points along curve)
        // CableTray/Conduit không có API BreakCurve — chỉ liệt kê để báo cáo, KHÔNG tính vào tổng sẽ cắt.
        var plan = new List<(Element Element, string Category, List<XYZ> SplitPoints, bool Splittable)>();

        foreach (var (elem, category) in elements)
        {
            if (!(elem.Location is LocationCurve locCurve)) continue;
            var curve = locCurve.Curve;
            double lengthFt = curve.Length;

            // Sắp xếp điểm cắt theo tham số dọc tuyến — thứ tự quan trọng vì sau mỗi lần cắt phải
            // xác định điểm tiếp theo nằm trên đoạn nào.
            var splitPoints = new List<XYZ>();
            foreach (var pos in MepLayout.SplitPositions(lengthFt, maxSegmentFt).OrderBy(x => x))
            {
                double normalized = pos / lengthFt;
                splitPoints.Add(curve.Evaluate(normalized, true));
            }

            if (splitPoints.Count > 0)
                plan.Add((elem, category, splitPoints, PipeSplitPlanner.IsSplittable(category)));
        }

        var splittable = plan.Where(p => p.Splittable).ToList();
        var reportOnly = plan.Where(p => !p.Splittable).ToList();

        if (config.DryRun)
        {
            int totalSplits = splittable.Sum(p => p.SplitPoints.Count);
            var preview = CommandResult.Ok(PipeSplitPlanner.PreviewSummary(splittable.Count, totalSplits, reportOnly.Count), totalSplits);
            foreach (var (elem, cat, pts, ok) in plan)
            {
                preview.Messages.Add(PipeSplitPlanner.PreviewLine(cat, RevitCompat.IdValue(elem.Id), ok, pts.Select(Mm).ToList()));
            }
            return preview;
        }

        // 3. Execute splits
        int totalSplitsDone = 0;
        var failures = new List<string>();

        using var tx = new Transaction(document, "DHCB - Cắt đoạn MEP dài");
        tx.Start();
        RevitCompat.ApplyFailurePolicy(tx);

        foreach (var (elem, category, splitPoints, _) in splittable)
        {
            bool isPipe = PipeSplitPlanner.IsPipe(category);

            // BreakCurve trả về id của đoạn MỚI (phần đuôi); đoạn gốc giữ id cũ nhưng ngắn lại.
            // Điểm cắt tiếp theo (đã sắp xếp dọc tuyến) nằm trên phần đuôi, nên sau mỗi lần cắt phải
            // chọn lại đoạn chứa điểm đó thay vì cắt mãi đoạn gốc.
            var currentId = elem.Id;

            foreach (var splitPoint in splitPoints)
            {
                try
                {
                    var newSegmentId = isPipe
                        ? PlumbingUtils.BreakCurve(document, currentId, splitPoint)
                        : MechanicalUtils.BreakCurve(document, currentId, splitPoint);

                    if (newSegmentId == null || newSegmentId == ElementId.InvalidElementId)
                    {
                        var (fx, fy, fz) = Mm(splitPoint);
                        failures.Add(PipeSplitPlanner.BreakReturnedNothing(category, RevitCompat.IdValue(elem.Id), fx, fy, fz));
                        continue;
                    }

                    totalSplitsDone++;
                    currentId = newSegmentId;
                }
                catch (System.Exception ex)
                {
                    var (fx, fy, fz) = Mm(splitPoint);
                    failures.Add(PipeSplitPlanner.BreakFailed(category, RevitCompat.IdValue(elem.Id), fx, fy, fz, ex.Message));
                }
            }
        }

        tx.Commit();

        var result = CommandResult.Ok(
            PipeSplitPlanner.WriteSummary(totalSplitsDone, splittable.Count, failures.Count, reportOnly.Count), totalSplitsDone);
        result.Messages.AddRange(failures);
        foreach (var (elem, cat, pts, _) in reportOnly)
        {
            result.Messages.Add(PipeSplitPlanner.ReportOnlyLine(cat, RevitCompat.IdValue(elem.Id), pts.Count));
        }

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (double X, double Y, double Z) Mm(XYZ p) =>
        (RevitCompat.FtToMm(p.X), RevitCompat.FtToMm(p.Y), RevitCompat.FtToMm(p.Z));

    private static List<(Element Element, string Category)> CollectElements(Document doc, PipeSplitterConfig config, List<string> unknown)
    {
        var result = new List<(Element, string)>();

        IEnumerable<KeyValuePair<string, BuiltInCategory>> categoriesToSearch;
        if (config.Categories != null && config.Categories.Count > 0)
        {
            var filtered = new List<KeyValuePair<string, BuiltInCategory>>();
            foreach (var cat in config.Categories)
            {
                BuiltInCategory bic;
                if (RevitCompat.MepCurveCategories.TryGetValue(cat, out bic))
                    filtered.Add(new KeyValuePair<string, BuiltInCategory>(cat, bic));
                else
                    unknown.Add(cat);
            }
            categoriesToSearch = filtered;
        }
        else
        {
            categoriesToSearch = RevitCompat.MepCurveCategories;
        }

        foreach (var kvp in categoriesToSearch)
        {
            var elems = new FilteredElementCollector(doc)
                .OfCategory(kvp.Value)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in elems)
            {
                var pln = config.LevelName ?? string.Empty;
                if (!string.IsNullOrEmpty(pln) && !RevitCompat.BelongsToLevel(doc, e, pln))
                    continue;
                if (e.Location is LocationCurve)
                    result.Add((e, kvp.Key));
            }
        }

        return result;
    }

}
