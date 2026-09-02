using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.MEPF;

/// <summary>
/// Gán cao độ (elevation) đáy, đỉnh, tim đường ống/duct/cable tray vào các tham số người dùng.
/// </summary>
public sealed class ElevationTagCommand : ICoreCommand<ElevationTagConfig>
{
    public string CommandName => "ElevationTag";

    private const double FtToMm = 304.8;

    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_CableTray,
        BuiltInCategory.OST_Conduit,
    };

    public CommandResult Execute(Document document, ElevationTagConfig config)
    {
        var elements = CollectElements(document, config);
        if (elements.Count == 0)
        {
            return CommandResult.Fail("Không có phần tử MEP nào phù hợp với cấu hình.");
        }

        // Build plan: element → (bottomMm, topMm, centreMm)
        var plan = new List<(Element Element, double BottomMm, double TopMm, double CentreMm)>();

        foreach (var elem in elements)
        {
            var bb = elem.get_BoundingBox(null);
            if (bb == null) continue;

            var elevations = MepLayout.Elevations(bb.Min.Z, bb.Max.Z);

            plan.Add((elem, elevations.BottomMm, elevations.TopMm, elevations.CentreMm));
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ gán cao độ cho {plan.Count} phần tử MEP.",
                plan.Count);
            foreach (var (elem, bottom, top, centre) in plan)
            {
                preview.Messages.Add(
                    $"  {elem.Id}: đáy={bottom:F1}mm, đỉnh={top:F1}mm, tim={centre:F1}mm");
            }
            return preview;
        }

        int updated = 0;
        using var tx = new Transaction(document, "DHCB - Gán cao độ MEP");
        tx.Start();
        tx.SetFailureHandlingOptions(
            tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor()));

        var result = CommandResult.Ok(string.Empty);
        foreach (var (elem, bottom, top, centre) in plan)
        {
            bool anySet = false;
            anySet |= TrySetDoubleParam(elem, config.BottomElevParamName, bottom, result);
            anySet |= TrySetDoubleParam(elem, config.TopElevParamName, top, result);
            anySet |= TrySetDoubleParam(elem, config.CenterElevParamName, centre, result);
            if (anySet) updated++;
        }

        tx.Commit();
        return CommandResult.Ok($"Đã gán cao độ cho {updated}/{plan.Count} phần tử MEP.", updated);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<Element> CollectElements(Document doc, ElevationTagConfig config)
    {
        var result = new List<Element>();
        bool filterCat = config.Categories != null && config.Categories.Count > 0;

        foreach (var bic in DefaultCategories)
        {
            if (filterCat)
            {
                var catKey = bic.ToString()
                    .Replace("OST_", string.Empty)
                    .Replace("Curves", string.Empty);
                bool include = false;
                foreach (var cat in config.Categories ?? new System.Collections.Generic.List<string>())
                {
                    if (catKey.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cat.IndexOf(catKey, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        include = true;
                        break;
                    }
                }
                if (!include) continue;
            }

            var elems = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in elems)
            {
                var eln = config.LevelName ?? string.Empty;
                if (!string.IsNullOrEmpty(eln) && !BelongsToLevel(doc, e, eln))
                    continue;
                result.Add(e);
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

    private static bool TrySetDoubleParam(Element elem, string paramName, double valueMm, CommandResult log)
    {
        if (string.IsNullOrEmpty(paramName)) return false;
        var param = elem.LookupParameter(paramName);
        if (param == null || param.IsReadOnly) return false;

        try
        {
            if (param.StorageType == StorageType.Double)
                param.Set(valueMm / FtToMm); // internal units = feet
            else if (param.StorageType == StorageType.String)
                param.Set(NumericText.Format(valueMm, 1)); // Invariant: máy tiếng Việt không được ghi "3200,0"
            else
                return false;
            return true;
        }
        catch (System.Exception ex)
        {
            log.Messages.Add($"Không gán được {paramName} cho {elem.Id}: {ex.Message}");
            return false;
        }
    }
}
