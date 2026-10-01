using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic.Cad;

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
        var timeouts = 0;

        using var transaction = database.TransactionManager.StartTransaction();
        var lockedLayers = AcadHelpers.LockedLayerIds(database, transaction);
        var lockedSkips = new LockedLayerSkips();

        // Đưa một đối tượng vào kế hoạch — trừ khi nó nằm trên layer khoá: mở ForWrite sẽ ném eOnLockedLayer và sập
        // cả lệnh (bản cũ). Bỏ qua ngay lúc lập kế hoạch nên xem trước và chạy thật cùng một con số, như FIND của AutoCAD.
        void AddToPlan(Entity entity, ObjectId id, string kind, string oldValue, string newValue)
        {
            if (lockedLayers.Contains(entity.LayerId))
            {
                lockedSkips.Add(entity.Layer);
                return;
            }

            plan.Add((id, kind, oldValue, newValue));
        }

        string Substitute(string value)
        {
            var replaced = Apply(value, config, regex, out var timedOut);
            if (timedOut)
            {
                timeouts++;
            }

            return replaced;
        }

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
                            var replaced = Substitute(text.TextString);
                            if (replaced != text.TextString)
                            {
                                AddToPlan(text, entityId, "DBText", text.TextString, replaced);
                            }
                            break;
                        }
                    case MText mtext:
                        {
                            var replaced = Substitute(mtext.Contents);
                            if (replaced != mtext.Contents)
                            {
                                AddToPlan(mtext, entityId, "MText", mtext.Contents, replaced);
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
                                var replaced = Substitute(attRef.TextString);
                                if (replaced != attRef.TextString)
                                {
                                    AddToPlan(attRef, attId, "Attribute", attRef.TextString, replaced);
                                }
                            }
                            break;
                        }
                }
            }
        }

        if (lockedSkips.Message("đối tượng văn bản") is { } lockedNote)
        {
            formattingNotes.Insert(0, lockedNote);
        }

        if (timeouts > 0)
        {
            // Bản cũ giữ nguyên im lặng: xem trước "sẽ thay N" trong khi có chuỗi khớp mà không được thay.
            formattingNotes.Insert(0,
                $"{timeouts} chuỗi văn bản bị bỏ qua vì regex chạy quá {MatchTimeout.TotalSeconds:0} giây — đơn giản hoá biểu thức rồi chạy lại.");
        }

        if (plan.Count == 0)
        {
            transaction.Commit();
            var none = CommandResult.Ok(lockedSkips.Count > 0 || timeouts > 0
                ? "Không có văn bản nào thay được — xem lý do bên dưới."
                : "Không tìm thấy văn bản nào khớp để thay.");
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
                    // Như AttributeImport: attribute canh giữa/Fit/Aligned giữ hình học canh cũ sau khi đổi chữ.
                    attRef.AdjustAlignment(database);
                    break;
                case DBText text:
                    text.TextString = newValue;
                    text.AdjustAlignment(database);
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

    private static string Apply(string value, TextReplaceConfig config, Regex? regex, out bool timedOut)
    {
        timedOut = false;
        try
        {
            return regex is not null
                ? regex.Replace(value, config.Replace)
                : Shared.Logic.TextReplace.ReplaceAll(value, config.Find, config.Replace, config.IgnoreCase);
        }
        catch (RegexMatchTimeoutException)
        {
            // Một chuỗi quá lâu không được phép làm hỏng cả lệnh: giữ nguyên đối tượng đó — và BÁO (timedOut).
            timedOut = true;
            return value;
        }
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
