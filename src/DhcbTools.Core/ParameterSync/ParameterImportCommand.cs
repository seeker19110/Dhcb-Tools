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
            var elementId = new ElementId((long)idValue);
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

                var parameter = element.LookupParameter(parameterColumns[col]);
                if (parameter is null || parameter.IsReadOnly)
                {
                    continue;
                }

                if (TrySetParameter(parameter, cells[cellIndex]))
                {
                    updated++;
                }
            }
        }

        if (config.DryRun)
        {
            transaction.RollBack();
            return CommandResult.Ok($"[Xem trước] Sẽ cập nhật {updated} giá trị tham số (chưa ghi vào mô hình).", updated);
        }

        transaction.Commit();
        return CommandResult.Ok($"Đã cập nhật {updated} giá trị tham số từ \"{config.InputPath}\".", updated);
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
                    return int.TryParse(rawValue, out var intValue) && parameter.Set(intValue);
                case StorageType.Double:
                    return double.TryParse(rawValue, out var doubleValue) && parameter.Set(doubleValue);
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
