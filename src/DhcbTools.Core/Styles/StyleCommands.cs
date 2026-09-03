using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.Styles;

/// <summary>Mục 7.3 — purge style không được tham chiếu (Ideate StyleManager, pyRevit Wipe).</summary>
public sealed class StylePurgeConfig
{
    /// <summary>ViewTemplates, Filters, LinePatterns, FillPatterns, TextTypes, DimensionTypes, Materials.</summary>
    public List<string> Kinds { get; init; } = new List<string> { "ViewTemplates", "Filters", "LinePatterns", "FillPatterns", "TextTypes", "DimensionTypes" };

    public List<string> KeepNameContains { get; init; } = new List<string>();

    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Phân tích tham chiếu rồi mới xoá: view template → view.ViewTemplateId; filter → view.GetFilters + view template;
/// line pattern → GraphicsStyle/category line pattern + override trong view; fill pattern → material/filled region type/override;
/// text/dim type → instance đang dùng type; material → phần tử/type có tham số material trỏ tới.
/// Không bao giờ xoá style hệ thống (&lt;Solid fill&gt;, &lt;Invisible lines&gt;…) — dùng <see cref="CleanupDecider"/>.
/// </summary>
public sealed class StylePurgeCommand : ICoreCommand<StylePurgeConfig>
{
    public string CommandName => "StylePurge";

    public CommandResult Execute(Document document, StylePurgeConfig config)
    {
        var result = CommandResult.Ok(string.Empty);
        var toDelete = new List<(ElementId Id, string Kind, string Name)>();
        var kinds = new HashSet<string>(config.Kinds, StringComparer.OrdinalIgnoreCase);
        var views = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().ToList();
        var realViews = views.Where(v => !v.IsTemplate).ToList();

        if (kinds.Contains("ViewTemplates"))
        {
            var used = new HashSet<ElementId>(realViews.Select(v => v.ViewTemplateId).Where(id => id != ElementId.InvalidElementId));
            foreach (var t in views.Where(v => v.IsTemplate))
            {
                Consider(t.Id, "ViewTemplate", t.Name, used.Contains(t.Id));
            }
        }

        if (kinds.Contains("Filters"))
        {
            var used = new HashSet<ElementId>();
            foreach (var v in views)
            {
                try { foreach (var f in v.GetFilters()) used.Add(f); } catch { /* view không hỗ trợ filter */ }
            }
            foreach (var f in new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
            {
                Consider(f.Id, "Filter", f.Name, used.Contains(f.Id));
            }
            foreach (var f in new FilteredElementCollector(document).OfClass(typeof(SelectionFilterElement)).Cast<SelectionFilterElement>())
            {
                Consider(f.Id, "SelectionFilter", f.Name, used.Contains(f.Id));
            }
        }

        if (kinds.Contains("LinePatterns"))
        {
            var used = new HashSet<ElementId>();
            foreach (Category cat in document.Settings.Categories)
            {
                try
                {
                    used.Add(cat.GetLinePatternId(GraphicsStyleType.Projection));
                    used.Add(cat.GetLinePatternId(GraphicsStyleType.Cut));
                    foreach (Category sub in cat.SubCategories)
                    {
                        used.Add(sub.GetLinePatternId(GraphicsStyleType.Projection));
                        used.Add(sub.GetLinePatternId(GraphicsStyleType.Cut));
                    }
                }
                catch { /* category không có line pattern */ }
            }
            foreach (var v in views)
            {
                try
                {
                    foreach (var f in v.GetFilters())
                    {
                        var o = v.GetFilterOverrides(f);
                        used.Add(o.ProjectionLinePatternId);
                        used.Add(o.CutLinePatternId);
                    }
                }
                catch { /* ignore */ }
            }
            foreach (var lp in new FilteredElementCollector(document).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>())
            {
                Consider(lp.Id, "LinePattern", lp.Name, used.Contains(lp.Id));
            }
        }

        if (kinds.Contains("FillPatterns"))
        {
            var used = new HashSet<ElementId>();
            foreach (var m in new FilteredElementCollector(document).OfClass(typeof(Material)).Cast<Material>())
            {
                used.Add(m.SurfaceForegroundPatternId); used.Add(m.SurfaceBackgroundPatternId);
                used.Add(m.CutForegroundPatternId); used.Add(m.CutBackgroundPatternId);
            }
            foreach (var fr in new FilteredElementCollector(document).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>())
            {
                used.Add(fr.ForegroundPatternId); used.Add(fr.BackgroundPatternId);
            }
            foreach (var v in views)
            {
                try
                {
                    foreach (var f in v.GetFilters())
                    {
                        var o = v.GetFilterOverrides(f);
                        used.Add(o.SurfaceForegroundPatternId); used.Add(o.SurfaceBackgroundPatternId);
                        used.Add(o.CutForegroundPatternId); used.Add(o.CutBackgroundPatternId);
                    }
                }
                catch { /* ignore */ }
            }
            foreach (var fp in new FilteredElementCollector(document).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                var pattern = fp.GetFillPattern();
                var system = pattern.IsSolidFill || fp.Name.StartsWith("<", StringComparison.Ordinal);
                Consider(fp.Id, "FillPattern", fp.Name, used.Contains(fp.Id), system);
            }
        }

        if (kinds.Contains("TextTypes"))
        {
            var used = new HashSet<ElementId>(new FilteredElementCollector(document).OfClass(typeof(TextNote)).Select(t => t.GetTypeId()));
            var types = new FilteredElementCollector(document).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            foreach (var t in types)
            {
                Consider(t.Id, "TextType", t.Name, used.Contains(t.Id) || types.Count == 1);
            }
        }

        if (kinds.Contains("DimensionTypes"))
        {
            var used = new HashSet<ElementId>(new FilteredElementCollector(document).OfClass(typeof(Dimension)).Select(d => d.GetTypeId()));
            foreach (var t in new FilteredElementCollector(document).OfClass(typeof(DimensionType)).Cast<DimensionType>())
            {
                Consider(t.Id, "DimensionType", t.Name, used.Contains(t.Id));
            }
        }

        if (kinds.Contains("Materials"))
        {
            var used = new HashSet<ElementId>();
            foreach (var e in new FilteredElementCollector(document).WhereElementIsElementType().ToElements().Concat(new FilteredElementCollector(document).WhereElementIsNotElementType().ToElements()))
            {
                try
                {
                    foreach (var mid in e.GetMaterialIds(false)) used.Add(mid);
                    foreach (var mid in e.GetMaterialIds(true)) used.Add(mid);
                }
                catch { /* phần tử không có material */ }
            }
            foreach (var m in new FilteredElementCollector(document).OfClass(typeof(Material)).Cast<Material>())
            {
                Consider(m.Id, "Material", m.Name, used.Contains(m.Id));
            }
        }

        void Consider(ElementId id, string kind, string name, bool isUsed, bool isSystem = false)
        {
            var sys = isSystem || name.StartsWith("<", StringComparison.Ordinal) || name.StartsWith("Default", StringComparison.OrdinalIgnoreCase) && kind == "DimensionType";
            if (CleanupDecider.ShouldErase(name, isUsed, false, sys, config.KeepNameContains))
            {
                toDelete.Add((id, kind, name));
            }
        }

        if (toDelete.Count == 0)
        {
            result.Summary = "Không có style thừa trong các nhóm đã chọn.";
            return result;
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ xoá {toDelete.Count} style không được tham chiếu.";
            foreach (var g in toDelete.GroupBy(t => t.Kind))
            {
                result.Messages.Add($"{g.Key}: {g.Count()}");
                result.Messages.AddRange(g.Take(100).Select(t => "  " + t.Name));
            }
            result.AffectedCount = toDelete.Count;
            return result;
        }

        var deleted = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Dọn style thừa");
        foreach (var (id, kind, name) in toDelete)
        {
            try
            {
                if (document.GetElement(id) != null)
                {
                    document.Delete(id);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Không xoá được {kind} \"{name}\": {ex.Message}");
            }
        }

        tx.Commit();
        result.Summary = $"Đã xoá {deleted}/{toDelete.Count} style.";
        result.AffectedCount = deleted;
        return result;
    }
}

/// <summary>Mục 7.4 — tô màu theo giá trị tham số (Colour Splasher).</summary>
public sealed class ColorByParameterConfig
{
    /// <summary>Tên view; rỗng = view đang mở (vỏ truyền vào) hoặc lỗi nếu chạy batch.</summary>
    public string? ViewName { get; init; }

    public List<string> Categories { get; init; } = new List<string>();

    public required string ParameterName { get; init; }

    /// <summary>Giá trị → mã hex cố định (tuỳ chọn); còn lại tự sinh palette.</summary>
    public Dictionary<string, string> FixedColors { get; init; } = new Dictionary<string, string>();

    public string? LegendCsvPath { get; init; }

    /// <summary>Xoá override của các phần tử trong view thay vì tô.</summary>
    public bool Reset { get; init; }

    public bool FillSurfaces { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

public sealed class ColorByParameterCommand : ICoreCommand<ColorByParameterConfig>
{
    public string CommandName => "ColorByParameter";

    public CommandResult Execute(Document document, ColorByParameterConfig config)
    {
        var view = string.IsNullOrEmpty(config.ViewName)
            ? document.ActiveView
            : new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, config.ViewName, StringComparison.OrdinalIgnoreCase));
        if (view == null)
        {
            return CommandResult.Fail("Không tìm thấy view (viewName rỗng cần có view đang mở).");
        }

        var collector = new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType();
        if (config.Categories.Count > 0)
        {
            var ids = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
            if (ids.Count == 0) return CommandResult.Fail("Category không có trong mô hình: " + string.Join(", ", unknown));
            collector = collector.WherePasses(new ElementMulticategoryFilter(ids.ToList()));
        }

        var elements = collector.ToElements().Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model).ToList();
        var result = CommandResult.Ok(string.Empty);

        if (config.Reset)
        {
            if (config.DryRun)
            {
                result.Summary = $"[Xem trước] Sẽ xoá override của {elements.Count} phần tử trong \"{view.Name}\".";
                return result;
            }

            using var txr = RevitCompat.StartTransaction(document, "DHCB - Xoá màu override");
            var blank = new OverrideGraphicSettings();
            foreach (var e in elements) view.SetElementOverrides(e.Id, blank);
            txr.Commit();
            result.Summary = $"Đã xoá override của {elements.Count} phần tử.";
            result.AffectedCount = elements.Count;
            return result;
        }

        var valueOf = elements.ToDictionary(e => e.Id, e => RevitCompat.ReadString(e, config.ParameterName));
        var palette = PaletteGenerator.Assign(valueOf.Values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase), config.FixedColors);
        var counts = valueOf.Values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(config.LegendCsvPath))
        {
            var sb = new StringBuilder("Value,Color,Count\n");
            foreach (var kv in palette.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(CsvText.JoinLine(new[] { kv.Key.Length == 0 ? "(trống)" : kv.Key, kv.Value.ToString(), counts.TryGetValue(kv.Key, out var c) ? c.ToString() : "0" })).Append('\n');
            }
            File.WriteAllText(config.LegendCsvPath!, sb.ToString(), CsvText.Utf8WithBom);
        }

        result.Messages.AddRange(palette.OrderByDescending(k => counts.TryGetValue(k.Key, out var c) ? c : 0).Take(50).Select(k => $"{k.Value} {(k.Key.Length == 0 ? "(trống)" : k.Key)}: {(counts.TryGetValue(k.Key, out var c) ? c : 0)}"));
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ tô {elements.Count} phần tử theo \"{config.ParameterName}\" ({palette.Count} giá trị) trong \"{view.Name}\".";
            result.AffectedCount = elements.Count;
            return result;
        }

        var solid = new FilteredElementCollector(document).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Tô màu theo tham số");
        foreach (var e in elements)
        {
            var rgb = palette[valueOf[e.Id]];
            var color = new Color(rgb.R, rgb.G, rgb.B);
            var ogs = new OverrideGraphicSettings().SetProjectionLineColor(color).SetCutLineColor(color);
            if (config.FillSurfaces && solid != null)
            {
                ogs.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(color).SetCutForegroundPatternId(solid.Id).SetCutForegroundPatternColor(color);
            }
            try { view.SetElementOverrides(e.Id, ogs); done++; }
            catch (Exception ex) { result.Errors.Add($"{e.Id}: {ex.Message}"); }
        }

        tx.Commit();
        result.Summary = $"Đã tô {done}/{elements.Count} phần tử theo \"{config.ParameterName}\" ({palette.Count} màu). Chạy lại với reset=true để xoá.";
        result.AffectedCount = done;
        return result;
    }
}

/// <summary>Mục 7.5 — kiểm kê và đổi tên family (DiRoots FamilyReviser).</summary>
public sealed class FamilyAuditConfig
{
    public string? OutputPath { get; init; }

    /// <summary>Mẫu tên family mới; token {Name} {Category} {n}. Rỗng = chỉ kiểm kê.</summary>
    public string? RenamePattern { get; init; }

    public string? Find { get; init; }

    public string Replace { get; init; } = string.Empty;

    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Chỉ đổi tên family có tên chứa chuỗi này.</summary>
    public string? FilterContains { get; init; }

    public bool DryRun { get; init; } = true;
}

public sealed class FamilyAuditCommand : ICoreCommand<FamilyAuditConfig>
{
    public string CommandName => "FamilyAudit";

    public CommandResult Execute(Document document, FamilyAuditConfig config)
    {
        var families = new FilteredElementCollector(document).OfClass(typeof(Family)).Cast<Family>()
            .Where(f => config.Categories.Count == 0 || (f.FamilyCategory != null && config.Categories.Any(c => string.Equals(c, f.FamilyCategory.Name, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(f => f.FamilyCategory?.Name).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var instanceCount = new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .GroupBy(i => i.Symbol.Family.Id).ToDictionary(g => g.Key, g => g.Count());

        var rows = families.Select(f => new
        {
            Family = f,
            Category = f.FamilyCategory?.Name ?? string.Empty,
            Types = f.GetFamilySymbolIds().Count,
            Instances = instanceCount.TryGetValue(f.Id, out var n) ? n : 0,
            InPlace = f.IsInPlace,
        }).ToList();

        var result = CommandResult.Ok(string.Empty);
        if (!string.IsNullOrEmpty(config.OutputPath))
        {
            var sb = new StringBuilder("Family,Category,Types,Instances,InPlace,Editable\n");
            foreach (var r in rows)
            {
                sb.Append(CsvText.JoinLine(new[] { r.Family.Name, r.Category, r.Types.ToString(), r.Instances.ToString(), r.InPlace ? "true" : "false", r.Family.IsEditable ? "true" : "false" })).Append('\n');
            }
            File.WriteAllText(config.OutputPath!, sb.ToString(), CsvText.Utf8WithBom);
        }

        var unused = rows.Count(r => r.Instances == 0);
        var inPlace = rows.Count(r => r.InPlace);
        result.Messages.Add($"{rows.Count} family, {unused} không có instance, {inPlace} in-place.");

        if (string.IsNullOrEmpty(config.RenamePattern) && string.IsNullOrEmpty(config.Find))
        {
            result.Summary = $"Kiểm kê {rows.Count} family" + (config.OutputPath != null ? $" → \"{config.OutputPath}\"." : ".");
            result.AffectedCount = rows.Count;
            result.Messages.AddRange(rows.Where(r => r.Instances == 0).Take(50).Select(r => $"Không dùng: {r.Category} / {r.Family.Name}"));
            return result;
        }

        var targets = rows.Where(r => !r.InPlace && (string.IsNullOrEmpty(config.FilterContains) || r.Family.Name.IndexOf(config.FilterContains!, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        var pattern = new NamePattern(config.RenamePattern ?? "{Name}") { Find = config.Find, Replace = config.Replace };
        var reserved = new HashSet<string>(families.Except(targets.Select(t => t.Family)).Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        var values = targets.Select(t => (IDictionary<string, string>?)new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = t.Family.Name, ["Category"] = t.Category }).ToList();
        var newNames = pattern.ApplyAll(values, reserved, out var notes);
        result.Messages.AddRange(notes);

        var plan = targets.Select((t, i) => (t.Family, NewName: newNames[i])).Where(p => !string.Equals(p.Family.Name, p.NewName, StringComparison.Ordinal)).ToList();
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ đổi tên {plan.Count}/{targets.Count} family.";
            result.Messages.AddRange(plan.Select(p => $"{p.Family.Name} → {p.NewName}"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Đổi tên family");
        foreach (var (family, newName) in plan)
        {
            try { family.Name = newName; done++; }
            catch (Exception ex) { result.Errors.Add($"{family.Name}: {ex.Message}"); }
        }

        tx.Commit();
        result.Summary = $"Đã đổi tên {done}/{plan.Count} family.";
        result.AffectedCount = done;
        return result;
    }
}
