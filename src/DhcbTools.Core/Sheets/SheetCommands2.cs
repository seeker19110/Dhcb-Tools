using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.Sheets;

/// <summary>P2 — xuất schedule ra CSV (DiRoots SheetLink "by schedules"): đúng cột/hàng đang hiển thị, kể cả header.</summary>
public sealed class ScheduleExportConfig
{
    public required string OutputFolder { get; init; }

    /// <summary>Lọc tên schedule chứa chuỗi (rỗng = tất cả schedule không phải template/keynote/revision).</summary>
    public string? NameContains { get; init; }

    public List<string> Names { get; init; } = new List<string>();

    public bool IncludeHeader { get; init; } = true;
}

public sealed class ScheduleExportCommand : ICoreCommand<ScheduleExportConfig>
{
    public string CommandName => "ScheduleExport";

    public CommandResult Execute(Document document, ScheduleExportConfig config)
    {
        var schedules = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
            .Where(s => config.Names.Count > 0
                ? config.Names.Any(n => string.Equals(n, s.Name, StringComparison.OrdinalIgnoreCase))
                : string.IsNullOrEmpty(config.NameContains) || s.Name.IndexOf(config.NameContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (schedules.Count == 0)
        {
            return CommandResult.Fail("Không có schedule nào khớp bộ lọc.");
        }

        Directory.CreateDirectory(config.OutputFolder);
        var result = CommandResult.Ok(string.Empty);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        foreach (var s in schedules)
        {
            try
            {
                var table = s.GetTableData();
                var sb = new StringBuilder();
                var rows = 0;
                if (config.IncludeHeader)
                {
                    rows += Append(s, table.GetSectionData(SectionType.Header), SectionType.Header, sb);
                }
                rows += Append(s, table.GetSectionData(SectionType.Body), SectionType.Body, sb);
                var file = Path.Combine(config.OutputFolder, FileNaming.MakeUnique(s.Name, used) + ".csv");
                File.WriteAllText(file, sb.ToString(), CsvText.Utf8WithBom);
                result.Messages.Add($"{s.Name}: {rows} hàng → {Path.GetFileName(file)}");
                done++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{s.Name}: {ex.Message}");
            }
        }

        result.Summary = $"Đã xuất {done}/{schedules.Count} schedule → \"{config.OutputFolder}\".";
        result.AffectedCount = done;
        return result;
    }

    private static int Append(ViewSchedule s, TableSectionData section, SectionType type, StringBuilder sb)
    {
        if (section == null) return 0;
        var rows = 0;
        for (var r = section.FirstRowNumber; r <= section.LastRowNumber; r++)
        {
            var cells = new List<string>();
            for (var c = section.FirstColumnNumber; c <= section.LastColumnNumber; c++)
            {
                cells.Add(s.GetCellText(type, r, c));
            }
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            sb.Append(CsvText.JoinLine(cells)).Append('\n');
            rows++;
        }
        return rows;
    }
}

/// <summary>
/// P2 — copy viewport legend/schedule từ một sheet sang nhiều sheet (pyRevit "Copy viewports"): chỉ legend và schedule
/// đặt được trên nhiều sheet; view thường Revit không cho — tool báo bỏ qua thay vì lỗi.
/// </summary>
public sealed class ViewportCopyConfig
{
    public required string SourceSheetNumber { get; init; }

    public List<string> TargetSheetNumbers { get; init; } = new List<string>();

    public string? TargetSheetContains { get; init; }

    /// <summary>Ghim toàn bộ viewport trên sheet đích sau khi copy.</summary>
    public bool PinAfterCopy { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

public sealed class ViewportCopyCommand : ICoreCommand<ViewportCopyConfig>
{
    public string CommandName => "ViewportCopy";

    public CommandResult Execute(Document document, ViewportCopyConfig config)
    {
        var sheets = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
        var source = sheets.FirstOrDefault(s => string.Equals(s.SheetNumber, config.SourceSheetNumber, StringComparison.OrdinalIgnoreCase));
        if (source == null) return CommandResult.Fail($"Không có sheet {config.SourceSheetNumber}.");

        var targets = sheets.Where(s => s.Id != source.Id && (config.TargetSheetNumbers.Count > 0
                ? config.TargetSheetNumbers.Any(n => string.Equals(n, s.SheetNumber, StringComparison.OrdinalIgnoreCase))
                : !string.IsNullOrEmpty(config.TargetSheetContains) && s.SheetNumber.IndexOf(config.TargetSheetContains!, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count == 0) return CommandResult.Fail("Không có sheet đích (targetSheetNumbers hoặc targetSheetContains).");

        var viewports = new FilteredElementCollector(document, source.Id).OfClass(typeof(Viewport)).Cast<Viewport>().ToList();
        var schedules = new FilteredElementCollector(document, source.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Where(i => !i.IsTitleblockRevisionSchedule).ToList();

        var result = CommandResult.Ok(string.Empty);
        var legends = viewports.Where(v => document.GetElement(v.ViewId) is View { ViewType: ViewType.Legend }).ToList();
        foreach (var v in viewports.Except(legends))
        {
            result.Messages.Add($"Bỏ qua viewport \"{document.GetElement(v.ViewId)?.Name}\": view thường chỉ đặt được trên một sheet.");
        }

        var planned = targets.Count * (legends.Count + schedules.Count);
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ copy {legends.Count} legend + {schedules.Count} schedule lên {targets.Count} sheet ({planned} viewport).";
            result.Messages.AddRange(targets.Select(t => t.SheetNumber + " - " + t.Name));
            result.AffectedCount = planned;
            return result;
        }

        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Copy viewport");
        foreach (var t in targets)
        {
            foreach (var lg in legends)
            {
                try
                {
                    if (!Viewport.CanAddViewToSheet(document, t.Id, lg.ViewId)) { result.Messages.Add($"{t.SheetNumber}: legend đã có."); continue; }
                    var vp = Viewport.Create(document, t.Id, lg.ViewId, lg.GetBoxCenter());
                    try { vp.ChangeTypeId(lg.GetTypeId()); } catch { }
                    if (config.PinAfterCopy) vp.Pinned = true;
                    done++;
                }
                catch (Exception ex) { result.Errors.Add($"{t.SheetNumber} legend: {ex.Message}"); }
            }
            foreach (var sc in schedules)
            {
                try
                {
                    var inst = ScheduleSheetInstance.Create(document, t.Id, sc.ScheduleId, sc.Point);
                    if (config.PinAfterCopy) inst.Pinned = true;
                    done++;
                }
                catch (Exception ex) { result.Errors.Add($"{t.SheetNumber} schedule: {ex.Message}"); }
            }
        }
        tx.Commit();
        result.Summary = $"Đã copy {done}/{planned} viewport lên {targets.Count} sheet.";
        result.AffectedCount = done;
        return result;
    }
}
