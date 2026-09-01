using Autodesk.Revit.DB;

namespace DhcbTools.Core.AutoNumbering;

/// <summary>
/// Đánh số hàng loạt theo vị trí hình học (mục 2.2 của tài liệu nghiên cứu). Sắp phần tử theo hướng
/// quét đã chọn rồi ghi "{Prefix}{số}" vào tham số đích — dùng cho cửa, phòng, thiết bị MEP...
/// </summary>
public sealed class AutoNumberingCommand : ICoreCommand<AutoNumberingConfig>
{
    public string CommandName => "AutoNumbering";

    public CommandResult Execute(Document document, AutoNumberingConfig config)
    {
        var categoryIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(
            document, new[] { config.Category }, out var unknown);

        if (categoryIds.Count == 0)
        {
            return CommandResult.Fail($"Không tìm thấy category \"{config.Category}\" trong mô hình.");
        }

        var elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .Where(e => e.Category is not null && categoryIds.Contains(e.Category.Id))
            .Where(e => config.LevelName is null || BelongsToLevel(document, e, config.LevelName))
            .Select(e => (Element: e, Location: GetLocationPoint(e)))
            .Where(t => t.Location is not null)
            .ToList();

        if (elements.Count == 0)
        {
            return CommandResult.Fail($"Không có phần tử nào của category \"{config.Category}\" có vị trí để đánh số.");
        }

        var ordered = config.Direction == NumberingDirection.LeftToRightThenTopToBottom
            ? elements.OrderByDescending(t => t.Location!.Y).ThenBy(t => t.Location!.X)
            : elements.OrderBy(t => t.Location!.X).ThenByDescending(t => t.Location!.Y);

        var plan = new List<(Element Element, string Value)>();
        var number = config.StartNumber;
        foreach (var (element, _) in ordered)
        {
            var digits = number.ToString();
            if (config.PadWidth > 0)
            {
                digits = digits.PadLeft(config.PadWidth, '0');
            }
            plan.Add((element, config.Prefix + digits));
            number += config.Step;
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đánh số {plan.Count} phần tử \"{config.Category}\" vào tham số \"{config.ParameterName}\".",
                plan.Count);
            preview.Messages.AddRange(plan.Select(p => $"{p.Element.Id}: \"{p.Value}\""));
            return preview;
        }

        var updated = 0;
        using var transaction = new Transaction(document, $"DHCB - Đánh số {config.Category}");
        transaction.Start();
        transaction.SetFailureHandlingOptions(
            transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor()));

        var result = CommandResult.Ok(string.Empty);
        foreach (var (element, value) in plan)
        {
            var parameter = element.LookupParameter(config.ParameterName);
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                result.Messages.Add($"Bỏ qua phần tử {element.Id}: tham số \"{config.ParameterName}\" không ghi được.");
                continue;
            }

            parameter.Set(value);
            updated++;
        }

        if (unknown.Count > 0)
        {
            result.Messages.Add($"Bỏ qua category không xác định: {string.Join(", ", unknown)}.");
        }

        transaction.Commit();

        return CommandResult.Ok($"Đã đánh số {updated}/{plan.Count} phần tử \"{config.Category}\".", updated);
    }

    private static bool BelongsToLevel(Document document, Element element, string levelName)
    {
        // Không phải mọi category đều có property Level thống nhất trong API (Room dùng SpatialElement.Level,
        // cửa/thiết bị dùng tham số instance "Level"...) nên tra theo tham số để dùng chung cho mọi category.
        var levelParameter = element.LookupParameter("Level")
            ?? element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
            ?? element.get_Parameter(BuiltInParameter.LEVEL_PARAM);

        if (levelParameter is null || levelParameter.StorageType != StorageType.ElementId)
        {
            return false;
        }

        var levelId = levelParameter.AsElementId();
        if (levelId is null || levelId == ElementId.InvalidElementId)
        {
            return false;
        }

        var level = document.GetElement(levelId) as Level;
        return level is not null && string.Equals(level.Name, levelName, StringComparison.OrdinalIgnoreCase);
    }

    private static XYZ? GetLocationPoint(Element element)
    {
        return element.Location switch
        {
            LocationPoint point => point.Point,
            LocationCurve curve => curve.Curve.Evaluate(0.5, true),
            _ => element.get_BoundingBox(null) is { } box ? (box.Min + box.Max) / 2 : null,
        };
    }
}
