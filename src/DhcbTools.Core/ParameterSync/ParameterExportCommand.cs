using System.Globalization;
using System.Text;
using Autodesk.Revit.DB;

namespace DhcbTools.Core.ParameterSync;

/// <summary>
/// Xuất tham số của các phần tử theo category ra file CSV (mở/sửa trực tiếp bằng Excel).
/// Cột đầu luôn là ElementId để lệnh nhập (<see cref="ParameterImportCommand"/>) ghi ngược đúng phần tử.
/// </summary>
public sealed class ParameterExportCommand : ICoreCommand<ParameterExportConfig>
{
    public string CommandName => "ParameterExport";

    public CommandResult Execute(Document document, ParameterExportConfig config)
    {
        var categoryIds = ResolveCategoryIds(document, config.Categories, out var unknownCategories);

        var collector = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .Where(e => e.Category is not null && categoryIds.Contains(e.Category.Id))
            .ToList();

        var sb = new StringBuilder();
        sb.Append("ElementId,Category,Name");
        foreach (var paramName in config.ParameterNames)
        {
            sb.Append(',').Append(CsvEscape(paramName));
        }
        sb.Append('\n');

        foreach (var element in collector)
        {
            sb.Append(element.Id.ToString()).Append(',')
              .Append(CsvEscape(element.Category!.Name)).Append(',')
              .Append(CsvEscape(element.Name));

            foreach (var paramName in config.ParameterNames)
            {
                var value = ReadParameterAsString(element, paramName);
                sb.Append(',').Append(CsvEscape(value));
            }
            sb.Append('\n');
        }

        // UTF-8 kèm BOM: Excel trên Windows cần BOM mới nhận đúng tên tiếng Việt (lỗi #4).
        File.WriteAllText(config.OutputPath, sb.ToString(), new UTF8Encoding(true));

        var result = CommandResult.Ok(
            $"Đã xuất {collector.Count} phần tử, {config.ParameterNames.Count} tham số ra \"{config.OutputPath}\".",
            collector.Count);

        foreach (var unknown in unknownCategories)
        {
            result.Messages.Add($"Bỏ qua category không tồn tại trong mô hình: \"{unknown}\".");
        }

        return result;
    }

    internal static HashSet<ElementId> ResolveCategoryIds(Document document, IEnumerable<string> categoryNames, out List<string> unknown)
    {
        unknown = new List<string>();
        var ids = new HashSet<ElementId>();
        var allCategories = document.Settings.Categories;

        foreach (var name in categoryNames)
        {
            var category = allCategories.Cast<Category>().FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

            if (category is null)
            {
                unknown.Add(name);
            }
            else
            {
                ids.Add(category.Id);
            }
        }

        return ids;
    }

    internal static string ReadParameterAsString(Element element, string parameterName)
    {
        var parameter = element.LookupParameter(parameterName);
        if (parameter is null)
        {
            // Không tìm thấy ở instance — thử tra ở Type (ví dụ tham số kiểu như "Fire Rating").
            var typeElement = element.Document.GetElement(element.GetTypeId());
            parameter = typeElement?.LookupParameter(parameterName);
        }

        if (parameter is null || !parameter.HasValue)
        {
            return string.Empty;
        }

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString() ?? string.Empty,
            StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double => parameter.AsDouble().ToString(CultureInfo.InvariantCulture),
            StorageType.ElementId => parameter.AsValueString() ?? string.Empty,
            _ => parameter.AsValueString() ?? string.Empty,
        };
    }

    private static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
