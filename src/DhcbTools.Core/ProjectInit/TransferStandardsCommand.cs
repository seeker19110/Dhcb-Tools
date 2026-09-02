using Autodesk.Revit.DB;

namespace DhcbTools.Core.ProjectInit;

/// <summary>Mục 2.2 — chuyển chuẩn từ file mẫu sang file hiện tại.</summary>
public sealed class TransferStandardsConfig
{
    public required string SourcePath { get; init; }

    /// <summary>ViewTemplates, Filters, LineStyles, ObjectStyles, Materials, TextTypes, DimensionTypes, FillPatterns, BrowserOrganization.</summary>
    public List<string> Categories { get; init; } = new List<string> { "ViewTemplates", "Filters", "LineStyles", "Materials", "TextTypes", "DimensionTypes" };

    /// <summary>Chỉ chuyển phần tử có tên chứa chuỗi này (rỗng = tất cả).</summary>
    public string? NameContains { get; init; }

    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Dùng <c>ElementTransformUtils.CopyElements</c> theo từng nhóm; trùng tên → giữ bản đích
/// (<see cref="DuplicateTypeAction.UseDestinationTypes"/>) và ghi rõ từng cái bị bỏ qua.
/// </summary>
public sealed class TransferStandardsCommand : ICoreCommand<TransferStandardsConfig>
{
    public string CommandName => "TransferStandards";

    private sealed class KeepDestination : IDuplicateTypeNamesHandler
    {
        public List<string> Duplicates { get; } = new List<string>();

        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            Duplicates.Add("Trùng tên type — giữ bản đích.");
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }

    public CommandResult Execute(Document document, TransferStandardsConfig config)
    {
        if (!File.Exists(config.SourcePath))
        {
            return CommandResult.Fail($"Không tìm thấy file chuẩn \"{config.SourcePath}\".");
        }

        if (string.Equals(Path.GetFullPath(config.SourcePath), document.PathName, StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail("File chuẩn chính là file đang mở.");
        }

        var result = CommandResult.Ok(string.Empty);
        Document? source = null;
        try
        {
            source = document.Application.OpenDocumentFile(config.SourcePath);
            var groups = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in config.Categories)
            {
                var ids = Collect(source, cat, config.NameContains, out var note);
                if (note != null)
                {
                    result.Messages.Add(note);
                }
                if (ids.Count > 0)
                {
                    groups[cat] = ids;
                }
            }

            // Loại phần tử đã có cùng tên ở đích (CopyElements sẽ tạo bản "… 1" cho view template/filter trùng tên).
            var destNames = new FilteredElementCollector(document).WhereElementIsElementType().Cast<Element>().Select(e => e.Name)
                .Concat(new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate).Select(v => v.Name))
                .Concat(new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement)).Select(f => f.Name))
                .Concat(new FilteredElementCollector(document).OfClass(typeof(Material)).Select(m => m.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var total = 0;
            foreach (var g in groups.ToList())
            {
                var keep = new List<ElementId>();
                foreach (var id in g.Value)
                {
                    var name = source.GetElement(id)?.Name ?? string.Empty;
                    if (destNames.Contains(name))
                    {
                        result.Messages.Add($"[{g.Key}] \"{name}\" đã có ở đích — bỏ qua.");
                    }
                    else
                    {
                        keep.Add(id);
                    }
                }
                groups[g.Key] = keep;
                total += keep.Count;
            }

            if (config.DryRun)
            {
                result.Summary = $"[Xem trước] Sẽ chuyển {total} phần tử chuẩn từ \"{Path.GetFileName(config.SourcePath)}\".";
                foreach (var g in groups)
                {
                    result.Messages.Add($"{g.Key}: {g.Value.Count}");
                }
                result.AffectedCount = total;
                return result;
            }

            var copied = 0;
            var handler = new KeepDestination();
            var options = new CopyPasteOptions();
            options.SetDuplicateTypeNamesHandler(handler);

            using var tx = RevitCompat.StartTransaction(document, "DHCB - Transfer standards");
            foreach (var g in groups.Where(g => g.Value.Count > 0))
            {
                try
                {
                    var newIds = ElementTransformUtils.CopyElements(source, g.Value, document, Transform.Identity, options);
                    copied += newIds.Count;
                    result.Messages.Add($"{g.Key}: chuyển {newIds.Count}.");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{g.Key}: {ex.Message}");
                }
            }
            tx.Commit();

            result.Summary = $"Đã chuyển {copied} phần tử chuẩn ({handler.Duplicates.Count} trùng type giữ bản đích).";
            result.AffectedCount = copied;
            return result;
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("Lỗi: " + ex.Message, result.Messages);
        }
        finally
        {
            try { source?.Close(false); } catch { /* ignore */ }
        }
    }

    private static List<ElementId> Collect(Document src, string category, string? nameContains, out string? note)
    {
        note = null;
        IEnumerable<Element> elements;
        switch (category.Trim().ToUpperInvariant())
        {
            case "VIEWTEMPLATES":
                elements = new FilteredElementCollector(src).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate);
                break;
            case "FILTERS":
                elements = new FilteredElementCollector(src).OfClass(typeof(ParameterFilterElement));
                break;
            case "LINESTYLES":
                var linesCat = src.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                elements = linesCat.SubCategories.Cast<Category>().Select(c => src.GetElement(c.Id)).Where(e => e != null)!;
                if (!elements.Any())
                {
                    note = "LineStyles: API không cho copy trực tiếp subcategory ở phiên bản này — dùng Transfer Project Standards tay.";
                }
                break;
            case "OBJECTSTYLES":
                note = "ObjectStyles: không copy được qua CopyElements — dùng Transfer Project Standards tay.";
                elements = Enumerable.Empty<Element>();
                break;
            case "MATERIALS":
                elements = new FilteredElementCollector(src).OfClass(typeof(Material));
                break;
            case "TEXTTYPES":
                elements = new FilteredElementCollector(src).OfClass(typeof(TextNoteType));
                break;
            case "DIMENSIONTYPES":
                elements = new FilteredElementCollector(src).OfClass(typeof(DimensionType));
                break;
            case "FILLPATTERNS":
                elements = new FilteredElementCollector(src).OfClass(typeof(FillPatternElement));
                break;
            case "BROWSERORGANIZATION":
                elements = new FilteredElementCollector(src).OfClass(typeof(BrowserOrganization));
                break;
            default:
                note = $"Nhóm \"{category}\" không hỗ trợ — bỏ qua.";
                elements = Enumerable.Empty<Element>();
                break;
        }

        return elements
            .Where(e => string.IsNullOrEmpty(nameContains) || (e.Name ?? string.Empty).IndexOf(nameContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(e => e.Id)
            .ToList();
    }
}
