using Autodesk.Revit.DB;

namespace DhcbTools.Core.ModelCleanup;

/// <summary>
/// Dọn dẹp mô hình: xoá view không đặt trên sheet và sheet rỗng, theo mục 2.1 của tài liệu nghiên cứu
/// (nhóm "Quản lý mô hình / dọn dẹp"). Đây là dạng lệnh điển hình chạy được cả trên Ribbon (kỹ sư xem
/// trước bằng DryRun) lẫn trong batch đêm (DryRun=false, dùng SilentFailuresPreprocessor).
/// </summary>
public sealed class RemoveUnusedViewsCommand : ICoreCommand<CleanupConfig>
{
    public string CommandName => "RemoveUnusedViews";

    public CommandResult Execute(Document document, CleanupConfig config)
    {
        var toDelete = new List<ElementId>();
        var report = new List<string>();

        if (config.RemoveUnplacedViews)
        {
            var unplacedViews = FindUnplacedViews(document, config.KeepViewNameContains);
            toDelete.AddRange(unplacedViews.Select(v => v.Id));
            report.AddRange(unplacedViews.Select(v => $"View: \"{v.Name}\" (không đặt trên sheet)"));
        }

        if (config.RemoveEmptySheets)
        {
            var emptySheets = FindEmptySheets(document);
            toDelete.AddRange(emptySheets.Select(s => s.Id));
            report.AddRange(emptySheets.Select(s => $"Sheet: \"{s.SheetNumber} - {s.Name}\" (không có view nào)"));
        }

        if (toDelete.Count == 0)
        {
            return CommandResult.Ok("Không có view/sheet thừa cần dọn.");
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ xoá {toDelete.Count} phần tử (view thừa + sheet rỗng).", toDelete.Count);
            preview.Messages.AddRange(report);
            return preview;
        }

        using var transaction = new Transaction(document, "DHCB - Dọn dẹp view/sheet thừa");
        transaction.Start();
        transaction.SetFailureHandlingOptions(
            transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor()));

        document.Delete(toDelete);

        transaction.Commit();

        var result = CommandResult.Ok($"Đã xoá {toDelete.Count} view/sheet thừa.", toDelete.Count);
        result.Messages.AddRange(report);
        return result;
    }

    private static List<View> FindUnplacedViews(Document document, IReadOnlyCollection<string> keepNameContains)
    {
        var placedViewIds = new FilteredElementCollector(document)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Select(v => v.ViewId)
            .ToHashSet();

        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .Where(v => v.ViewType is not (ViewType.DrawingSheet or ViewType.Legend or ViewType.Schedule or ViewType.SystemBrowser or ViewType.ProjectBrowser or ViewType.Internal or ViewType.Undefined))
            .Where(v => !placedViewIds.Contains(v.Id))
            .Where(v => !keepNameContains.Any(keep => v.Name.IndexOf(keep, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
    }

    private static List<ViewSheet> FindEmptySheets(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(sheet => sheet.GetAllPlacedViews().Count == 0)
            .ToList();
    }
}
