using System.Globalization;
using Autodesk.Revit.DB;

namespace DhcbTools.Core.ParameterSync;

/// <summary>
/// Đọc lại file CSV do <see cref="ParameterExportCommand"/> tạo ra (đã được kỹ sư chỉnh sửa trong Excel)
/// và ghi giá trị tham số ngược vào mô hình theo ElementId ở cột đầu tiên.
/// </summary>
public sealed class ParameterImportCommand : ICoreCommand<ParameterImportConfig>
{
    public string CommandName => "ParameterImport";

    public CommandResult Execute(Document document, ParameterImportConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy file: \"{config.InputPath}\".");
        }

        var lines = File.ReadAllLines(config.InputPath);
        if (lines.Length < 2)
        {
            return CommandResult.Fail("File CSV không có dữ liệu (chỉ có dòng tiêu đề hoặc rỗng).");
        }

        var header = SplitCsvLine(lines[0]);
        // 3 cột đầu cố định: ElementId, Category, Name — phần còn lại là tên tham số.
        var parameterColumns = header.Skip(3).ToList();

        var updated = 0;
        var result = CommandResult.Ok(string.Empty);

        using var transaction = new Transaction(document, "DHCB - Nhập tham số từ CSV");
        transaction.Start();
        transaction.SetFailureHandlingOptions(
            transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor()));

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = SplitCsvLine(lines[i]);
            if (cells.Count < 3 || !long.TryParse(cells[0], out var idValue))
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: ElementId không hợp lệ.");
                continue;
            }

            // ElementId chuyển sang dùng kiểu long kể từ Revit 2024 và bỏ hẳn constructor int từ Revit 2025;
            // TargetFramework net8.0-windows chỉ dùng cho Revit 2025+ (xem Directory.Build.props) nên tách theo TFM.
#if NET8_0_WINDOWS
            var elementId = new ElementId(idValue);
#else
            var elementId = new ElementId((int)idValue);
#endif
            var element = document.GetElement(elementId);
            if (element is null)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: không tìm thấy phần tử {idValue} trong mô hình.");
                continue;
            }

            for (var col = 0; col < parameterColumns.Count; col++)
            {
                var cellIndex = 3 + col;
                if (cellIndex >= cells.Count)
                {
                    continue;
                }

                var parameter = ResolveParameter(element, parameterColumns[col]);
                if (parameter is null)
                {
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    result.Messages.Add(
                        $"Dòng {i + 1}: tham số \"{parameterColumns[col]}\" chỉ đọc, không ghi được.");
                    continue;
                }

                var rawValue = cells[cellIndex];
                if (TrySetParameter(parameter, rawValue))
                {
                    updated++;
                }
                else if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    result.Messages.Add(
                        $"Dòng {i + 1}: không ghi được giá trị \"{rawValue}\" vào tham số \"{parameterColumns[col]}\".");
                }
            }
        }

        if (config.DryRun)
        {
            transaction.RollBack();
            return result.With(
                $"[Xem trước] Sẽ cập nhật {updated} giá trị tham số (chưa ghi vào mô hình).", updated);
        }

        transaction.Commit();
        return result.With($"Đã cập nhật {updated} giá trị tham số từ \"{config.InputPath}\".", updated);
    }

    /// <summary>
    /// Tra tham số ở instance, nếu không có thì tra tiếp ở Type — đối xứng với
    /// <see cref="ParameterExportCommand.ReadParameterAsString"/> (lỗi #3).
    /// </summary>
    internal static Parameter? ResolveParameter(Element element, string parameterName)
    {
        var parameter = element.LookupParameter(parameterName);
        if (parameter is not null)
        {
            return parameter;
        }

        var typeElement = element.Document.GetElement(element.GetTypeId());
        return typeElement?.LookupParameter(parameterName);
    }

    /// <summary>
    /// Đọc số theo <see cref="CultureInfo.InvariantCulture"/> đúng như lúc xuất, có fallback sang
    /// culture hệ thống cho file người dùng tự gõ tay trong Excel tiếng Việt (lỗi #1).
    /// </summary>
    internal static bool TryParseDouble(string rawValue, out double value)
    {
        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    internal static bool TryParseInt(string rawValue, out int value)
    {
        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
    }

    private static bool TrySetParameter(Parameter parameter, string rawValue)
    {
        try
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.Set(rawValue);
                case StorageType.Integer:
                    return TryParseInt(rawValue, out var intValue) && parameter.Set(intValue);
                case StorageType.Double:
                    return TryParseDouble(rawValue, out var doubleValue) && parameter.Set(doubleValue);
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        cells.Add(current.ToString());
        return cells;
    }
}
