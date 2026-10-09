using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.ModelCleanup;

/// <summary>
/// Dọn dẹp mô hình: xoá view không đặt trên sheet và sheet rỗng, theo mục 2.1 của tài liệu nghiên cứu
/// (nhóm "Quản lý mô hình / dọn dẹp"). Đây là dạng lệnh điển hình chạy được cả trên Ribbon (kỹ sư xem
/// trước bằng DryRun) lẫn trong batch đêm (DryRun=false, dùng SilentFailuresPreprocessor).
/// <para>
/// Ba lỗi xoá nhầm đã sửa (audit 2026-10-01), cả ba đều xoá thứ ĐANG nằm trên sheet:
/// view chính không có viewport bị xoá kéo theo view phụ thuộc đang trên sheet (quyết định nằm ở
/// <see cref="ViewCleanupPlanner"/>); panel schedule đặt bằng <c>PanelScheduleSheetInstance</c> chứ không bằng
/// viewport nên bị coi là "chưa đặt"; và <c>GetAllPlacedViews</c> không trả schedule (tài liệu API ghi rõ) nên
/// sheet chỉ có bảng thống kê/ghi chú/ảnh bị coi là "rỗng".
/// </para>
/// </summary>
public sealed class RemoveUnusedViewsCommand : ICoreCommand<CleanupConfig>
{
    public string CommandName => "RemoveUnusedViews";

    public CommandResult Execute(Document document, CleanupConfig config)
    {
        var toDelete = new List<ElementId>();
        var report = new List<string>();
        var kept = new List<string>();

        if (config.RemoveUnplacedViews)
        {
            var plan = PlanViews(document, config.KeepViewNameContains);
            toDelete.AddRange(plan.Delete.Select(v => RevitCompat.MakeId(v.Id)));
            report.AddRange(plan.Delete.Select(v => $"View: \"{v.Name}\" (không đặt trên sheet)"));
            kept.AddRange(plan.Kept.Select(k => $"Giữ view \"{k.Key.Name}\": {k.Value}."));
        }

        if (config.RemoveEmptySheets)
        {
            foreach (var sheet in new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                var uncertain = EmptySheetCheck(document, sheet, out var empty);
                if (uncertain != null)
                {
                    kept.Add($"Giữ sheet \"{sheet.SheetNumber} - {sheet.Name}\": không kiểm được nội dung ({uncertain}).");
                }
                else if (empty)
                {
                    toDelete.Add(sheet.Id);
                    report.Add($"Sheet: \"{sheet.SheetNumber} - {sheet.Name}\" (chỉ có khung tên)");
                }
            }
        }

        if (toDelete.Count == 0)
        {
            var none = CommandResult.Ok("Không có view/sheet thừa cần dọn.");
            none.Messages.AddRange(kept);
            return none;
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ xoá {toDelete.Count} phần tử (view thừa + sheet rỗng).", toDelete.Count);
            preview.Messages.AddRange(report);
            preview.Messages.AddRange(kept);
            return preview;
        }

        using var transaction = new RevitTransaction(document, "DHCB - Dọn dẹp view/sheet thừa");
        transaction.Start();
        RevitCompat.ApplyFailurePolicy(transaction);

        document.Delete(toDelete);

        transaction.Commit();

        var result = CommandResult.Ok($"Đã xoá {toDelete.Count} view/sheet thừa.", toDelete.Count);
        result.Messages.AddRange(report);
        result.Messages.AddRange(kept);
        return result;
    }

    /// <summary>
    /// Loại view có thể dọn. Schedule, panel schedule và graphical column schedule nằm trên sheet qua
    /// *SheetInstance chứ không qua viewport — trước đây chỉ loại <c>Schedule</c>, nên panel schedule đang trên
    /// sheet bị coi là "chưa đặt" và bị xoá.
    /// </summary>
    private static bool IsCleanableType(ViewType type) => type is not (ViewType.DrawingSheet or ViewType.Legend
        or ViewType.Schedule or ViewType.PanelSchedule or ViewType.ColumnSchedule
        or ViewType.SystemBrowser or ViewType.ProjectBrowser or ViewType.Internal or ViewType.Undefined);

    private static ViewCleanupPlan PlanViews(Document document, IReadOnlyCollection<string> keepNameContains)
    {
        var placed = PlacedViewIds(document);
        var activeId = ActiveViewId(document);
        var items = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && IsCleanableType(v.ViewType))
            .Select(v =>
            {
                var id = RevitCompat.IdValue(v.Id);
                var isPlaced = placed.Contains(id);
                return new ViewCleanupItem(
                    id,
                    v.Name,
                    isPlaced,
                    v.GetDependentViewIds().Select(RevitCompat.IdValue),
                    isPlaced ? null : id == activeId ? "đang mở trong Revit" : CascadeReason(v));
            })
            .ToList();

        return ViewCleanupPlanner.Plan(items, keepNameContains);
    }

    /// <summary>View có mặt trên sheet: viewport, schedule và panel schedule.</summary>
    private static HashSet<long> PlacedViewIds(Document document)
    {
        var ids = new HashSet<long>(new FilteredElementCollector(document)
            .OfClass(typeof(Viewport)).Cast<Viewport>().Select(v => RevitCompat.IdValue(v.ViewId)));
        ids.UnionWith(new FilteredElementCollector(document)
            .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Select(s => RevitCompat.IdValue(s.ScheduleId)));
        ids.UnionWith(new FilteredElementCollector(document)
            .OfClass(typeof(PanelScheduleSheetInstance)).Cast<PanelScheduleSheetInstance>().Select(s => RevitCompat.IdValue(s.ScheduleId)));
        return ids;
    }

    /// <summary>Revit từ chối xoá view đang mở — cả lệnh rollback vì một view. Batch mở nền thì không có (−1).</summary>
    private static long ActiveViewId(Document document)
    {
        try
        {
            return document.ActiveView is { } active ? RevitCompat.IdValue(active.Id) : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// Chốt chặn thứ hai, không dựa vào hiểu biết của ta về quan hệ cha–con: hỏi chính Revit phần tử nào sẽ bị xoá
    /// CÙNG view này (<c>GetDependentElements</c>), có khung nhìn hay schedule nào đang trên sheet trong đó thì giữ.
    /// Bắt được cả view phụ thuộc lẫn mọi quan hệ khác Revit xoá dây chuyền. Không hỏi được thì giữ (không chắc
    /// thì không xoá — như StylePurge).
    /// </summary>
    private static string? CascadeReason(View view)
    {
        try
        {
            var onSheets = view.GetDependentElements(new ElementMulticlassFilter(new List<Type>
            {
                typeof(Viewport), typeof(ScheduleSheetInstance), typeof(PanelScheduleSheetInstance),
            }));
            return onSheets.Count > 0
                ? $"xoá view này Revit xoá kèm {onSheets.Count} khung nhìn/bảng đang nằm trên sheet"
                : null;
        }
        catch (Exception ex)
        {
            return "không kiểm được phần tử bị xoá kèm (" + ex.Message + ")";
        }
    }

    /// <summary>
    /// Sheet "rỗng" là sheet chỉ còn khung tên: không view, không schedule (trừ bảng revision nằm trong khung tên),
    /// không ghi chú, ảnh, đường nét nào đặt thẳng lên sheet. Trả lý do nếu không kiểm được (khi đó giữ sheet).
    /// </summary>
    private static string? EmptySheetCheck(Document document, ViewSheet sheet, out bool empty)
    {
        empty = false;
        try
        {
            if (sheet.GetAllPlacedViews().Count > 0)
            {
                return null;
            }

            var content = new FilteredElementCollector(document)
                .OwnedByView(sheet.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementCategoryFilter(BuiltInCategory.OST_TitleBlocks, inverted: true))
                .Count(e => !(e is ScheduleSheetInstance s && s.IsTitleblockRevisionSchedule));
            empty = content == 0;
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
