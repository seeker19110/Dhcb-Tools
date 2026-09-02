using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.MEPF;

/// <summary>
/// Đặt hanger family dọc theo đường ống/duct/cable tray theo khoảng cách đều.
/// </summary>
public sealed class HangerCommand : ICoreCommand<HangerConfig>
{
    public string CommandName => "HangerAuto";


    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_CableTray,
    };

    public CommandResult Execute(Document document, HangerConfig config)
    {
        // 1. Find hanger FamilySymbol
        var symbol = FindFamilySymbol(document, config.HangerFamilyName);
        if (symbol == null)
        {
            return CommandResult.Fail(
                $"Không tìm thấy FamilySymbol \"{config.HangerFamilyName}\" trong mô hình.");
        }

        // 2. Collect MEP elements
        var elements = CollectElements(document, config);
        if (elements.Count == 0)
        {
            return CommandResult.Fail("Không có phần tử MEP nào phù hợp với cấu hình.");
        }

        if (config.SpacingMm <= 0)
        {
            return CommandResult.Fail("SpacingMm phải lớn hơn 0.");
        }

        double spacingFt = MepLayout.MmToFeet(config.SpacingMm);
        double offsetFt = MepLayout.MmToFeet(config.OffsetMm);

        // 3. Build placement plan
        var plan = new List<(XYZ Point, XYZ Direction)>();

        foreach (var elem in elements)
        {
            if (!(elem.Location is LocationCurve locCurve)) continue;
            var curve = locCurve.Curve;
            double lengthFt = curve.Length;

            if (lengthFt <= 0) continue;

            // Vị trí đặt tính bằng MepLayout.HangerPositions: spacing/2, 3·spacing/2, … và luôn có
            // đúng một hanger cho đoạn ngắn. Bản cũ kiểm tra `plan.Count == 0 || lengthFt < spacingFt`
            // trên danh sách plan DÙNG CHUNG cho mọi phần tử nên đoạn dài hơn nửa khoảng cách mà ngắn
            // hơn một khoảng cách bị đặt hai hanger chồng nhau.
            foreach (var pos in MepLayout.HangerPositions(lengthFt, spacingFt))
            {
                double normalizedParam = pos / lengthFt;
                var point = curve.Evaluate(normalizedParam, true);
                var tangent = GetCurveTangent(curve, normalizedParam);

                // Offset upward
                var insertPoint = new XYZ(point.X, point.Y, point.Z + offsetFt);
                plan.Add((insertPoint, tangent));
            }
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đặt {plan.Count} hanger trên {elements.Count} phần tử MEP.",
                plan.Count);
            foreach (var (pt, dir) in plan)
            {
                preview.Messages.Add(
                    $"  → ({RevitCompat.FtToMm(pt.X):F0}, {RevitCompat.FtToMm(pt.Y):F0}, {RevitCompat.FtToMm(pt.Z):F0}) mm" +
                    $"  dir=({dir.X:F2},{dir.Y:F2},{dir.Z:F2})");
            }
            return preview;
        }

        // 4. Place hangers in single transaction
        int placed = 0;
        using var tx = new Transaction(document, "DHCB - Đặt hanger");
        tx.Start();
        RevitCompat.ApplyFailurePolicy(tx);

        if (!symbol.IsActive)
            symbol.Activate();

        foreach (var (point, direction) in plan)
        {
            try
            {
                var inst = document.Create.NewFamilyInstance(
                    point, symbol, StructuralType.NonStructural);

                // Rotate to align with element direction if not along X axis
                if (Math.Abs(direction.X) > 0.01 || Math.Abs(direction.Y) > 0.01)
                {
                    var angle = Math.Atan2(direction.Y, direction.X);
                    var axis = Line.CreateBound(point, point + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(document, inst.Id, axis, angle);
                }

                placed++;
            }
            catch (System.Exception)
            {
                // Continue on individual placement failures
            }
        }

        tx.Commit();
        return CommandResult.Ok($"Đã đặt {placed} hanger trên {elements.Count} phần tử MEP.", placed);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static FamilySymbol? FindFamilySymbol(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Element> CollectElements(Document doc, HangerConfig config)
    {
        var result = new List<Element>();
        bool filterCat = config.Categories != null && config.Categories.Count > 0;

        var categories = filterCat && config.Categories != null ? FilterCategories(config.Categories) : DefaultCategories;

        foreach (var bic in categories)
        {
            var elems = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in elems)
            {
                var ln = config.LevelName ?? string.Empty;
                if (!string.IsNullOrEmpty(ln) && !BelongsToLevel(doc, e, ln))
                    continue;
                if (e.Location is LocationCurve)
                    result.Add(e);
            }
        }
        return result;
    }

    private static BuiltInCategory[] FilterCategories(List<string> names)
    {
        var all = new Dictionary<string, BuiltInCategory>
        {
            { "Duct", BuiltInCategory.OST_DuctCurves },
            { "Pipe", BuiltInCategory.OST_PipeCurves },
            { "CableTray", BuiltInCategory.OST_CableTray },
            { "Conduit", BuiltInCategory.OST_Conduit },
        };
        var result = new List<BuiltInCategory>();
        foreach (var n in names)
        {
            BuiltInCategory bic;
            if (all.TryGetValue(n, out bic))
                result.Add(bic);
        }
        return result.Count > 0 ? result.ToArray() : DefaultCategories;
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

    private static XYZ GetCurveTangent(Curve curve, double normalizedParam)
    {
        try
        {
            var rawParam = curve.GetEndParameter(0) + normalizedParam * (curve.GetEndParameter(1) - curve.GetEndParameter(0));
            var deriv = curve.ComputeDerivatives(rawParam, false);
            var tangent = deriv.BasisX;
            if (tangent.GetLength() > 1e-9)
                return tangent.Normalize();
        }
        catch (System.Exception) { }
        return XYZ.BasisX;
    }
}
