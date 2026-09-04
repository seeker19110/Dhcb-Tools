using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
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
                plan.Add((elem, category, splitPoints, IsSplittable(category)));
        }

        var splittable = plan.Where(p => p.Splittable).ToList();
        var reportOnly = plan.Where(p => !p.Splittable).ToList();

        if (config.DryRun)
        {
            int totalSplits = splittable.Sum(p => p.SplitPoints.Count);
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ cắt {splittable.Count} phần tử, tạo {totalSplits} điểm cắt."
                + (reportOnly.Count > 0
                    ? $" {reportOnly.Count} CableTray/Conduit quá dài chỉ liệt kê (Revit không có API cắt), không tính vào tổng."
                    : string.Empty),
                totalSplits);
            foreach (var (elem, cat, pts, ok) in plan)
            {
                preview.Messages.Add(
                    $"  {cat} {elem.Id}{(ok ? string.Empty : " [chỉ báo cáo]")}: {pts.Count} điểm cắt tại " +
                    string.Join(", ", pts.Select(p => $"({RevitCompat.FtToMm(p.X):F0},{RevitCompat.FtToMm(p.Y):F0},{RevitCompat.FtToMm(p.Z):F0})mm")));
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
            bool isPipe = category.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0;

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
                        failures.Add($"{category} {elem.Id}: BreakCurve không tạo được đoạn mới tại {Fmt(splitPoint)}.");
                        continue;
                    }

                    totalSplitsDone++;
                    currentId = newSegmentId;
                }
                catch (System.Exception ex)
                {
                    failures.Add($"{category} {elem.Id}: không cắt được tại {Fmt(splitPoint)} — {ex.Message}");
                }
            }
        }

        tx.Commit();

        var result = CommandResult.Ok(
            $"Đã cắt {totalSplitsDone} điểm trên {splittable.Count} phần tử MEP"
            + (failures.Count > 0 ? $", {failures.Count} điểm cắt lỗi" : string.Empty)
            + (reportOnly.Count > 0 ? $"; {reportOnly.Count} CableTray/Conduit quá dài chỉ báo cáo (không có API cắt)" : string.Empty)
            + ".",
            totalSplitsDone);
        result.Messages.AddRange(failures);
        foreach (var (elem, cat, pts, _) in reportOnly)
        {
            result.Messages.Add($"  {cat} {elem.Id} [chỉ báo cáo]: cần {pts.Count} điểm cắt, cắt tay.");
        }

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsSplittable(string category) =>
        category.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0
        || category.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Fmt(XYZ p) =>
        $"({RevitCompat.FtToMm(p.X):F0},{RevitCompat.FtToMm(p.Y):F0},{RevitCompat.FtToMm(p.Z):F0})mm";

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
                if (!string.IsNullOrEmpty(pln) && !BelongsToLevel(doc, e, pln))
                    continue;
                if (e.Location is LocationCurve)
                    result.Add((e, kvp.Key));
            }
        }

        return result;
    }

    private static bool BelongsToLevel(Document doc, Element elem, string levelName)
    {
        var levelParam = RevitCompat.Lookup(elem, "level")
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
