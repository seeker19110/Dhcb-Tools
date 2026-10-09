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

        // RFC 4180 qua CsvText.ReadRecords — ô Comments/Description nhiều dòng do ParameterExport ghi ra
        // trước đây bị ReadAllLines cắt thành hai bản ghi hỏng.
        var rows = CsvText.ReadRecords(config.InputPath).ToList();
        if (rows.Count < 2)
        {
            return CommandResult.Fail("File CSV không có dữ liệu (chỉ có dòng tiêu đề hoặc rỗng).");
        }

        var header = rows[0].ToList();
        // 3 cột đầu cố định: ElementId, Category, Name — phần còn lại là tên tham số.
        var parameterColumns = header.Skip(3).ToList();
        var result = CommandResult.Ok(string.Empty);

        // Lượt 1 — chỉ ĐỌC: so mọi ô với giá trị của mô hình TRƯỚC khi nhập. Bản cũ ghi ngay từng dòng và so với giá
        // trị hiện tại, nên tham số type (lặp ở mọi dòng cùng type) bị dòng sau ghi đè lại giá trị cũ — xem
        // ParameterImportPlanner.
        var cells = new List<ParameterCell>();
        var targets = new Dictionary<(long, string), Parameter>();
        for (var i = 1; i < rows.Count; i++)
        {
            var record = rows[i];
            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (record.Length < 3 || !long.TryParse(record[0], out var idValue))
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: ElementId không hợp lệ.");
                continue;
            }

            // ElementId dùng long từ Revit 2024, int trước đó — RevitCompat.MakeId tách theo
            // REVIT2024_OR_GREATER. (Trước đây chỗ này tự tách bằng #if NET8_0_WINDOWS, một symbol
            // không bao giờ được định nghĩa — TFM net8.0-windows sinh ra NET8_0_WINDOWS7_0 — nên
            // nhánh long luôn thắng và Revit ≤ 2023 không biên dịch được.)
            var element = document.GetElement(RevitCompat.MakeId(idValue));
            if (element is null)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: không tìm thấy phần tử {idValue} trong mô hình.");
                continue;
            }

            for (var col = 0; col < parameterColumns.Count; col++)
            {
                var cellIndex = 3 + col;
                if (cellIndex >= record.Length)
                {
                    continue;
                }

                var name = parameterColumns[col];
                var owner = element;
                var typeLevel = false;
                var parameter = RevitCompat.LookupInstance(element, name, name);
                if (parameter is null)
                {
                    typeLevel = true;
                    // Export có fallback đọc tham số ở Type; import phải đối xứng, nếu không thì tham số
                    // Type xuất ra được mà sửa xong không nhập lại được và không có cảnh báo nào (lỗi #3).
                    owner = document.GetElement(element.GetTypeId());
                    parameter = owner?.LookupParameter(name);
                }

                if (parameter is null || owner is null)
                {
                    result.Messages.Add($"Bỏ qua dòng {i + 1}, cột \"{name}\": phần tử {idValue} không có tham số này.");
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    result.Messages.Add($"Bỏ qua dòng {i + 1}, cột \"{name}\": tham số chỉ đọc.");
                    continue;
                }

                var unchanged = IsUnchanged(parameter, record[cellIndex], out var readError);
                if (readError != null)
                {
                    // Trước đây lỗi đọc tham số bị catch rỗng và coi là "đã đổi" rồi ghi đè im lặng —
                    // nay báo rõ và vẫn thử ghi (TrySetParameter có catch/báo lỗi riêng của nó).
                    result.Messages.Add($"Dòng {i + 1}, cột \"{name}\": không đọc được giá trị hiện tại ({readError}), vẫn thử ghi.");
                }

                var ownerId = RevitCompat.IdValue(owner.Id);
                targets[(ownerId, name)] = parameter;
                cells.Add(new ParameterCell(i + 1, ownerId, name, record[cellIndex], unchanged, typeLevel));
            }
        }

        var plan = ParameterImportPlanner.Plan(cells);

        // Lượt 2 — ghi đúng các ô kế hoạch chọn, mỗi (phần tử, tham số) tối đa một lần. Xem trước cũng ghi thật trong
        // transaction rồi rollback, để "sẽ cập nhật N" chỉ đếm những giá trị Revit nhận.
        var updated = 0;
        using var transaction = new RevitTransaction(document, "DHCB - Nhập tham số từ CSV");
        transaction.Start();
        RevitCompat.ApplyFailurePolicy(transaction);

        foreach (var write in plan.Writes)
        {
            var parameter = targets[(write.ElementId, write.Parameter)];
            if (TrySetParameter(parameter, write.Value))
            {
                updated++;
            }
            else
            {
                result.Messages.Add(
                    $"Bỏ qua dòng {write.Row}, cột \"{write.Parameter}\": không ghi được giá trị \"{write.Value}\" ({parameter.StorageType}).");
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
        if (plan.Unchanged > 0)
        {
            final.Messages.Add($"{plan.Unchanged} ô giữ nguyên vì giá trị trong CSV trùng với mô hình (hoặc là bản sao giá trị cũ của tham số type đã đổi ở dòng khác).");
        }

        final.Messages.AddRange(plan.Notes);
        final.Errors.AddRange(plan.Conflicts);
        if (plan.Conflicts.Count > 0)
        {
            // Đã ghi được phần không xung đột nhưng KHÔNG phải tất cả — báo cáo batch/Bridge không được gọi là "xanh".
            final.PartialSuccess = true;
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
