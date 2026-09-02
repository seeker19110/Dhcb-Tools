using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

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

        var header = CsvText.SplitLine(lines[0]);
        // 3 cột đầu cố định: ElementId, Category, Name — phần còn lại là tên tham số.
        var parameterColumns = header.Skip(3).ToList();

        var updated = 0;
        var unchanged = 0;
        var result = CommandResult.Ok(string.Empty);

        using var transaction = new Transaction(document, "DHCB - Nhập tham số từ CSV");
        transaction.Start();
        RevitCompat.ApplyFailurePolicy(transaction);

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = CsvText.SplitLine(lines[i]);
            if (cells.Count < 3 || !long.TryParse(cells[0], out var idValue))
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: ElementId không hợp lệ.");
                continue;
            }

            // ElementId dùng long từ Revit 2024, int trước đó — RevitCompat.MakeId tách theo
            // REVIT2024_OR_GREATER. (Trước đây chỗ này tự tách bằng #if NET8_0_WINDOWS, một symbol
            // không bao giờ được định nghĩa — TFM net8.0-windows sinh ra NET8_0_WINDOWS7_0 — nên
            // nhánh long luôn thắng và Revit ≤ 2023 không biên dịch được.)
            var elementId = RevitCompat.MakeId(idValue);
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

                var name = parameterColumns[col];
                var parameter = element.LookupParameter(name);

                if (parameter is null)
                {
                    // Export có fallback đọc tham số ở Type; import phải đối xứng, nếu không thì tham số
                    // Type xuất ra được mà sửa xong không nhập lại được và không có cảnh báo nào (lỗi #3).
                    parameter = document.GetElement(element.GetTypeId())?.LookupParameter(name);
                }

                if (parameter is null)
                {
                    result.Messages.Add($"Bỏ qua dòng {i + 1}, cột \"{name}\": phần tử {idValue} không có tham số này.");
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    result.Messages.Add($"Bỏ qua dòng {i + 1}, cột \"{name}\": tham số chỉ đọc.");
                    continue;
                }

                if (IsUnchanged(parameter, cells[cellIndex], out var readError))
                {
                    // Không ghi lại giá trị đã giống hệt: tránh mở transaction đụng vào 100% phần tử
                    // (và tham số Type dùng chung) khi kỹ sư chỉ sửa vài ô trong CSV xuất ra.
                    unchanged++;
                    continue;
                }
                if (readError != null)
                {
                    // Trước đây lỗi đọc tham số bị catch rỗng và coi là "đã đổi" rồi ghi đè im lặng —
                    // nay báo rõ và vẫn thử ghi (TrySetParameter có catch/báo lỗi riêng của nó).
                    result.Messages.Add($"Dòng {i + 1}, cột \"{name}\": không đọc được giá trị hiện tại ({readError}), vẫn thử ghi.");
                }

                if (TrySetParameter(parameter, cells[cellIndex]))
                {
                    updated++;
                }
                else
                {
                    result.Messages.Add(
                        $"Bỏ qua dòng {i + 1}, cột \"{name}\": không ghi được giá trị \"{cells[cellIndex]}\" ({parameter.StorageType}).");
                }
            }
        }

        string summary;
        if (config.DryRun)
        {
            transaction.RollBack();
            summary = $"[Xem trước] Sẽ cập nhật {updated} giá trị tham số (chưa ghi vào mô hình).";
        }
        else
        {
            transaction.Commit();
            summary = $"Đã cập nhật {updated} giá trị tham số từ \"{config.InputPath}\".";
        }

        // Giữ nguyên object `result` để không đánh rơi toàn bộ cảnh báo đã gom ở trên (cùng dạng lỗi #2).
        var final = CommandResult.Ok(summary, updated);
        if (unchanged > 0)
        {
            final.Messages.Add($"{unchanged} ô giữ nguyên vì giá trị trong CSV trùng với mô hình.");
        }
        final.Messages.AddRange(result.Messages);
        return final;
    }

    /// <summary>
    /// Ô CSV trùng giá trị hiện tại của tham số (so sánh theo StorageType, số thực theo dung sai).
    /// <paramref name="readError"/> khác null khi đọc tham số ném lỗi — trước đây bị nuốt im lặng và
    /// coi như "đã đổi", nên một tham số đọc lỗi luôn bị ghi đè mà không ai biết vì sao.
    /// </summary>
    private static bool IsUnchanged(Parameter parameter, string rawValue, out string? readError)
    {
        readError = null;
        try
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return string.Equals(parameter.AsString() ?? string.Empty, rawValue, StringComparison.Ordinal);
                case StorageType.Integer:
                    return NumericText.TryParseInt(rawValue, out var intValue) && parameter.AsInteger() == intValue;
                case StorageType.Double:
                    return NumericText.TryParseDouble(rawValue, out var doubleValue)
                           && Math.Abs(parameter.AsDouble() - doubleValue) < 1e-9;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            readError = ex.Message;
            return false;
        }
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
                    return NumericText.TryParseInt(rawValue, out var intValue) && parameter.Set(intValue);
                case StorageType.Double:
                    // Export ghi bằng InvariantCulture; đọc lại cũng phải Invariant, đồng thời chấp nhận
                    // dấu phẩy thập phân do kỹ sư gõ tay trong Excel tiếng Việt (lỗi #1).
                    return NumericText.TryParseDouble(rawValue, out var doubleValue) && parameter.Set(doubleValue);
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

}
