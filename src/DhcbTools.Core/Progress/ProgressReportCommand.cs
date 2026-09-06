using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;
using DhcbTools.Core.MEPF;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Progress;

namespace DhcbTools.Core.Progress;

/// <summary>
/// Đề xuất B1: báo cáo tiến độ thi công đọc thẳng từ mô hình — % theo <b>số lượng</b> và theo
/// <b>chiều dài</b>, gộp theo tầng / hệ / category, kèm chuỗi luỹ kế theo tuần. Chỉ đọc.
/// <para>
/// Phần quyết định, HTML, Summary và mọi câu cảnh báo nằm ở <see cref="ProgressReportLogic"/> (có test
/// trên CI); file này chỉ còn đọc tham số phần tử từ Revit.
/// </para>
/// </summary>
public sealed class ProgressReportConfig
{
    /// <summary>File HTML báo cáo.</summary>
    public required string OutputPath { get; init; }

    /// <summary>CSV cùng nội dung để đưa vào bảng tiến độ của ban chỉ huy (tuỳ chọn).</summary>
    public string? CsvPath { get; init; }

    /// <summary>Category cần tính; rỗng = nhóm MEP + thiết bị mặc định.</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Gộp theo: <c>Level</c> (mặc định) | <c>System</c> | <c>Category</c>.</summary>
    public string GroupBy { get; init; } = "Level";

    /// <summary>Tên tham số trạng thái; rỗng = từ điển <c>constructionStatus</c>.</summary>
    public string? StatusParameter { get; init; }

    /// <summary>Tham số ngày để dựng chuỗi theo tuần; rỗng = từ điển <c>constructionDate</c>.</summary>
    public string? DateParameter { get; init; }

    public string? LevelName { get; init; }

    public string? SystemContains { get; init; }
}

public sealed class ProgressReportCommand : ICoreCommand<ProgressReportConfig>
{
    public string CommandName => "ProgressReport";

    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_DuctTerminal,
    };

    public CommandResult Execute(Document document, ProgressReportConfig config)
    {
        if (!ProgressReportLogic.TryParseGroupBy(config.GroupBy, out var groupBy, out var groupError))
        {
            return CommandResult.Fail(groupError);
        }

        ICollection<ElementId> categoryIds;
        if (config.Categories.Count > 0)
        {
            categoryIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
            if (unknown.Count > 0)
            {
                return CommandResult.Fail(ProgressReportLogic.UnknownCategoriesError(unknown));
            }
        }
        else
        {
            categoryIds = DefaultCategories.Select(c => new ElementId(c)).ToList();
        }

        var elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds.ToList()))
            .ToElements();

        var items = new List<StatusItem>();
        var withStatusParameter = 0;
        var unreadable = new List<string>();
        var filtered = 0;

        foreach (var element in elements)
        {
            var levelName = LevelNameOf(document, element);
            if (!string.IsNullOrWhiteSpace(config.LevelName)
                && !string.Equals(levelName, config.LevelName, StringComparison.OrdinalIgnoreCase))
            {
                filtered++;
                continue;
            }

            var system = MepParams.SystemNameOrType(element);
            if (!string.IsNullOrWhiteSpace(config.SystemContains)
                && system.IndexOf(config.SystemContains!, StringComparison.OrdinalIgnoreCase) < 0)
            {
                filtered++;
                continue;
            }

            var group = ProgressReportLogic.GroupOf(groupBy, levelName, system, element.Category?.Name);

            var stage = ConstructionStage.ChuaCoDuLieu;
            var parameter = RevitCompat.Lookup(element, "constructionStatus", config.StatusParameter);
            if (parameter != null)
            {
                withStatusParameter++;
                var text = parameter.StorageType == StorageType.String
                    ? parameter.AsString()
                    : parameter.AsValueString();

                if (!ConstructionStatusValue.TryParse(text, out stage))
                {
                    // Giá trị lạ trong mô hình KHÔNG được lặng lẽ tính là "chưa lắp": nói ra để kỹ sư sửa.
                    stage = ConstructionStage.ChuaCoDuLieu;
                    if (unreadable.Count < ProgressReportLogic.MaxUnreadableListed)
                    {
                        unreadable.Add(ProgressReportLogic.UnreadableEntry(RevitCompat.IdValue(element.Id), text));
                    }
                }
            }

            double lengthMm = 0;
            if (element is MEPCurve)
            {
                var lengthParameter = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (lengthParameter != null && lengthParameter.HasValue)
                {
                    lengthMm = RevitCompat.FtToMm(lengthParameter.AsDouble());
                }
            }

            items.Add(new StatusItem(group, stage, lengthMm, DateOf(element, config), RevitCompat.IdValue(element.Id)));
        }

        var result = CommandResult.Ok(string.Empty);
        var precondition = Precondition.NonEmptyInput(
            CommandName, "phần tử nào theo categories/levelName/systemContains", items.Count,
            "Kiểm lại bộ lọc; tra category có thật bằng query elements.");
        if (RevitPrecondition.Blocks(precondition, result))
        {
            return result;
        }

        if (withStatusParameter == 0)
        {
            return CommandResult.Fail(ProgressReportLogic.NoStatusParameterMessage(
                RevitCompat.LookupFailed("constructionStatus", config.StatusParameter), items.Count));
        }

        var rows = StatusRoll.By(items);
        var total = StatusRoll.Total(items);
        var series = WeeklyProgress.Series(items);
        var groupHeader = ProgressReportLogic.GroupHeader(groupBy);

        RevitCompat.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(
            config.OutputPath,
            ProgressReportLogic.Html(document.Title, config.StatusParameter, groupBy, rows, total, series, unreadable, DateTime.Now),
            Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(config.CsvPath))
        {
            RevitCompat.EnsureParentDirectory(config.CsvPath);
            var csvRows = rows.ToList();
            csvRows.Add(total);
            File.WriteAllText(config.CsvPath!, ProgressCsv.WriteReport(csvRows, groupHeader), CsvText.Utf8WithBom);
        }

        result.Summary = ProgressReportLogic.Summary(total, rows.Count, groupBy, config.OutputPath);
        result.AffectedCount = items.Count;
        result.Messages.AddRange(ProgressReportLogic.Notes(total, series, unreadable, filtered, config.CsvPath));
        return result;
    }

    private static DateTime? DateOf(Element element, ProgressReportConfig config)
    {
        var parameter = RevitCompat.Lookup(element, "constructionDate", config.DateParameter);
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        var text = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return ProgressCsv.TryParseDate(text, out var date) ? date : (DateTime?)null;
    }

    private static string LevelNameOf(Document document, Element element)
    {
        try
        {
            if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId
                && document.GetElement(element.LevelId) is Level direct)
            {
                return direct.Name;
            }

            var parameter = RevitCompat.Lookup(element, "level")
                ?? element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (parameter?.StorageType == StorageType.ElementId && document.GetElement(parameter.AsElementId()) is Level viaParameter)
            {
                return viaParameter.Name;
            }
        }
        catch (Exception)
        {
        }

        return string.Empty;
    }
}
