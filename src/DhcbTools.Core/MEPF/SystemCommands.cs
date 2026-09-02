using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>Mục 3.4 — filter + màu theo hệ.</summary>
public sealed class SystemColorConfig
{
    /// <summary>Tên hệ (System Type name hoặc chuỗi con của System Name) → mã màu hex.</summary>
    public required Dictionary<string, string> Colors { get; init; }

    /// <summary>View template (hoặc view) nhận filter. Rỗng = tạo filter nhưng không áp.</summary>
    public string? ViewTemplateName { get; init; }

    /// <summary>Tiền tố tên filter.</summary>
    public string FilterPrefix { get; init; } = "DHCB-SYS-";

    /// <summary>Tô cả mặt (solid fill) ngoài đường nét.</summary>
    public bool FillSurfaces { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

public sealed class SystemNameConfig
{
    public string Discipline { get; init; } = "MEC";

    public string? Zone { get; init; }

    public int PadWidth { get; init; } = 2;

    /// <summary>Tên loại hệ → viết tắt; thiếu thì dùng bảng mặc định.</summary>
    public Dictionary<string, string> Abbreviations { get; init; } = new Dictionary<string, string>();

    /// <summary>Chỉ đổi hệ có tên hiện tại còn mặc định (chứa số cuối do Revit tự sinh) — an toàn khi chạy lại.</summary>
    public bool OnlyDefaultNames { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

internal static class MepCategories
{
    public static readonly List<BuiltInCategory> All = new()
    {
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_DuctTerminal,
        BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_FlexPipeCurves, BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers,
    };
}

/// <summary>Tạo ParameterFilterElement theo System Name (contains) và OverrideGraphicSettings màu, áp vào view template.</summary>
public sealed class SystemColorCommand : ICoreCommand<SystemColorConfig>
{
    public string CommandName => "SystemColor";

    public CommandResult Execute(Document document, SystemColorConfig config)
    {
        var result = CommandResult.Ok(string.Empty);
        var plan = new List<(string System, string FilterName, Rgb Color)>();
        foreach (var kv in config.Colors)
        {
            if (!SystemNaming.TryParseHex(kv.Value, out var rgb))
            {
                result.Messages.Add($"Bỏ qua \"{kv.Key}\": mã màu \"{kv.Value}\" không hợp lệ (cần #RRGGBB).");
                continue;
            }

            plan.Add((kv.Key, config.FilterPrefix + kv.Key, rgb));
        }

        View? view = null;
        if (!string.IsNullOrEmpty(config.ViewTemplateName))
        {
            view = RevitCompat.FindViewTemplate(document, config.ViewTemplateName)
                   ?? new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, config.ViewTemplateName, StringComparison.OrdinalIgnoreCase));
            if (view == null)
            {
                return CommandResult.Fail($"Không tìm thấy view/view template \"{config.ViewTemplateName}\".");
            }
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ tạo/cập nhật {plan.Count} filter" + (view != null ? $" và áp vào \"{view.Name}\"." : ".");
            result.Messages.AddRange(plan.Select(p => $"{p.FilterName} → {p.Color}"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var existing = new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
            .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);
        var categoryIds = MepCategories.All.Select(c => new ElementId(c)).ToList();
        var solid = new FilteredElementCollector(document).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
        var systemNameParamId = new ElementId(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);

        var applied = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Màu theo hệ");
        foreach (var (system, filterName, rgb) in plan)
        {
            try
            {
                var rule = RevitCompat.CreateContainsRule(systemNameParamId, system);
                var elementFilter = new ElementParameterFilter(rule);

                if (!existing.TryGetValue(filterName, out var filter))
                {
                    filter = ParameterFilterElement.Create(document, filterName, categoryIds, elementFilter);
                    existing[filterName] = filter;
                    result.Messages.Add($"Tạo filter {filterName}.");
                }
                else
                {
                    filter.SetCategories(categoryIds);
                    filter.SetElementFilter(elementFilter);
                    result.Messages.Add($"Cập nhật filter {filterName}.");
                }

                if (view != null)
                {
                    var color = new Color(rgb.R, rgb.G, rgb.B);
                    var ogs = new OverrideGraphicSettings()
                        .SetProjectionLineColor(color)
                        .SetCutLineColor(color);
                    if (config.FillSurfaces && solid != null)
                    {
                        ogs.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(color)
                           .SetCutForegroundPatternId(solid.Id).SetCutForegroundPatternColor(color);
                    }

                    if (!view.GetFilters().Contains(filter.Id))
                    {
                        view.AddFilter(filter.Id);
                    }

                    view.SetFilterOverrides(filter.Id, ogs);
                    view.SetFilterVisibility(filter.Id, true);
                }

                applied++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{filterName}: {ex.Message}");
            }
        }

        tx.Commit();
        result.Summary = $"Đã tạo/áp {applied}/{plan.Count} filter màu theo hệ" + (view != null ? $" vào \"{view.Name}\"." : ".");
        result.AffectedCount = applied;
        return result;
    }
}

/// <summary>Đặt System Name cho MEPSystem theo quy tắc {Discipline}-{Abbr}-{Zone}-{N}, đếm riêng từng loại hệ.</summary>
public sealed class SystemNameCommand : ICoreCommand<SystemNameConfig>
{
    public string CommandName => "SystemName";

    public CommandResult Execute(Document document, SystemNameConfig config)
    {
        var systems = new FilteredElementCollector(document).OfClass(typeof(MEPSystem)).Cast<MEPSystem>()
            .Where(s => s.Category != null)
            .OrderBy(s => RevitCompat.IdValue(s.Id))
            .ToList();
        if (systems.Count == 0)
        {
            return CommandResult.Fail("Mô hình không có MEP system nào.");
        }

        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<(MEPSystem System, string NewName)>();
        var result = CommandResult.Ok(string.Empty);

        foreach (var sys in systems)
        {
            var typeName = document.GetElement(sys.GetTypeId())?.Name ?? sys.Category!.Name;
            var abbr = SystemNaming.Abbreviate(typeName, config.Abbreviations);
            counters[abbr] = counters.TryGetValue(abbr, out var n) ? n + 1 : 1;
            var newName = SystemNaming.Build(config.Discipline, abbr, config.Zone, counters[abbr], config.PadWidth);

            if (config.OnlyDefaultNames && !LooksDefault(sys.Name, typeName))
            {
                result.Messages.Add($"Giữ \"{sys.Name}\" (đã đặt tay).");
                continue;
            }

            if (string.Equals(sys.Name, newName, StringComparison.Ordinal))
            {
                continue;
            }

            plan.Add((sys, newName));
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ đổi tên {plan.Count}/{systems.Count} hệ.";
            result.Messages.AddRange(plan.Select(p => $"{p.System.Name} → {p.NewName}"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var renamed = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Đặt tên hệ");
        foreach (var (sys, newName) in plan)
        {
            try
            {
                sys.Name = newName;
                renamed++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{sys.Name}: {ex.Message}");
            }
        }

        tx.Commit();
        result.Summary = $"Đã đổi tên {renamed}/{plan.Count} hệ.";
        result.AffectedCount = renamed;
        return result;
    }

    /// <summary>Tên mặc định Revit sinh: "Mechanical Supply Air 12", "Domestic Cold Water 3", hoặc chữ viết tắt + số.</summary>
    internal static bool LooksDefault(string? name, string typeName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var trimmed = name!.Trim();
        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace < 0)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[A-Za-z]{1,4}\d{1,4}$");
        }

        var head = trimmed.Substring(0, lastSpace);
        var tail = trimmed.Substring(lastSpace + 1);
        return int.TryParse(tail, out _) && (typeName.IndexOf(head, StringComparison.OrdinalIgnoreCase) >= 0 || head.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0 || head.Length <= 4);
    }
}
