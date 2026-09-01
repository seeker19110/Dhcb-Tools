using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Checks;

namespace DhcbTools.Core.Checks;

/// <summary>Mục 4.2 — kiểm tra tham số thiếu / sai quy tắc đặt tên theo bộ quy tắc JSON, báo cáo HTML.</summary>
public sealed class ParameterRuleCheckConfig
{
    /// <summary>File JSON: mảng {category, parameter, required, pattern, allowedValues, severity}.</summary>
    public required string RulesPath { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>Tạo 3D view tô sáng phần tử vi phạm.</summary>
    public bool Create3dView { get; init; } = false;

    public string ViewName { get; init; } = "DHCB - Rule Violations";
}

public sealed class ParameterRuleCheckCommand : ICoreCommand<ParameterRuleCheckConfig>
{
    public string CommandName => "ParameterRuleCheck";

    public CommandResult Execute(Document document, ParameterRuleCheckConfig config)
    {
        if (!File.Exists(config.RulesPath))
        {
            return CommandResult.Fail($"Không tìm thấy file quy tắc \"{config.RulesPath}\".");
        }

        List<ParameterRule> rules;
        try
        {
            rules = RuleChecker.ParseRules(File.ReadAllText(config.RulesPath));
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("File quy tắc không hợp lệ: " + ex.Message);
        }

        if (rules.Count == 0)
        {
            return CommandResult.Fail("File quy tắc rỗng.");
        }

        var result = CommandResult.Ok(string.Empty);
        var violations = new List<RuleViolation>();
        var violatingIds = new HashSet<ElementId>();
        var checkedCount = 0;

        foreach (var group in rules.GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase))
        {
            var ids = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, new[] { group.Key }, out var unknown);
            if (ids.Count == 0)
            {
                result.Messages.Add($"Category \"{group.Key}\" không có trong mô hình — bỏ qua {group.Count()} quy tắc.");
                continue;
            }

            var elements = new FilteredElementCollector(document).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(ids.ToList())).ToElements();
            foreach (var el in elements)
            {
                foreach (var rule in group)
                {
                    var value = RevitCompat.ReadString(el, rule.Parameter);
                    checkedCount++;
                    var reason = RuleChecker.Check(rule, value);
                    if (reason != null)
                    {
                        violations.Add(new RuleViolation(group.Key, RevitCompat.IdValue(el.Id).ToString(), el.Name ?? string.Empty, rule.Parameter, value, reason + (rule.Description != null ? " — " + rule.Description : string.Empty), rule.Severity));
                        violatingIds.Add(el.Id);
                    }
                }
            }
        }

        var html = RuleChecker.RenderHtml($"DHCB - Kiểm tra tham số: {document.Title}", violations, checkedCount);
        var dir = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(config.OutputPath, html, System.Text.Encoding.UTF8);

        if (config.Create3dView && violatingIds.Count > 0)
        {
            try
            {
                using var tx = RevitCompat.StartTransaction(document, "DHCB - View vi phạm quy tắc");
                var vft = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                var view = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && v.Name == config.ViewName)
                           ?? (vft != null ? View3D.CreateIsometric(document, vft.Id) : null);
                if (view != null)
                {
                    try { view.Name = config.ViewName; } catch { /* trùng tên */ }
                    view.IsolateElementsTemporary(violatingIds.ToList());
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                result.Messages.Add("Không tạo được 3D view: " + ex.Message);
            }
        }

        foreach (var kv in RuleChecker.CountByCategory(violations).OrderByDescending(k => k.Value))
        {
            result.Messages.Add($"{kv.Key}: {kv.Value} vi phạm");
        }

        result.Summary = $"Đã kiểm {checkedCount} giá trị, {violations.Count} vi phạm trên {violatingIds.Count} phần tử → \"{config.OutputPath}\".";
        result.AffectedCount = violations.Count;
        return result;
    }
}
