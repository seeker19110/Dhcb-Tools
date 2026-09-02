using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

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

        // Lỗi #10: lọc category (và level nếu có) ở tầng Revit bằng ElementMulticategoryFilter/ElementLevelFilter.
        var collector = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds.ToList()));
        var level = RevitCompat.FindLevel(document, config.LevelName);
        if (level is not null)
        {
            collector = collector.WherePasses(new ElementLevelFilter(level.Id));
        }

        var elements = collector
            .Where(e => config.LevelName is null || level is not null || BelongsToLevel(document, e, config.LevelName))
            .Select(e => (Element: e, Location: GetLocationPoint(e)))
            .Where(t => t.Location is not null)
            .ToList();

        if (elements.Count == 0)
        {
            return CommandResult.Fail($"Không có phần tử nào của category \"{config.Category}\" có vị trí để đánh số.");
        }

        // Sắp xếp có gom dải theo dung sai (mặc định 300 mm): hai cửa cùng hàng lệch vài mm phải nằm
        // cùng một "hàng" thì thứ tự trái→phải mới có nghĩa (lỗi #5 trong docs/progress.md).
        var items = elements
            .Select(t => new NumberingItem<Element>(t.Element, t.Location!.X, t.Location!.Y))
            .ToList();

        var direction = config.Direction == NumberingDirection.LeftToRightThenTopToBottom
            ? ScanDirection.LeftToRightThenTopToBottom
            : ScanDirection.TopToBottomThenLeftToRight;

        var ordered = NumberingPlanner.Order(items, direction, config.RowToleranceMm / MepLayout.FeetToMm);

        var plan = NumberingPlanner
            .Assign(ordered, config.Prefix, config.StartNumber, config.Step, config.PadWidth)
            .Select(a => (Element: a.Key, Value: a.Value))
            .ToList();

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
        RevitCompat.ApplyFailurePolicy(transaction);

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

        // Lỗi #2: bản cũ `return CommandResult.Ok(...)` tạo object mới nên toàn bộ dòng "Bỏ qua phần tử X"
        // gom trong `result` bị mất — kỹ sư thấy "40/120" mà không biết 80 phần tử kia hỏng vì lý do gì.
        var final = CommandResult.Ok($"Đã đánh số {updated}/{plan.Count} phần tử \"{config.Category}\".", updated);
        final.Messages.AddRange(result.Messages);
        return final;
    }

    private static bool BelongsToLevel(Document document, Element element, string levelName)
    {
        // Không phải mọi category đều có property Level thống nhất trong API (Room dùng SpatialElement.Level,
        // cửa/thiết bị dùng tham số instance "Level"...) nên tra theo tham số để dùng chung cho mọi category.
        var levelParameter = RevitCompat.Lookup(element, "level")
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
