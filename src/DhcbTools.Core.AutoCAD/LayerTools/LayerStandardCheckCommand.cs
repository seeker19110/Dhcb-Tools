using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using Newtonsoft.Json;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Kiểm tra tên layer theo bộ quy tắc (regex) đọc từ file JSON — layer hợp lệ nếu tên khớp ít nhất
/// một pattern. Xuất báo cáo HTML, layer không hợp lệ tô đỏ.
/// </summary>
public sealed class LayerStandardCheckCommand : ICoreCommand<LayerStandardCheckConfig>
{
    public string CommandName => "LayerStandardCheck";

    public CommandResult Execute(Database database, LayerStandardCheckConfig config)
    {
        if (!File.Exists(config.RulesPath))
        {
            return CommandResult.Fail($"Không tìm thấy file quy tắc: \"{config.RulesPath}\".");
        }

        List<LayerNamingRule>? rules;
        try
        {
            rules = ParseRules(File.ReadAllText(config.RulesPath));
        }
        catch (JsonException ex)
        {
            return CommandResult.Fail($"File quy tắc không hợp lệ: {ex.Message}");
        }

        if (rules is null || rules.Count == 0)
        {
            return CommandResult.Fail("File quy tắc rỗng hoặc không đọc được.");
        }

        // Một quy tắc thiếu "pattern" từng biến thành Regex("") — khớp MỌI tên layer. Vì layer chỉ
        // cần khớp một quy tắc là hợp lệ, đúng một dòng thiếu pattern đủ để cả phép kiểm tra luôn
        // báo "0 sai chuẩn". Với một công cụ kiểm tra thì im lặng bỏ sót còn tệ hơn là báo lỗi.
        var compiled = new List<(Regex Regex, string Description)>();
        var skipped = new List<string>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                skipped.Add(string.IsNullOrWhiteSpace(rule.Description) ? "(không mô tả)" : rule.Description);
                continue;
            }

            try
            {
                compiled.Add((new Regex(rule.Pattern), rule.Description));
            }
            catch (ArgumentException)
            {
                skipped.Add(rule.Pattern);
            }
        }

        if (compiled.Count == 0)
        {
            return CommandResult.Fail(
                "Không có quy tắc dùng được nào (thiếu \"pattern\" hoặc regex sai). "
                + "Nếu vẫn chạy thì mọi layer đều được coi là hợp lệ — nên dừng ở đây.");
        }

        var allLayers = new List<string>();
        var invalidLayers = new List<string>();

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId layerId in layerTable)
            {
                var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
                allLayers.Add(layer.Name);

                var isValid = compiled.Any(r => r.Regex.IsMatch(layer.Name));
                if (!isValid)
                {
                    invalidLayers.Add(layer.Name);
                }
            }

            transaction.Commit();
        }

        var html = BuildHtml(allLayers, invalidLayers, rules);
        AcadHelpers.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, html, Encoding.UTF8);

        var result = CommandResult.Ok(
            $"Đã kiểm tra {allLayers.Count} layer, {invalidLayers.Count} layer không đúng chuẩn. Báo cáo: \"{config.OutputPath}\".",
            invalidLayers.Count);

        foreach (var s in skipped)
        {
            result.Messages.Add($"Bỏ qua quy tắc không dùng được: {s}");
        }

        return result;
    }

    /// <summary>
    /// Chấp nhận cả hai dạng: mảng thuần <c>[{...}]</c> và object bọc <c>{"rules":[{...}]}</c>.
    /// File mẫu của repo (configs/layer-rules.sample.json) dùng dạng thứ hai, nên chỉ nhận dạng
    /// mảng thuần khiến người dùng làm theo đúng mẫu vẫn gặp lỗi deserialize.
    /// </summary>
    internal static List<LayerNamingRule>? ParseRules(string json)
    {
        var trimmed = json.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            var wrapper = JsonConvert.DeserializeObject<LayerRulesFile>(json);
            return wrapper?.Rules;
        }

        return JsonConvert.DeserializeObject<List<LayerNamingRule>>(json);
    }

    private static string BuildHtml(List<string> allLayers, List<string> invalidLayers, List<LayerNamingRule> rules)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Kiểm tra chuẩn layer</title>")
          .Append("<style>body{font-family:Arial,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}")
          .Append("th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}th{background:#f0f0f0}")
          .Append(".invalid{background:#ffdddd;color:#a00}.valid{background:#eaffea}</style></head><body>");

        sb.Append("<h1>Báo cáo kiểm tra chuẩn layer</h1>");
        sb.Append($"<p>Tổng số layer: {allLayers.Count} — Không đúng chuẩn: <b>{invalidLayers.Count}</b></p>");

        sb.Append("<h2>Quy tắc áp dụng</h2><ul>");
        foreach (var rule in rules)
        {
            sb.Append($"<li><code>{HtmlText.Escape(rule.Pattern)}</code> — {HtmlText.Escape(rule.Description)}</li>");
        }
        sb.Append("</ul>");

        sb.Append("<h2>Danh sách layer</h2><table><tr><th>Tên layer</th><th>Trạng thái</th></tr>");
        foreach (var name in allLayers)
        {
            var isInvalid = invalidLayers.Contains(name);
            var cssClass = isInvalid ? "invalid" : "valid";
            var status = isInvalid ? "Không đúng chuẩn" : "Hợp lệ";
            sb.Append($"<tr class=\"{cssClass}\"><td>{HtmlText.Escape(name)}</td><td>{status}</td></tr>");
        }
        sb.Append("</table></body></html>");

        return sb.ToString();
    }
}
