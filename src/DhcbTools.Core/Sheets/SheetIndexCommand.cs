using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Handover;

namespace DhcbTools.Core.Sheets;

/// <summary>Mục 11.3 — danh mục bản vẽ của mô hình ra CSV (và HTML tuỳ chọn), để gói bàn giao có "danh mục bản vẽ" đúng nghĩa.</summary>
public sealed class SheetIndexConfig
{
    /// <summary>File CSV danh mục. Bắt buộc. Tiêu đề cột là hợp đồng với gói bàn giao (<see cref="SheetIndexRow.CsvHeader"/>).</summary>
    public required string OutputPath { get; init; }

    /// <summary>File HTML để in; rỗng = không ghi.</summary>
    public string? HtmlPath { get; init; }

    /// <summary>Chỉ lấy sheet có số chứa chuỗi này; rỗng = mọi sheet.</summary>
    public string? SheetNumberContains { get; init; }

    /// <summary>Bỏ sheet placeholder (không có view, chỉ giữ chỗ trong danh mục). Mặc định bỏ.</summary>
    public bool SkipPlaceholders { get; init; } = true;
}

/// <summary>
/// Liệt kê sheet với revision hiện hành, ngày phát hành, người vẽ/kiểm và số view — chỉ đọc. Không có
/// lệnh này thì "danh mục bản vẽ" trong gói bàn giao là do người gõ tay, và nó lệch với mô hình ngay
/// sau lần sửa đầu tiên.
/// </summary>
public sealed class SheetIndexCommand : ICoreCommand<SheetIndexConfig>
{
    public string CommandName => "SheetIndex";

    public CommandResult Execute(Document document, SheetIndexConfig config)
    {
        var sheets = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => !config.SkipPlaceholders || !s.IsPlaceholder)
            .Where(s => string.IsNullOrEmpty(config.SheetNumberContains)
                        || s.SheetNumber.IndexOf(config.SheetNumberContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var precondition = Precondition.NonEmptyInput(
            CommandName, "sheet trong mô hình", sheets.Count,
            "Mô hình không có sheet nào (hoặc sheetNumberContains lọc hết) — danh mục rỗng không phải là danh mục.");
        var blocked = CommandResult.Ok(string.Empty);
        if (RevitPrecondition.Blocks(precondition, blocked))
        {
            return blocked;
        }

        var rows = new List<SheetIndexRow>();
        foreach (var sheet in sheets)
        {
            var revision = string.Empty;
            var revisionDate = string.Empty;
            var currentId = sheet.GetCurrentRevision();
            if (currentId != ElementId.InvalidElementId && document.GetElement(currentId) is Revision current)
            {
                revision = sheet.GetRevisionNumberOnSheet(currentId);
                if (string.IsNullOrEmpty(revision))
                {
                    revision = current.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrEmpty(current.Description))
                {
                    revision += " — " + current.Description;
                }

                revisionDate = current.RevisionDate ?? string.Empty;
            }

            rows.Add(new SheetIndexRow(
                sheet.SheetNumber,
                sheet.Name,
                revision,
                revisionDate,
                Text(sheet, BuiltInParameter.SHEET_ISSUE_DATE),
                Text(sheet, BuiltInParameter.SHEET_DRAWN_BY),
                Text(sheet, BuiltInParameter.SHEET_CHECKED_BY),
                sheet.GetAllPlacedViews().Count));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(config.OutputPath) ?? ".");
        File.WriteAllText(config.OutputPath, SheetIndexRow.ToCsv(rows), CsvText.Utf8WithBom);

        if (!string.IsNullOrWhiteSpace(config.HtmlPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.HtmlPath!) ?? ".");
            File.WriteAllText(config.HtmlPath!, SheetIndexRow.ToHtml(document.Title, rows), new UTF8Encoding(true));
        }

        var withRevision = rows.Count(r => r.Revision.Length > 0);
        var result = CommandResult.Ok(
            $"Danh mục {rows.Count} sheet ({withRevision} có revision, {rows.Count(r => r.ViewCount == 0)} chưa đặt view) → \"{config.OutputPath}\".",
            rows.Count);
        foreach (var row in rows.Where(r => r.ViewCount == 0).Take(10))
        {
            result.Messages.Add($"{row.Number} \"{row.Name}\": chưa có view nào trên sheet.");
        }

        return result;
    }

    private static string Text(ViewSheet sheet, BuiltInParameter parameter)
    {
        var p = sheet.get_Parameter(parameter);
        return p != null && p.HasValue ? p.AsString() ?? string.Empty : string.Empty;
    }
}
