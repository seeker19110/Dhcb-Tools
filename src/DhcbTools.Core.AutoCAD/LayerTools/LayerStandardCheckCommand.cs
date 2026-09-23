using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
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
            rules = LayerRuleSet.Parse(File.ReadAllText(config.RulesPath));
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

        var html = LayerRuleSet.Html(allLayers, invalidLayers, rules);
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
}
