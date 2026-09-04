using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD.TextTools;

/// <summary>
/// Tìm/thay văn bản trong DBText, MText và AttributeReference — duyệt mọi Block Table Record
/// (Model Space, Paper Space và cả block definition) chứ không chỉ Model Space, vì text hay nằm
/// trong block định nghĩa (title block, ghi chú lặp lại). Bỏ qua block của xref và block anonymous.
/// <para>
/// <b>MText:</b> phép thay chạy trên <c>Contents</c> — chuỗi CÓ mã định dạng (\\pxqc;, {\\H0.7x;…}).
/// Vì vậy chuỗi cần tìm nằm vắt qua một mốc định dạng ("DHCB" viết nửa đậm nửa thường) sẽ KHÔNG khớp,
/// dù nhìn trên màn hình vẫn là "DHCB". Khi chuỗi không có trong <c>Contents</c> nhưng có trong
/// <c>Text</c> (bản đã bỏ định dạng), lệnh không tự sửa — vì ghi lại <c>Text</c> sẽ xoá sạch định dạng
/// của cả đối tượng — mà BÁO ra để kỹ sư xử lý tay. Không báo thành công im lặng.
/// </para>
/// </summary>
public sealed class TextReplaceCommand : ICoreCommand<TextReplaceConfig>
{
    public string CommandName => "TextReplace";

    /// <summary>Trần thời gian cho một phép so khớp regex — chặn regex "bùng nổ" (a+)+ treo cả AutoCAD.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    public CommandResult Execute(Database database, TextReplaceConfig config)
    {
        // Find rỗng: chuỗi thường thì string.Replace("") ném/không có nghĩa, regex thì khớp mọi vị trí rỗng
        // và chèn Replace vào giữa từng ký tự — cả hai đều là lỗi cấu hình, không phải ý định.
        if (string.IsNullOrEmpty(config.Find))
        {
            return CommandResult.Fail("Thiếu chuỗi cần tìm (find) — không được để rỗng.");
        }

        Regex? regex = null;
        if (config.UseRegex)
        {
            try
            {
                var options = RegexOptions.CultureInvariant
                    | (config.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None)
                    | (config.Multiline ? RegexOptions.Multiline : RegexOptions.None);
                regex = new Regex(config.Find, options, MatchTimeout);
            }
            catch (ArgumentException ex)
            {
                return CommandResult.Fail($"Regex không hợp lệ: {ex.Message}");
            }
        }

        var plan = new List<(ObjectId Id, string Kind, string OldValue, string NewValue)>();
        var formattingNotes = new List<string>();

        using var transaction = database.TransactionManager.StartTransaction();

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            if (AcadHelpers.IsProtectedBlock(block))
            {
                continue;
            }

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
                            else if (Matches(mtext.Text, config, regex))
                            {
                                // Có trong chuỗi đã bỏ định dạng nhưng không có trong Contents: chuỗi bị mã định dạng
                                // cắt ngang. Ghi đè Contents bằng Text sẽ xoá định dạng nên chỉ báo, không tự sửa.
                                formattingNotes.Add(
                                    $"[MText] {AcadHelpers.HandleOf(entityId)}: khớp trên nội dung hiển thị nhưng mã định dạng cắt ngang chuỗi — cần sửa tay.");
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
            var none = CommandResult.Ok("Không tìm thấy văn bản nào khớp để thay.");
            none.Messages.AddRange(formattingNotes);
            return none;
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ thay {plan.Count} đối tượng văn bản.",
                plan.Count);
            foreach (var (id, kind, oldValue, newValue) in plan)
            {
                preview.Messages.Add($"[{kind}] {AcadHelpers.HandleOf(id)}: \"{oldValue}\" → \"{newValue}\"");
            }
            preview.Messages.AddRange(formattingNotes);
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

        var result = CommandResult.Ok($"Đã thay {plan.Count} đối tượng văn bản.", plan.Count);
        result.Messages.AddRange(formattingNotes);
        return result;
    }

    private static string Apply(string value, TextReplaceConfig config, Regex? regex)
    {
        try
        {
            return regex is not null
                ? regex.Replace(value, config.Replace)
                : ReplaceAll(value, config.Find, config.Replace, config.IgnoreCase);
        }
        catch (RegexMatchTimeoutException)
        {
            // Một chuỗi quá lâu không được phép làm hỏng cả lệnh: giữ nguyên đối tượng đó.
            return value;
        }
    }

    /// <summary>Thay mọi lần xuất hiện. Tự viết vì <c>string.Replace(…, StringComparison)</c> không có trên net48 (AutoCAD ≤ 2024).</summary>
    internal static string ReplaceAll(string value, string find, string replace, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var sb = new System.Text.StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var hit = value.IndexOf(find, index, comparison);
            if (hit < 0)
            {
                sb.Append(value, index, value.Length - index);
                break;
            }

            sb.Append(value, index, hit - index).Append(replace);
            index = hit + find.Length;
        }

        return sb.ToString();
    }

    private static bool Matches(string value, TextReplaceConfig config, Regex? regex)
    {
        try
        {
            return regex is not null
                ? regex.IsMatch(value)
                : value.IndexOf(config.Find, config.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) >= 0;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
