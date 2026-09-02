using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD.TextTools;

/// <summary>
/// Tìm/thay văn bản trong DBText, MText và AttributeReference — duyệt mọi Block Table Record
/// (Model Space, Paper Space và cả block definition) chứ không chỉ Model Space, vì text hay nằm
/// trong block định nghĩa (title block, ghi chú lặp lại).
/// </summary>
public sealed class TextReplaceCommand : ICoreCommand<TextReplaceConfig>
{
    public string CommandName => "TextReplace";

    public CommandResult Execute(Database database, TextReplaceConfig config)
    {
        Regex? regex = null;
        if (config.UseRegex)
        {
            try
            {
                regex = new Regex(config.Find);
            }
            catch (ArgumentException ex)
            {
                return CommandResult.Fail($"Regex không hợp lệ: {ex.Message}");
            }
        }

        var plan = new List<(ObjectId Id, string Kind, string OldValue, string NewValue)>();

        using var transaction = database.TransactionManager.StartTransaction();

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);

            foreach (ObjectId entityId in block)
            {
                var entity = transaction.GetObject(entityId, OpenMode.ForRead);

                switch (entity)
                {
                    case DBText text:
                        {
                            var replaced = Apply(text.TextString, config, regex);
                            if (replaced != text.TextString)
                            {
                                plan.Add((entityId, "DBText", text.TextString, replaced));
                            }
                            break;
                        }
                    case MText mtext:
                        {
                            var replaced = Apply(mtext.Contents, config, regex);
                            if (replaced != mtext.Contents)
                            {
                                plan.Add((entityId, "MText", mtext.Contents, replaced));
                            }
                            break;
                        }
                    case BlockReference blockRef:
                        {
                            foreach (ObjectId attId in blockRef.AttributeCollection)
                            {
                                var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);
                                var replaced = Apply(attRef.TextString, config, regex);
                                if (replaced != attRef.TextString)
                                {
                                    plan.Add((attId, "Attribute", attRef.TextString, replaced));
                                }
                            }
                            break;
                        }
                }
            }
        }

        if (plan.Count == 0)
        {
            transaction.Commit();
            return CommandResult.Ok("Không tìm thấy văn bản nào khớp để thay.");
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ thay {plan.Count} đối tượng văn bản.",
                plan.Count);
            foreach (var (id, kind, oldValue, newValue) in plan)
            {
                preview.Messages.Add($"[{kind}] {id}: \"{oldValue}\" → \"{newValue}\"");
            }
            return preview;
        }

        foreach (var (id, kind, _, newValue) in plan)
        {
            var obj = transaction.GetObject(id, OpenMode.ForWrite);
            switch (obj)
            {
                // AttributeReference kế thừa DBText trong AutoCAD API — phải khớp trước DBText,
                // nếu không case DBText sẽ "nuốt" mất case này (CS8120: unreachable).
                case AttributeReference attRef:
                    attRef.TextString = newValue;
                    break;
                case DBText text:
                    text.TextString = newValue;
                    break;
                case MText mtext:
                    mtext.Contents = newValue;
                    break;
            }
        }

        transaction.Commit();

        return CommandResult.Ok($"Đã thay {plan.Count} đối tượng văn bản.", plan.Count);
    }

    private static string Apply(string value, TextReplaceConfig config, Regex? regex)
    {
        return regex is not null
            ? regex.Replace(value, config.Replace)
            : value.Replace(config.Find, config.Replace);
    }
}
