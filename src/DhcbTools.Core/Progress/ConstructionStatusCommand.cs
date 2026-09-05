using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Progress;

namespace DhcbTools.Core.Progress;

/// <summary>
/// Đề xuất B1 (<c>docs/nghien-cuu-chuoi-den-hoan-cong.md</c>): ghi trạng thái lắp đặt/nghiệm thu do
/// hiện trường báo về vào chính mô hình, thay cho một file Excel rời sống song song với mô hình.
/// </summary>
public sealed class ConstructionStatusConfig
{
    /// <summary>CSV hiện trường: cột mã cấu kiện + trạng thái, tuỳ chọn ngày / người xác nhận / ghi chú.</summary>
    public required string InputPath { get; init; }

    /// <summary>Tên tham số trạng thái của dự án; rỗng = tra theo từ điển khoá <c>constructionStatus</c>.</summary>
    public string? StatusParameter { get; init; }

    /// <summary>
    /// Tham số dùng làm <b>mã cấu kiện</b> trong CSV (Mark, số hiệu cấu kiện…); rỗng = cột mã là
    /// ElementId như trước.
    /// <para>
    /// Vì sao cần: ElementId chỉ có nghĩa trong đúng file sinh ra nó, nên bảng nghiệm thu của hiện
    /// trường — vốn ghi "D-102" — không dùng được, và mỗi lần phát hành lại mô hình là phải xuất lại
    /// danh sách. Khớp theo tham số đánh dấu thì file của hiện trường sống lâu hơn một bản phát hành.
    /// </para>
    /// </summary>
    public string? KeyParameter { get; init; }

    /// <summary>Category để tìm phần tử khi khớp theo <see cref="KeyParameter"/>; rỗng = toàn mô hình.</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Tham số ngày (kiểu Text); rỗng = từ điển <c>constructionDate</c>. Bỏ qua nếu mô hình không có.</summary>
    public string? DateParameter { get; init; }

    /// <summary>Tham số người xác nhận; rỗng = từ điển <c>constructionBy</c>. Bỏ qua nếu mô hình không có.</summary>
    public string? PersonParameter { get; init; }

    /// <summary>Tham số ghi chú; rỗng = từ điển <c>comments</c>. Chỉ ghi khi CSV có cột ghi chú.</summary>
    public string? NoteParameter { get; init; }

    /// <summary>Dạng ngày ghi vào tham số Text.</summary>
    public string DateFormat { get; init; } = "dd/MM/yyyy";

    /// <summary>
    /// Cho phép lùi trạng thái (đã nghiệm thu → đang lắp). Mặc định <b>tắt</b>: lùi trạng thái thường là
    /// dấu hiệu file CSV cũ bị nhập đè, và nó xoá mất một mốc nghiệm thu đã ghi nhận.
    /// </summary>
    public bool AllowDowngrade { get; init; }

    public bool DryRun { get; init; } = true;
}

public sealed class ConstructionStatusCommand : ICoreCommand<ConstructionStatusConfig>
{
    public string CommandName => "ConstructionStatus";

    public CommandResult Execute(Document document, ConstructionStatusConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"E-PATH-MISSING: không tìm thấy file CSV \"{config.InputPath}\".");
        }

        var byKey = !string.IsNullOrWhiteSpace(config.KeyParameter);
        var csv = ProgressCsv.Read(
            CsvText.ReadRecords(config.InputPath).Select(r => r.ToArray()),
            byKey ? ProgressCsvKey.Text : ProgressCsvKey.ElementId);
        if (!csv.Ok)
        {
            return CommandResult.Fail(csv.FatalError);
        }

        var precondition = Precondition.NonEmptyInput(
            CommandName, "dòng trạng thái đọc được trong CSV", csv.Rows.Count,
            "Kiểm lại file: mỗi dòng cần mã cấu kiện là số và một ô trạng thái hợp lệ.");
        var empty = CommandResult.Ok(string.Empty);
        if (RevitPrecondition.Blocks(precondition, empty))
        {
            foreach (var error in csv.Errors.Take(50))
            {
                empty.Errors.Add(error);
            }

            return empty;
        }

        var result = CommandResult.Ok(string.Empty);
        foreach (var error in csv.Errors.Take(50))
        {
            result.Messages.Add(error);
        }

        Dictionary<string, List<Element>>? keyIndex = null;
        if (byKey)
        {
            ICollection<ElementId> categoryIds = new List<ElementId>();
            if (config.Categories.Count > 0)
            {
                categoryIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
                if (unknown.Count > 0)
                {
                    return CommandResult.Fail("Category không có: " + string.Join(", ", unknown) + ".");
                }
            }

            var collector = new FilteredElementCollector(document).WhereElementIsNotElementType();
            var scope = categoryIds.Count > 0
                ? collector.WherePasses(new ElementMulticategoryFilter(categoryIds.ToList())).ToElements()
                : collector.ToElements();

            keyIndex = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in scope)
            {
                var parameter = RevitCompat.LookupInstance(element, "constructionKey", config.KeyParameter);
                var value = parameter?.AsString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!keyIndex.TryGetValue(value!.Trim(), out var list))
                {
                    list = new List<Element>();
                    keyIndex[value.Trim()] = list;
                }

                list.Add(element);
            }

            // Không phần tử nào MANG tham số khoá = tra sai tên tham số, không phải "mô hình không có
            // cấu kiện nào" — hai câu đó dẫn kỹ sư đi hai hướng khác hẳn nhau.
            if (keyIndex.Count == 0)
            {
                return CommandResult.Fail(
                    $"Không phần tử nào trong phạm vi có giá trị ở tham số khoá \"{config.KeyParameter}\". "
                    + "Kiểm lại tên tham số (keyParameter) và phạm vi (categories); "
                    + "đánh số cấu kiện trước bằng AutoNumbering nếu tham số còn trống.");
            }
        }

        var written = 0;
        var unchanged = 0;
        var missingElement = 0;
        var downgradeBlocked = 0;
        var noStatusParameter = new List<long>();
        var dateSkipped = 0;
        var personSkipped = 0;

        using var transaction = RevitCompat.StartTransaction(document, "DHCB - Ghi trạng thái thi công");

        foreach (var row in csv.Rows)
        {
            var matches = new List<Element>();
            if (keyIndex != null)
            {
                if (keyIndex.TryGetValue(row.Key, out var found))
                {
                    matches.AddRange(found);
                }
            }
            else
            {
                var byId = document.GetElement(RevitCompat.MakeId(row.ElementId));
                if (byId != null)
                {
                    matches.Add(byId);
                }
            }

            if (matches.Count == 0)
            {
                missingElement++;
                if (missingElement <= 20)
                {
                    result.Messages.Add($"Dòng {row.Line}: không có phần tử {(keyIndex != null ? "\"" + row.Key + "\"" : row.ElementId.ToString())} trong mô hình.");
                }

                continue;
            }

            // Một mã khớp nhiều phần tử là chuyện có thật (Mark trùng giữa hai category): ghi cho TẤT CẢ
            // và nói ra, chứ không im lặng chọn cái đầu tiên.
            if (matches.Count > 1)
            {
                result.Messages.Add($"Dòng {row.Line}: mã \"{row.Key}\" khớp {matches.Count} phần tử — ghi cho cả {matches.Count}.");
            }

            foreach (var element in matches)
            {
                var label = keyIndex != null ? "\"" + row.Key + "\"" : row.ElementId.ToString();
                var statusParameter = RevitCompat.LookupInstance(element, "constructionStatus", config.StatusParameter);
                if (statusParameter == null)
                {
                    noStatusParameter.Add(RevitCompat.IdValue(element.Id));
                    continue;
                }

                if (statusParameter.IsReadOnly || statusParameter.StorageType != StorageType.String)
                {
                    result.Messages.Add($"Dòng {row.Line}: tham số trạng thái của phần tử {label} "
                        + (statusParameter.IsReadOnly ? "chỉ đọc (E-PARAM-READONLY)." : "không phải kiểu Text."));
                    continue;
                }

                var current = statusParameter.AsString() ?? string.Empty;
                ConstructionStatusValue.TryParse(current, out var currentStage);
                if (!config.AllowDowngrade && currentStage > row.Stage)
                {
                    downgradeBlocked++;
                    if (downgradeBlocked <= 20)
                    {
                        result.Messages.Add($"Dòng {row.Line}: phần tử {label} đang là \"{current}\", "
                            + $"CSV ghi \"{row.StatusText}\" — lùi trạng thái nên bỏ qua. Đặt allowDowngrade: true nếu đúng là muốn sửa lại.");
                    }

                    continue;
                }

                if (string.Equals(current, row.StatusText, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                if (!config.DryRun)
                {
                    statusParameter.Set(row.StatusText);

                    if (row.Date != null)
                    {
                        var text = row.Date.Value.ToString(config.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
                        if (!TrySetText(element, "constructionDate", config.DateParameter, text))
                        {
                            dateSkipped++;
                        }
                    }

                    if (row.Person.Length > 0 && !TrySetText(element, "constructionBy", config.PersonParameter, row.Person))
                    {
                        personSkipped++;
                    }

                    if (row.Note.Length > 0)
                    {
                        TrySetText(element, "comments", config.NoteParameter, row.Note);
                    }
                }

                written++;
                result.WithChanged(RevitCompat.IdValue(element.Id));
            }
        }

        // Không mã nào khớp phần tử nào = file của mô hình khác (hay của bản sao đã đổi id), không phải
        // "không có gì để cập nhật". Trả 0 và báo thành công ở đây là đúng loại no-op im lặng mà E-PRECOND
        // sinh ra để chặn.
        if (missingElement == csv.Rows.Count)
        {
            transaction.RollBack();
            var precondFail = CommandResult.Ok(string.Empty);
            RevitPrecondition.Blocks(
                Precondition.NonEmptyInput(
                    CommandName, $"phần tử khớp mã cấu kiện trong CSV (đã thử {csv.Rows.Count} mã)", 0,
                    "File CSV này có thể của mô hình khác: mã cấu kiện là ElementId của đúng file đang mở. "
                        + "Xuất lại danh sách bằng ParameterExport trên chính file này rồi ghi trạng thái vào đó."),
                precondFail);
            return precondFail;
        }

        // Không phần tử nào có tham số trạng thái = tra sai tên, không phải "mô hình chưa lắp gì".
        if (written == 0 && unchanged == 0 && noStatusParameter.Count > 0)
        {
            transaction.RollBack();
            return CommandResult.Fail(
                RevitCompat.LookupFailed("constructionStatus", config.StatusParameter)
                + $" {noStatusParameter.Count} phần tử trong CSV đều không có tham số này — "
                + "thêm shared parameter cho category tương ứng, hoặc chạy DictionaryLearn để lấy tên thật của dự án.");
        }

        if (config.DryRun)
        {
            transaction.RollBack();
        }
        else
        {
            transaction.Commit();
        }

        result.Summary = (config.DryRun ? "[Xem trước] " : string.Empty)
            + $"{written} phần tử {(config.DryRun ? "sẽ đổi" : "đã đổi")} trạng thái thi công"
            + (unchanged > 0 ? $", {unchanged} đã đúng sẵn" : string.Empty)
            + $" (đọc {csv.Rows.Count} dòng từ \"{config.InputPath}\").";
        result.AffectedCount = written;

        foreach (var group in csv.Rows.GroupBy(r => r.StatusText).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Messages.Add($"{group.Key}: {group.Count()} dòng");
        }

        if (noStatusParameter.Count > 0)
        {
            result.Messages.Add($"{noStatusParameter.Count} phần tử không có tham số trạng thái, đã bỏ qua: "
                + string.Join(", ", noStatusParameter.Take(20)) + (noStatusParameter.Count > 20 ? ", …" : string.Empty));
        }

        if (missingElement > 20)
        {
            result.Messages.Add($"… tổng {missingElement} mã cấu kiện không có trong mô hình.");
        }

        if (downgradeBlocked > 20)
        {
            result.Messages.Add($"… tổng {downgradeBlocked} dòng bị bỏ qua vì lùi trạng thái.");
        }

        if (dateSkipped > 0)
        {
            result.Messages.Add($"{dateSkipped} phần tử không ghi được ngày ({RevitCompat.LookupFailed("constructionDate", config.DateParameter)}).");
        }

        if (personSkipped > 0)
        {
            result.Messages.Add($"{personSkipped} phần tử không ghi được người xác nhận ({RevitCompat.LookupFailed("constructionBy", config.PersonParameter)}).");
        }

        if (csv.Errors.Count > 50)
        {
            result.Messages.Add($"… và {csv.Errors.Count - 50} dòng lỗi nữa trong CSV.");
        }

        return result;
    }

    private static bool TrySetText(Element element, string key, string? preferred, string value)
    {
        var parameter = RevitCompat.LookupInstance(element, key, preferred);
        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
        {
            return false;
        }

        parameter.Set(value);
        return true;
    }
}
