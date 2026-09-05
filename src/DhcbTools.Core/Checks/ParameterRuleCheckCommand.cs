using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Checks;

namespace DhcbTools.Core.Checks;

/// <summary>Mục 4.2 — kiểm tra tham số thiếu / sai quy tắc đặt tên theo bộ quy tắc JSON, báo cáo HTML.</summary>
public sealed class ParameterRuleCheckConfig
{
    /// <summary>File JSON: mảng {category, parameter, required, pattern, allowedValues, severity}.</summary>
    public required string RulesPath { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>Tạo 3D view tô sáng phần tử vi phạm — thao tác GHI duy nhất của lệnh, chỉ chạy khi <see cref="DryRun"/> = false.</summary>
    public bool Create3dView { get; init; } = false;

    public string ViewName { get; init; } = "DHCB - Rule Violations";

    /// <summary>Xem trước: kiểm và ghi báo cáo như thường, nhưng không tạo 3D view trong mô hình.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>
    /// File BCF 2.1 (tuỳ chọn) — mỗi phần tử vi phạm một vấn đề. Không có camera vì vi phạm tham số
    /// không có toạ độ; máy đọc BCF vẫn chọn được đúng phần tử qua IFC GUID.
    /// </summary>
    public string? BcfPath { get; init; }
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

        var violations = new List<RuleViolation>();
        var violatingIds = new HashSet<ElementId>();
        var checkedCount = 0;

        List<ParameterRule> rules;
        try
        {
            rules = RuleChecker.ParseRules(File.ReadAllText(config.RulesPath));
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("File quy tắc không hợp lệ: " + ex.Message);
        }

        // Chỉ giữ quy tắc tham số có category; quy tắc ngưỡng (metric) đọc riêng — cùng một file checkset (mục 7.7).
        rules = rules.Where(r => !string.IsNullOrWhiteSpace(r.Category)).ToList();
        var thresholds = ThresholdRule.Parse(File.ReadAllText(config.RulesPath));
        if (rules.Count == 0 && thresholds.Count == 0)
        {
            return CommandResult.Fail("File quy tắc rỗng.");
        }

        var result = CommandResult.Ok(string.Empty);
        if (thresholds.Count > 0)
        {
            var notes = new List<string>();
            var metrics = CollectMetrics(document, notes);
            violations.AddRange(ThresholdRule.Evaluate(thresholds, metrics, notes));
            result.Messages.AddRange(notes);
            result.Messages.Add("Số đo mô hình: " + string.Join(", ", metrics.Select(m => m.Key + "=" + Shared.Logic.NumericText.Format(m.Value, 1))));
        }
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

        if (config.Create3dView && violatingIds.Count > 0 && config.DryRun)
        {
            result.Messages.Add($"[Xem trước] Sẽ tạo/ghi đè 3D view \"{config.ViewName}\" isolate {violatingIds.Count} phần tử vi phạm.");
        }
        else if (config.Create3dView && violatingIds.Count > 0)
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

        WriteBcf(document, config, violations, result);

        foreach (var kv in RuleChecker.CountByCategory(violations).OrderByDescending(k => k.Value))
        {
            result.Messages.Add($"{kv.Key}: {kv.Value} vi phạm");
        }

        result.Summary = $"Đã kiểm {checkedCount} giá trị + {thresholds.Count} ngưỡng, {violations.Count} vi phạm trên {violatingIds.Count} phần tử → \"{config.OutputPath}\".";
        result.AffectedCount = violations.Count;
        return result;
    }

    /// <summary>
    /// Một topic cho mỗi <b>phần tử</b> vi phạm, không phải mỗi vi phạm: một cửa thiếu ba tham số là một
    /// việc phải sửa, không phải ba việc — người nhận BCF đọc theo phần tử.
    /// </summary>
    private static void WriteBcf(Document document, ParameterRuleCheckConfig config, List<RuleViolation> violations, CommandResult result)
    {
        if (string.IsNullOrWhiteSpace(config.BcfPath))
        {
            return;
        }

        var byElement = violations
            .Where(v => !string.IsNullOrEmpty(v.ElementId))
            .GroupBy(v => v.ElementId, StringComparer.Ordinal)
            .ToList();

        var topics = new List<Shared.Logic.Bcf.BcfTopic>();
        foreach (var group in byElement.Take(RevitBcf.MaxTopics))
        {
            var first = group.First();
            var element = RevitCompat.TryParseId(group.Key, out var id) ? document.GetElement(id) : null;
            var name = element?.Name ?? first.ElementName;

            var topic = new Shared.Logic.Bcf.BcfTopic($"Tham số chưa đạt: {first.Category} {name}".Trim())
            {
                TopicType = "Issue",
                TopicStatus = "Open",
                Priority = group.Any(v => string.Equals(v.Severity, "error", StringComparison.OrdinalIgnoreCase)) ? "High" : "Normal",
                Description = $"Phần tử {group.Key} ({first.Category}) có {group.Count()} vi phạm:"
                    + string.Concat(group.Take(20).Select(v => "\n- " + v.Parameter + ": " + v.Reason)),
            };

            topic.Labels.Add(first.Category);

            var component = RevitBcf.ComponentOf(element);
            if (component != null)
            {
                topic.Components.Add(component);
            }

            topics.Add(topic);
        }

        RevitBcf.Write(config.BcfPath, topics, byElement.Count, result);
    }

    /// <summary>Số đo mô hình cho checkset (Autodesk Model Checker style).</summary>
    /// <summary>
    /// Số đo mô hình cho checkset. Trước đây mỗi nhóm có <c>catch { }</c> rỗng riêng — một model hỏng
    /// (link lỗi, family corrupt…) cho ra báo cáo "sạch" vì thiếu số liệu mà không ai biết. Nay mỗi lỗi
    /// được ghi vào <paramref name="notes"/> để hiện trong <c>CommandResult.Messages</c>.
    /// </summary>
    internal static Dictionary<string, double> CollectMetrics(Document doc, List<string>? notes = null)
    {
        var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Try(string label, Action run)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                notes?.Add($"Không tính được số đo \"{label}\": {ex.Message}");
            }
        }

        Try("warnings", () => m["warnings"] = doc.GetWarnings().Count);
        Try("views/sheets", () =>
        {
            var placed = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(v => v.ViewId));
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v is not ViewSheet && v.CanBePrinted).ToList();
            m["views"] = views.Count;
            m["unplacedViews"] = views.Count(v => !placed.Contains(v.Id));
            m["sheets"] = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
        });
        Try("elements", () => m["elements"] = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount());
        Try("families", () =>
        {
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();
            m["families"] = families.Count;
            m["inPlaceFamilies"] = families.Count(f => f.IsInPlace);
            var usedFamilies = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Select(i => i.Symbol.Family.Id));
            m["unusedFamilies"] = families.Count(f => !usedFamilies.Contains(f.Id));
        });
        Try("links", () =>
        {
            var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>().ToList();
            m["links"] = links.Count;
            m["missingLinks"] = links.Count(l => l.GetLinkedFileStatus() != LinkedFileStatus.Loaded);
        });
        Try("cadImports", () => m["cadImports"] = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).GetElementCount());
        Try("fileSizeMb", () =>
        {
            if (!string.IsNullOrEmpty(doc.PathName) && File.Exists(doc.PathName))
            {
                m["fileSizeMb"] = new FileInfo(doc.PathName).Length / (1024.0 * 1024.0);
            }
        });

        return m;
    }
}
