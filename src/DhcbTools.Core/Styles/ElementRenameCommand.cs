using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.Styles;

/// <summary>
/// Đổi tên hàng loạt view template, filter, type (kể cả loại ống/ống gió/máng cáp) và material theo mẫu token + tìm/thay.
/// Bổ sung cho <c>SheetRename</c> (sheet, view thường) và <c>FamilyAudit</c> (family).
/// </summary>
public sealed class ElementRenameConfig
{
    /// <summary>ViewTemplates | Filters | Types | Materials.</summary>
    public required string Kind { get; init; }

    /// <summary>Mẫu tên mới. Token: {Name} {Category} {Family} {ViewType} {n} {n:000}. Mặc định giữ tên rồi áp find/replace.</summary>
    public string NamePattern { get; init; } = "{Name}";

    /// <summary>Chuỗi/regex tìm trên tên sau khi áp mẫu.</summary>
    public string? Find { get; init; }

    public string Replace { get; init; } = string.Empty;

    public bool FindIsRegex { get; init; } = true;

    /// <summary>Chỉ đổi phần tử có tên chứa chuỗi này.</summary>
    public string? NameContains { get; init; }

    /// <summary>Chỉ với Types: tên category (Pipes, Ducts, Cable Trays…). Types cần categories hoặc nameContains.</summary>
    public List<string> Categories { get; init; } = new List<string>();

    public int CounterStart { get; init; } = 1;

    public bool DryRun { get; init; } = true;
}

public sealed class ElementRenameCommand : ICoreCommand<ElementRenameConfig>
{
    /// <summary>Trần số phần tử một lần — đổi tên hàng nghìn type một lúc gần như chắc chắn là lọc nhầm.</summary>
    public const int MaxItems = 5000;

    private sealed record Candidate(Element Element, RenameItem Item);

    public string CommandName => "ElementRename";

    public CommandResult Execute(Document document, ElementRenameConfig config)
    {
        var kind = RenamePlanner.NormalizeKind(config.Kind);
        if (kind is null)
        {
            return CommandResult.Fail($"kind \"{config.Kind}\" không hợp lệ. Dùng: {string.Join(", ", RenamePlanner.Kinds)}.");
        }

        if (string.IsNullOrEmpty(config.Find) && (string.IsNullOrWhiteSpace(config.NamePattern) || config.NamePattern.Trim() == "{Name}"))
        {
            return CommandResult.Fail("Cần find hoặc namePattern khác \"{Name}\" — nếu không thì không có gì để đổi.");
        }

        if (kind == "Types" && config.Categories.Count == 0 && string.IsNullOrWhiteSpace(config.NameContains))
        {
            return CommandResult.Fail("Đổi tên Types cần categories hoặc nameContains — mô hình có hàng nghìn type hệ thống, không đổi tràn lan.");
        }

        var all = Collect(document, kind, config.Categories);
        var chosen = all.Where(c => string.IsNullOrEmpty(config.NameContains)
            || c.Item.Name.IndexOf(config.NameContains!, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (chosen.Count == 0)
        {
            return CommandResult.Fail($"Không có {Label(kind)} nào khớp bộ lọc.");
        }

        if (chosen.Count > MaxItems)
        {
            return CommandResult.Fail($"{chosen.Count} {Label(kind)} vượt giới hạn {MaxItems} — thu hẹp bằng nameContains/categories.");
        }

        var chosenIds = new HashSet<ElementId>(chosen.Select(c => c.Element.Id));
        var others = all.Where(c => !chosenIds.Contains(c.Element.Id)).Select(c => (c.Item.Scope, c.Item.Name)).ToList();
        if (kind == "ViewTemplates")
        {
            // Template và view thường cùng loại dùng chung không gian tên: tên view thường cũng là tên đã dùng.
            others.AddRange(new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate).Select(v => (v.ViewType.ToString(), v.Name)));
        }

        var pattern = new NamePattern(string.IsNullOrWhiteSpace(config.NamePattern) ? "{Name}" : config.NamePattern)
        {
            Find = config.Find,
            Replace = config.Replace,
            FindIsRegex = config.FindIsRegex,
            CounterStart = config.CounterStart,
        };

        List<string> newNames;
        List<string> notes;
        try
        {
            var ordered = chosen.OrderBy(c => c.Item.Scope, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Item.Name, StringComparer.OrdinalIgnoreCase).ToList();
            chosen = ordered;
            newNames = RenamePlanner.Plan(chosen.Select(c => c.Item).ToList(), others, pattern, out notes);
        }
        catch (ArgumentException ex)
        {
            return CommandResult.Fail("Mẫu tìm/thay không hợp lệ: " + ex.Message);
        }

        var result = CommandResult.Ok(string.Empty);
        result.Messages.AddRange(notes);
        var plan = chosen.Select((c, i) => (c.Element, c.Item, NewName: newNames[i]))
            .Where(p => !string.Equals(p.Item.Name, p.NewName, StringComparison.Ordinal))
            .ToList();
        var empty = plan.Where(p => string.IsNullOrWhiteSpace(p.NewName)).ToList();
        foreach (var p in empty)
        {
            result.Messages.Add($"Bỏ qua \"{p.Item.Name}\": tên mới rỗng.");
        }

        plan = plan.Except(empty).ToList();

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ đổi tên {plan.Count}/{chosen.Count} {Label(kind)}.";
            result.Messages.AddRange(plan.Take(500).Select(p => $"{Scope(p.Item)}\"{p.Item.Name}\" → \"{p.NewName}\""));
            if (plan.Count > 500)
            {
                result.Messages.Add($"… và {plan.Count - 500} phần tử nữa.");
            }

            result.AffectedCount = plan.Count;
            return result;
        }

        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Đổi tên " + Label(kind));
        // Hoán đổi tên (A→B, B→A) va nhau giữa chừng: đổi tất cả qua tên tạm trước, lỗi thì trả lại tên gốc.
        var useTemporary = RenamePlanner.NeedsTemporaryNames(plan.Select(p => p.Item).ToList(), plan.Select(p => p.NewName).ToList());
        var failed = new HashSet<ElementId>();
        if (useTemporary)
        {
            foreach (var (element, item, _) in plan)
            {
                try
                {
                    element.Name = "~DHCB~" + RevitCompat.IdValue(element.Id);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    failed.Add(element.Id);
                    result.Errors.Add($"\"{item.Name}\": {ex.Message}");
                }
            }
        }

        foreach (var (element, item, newName) in plan)
        {
            if (failed.Contains(element.Id))
            {
                continue;
            }

            try
            {
                element.Name = newName;
                done++;
                result.WithChanged(RevitCompat.IdValue(element.Id));
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is ArgumentException || ex is InvalidOperationException)
            {
                result.Errors.Add($"\"{item.Name}\" → \"{newName}\": {ex.Message}");
                if (useTemporary)
                {
                    try { element.Name = item.Name; }
                    catch (Exception restore) when (restore is Autodesk.Revit.Exceptions.ApplicationException || restore is ArgumentException || restore is InvalidOperationException)
                    {
                        result.Errors.Add($"\"{item.Name}\": không trả lại được tên gốc: {restore.Message}");
                    }
                }
            }
        }

        tx.Commit();
        result.Summary = $"Đã đổi tên {done}/{plan.Count} {Label(kind)}.";
        result.AffectedCount = done;
        return result;
    }

    private static string Label(string kind) => kind switch
    {
        "ViewTemplates" => "view template",
        "Filters" => "filter",
        "Types" => "type",
        _ => "material",
    };

    private static string Scope(RenameItem item) => item.Scope.Length == 0 ? string.Empty : "[" + item.Scope + "] ";

    private static List<Candidate> Collect(Document document, string kind, List<string> categories)
    {
        Dictionary<string, string> Values(Element e, params (string Key, string Value)[] extra)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = e.Name,
                ["Category"] = e.Category?.Name ?? string.Empty,
            };
            foreach (var (key, value) in extra)
            {
                values[key] = value;
            }

            return values;
        }

        switch (kind)
        {
            case "ViewTemplates":
                return new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate)
                    .Select(v => new Candidate(v, new RenameItem(v.ViewType.ToString(), v.Name, Values(v, ("ViewType", v.ViewType.ToString())))))
                    .ToList();
            case "Filters":
                return new FilteredElementCollector(document).OfClass(typeof(FilterElement)).Cast<Element>()
                    .Select(f => new Candidate(f, new RenameItem(string.Empty, f.Name, Values(f))))
                    .ToList();
            case "Materials":
                return new FilteredElementCollector(document).OfClass(typeof(Material)).Cast<Element>()
                    .Select(m => new Candidate(m, new RenameItem(string.Empty, m.Name, Values(m))))
                    .ToList();
            default:
                var wanted = new HashSet<string>(categories.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
                return new FilteredElementCollector(document).WhereElementIsElementType().Cast<ElementType>()
                    .Where(t => t.Category != null && (wanted.Count == 0 || wanted.Contains(t.Category.Name)))
                    .Select(t => new Candidate(t, new RenameItem(t.FamilyName ?? string.Empty, t.Name, Values(t, ("Family", t.FamilyName ?? string.Empty)))))
                    .ToList();
        }
    }
}
