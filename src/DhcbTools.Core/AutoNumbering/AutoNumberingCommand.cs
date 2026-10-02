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

        // Tra tham số đích cho CẢ xem trước lẫn chạy thật. Bản cũ chỉ tra lúc ghi: xem trước báo "sẽ đánh số 120" rồi
        // chạy thật "40/120" vì 80 phần tử không có tham số / tham số chỉ đọc — xem trước mất tác dụng.
        var result = CommandResult.Ok(string.Empty);
        var targets = new List<(Element Element, Parameter Parameter, string Value)>();
        var unchanged = 0;
        foreach (var (element, value) in plan)
        {
            // Chỉ instance (ghi vào type là đổi cả loạt) nhưng qua từ điển để nhận tên đồng nghĩa tiếng Việt.
            var parameter = RevitCompat.LookupInstance(element, config.ParameterName, config.ParameterName);
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                result.Messages.Add($"Bỏ qua phần tử {element.Id}: tham số \"{config.ParameterName}\" không ghi được.");
                continue;
            }

            if (string.Equals(parameter.AsString() ?? string.Empty, value, StringComparison.Ordinal))
            {
                // Đã đúng số: không ghi lại (không làm bẩn mô hình, không đếm là "đã đánh số").
                unchanged++;
                continue;
            }

            targets.Add((element, parameter, value));
        }

        if (unknown.Count > 0)
        {
            result.Messages.Add($"Bỏ qua category không xác định: {string.Join(", ", unknown)}.");
        }

        if (unchanged > 0)
        {
            result.Messages.Insert(0, $"{unchanged} phần tử đã đúng số, không ghi lại.");
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đánh số {targets.Count}/{plan.Count} phần tử \"{config.Category}\" vào tham số \"{config.ParameterName}\".",
                targets.Count);
            preview.Messages.AddRange(result.Messages);
            preview.Messages.AddRange(targets.Select(t => $"{t.Element.Id}: \"{t.Value}\""));
            return preview;
        }

        var updated = 0;
        using var transaction = new Transaction(document, $"DHCB - Đánh số {config.Category}");
        transaction.Start();
        RevitCompat.ApplyFailurePolicy(transaction);

        foreach (var (element, parameter, value) in targets)
        {
            // Set trả false khi Revit không nhận giá trị — trước đây kết quả này bị bỏ qua và vẫn đếm là đã đánh số.
            if (!parameter.Set(value))
            {
                result.Messages.Add($"Bỏ qua phần tử {element.Id}: Revit không nhận giá trị \"{value}\" cho \"{config.ParameterName}\".");
                continue;
            }

            updated++;
            result.WithChanged(RevitCompat.IdValue(element.Id));
        }

        transaction.Commit();

        // Lỗi #2: bản cũ `return CommandResult.Ok(...)` tạo object mới nên toàn bộ dòng "Bỏ qua phần tử X"
        // gom trong `result` bị mất — kỹ sư thấy "40/120" mà không biết 80 phần tử kia hỏng vì lý do gì.
        var final = CommandResult.Ok($"Đã đánh số {updated}/{plan.Count} phần tử \"{config.Category}\".", updated)
            .WithChanged(result.ChangedIds);   // giai đoạn 10.2: object mới cũng phải mang theo ChangedIds
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
