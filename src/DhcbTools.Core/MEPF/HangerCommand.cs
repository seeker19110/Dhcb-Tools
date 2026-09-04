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
        var symbol = RevitCompat.FindFamilySymbol(document, config.HangerFamilyName);
        if (symbol == null)
        {
            return CommandResult.Fail(
                $"Không tìm thấy FamilySymbol \"{config.HangerFamilyName}\" trong mô hình.");
        }

        // 2. Collect MEP elements
        var elements = CollectElements(document, config, out var unknownCategories);
        if (unknownCategories.Count > 0)
        {
            return CommandResult.Fail(RevitCompat.UnknownMepCategories(unknownCategories));
        }

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
        // Chống trùng: hanger cùng family đã có trong model thì không đặt thêm ở đúng chỗ đó.
        // Thiếu bước này thì chạy lệnh lần hai là số hanger nhân đôi, và đường ghi thật cũng không
        // kiểm được gì (lần hai phải ra 0 mới chứng minh lần một đã commit).
        var existing = config.SkipExisting
            ? CollectExistingHangerPoints(document, config.HangerFamilyName)
            : new List<(double X, double Y, double Z)>();
        double existingToleranceFt = MepLayout.MmToFeet(config.ExistingToleranceMm);
        int skippedExisting = 0;

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

                if (MepLayout.IsNearAny(insertPoint.X, insertPoint.Y, insertPoint.Z, existing, existingToleranceFt))
                {
                    skippedExisting++;
                    continue;
                }

                // Hai đoạn khác nhau có thể sinh vị trí trùng nhau (chỗ nối) — tính cả những cái vừa
                // lên kế hoạch trong chính lượt này, không chỉ cái đã có sẵn trong model.
                existing.Add((insertPoint.X, insertPoint.Y, insertPoint.Z));
                plan.Add((insertPoint, tangent));
            }
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đặt {plan.Count} hanger trên {elements.Count} phần tử MEP."
                + SkipNote(skippedExisting),
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
        var placedIds = new List<long>();   // giai đoạn 10.2
        using var tx = new Transaction(document, "DHCB - Đặt hanger");
        tx.Start();
        RevitCompat.ApplyFailurePolicy(tx);

        if (!symbol.IsActive)
            symbol.Activate();

        var failed = 0;
        var failureReasons = new List<string>();
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
                placedIds.Add(RevitCompat.IdValue(inst.Id));
            }
            catch (System.Exception ex)
            {
                // Không huỷ cả lô vì một cái lỗi, nhưng phải ghi lý do — nuốt im lặng thì "0 hanger"
                // không ai biết vì sao.
                failed++;
                if (failureReasons.Count < 5 && !failureReasons.Contains(ex.Message))
                {
                    failureReasons.Add(ex.Message);
                }
            }
        }

        tx.Commit();
        var summary = $"Đã đặt {placed} hanger trên {elements.Count} phần tử MEP." + SkipNote(skippedExisting);
        if (failed > 0)
        {
            summary += $" {failed}/{plan.Count} vị trí đặt lỗi.";
        }

        var result = CommandResult.Ok(summary, placed).WithChanged(placedIds);
        if (failed > 0)
        {
            result.Messages.Add($"{failed} vị trí không đặt được hanger. Lý do (tối đa 5 loại): " + string.Join(" | ", failureReasons));
        }

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string SkipNote(int skippedExisting) =>
        skippedExisting > 0 ? $" Bỏ qua, đã có hanger: {skippedExisting} vị trí." : string.Empty;

    /// <summary>Vị trí các hanger cùng family đã có sẵn trong model (feet, toạ độ nội bộ Revit).</summary>
    private static List<(double X, double Y, double Z)> CollectExistingHangerPoints(Document doc, string familyName)
    {
        var points = new List<(double X, double Y, double Z)>();
        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(fi => fi.Symbol != null &&
                         (fi.Symbol.Name.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          fi.Symbol.FamilyName.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0));

        foreach (var fi in instances)
        {
            if (fi.Location is LocationPoint lp && lp.Point != null)
            {
                points.Add((lp.Point.X, lp.Point.Y, lp.Point.Z));
            }
        }
        return points;
    }

    private static List<Element> CollectElements(Document doc, HangerConfig config, out List<string> unknown)
    {
        unknown = new List<string>();
        var result = new List<Element>();
        bool filterCat = config.Categories != null && config.Categories.Count > 0;

        var categories = filterCat && config.Categories != null ? FilterCategories(config.Categories, out unknown) : DefaultCategories;

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

    /// <summary>
    /// Đổi tên category thành BuiltInCategory. Tên không nhận ra được trả ra ngoài qua
    /// <paramref name="unknown"/> — bản trước âm thầm rơi về DefaultCategories, tức là gõ sai một tên
    /// thì lệnh chạy trên TOÀN BỘ category mà vẫn báo thành công.
    /// </summary>
    private static BuiltInCategory[] FilterCategories(List<string> names, out List<string> unknown)
    {
        var result = RevitCompat.ResolveMepCategories(names, out unknown);
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
