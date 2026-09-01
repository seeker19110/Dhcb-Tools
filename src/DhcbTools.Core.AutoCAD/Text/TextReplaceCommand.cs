using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD.Text;

/// <summary>Tìm/thay văn bản hàng loạt trong DBText, MText, AttributeReference (kể cả trong layout).</summary>
public sealed class TextReplaceConfig
{
    public required string Find { get; init; }

    public string Replace { get; init; } = string.Empty;

    public bool UseRegex { get; init; }

    public bool CaseSensitive { get; init; }

    /// <summary>Lọc theo layer chứa chuỗi (rỗng = mọi layer).</summary>
    public string? LayerContains { get; init; }

    public bool IncludeText { get; init; } = true;

    public bool IncludeMText { get; init; } = true;

    public bool IncludeAttributes { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

public sealed class TextReplaceCommand : ICoreCommand<TextReplaceConfig>
{
    public string CommandName => "TextReplace";

    public CommandResult Execute(Database database, TextReplaceConfig config)
    {
        if (string.IsNullOrEmpty(config.Find))
        {
            return CommandResult.Fail("Thiếu chuỗi cần tìm (find).");
        }

        Regex regex;
        try
        {
            var options = config.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            regex = new Regex(config.UseRegex ? config.Find : Regex.Escape(config.Find), options);
        }
        catch (ArgumentException ex)
        {
            return CommandResult.Fail("Regex không hợp lệ: " + ex.Message);
        }

        var replacement = config.UseRegex ? config.Replace : config.Replace.Replace("$", "$$");
        var result = CommandResult.Ok(string.Empty);
        var plan = new List<(ObjectId Id, string Kind, string Old, string New)>();

        using var tr = database.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!btr.IsLayout) continue;

            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (!string.IsNullOrEmpty(config.LayerContains) && ent.Layer.IndexOf(config.LayerContains!, StringComparison.OrdinalIgnoreCase) < 0) continue;

                switch (ent)
                {
                    case DBText t when config.IncludeText:
                        Consider(id, "Text", t.TextString);
                        break;
                    case MText m when config.IncludeMText:
                        Consider(id, "MText", m.Contents);
                        break;
                    case BlockReference br when config.IncludeAttributes:
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                            Consider(attId, "Attribute " + att.Tag, att.TextString);
                        }
                        break;
                }
            }
        }

        void Consider(ObjectId id, string kind, string text)
        {
            if (text == null || !regex.IsMatch(text)) return;
            var replaced = regex.Replace(text, replacement);
            if (!string.Equals(replaced, text, StringComparison.Ordinal))
            {
                plan.Add((id, kind, text, replaced));
            }
        }

        if (config.DryRun)
        {
            tr.Abort();
            result.Summary = $"[Xem trước] Sẽ thay {plan.Count} chỗ.";
            result.Messages.AddRange(plan.Take(300).Select(p => $"{p.Kind} {p.Id.Handle}: \"{Short(p.Old)}\" → \"{Short(p.New)}\""));
            result.AffectedCount = plan.Count;
            return result;
        }

        var done = 0;
        foreach (var (id, kind, _, newText) in plan)
        {
            try
            {
                var obj = tr.GetObject(id, OpenMode.ForWrite);
                switch (obj)
                {
                    case AttributeReference a: a.TextString = newText; break; // AttributeReference kế thừa DBText — xét trước
                    case DBText t: t.TextString = newText; break;
                    case MText m: m.Contents = newText; break;
                    default: continue;
                }
                done++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{kind} {id.Handle}: {ex.Message}");
            }
        }

        tr.Commit();
        result.Summary = $"Đã thay {done}/{plan.Count} chỗ.";
        result.AffectedCount = done;
        return result;
    }

    private static string Short(string s) => s.Length > 60 ? s.Substring(0, 60) + "…" : s;
}
