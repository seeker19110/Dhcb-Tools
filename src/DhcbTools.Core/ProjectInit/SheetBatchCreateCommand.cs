using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.ProjectInit;

/// <summary>Mục 2.4 — tạo sheet hàng loạt từ CSV <c>SheetNumber,SheetName,TitleBlockType,ViewsToPlace</c>.</summary>
public sealed class SheetBatchCreateConfig
{
    public required string InputPath { get; init; }

    /// <summary>Title block mặc định khi cột TitleBlockType trống ("Family: Type" hoặc Type).</summary>
    public string? DefaultTitleBlockType { get; init; }

    /// <summary>"center" hoặc "x,y" (mm từ gốc sheet). Nhiều view thì xếp ngang cách nhau <see cref="ViewGapMm"/>.</summary>
    public string Placement { get; init; } = "center";

    public double ViewGapMm { get; init; } = 20;

    /// <summary>Dấu phân cách trong cột ViewsToPlace.</summary>
    public string ViewSeparator { get; init; } = ";";

    public bool DryRun { get; init; } = true;
}

public sealed class SheetBatchCreateCommand : ICoreCommand<SheetBatchCreateConfig>
{
    public string CommandName => "SheetBatchCreate";

    private sealed record Row(string Number, string Name, string TitleBlock, List<string> Views, int Line);

    public CommandResult Execute(Document document, SheetBatchCreateConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy \"{config.InputPath}\".");
        }

        var result = CommandResult.Ok(string.Empty);
        // RFC 4180 qua CsvText.ReadRecords: tên sheet có dấu phẩy/xuống dòng không còn làm lệch cột.
        var records = CsvText.ReadRecords(config.InputPath).ToList();
        var rows = new List<Row>();
        for (var i = 1; i < records.Count; i++)
        {
            var c = records[i];
            if (c.All(string.IsNullOrWhiteSpace)) continue;
            if (c.Length < 2 || string.IsNullOrWhiteSpace(c[0]))
            {
                result.Messages.Add($"Dòng {i + 1}: thiếu SheetNumber/SheetName — bỏ qua.");
                continue;
            }

            var views = c.Length > 3 ? c[3].Split(new[] { config.ViewSeparator }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0).ToList() : new List<string>();
            rows.Add(new Row(c[0].Trim(), c[1].Trim(), c.Length > 2 ? c[2].Trim() : string.Empty, views, i + 1));
        }

        var existingNumbers = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var titleBlocks = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<FamilySymbol>().ToList();
        var allViews = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v.CanBePrinted).ToList();

        var plan = new List<(Row Row, FamilySymbol? TitleBlock, List<View> Views)>();
        foreach (var r in rows)
        {
            if (existingNumbers.Contains(r.Number))
            {
                result.Messages.Add($"Sheet {r.Number} đã tồn tại — bỏ qua.");
                continue;
            }

            var tbName = string.IsNullOrEmpty(r.TitleBlock) ? config.DefaultTitleBlockType : r.TitleBlock;
            var tb = string.IsNullOrEmpty(tbName) ? titleBlocks.FirstOrDefault() : titleBlocks.FirstOrDefault(t => Matches(t, tbName!));
            if (tb == null)
            {
                result.Messages.Add($"Sheet {r.Number}: không tìm thấy title block \"{tbName}\" — bỏ qua.");
                continue;
            }

            var views = new List<View>();
            foreach (var vn in r.Views)
            {
                var v = allViews.FirstOrDefault(x => string.Equals(x.Name, vn, StringComparison.OrdinalIgnoreCase));
                if (v == null)
                {
                    result.Messages.Add($"Sheet {r.Number}: không có view \"{vn}\".");
                    continue;
                }
                views.Add(v);
            }

            plan.Add((r, tb, views));
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ tạo {plan.Count} sheet, đặt {plan.Sum(p => p.Views.Count)} view.";
            result.Messages.AddRange(plan.Select(p => $"{p.Row.Number} - {p.Row.Name} [{p.TitleBlock!.Name}] views: {string.Join(", ", p.Views.Select(v => v.Name))}"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var created = 0;
        var placed = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Tạo sheet hàng loạt");
        foreach (var (row, tb, views) in plan)
        {
            try
            {
                if (!tb!.IsActive) tb.Activate();
                var sheet = ViewSheet.Create(document, tb.Id);
                sheet.SheetNumber = row.Number;
                sheet.Name = row.Name;
                created++;

                var origin = PlacementPoint(document, sheet, config);
                var cursor = origin;
                foreach (var v in views)
                {
                    if (!Viewport.CanAddViewToSheet(document, sheet.Id, v.Id))
                    {
                        result.Messages.Add($"Sheet {row.Number}: view \"{v.Name}\" đã nằm trên sheet khác — bỏ qua.");
                        continue;
                    }

                    var vp = Viewport.Create(document, sheet.Id, v.Id, cursor);
                    placed++;
                    var box = vp.GetBoxOutline();
                    cursor = new XYZ(cursor.X + (box.MaximumPoint.X - box.MinimumPoint.X) + RevitCompat.MmToFt(config.ViewGapMm), cursor.Y, 0);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Sheet {row.Number}: {ex.Message}");
            }
        }

        tx.Commit();
        result.Summary = $"Đã tạo {created}/{plan.Count} sheet, đặt {placed} view.";
        result.AffectedCount = created;
        return result;
    }

    private static bool Matches(FamilySymbol s, string name)
        => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
           || string.Equals(s.FamilyName + ": " + s.Name, name, StringComparison.OrdinalIgnoreCase)
           || string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase);

    private static XYZ PlacementPoint(Document doc, ViewSheet sheet, SheetBatchCreateConfig config)
    {
        if (!string.Equals(config.Placement, "center", StringComparison.OrdinalIgnoreCase))
        {
            var parts = config.Placement.Split(',');
            if (parts.Length == 2 && NumericText.TryParseDouble(parts[0], out var x) && NumericText.TryParseDouble(parts[1], out var y))
            {
                return new XYZ(RevitCompat.MmToFt(x), RevitCompat.MmToFt(y), 0);
            }
        }

        var outline = sheet.Outline;
        return new XYZ((outline.Min.U + outline.Max.U) / 2, (outline.Min.V + outline.Max.V) / 2, 0);
    }
}
