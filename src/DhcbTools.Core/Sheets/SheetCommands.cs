using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.Sheets;

/// <summary>Mục 7.1 — đổi số/tên sheet hoặc view theo mẫu (học từ pyRevit Sheets, DiRoots).</summary>
public sealed class SheetRenameConfig
{
    /// <summary>Sheets | Views.</summary>
    public string Target { get; init; } = "Sheets";

    /// <summary>Mẫu số sheet (chỉ với Sheets). Token: {Number} {Name} {Level} {Discipline} {n} và mọi tham số của sheet. Rỗng = không đổi số.</summary>
    public string? NumberPattern { get; init; }

    /// <summary>Mẫu tên. Rỗng = không đổi tên.</summary>
    public string? NamePattern { get; init; }

    /// <summary>Regex tìm trên kết quả (áp cho cả số và tên).</summary>
    public string? Find { get; init; }

    public string Replace { get; init; } = string.Empty;

    public bool FindIsRegex { get; init; } = true;

    /// <summary>Chỉ đổi sheet/view có số hoặc tên chứa chuỗi này.</summary>
    public string? FilterContains { get; init; }

    /// <summary>Thứ tự bộ đếm {n}: theo số/tên hiện tại (mặc định) hoặc theo Level rồi tên.</summary>
    public string OrderBy { get; init; } = "Number";

    public int CounterStart { get; init; } = 1;

    public bool DryRun { get; init; } = true;
}

public sealed class SheetRenameCommand : ICoreCommand<SheetRenameConfig>
{
    public string CommandName => "SheetRename";

    public CommandResult Execute(Document document, SheetRenameConfig config)
    {
        var isSheets = !config.Target.Equals("Views", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(config.NumberPattern) && string.IsNullOrEmpty(config.NamePattern) && string.IsNullOrEmpty(config.Find))
        {
            return CommandResult.Fail("Cần ít nhất numberPattern, namePattern hoặc find.");
        }

        List<View> items = isSheets
            ? new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<View>().ToList()
            : new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v is not ViewSheet && v.CanBePrinted).ToList();

        if (!string.IsNullOrEmpty(config.FilterContains))
        {
            items = items.Where(v => Key(v).IndexOf(config.FilterContains!, StringComparison.OrdinalIgnoreCase) >= 0 || v.Name.IndexOf(config.FilterContains!, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        if (items.Count == 0)
        {
            return CommandResult.Fail("Không có sheet/view nào khớp bộ lọc.");
        }

        items = config.OrderBy.Equals("Level", StringComparison.OrdinalIgnoreCase)
            ? items.OrderBy(v => LevelName(document, v)).ThenBy(v => Key(v), StringComparer.OrdinalIgnoreCase).ToList()
            : items.OrderBy(v => Key(v), StringComparer.OrdinalIgnoreCase).ToList();

        var values = items.Select(v => (IDictionary<string, string>?)TokenValues(document, v)).ToList();
        var result = CommandResult.Ok(string.Empty);

        // Tên/số của phần tử KHÔNG nằm trong lô là "đã dùng" — chống trùng.
        var chosenIds = new HashSet<ElementId>(items.Select(i => i.Id));
        var reservedNumbers = new HashSet<string>(new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !chosenIds.Contains(s.Id)).Select(s => s.SheetNumber), StringComparer.OrdinalIgnoreCase);
        var reservedNames = new HashSet<string>(new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && !chosenIds.Contains(v.Id) && (v is ViewSheet) == isSheets).Select(v => v.Name), StringComparer.OrdinalIgnoreCase);

        List<string>? newNumbers = null;
        List<string>? newNames = null;
        if (isSheets && (!string.IsNullOrEmpty(config.NumberPattern) || !string.IsNullOrEmpty(config.Find)))
        {
            var p = new NamePattern(config.NumberPattern ?? "{Number}") { Find = config.Find, Replace = config.Replace, FindIsRegex = config.FindIsRegex, CounterStart = config.CounterStart };
            newNumbers = p.ApplyAll(values, reservedNumbers, out var notes);
            result.Messages.AddRange(notes.Select(n => "Số: " + n));
        }

        if (!string.IsNullOrEmpty(config.NamePattern) || (!string.IsNullOrEmpty(config.Find) && (string.IsNullOrEmpty(config.NumberPattern) || !isSheets)))
        {
            var p = new NamePattern(config.NamePattern ?? "{Name}") { Find = config.Find, Replace = config.Replace, FindIsRegex = config.FindIsRegex, CounterStart = config.CounterStart };
            newNames = p.ApplyAll(values, reservedNames, out var notes);
            result.Messages.AddRange(notes.Select(n => "Tên: " + n));
        }

        var plan = new List<(View View, string? Number, string? Name)>();
        for (var i = 0; i < items.Count; i++)
        {
            var num = newNumbers != null && items[i] is ViewSheet s && !string.Equals(s.SheetNumber, newNumbers[i], StringComparison.Ordinal) ? newNumbers[i] : null;
            var name = newNames != null && !string.Equals(items[i].Name, newNames[i], StringComparison.Ordinal) ? newNames[i] : null;
            if (num != null || name != null)
            {
                plan.Add((items[i], num, name));
            }
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ đổi {plan.Count}/{items.Count} {(isSheets ? "sheet" : "view")}.";
            result.Messages.AddRange(plan.Select(p => $"{Key(p.View)} \"{p.View.Name}\" → {(p.Number ?? Key(p.View))} \"{p.Name ?? p.View.Name}\""));
            result.AffectedCount = plan.Count;
            return result;
        }

        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Đổi tên sheet/view");
        // Đổi số sheet có thể va nhau tạm thời (A→B, B→A): đi hai vòng qua tên tạm.
        // Nhớ số gốc để khôi phục nếu vòng đổi tên thật thất bại — trước đây sheet lỗi bị bỏ lại vĩnh viễn
        // với số tạm "~DHCB~<id>" vì transaction vẫn Commit dù có Errors.
        var temp = plan.Where(p => p.Number != null).ToList();
        var originalNumbers = new Dictionary<ElementId, string>();
        foreach (var (view, _, _) in temp)
        {
            try
            {
                var sheet = (ViewSheet)view;
                originalNumbers[view.Id] = sheet.SheetNumber;
                sheet.SheetNumber = "~DHCB~" + RevitCompat.IdValue(view.Id);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{Key(view)}: {ex.Message}");
            }
        }

        foreach (var (view, number, name) in plan)
        {
            try
            {
                if (number != null) ((ViewSheet)view).SheetNumber = number;
                if (name != null) view.Name = name;
                done++;
                result.WithChanged(RevitCompat.IdValue(view.Id));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{Key(view)}: {ex.Message}");
                if (number != null && originalNumbers.TryGetValue(view.Id, out var original))
                {
                    try { ((ViewSheet)view).SheetNumber = original; }
                    catch (Exception restoreEx) { result.Errors.Add($"{Key(view)}: không khôi phục được số gốc \"{original}\": {restoreEx.Message}"); }
                }
            }
        }

        tx.Commit();
        result.Summary = $"Đã đổi {done}/{plan.Count} {(isSheets ? "sheet" : "view")}.";
        result.AffectedCount = done;
        return result;
    }

    private static string Key(View v) => v is ViewSheet s ? s.SheetNumber : v.Name;

    private static string LevelName(Document doc, View v)
    {
        if (v.GenLevel != null) return v.GenLevel.Name;
        var p = v.LookupParameter("Level") ?? v.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
        return p?.AsValueString() ?? string.Empty;
    }

    /// <summary>Token từ tham số: Number, Name, Level, Discipline, ViewType, và mọi tham số instance của sheet/view có giá trị.</summary>
    internal static Dictionary<string, string> TokenValues(Document doc, View v)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Number"] = Key(v),
            ["Name"] = v.Name,
            ["Level"] = LevelName(doc, v),
            ["ViewType"] = v.ViewType.ToString(),
        };
        foreach (Parameter p in v.Parameters)
        {
            if (p.Definition == null || !p.HasValue) continue;
            var name = p.Definition.Name;
            if (d.ContainsKey(name)) continue;
            var text = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            if (!string.IsNullOrEmpty(text)) d[name] = text!;
        }
        return d;
    }
}

/// <summary>Mục 7.2 — gán/bỏ revision trên nhiều sheet (pyRevit Set Revisions on Sheets).</summary>
public sealed class RevisionOnSheetsConfig
{
    /// <summary>Số thứ tự revision (Sequence Number) trong Sheet Issues/Revisions.</summary>
    public required int RevisionSequence { get; init; }

    public string? SheetNumberContains { get; init; }

    /// <summary>Danh sách số sheet chính xác (rỗng = theo bộ lọc chứa).</summary>
    public List<string> SheetNumbers { get; init; } = new List<string>();

    public bool Remove { get; init; }

    public bool DryRun { get; init; } = true;
}

public sealed class RevisionOnSheetsCommand : ICoreCommand<RevisionOnSheetsConfig>
{
    public string CommandName => "RevisionOnSheets";

    public CommandResult Execute(Document document, RevisionOnSheetsConfig config)
    {
        var revision = new FilteredElementCollector(document).OfClass(typeof(Revision)).Cast<Revision>().FirstOrDefault(r => r.SequenceNumber == config.RevisionSequence);
        if (revision == null)
        {
            var all = new FilteredElementCollector(document).OfClass(typeof(Revision)).Cast<Revision>().Select(r => $"{r.SequenceNumber}: {r.Description}");
            return CommandResult.Fail($"Không có revision số {config.RevisionSequence}. Hiện có: {string.Join("; ", all)}");
        }

        var sheets = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => config.SheetNumbers.Count > 0
                ? config.SheetNumbers.Any(n => string.Equals(n, s.SheetNumber, StringComparison.OrdinalIgnoreCase))
                : string.IsNullOrEmpty(config.SheetNumberContains) || s.SheetNumber.IndexOf(config.SheetNumberContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var plan = sheets.Where(s =>
        {
            var has = s.GetAdditionalRevisionIds().Contains(revision.Id) || s.GetAllRevisionIds().Contains(revision.Id);
            return config.Remove ? s.GetAdditionalRevisionIds().Contains(revision.Id) : !has;
        }).ToList();

        var result = CommandResult.Ok(string.Empty);
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ {(config.Remove ? "bỏ" : "gán")} revision {revision.SequenceNumber} \"{revision.Description}\" trên {plan.Count}/{sheets.Count} sheet.";
            result.Messages.AddRange(plan.Select(s => s.SheetNumber + " - " + s.Name));
            result.AffectedCount = plan.Count;
            return result;
        }

        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Revision trên sheet");
        foreach (var s in plan)
        {
            try
            {
                var ids = s.GetAdditionalRevisionIds().ToList();
                if (config.Remove) ids.Remove(revision.Id); else ids.Add(revision.Id);
                s.SetAdditionalRevisionIds(ids);
                done++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{s.SheetNumber}: {ex.Message}");
            }
        }

        tx.Commit();
        result.Summary = $"Đã {(config.Remove ? "bỏ" : "gán")} revision trên {done}/{plan.Count} sheet.";
        result.AffectedCount = done;
        return result;
    }
}

/// <summary>Mục 7.6 — xuất warning ra CSV kèm ElementId (Ideate Explorer).</summary>
public sealed class WarningsExportConfig
{
    public required string OutputPath { get; init; }
}

public sealed class WarningsExportCommand : ICoreCommand<WarningsExportConfig>
{
    public string CommandName => "WarningsExport";

    public CommandResult Execute(Document document, WarningsExportConfig config)
    {
        var warnings = document.GetWarnings();
        var sb = new StringBuilder("Severity,Description,ElementIds,Categories,Names\n");
        var byType = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in warnings)
        {
            var ids = w.GetFailingElements().ToList();
            var elements = ids.Select(document.GetElement).Where(e => e != null).ToList();
            var desc = w.GetDescriptionText();
            byType[desc] = byType.TryGetValue(desc, out var n) ? n + 1 : 1;
            sb.Append(CsvText.JoinLine(new[]
            {
                w.GetSeverity().ToString(), desc,
                string.Join(";", ids.Select(i => RevitCompat.IdValue(i).ToString())),
                string.Join(";", elements.Select(e => e!.Category?.Name ?? string.Empty).Distinct()),
                string.Join(";", elements.Select(e => e!.Name).Where(x => !string.IsNullOrEmpty(x)).Take(5)),
            })).Append('\n');
        }

        var dir = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        var result = CommandResult.Ok($"Đã xuất {warnings.Count} warning ({byType.Count} loại) → \"{config.OutputPath}\".", warnings.Count);
        result.Messages.AddRange(byType.OrderByDescending(k => k.Value).Take(30).Select(k => $"{k.Value} × {k.Key}"));
        return result;
    }
}
