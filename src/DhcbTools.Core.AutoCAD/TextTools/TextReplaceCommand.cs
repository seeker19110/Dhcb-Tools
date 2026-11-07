using System.Diagnostics;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.TextTools;

/// <summary>
/// Tìm/thay văn bản trong DBText, MText và AttributeReference — duyệt mọi Block Table Record
/// (Model Space, Paper Space và cả block definition) chứ không chỉ Model Space, vì text hay nằm
/// trong block định nghĩa (title block, ghi chú lặp lại). Bỏ qua block của xref và block anonymous.
/// <para>
/// <b>MText:</b> so khớp trên CHỮ HIỂN THỊ và chỉ thay chữ — không bao giờ sửa vào mã định dạng (\P, \pxqc;,
/// {\H0.7x;…}); xem <see cref="MTextReplace"/>. Bản trước thay thẳng trên <c>Contents</c>: regex <c>\d+</c> đổi luôn
/// chữ số trong <c>\H2.5x;</c>, tìm "PL" trúng <c>\PLine</c> — hỏng định dạng không báo. Chuỗi khớp nằm vắt qua một
/// mốc định dạng ("DHCB" viết nửa đậm nửa thường) thì không tự sửa — gộp lại sẽ xoá định dạng của một phần — mà BÁO
/// ra để kỹ sư xử lý tay. Không báo thành công im lặng.
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

        if (!Enum.IsDefined(typeof(TextReplaceScope), config.Scope))
            return CommandResult.Fail("Scope phải là All, ModelSpace, PaperSpace hoặc BlockDefinitions.");
        if (config.MaxPreviewItems < 0 || config.MaxPreviewItems > 10000 || config.TimeBudgetSeconds < 1 || config.TimeBudgetSeconds > 600)
            return CommandResult.Fail("MaxPreviewItems phải từ 0 đến 10000; TimeBudgetSeconds phải từ 1 đến 600.");
        if ((!config.IncludeDbText && !config.IncludeMText && !config.IncludeAttributes)
            || (!string.IsNullOrEmpty(config.AttributeTag) && !config.IncludeAttributes))
            return CommandResult.Fail("Chưa chọn loại văn bản để tìm; AttributeTag cần IncludeAttributes=true.");

        var watch = Stopwatch.StartNew();
        bool OverBudget() => watch.Elapsed.TotalSeconds > config.TimeBudgetSeconds;
        CommandResult BudgetExceeded() => CommandResult.Fail(
            $"E-TEXT-BUDGET: Vượt ngân sách {config.TimeBudgetSeconds} giây khi lập kế hoạch; chưa ghi đối tượng nào. Thu hẹp Scope/LayerContains hoặc đơn giản hoá regex.");

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
        var formattingSkips = 0;
        var fieldSkips = 0;
        var omittedNotes = 0;
        void Note(string message)
        {
            if (formattingNotes.Count < config.MaxPreviewItems) formattingNotes.Add(message);
            else omittedNotes++;
        }

        using var transaction = database.TransactionManager.StartTransaction();
        var lockedLayers = AcadHelpers.LockedLayerIds(database, transaction);
        var lockedSkips = new LockedLayerSkips();

        // Đưa một đối tượng vào kế hoạch — trừ khi nó nằm trên layer khoá: mở ForWrite sẽ ném eOnLockedLayer và sập
        // cả lệnh (bản cũ). Bỏ qua ngay lúc lập kế hoạch nên xem trước và chạy thật cùng một con số, như FIND của AutoCAD.
        void AddToPlan(Entity entity, ObjectId id, string kind, string oldValue, string newValue, string? lockedParentLayer = null)
        {
            if (entity.HasFields)
            {
                fieldSkips++;
                return;
            }
            if (lockedParentLayer != null || lockedLayers.Contains(entity.LayerId))
            {
                lockedSkips.Add(lockedParentLayer ?? entity.Layer);
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

        MTextReplaceResult SubstituteMText(string contents)
        {
            try
            {
                return regex is not null
                    ? MTextReplace.Replace(contents, regex, config.Replace)
                    : MTextReplace.ReplaceAll(contents, config.Find, config.Replace, config.IgnoreCase);
            }
            catch (RegexMatchTimeoutException)
            {
                // Như Substitute: một chuỗi quá lâu không làm hỏng cả lệnh — giữ nguyên và báo ở cuối.
                timeouts++;
                return new MTextReplaceResult(contents, 0, 0);
            }
        }

        var modelSpace = SymbolUtilityServices.GetBlockModelSpaceId(database);
        bool LayerMatches(Entity entity, BlockReference? parent = null)
        {
            var layer = parent != null && entity.Layer == "0" ? parent.Layer : entity.Layer;
            return string.IsNullOrEmpty(config.LayerContains)
                || layer.IndexOf(config.LayerContains, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        bool TagMatches(string tag) => string.IsNullOrEmpty(config.AttributeTag)
            || string.Equals(tag, config.AttributeTag, StringComparison.OrdinalIgnoreCase);

        void PlanPlain(Entity entity, ObjectId id, string kind, string value, string? lockedParent = null)
        {
            var replaced = Substitute(value);
            if (replaced != value) AddToPlan(entity, id, kind, value, replaced, lockedParent);
        }
        void PlanMText(Entity entity, ObjectId id, string kind, string value, string? lockedParent = null)
        {
            var outcome = SubstituteMText(value);
            if (outcome.Contents != value) AddToPlan(entity, id, kind, value, outcome.Contents, lockedParent);
            if (outcome.Skipped > 0)
            {
                formattingSkips += outcome.Skipped;
                Note($"[{kind}] {AcadHelpers.HandleOf(id)}: {outcome.Skipped} chỗ khớp vắt qua mã định dạng hoặc field — không thay, cần sửa tay.");
            }
        }

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blockTable)
        {
            if (OverBudget()) return BudgetExceeded();
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            if (AcadHelpers.IsProtectedBlock(block)
                || (config.Scope == TextReplaceScope.ModelSpace && blockId != modelSpace)
                || (config.Scope == TextReplaceScope.PaperSpace && (!block.IsLayout || blockId == modelSpace))
                || (config.Scope == TextReplaceScope.BlockDefinitions && block.IsLayout))
                continue;

            foreach (ObjectId entityId in block)
            {
                if (OverBudget()) return BudgetExceeded();
                var entity = transaction.GetObject(entityId, OpenMode.ForRead);
                switch (entity)
                {
                    case AttributeDefinition definition when config.IncludeAttributes && TagMatches(definition.Tag) && LayerMatches(definition):
                        if (definition.IsMTextAttributeDefinition)
                        {
                            using var mt = definition.MTextAttributeDefinition;
                            PlanMText(definition, entityId, "MTextAttributeDefinition", mt.Contents);
                        }
                        else PlanPlain(definition, entityId, "AttributeDefinition", definition.TextString);
                        break;
                    case DBText text when entity is not AttributeDefinition && config.IncludeDbText
                        && string.IsNullOrEmpty(config.AttributeTag) && LayerMatches(text):
                        PlanPlain(text, entityId, "DBText", text.TextString);
                        break;
                    case MText mtext when config.IncludeMText && string.IsNullOrEmpty(config.AttributeTag) && LayerMatches(mtext):
                        PlanMText(mtext, entityId, "MText", mtext.Contents);
                        break;
                    case BlockReference blockRef when config.IncludeAttributes:
                        foreach (ObjectId attId in blockRef.AttributeCollection)
                        {
                            if (OverBudget()) return BudgetExceeded();
                            var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);
                            if (!TagMatches(attRef.Tag) || !LayerMatches(attRef, blockRef)) continue;
                            var lockedParent = lockedLayers.Contains(blockRef.LayerId) ? blockRef.Layer : null;
                            if (attRef.IsMTextAttribute)
                            {
                                using var mt = attRef.MTextAttribute;
                                PlanMText(attRef, attId, "MTextAttribute", mt.Contents, lockedParent);
                            }
                            else PlanPlain(attRef, attId, "Attribute", attRef.TextString, lockedParent);
                        }
                        break;
                }
            }
        }
        // The final match may consume its entire per-string regex timeout.
        if (OverBudget()) return BudgetExceeded();
        if (fieldSkips > 0)
            formattingNotes.Add($"{fieldSkips} đối tượng có field động được giữ nguyên để bảo toàn liên kết/công thức.");
        if (omittedNotes > 0)
            formattingNotes.Add($"{omittedNotes} chi tiết bỏ qua khác đã được lược bớt (MaxPreviewItems={config.MaxPreviewItems}).");

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
            var none = CommandResult.Ok(lockedSkips.Count > 0 || timeouts > 0 || formattingSkips > 0 || fieldSkips > 0
                ? "Không có văn bản nào thay được — xem lý do bên dưới."
                : "Không tìm thấy văn bản nào khớp để thay.");
            none.Messages.AddRange(formattingNotes);
            return none.WithIncompleteWork(lockedSkips.Count > 0 || timeouts > 0 || formattingSkips > 0 || fieldSkips > 0);
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ thay {plan.Count} đối tượng văn bản.",
                plan.Count);
            foreach (var (id, kind, oldValue, newValue) in plan.Take(config.MaxPreviewItems))
            {
                preview.Messages.Add($"[{kind}] {AcadHelpers.HandleOf(id)}: \"{Snippet(oldValue)}\" → \"{Snippet(newValue)}\"");
            }
            if (plan.Count > config.MaxPreviewItems)
                preview.Messages.Add($"Đã lược bớt {plan.Count - config.MaxPreviewItems} chi tiết; tổng số thay đổi vẫn là {plan.Count}.");
            preview.Messages.AddRange(formattingNotes);
            return preview.WithIncompleteWork(lockedSkips.Count > 0 || timeouts > 0 || formattingSkips > 0 || fieldSkips > 0);
        }

        foreach (var (id, kind, _, newValue) in plan)
        {
            var obj = transaction.GetObject(id, OpenMode.ForWrite);
            switch (obj)
            {
                // AttributeReference kế thừa DBText trong AutoCAD API — phải khớp trước DBText,
                // nếu không case DBText sẽ "nuốt" mất case này (CS8120: unreachable).
                case AttributeReference attRef when kind == "MTextAttribute":
                    using (var mt = attRef.MTextAttribute)
                    {
                        mt.Contents = newValue;
                        attRef.MTextAttribute = mt;
                        attRef.UpdateMTextAttribute();
                    }
                    break;
                case AttributeDefinition definition when kind == "MTextAttributeDefinition":
                    using (var mt = definition.MTextAttributeDefinition)
                    {
                        mt.Contents = newValue;
                        definition.MTextAttributeDefinition = mt;
                        definition.UpdateMTextAttributeDefinition();
                    }
                    break;
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
        return result.WithIncompleteWork(lockedSkips.Count > 0 || timeouts > 0 || formattingSkips > 0 || fieldSkips > 0);
    }

    private static string Snippet(string value) => value.Length > 2000 ? value.Substring(0, 2000) + "…" : value;

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
}
