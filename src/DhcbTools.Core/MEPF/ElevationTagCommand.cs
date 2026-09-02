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
        RevitCompat.ApplyFailurePolicy(tx);

        var result = CommandResult.Ok(string.Empty);
        foreach (var (elem, bottom, top, centre) in plan)
        {
            bool anySet = false;
            anySet |= TrySetDoubleParam(elem, "bottomElevation", config.BottomElevParamName, bottom, result);
            anySet |= TrySetDoubleParam(elem, "topElevation", config.TopElevParamName, top, result);
            anySet |= TrySetDoubleParam(elem, "centreElevation", config.CenterElevParamName, centre, result);
            if (anySet)
            {
                updated++;
                result.WithChanged(RevitCompat.IdValue(elem.Id));
            }
        }

        tx.Commit();

        var final = CommandResult.Ok($"Đã gán cao độ cho {updated}/{plan.Count} phần tử MEP.", updated)
            .WithChanged(result.ChangedIds);
        final.Messages.AddRange(result.Messages);

        // Không phần tử nào ghi được nghĩa là dự án không có tham số cao độ nào trong từ điển —
        // trước đây lệnh vẫn báo "Đã gán cao độ cho 0/N" như thể mọi thứ bình thường.
        if (updated == 0 && plan.Count > 0)
        {
            final.Success = false;
            final.Summary = $"Không gán được cao độ cho phần tử nào trong {plan.Count} phần tử.";
            final.Errors.Add(RevitCompat.LookupFailed("bottomElevation", config.BottomElevParamName));
        }

        return final;
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

    /// <summary>
    /// Ghi một giá trị cao độ. <paramref name="key"/> là khoá từ điển (bottomElevation…),
    /// <paramref name="paramName"/> là tên người dùng chỉ định trong config (ưu tiên hơn từ điển).
    /// Trả false khi không có tham số nào ghi được — người gọi phải báo, không được im lặng.
    /// </summary>
    private static bool TrySetDoubleParam(Element elem, string key, string? paramName, double valueMm, CommandResult log)
    {
        var param = RevitCompat.Lookup(elem, key, paramName);
        if (param == null || param.IsReadOnly) return false;

        try
        {
            if (param.StorageType == StorageType.Double)
                param.Set(RevitCompat.MmToFt(valueMm)); // internal units = feet
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
