using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.AutoNumbering;

/// <summary>Yêu cầu đánh số Block Reference — phần chung của AutoNumbering và AttributeIncrement.</summary>
internal sealed class BlockNumberingRequest
{
    public required string BlockName { get; init; }

    /// <summary>Tag attribute nhận giá trị; null/rỗng = attribute đầu tiên của block.</summary>
    public string? AttributeTag { get; init; }

    public ScanDirection Direction { get; init; } = ScanDirection.LeftToRightThenTopToBottom;

    /// <summary>Dung sai gom hàng/cột (đơn vị bản vẽ; bản vẽ Việt Nam thường là mm).</summary>
    public double RowTolerance { get; init; } = 300.0;

    public int StartNumber { get; init; } = 1;

    public int Step { get; init; } = 1;

    /// <summary>Sinh nhãn từ số thứ tự (tiền tố + đệm 0, hoặc mẫu "{n:000}").</summary>
    public required Func<int, string> Label { get; init; }

    public bool DryRun { get; init; } = true;

    /// <summary>Câu tóm tắt xem trước, nhận số phần tử.</summary>
    public required Func<int, string> PreviewSummary { get; init; }

    /// <summary>Câu tóm tắt sau khi ghi, nhận (số đã ghi, tổng).</summary>
    public required Func<int, int, string> DoneSummary { get; init; }
}

/// <summary>
/// Đánh số Block Reference theo vị trí — dùng <see cref="NumberingPlanner"/> của Shared.Logic y như Revit.
/// Trước đây AutoNumbering và AttributeIncrement là hai bản chép ~90 % giống nhau, cùng sắp
/// <c>OrderByDescending(Y).ThenBy(X)</c> không dung sai hàng: hai block cùng hàng lệch 1 mm rơi vào hai
/// hàng khác nhau và thứ tự trái→phải mất nghĩa (lỗi #5 đã sửa bên Revit nhưng bên này thì chưa).
/// </summary>
internal static class BlockNumbering
{
    public static CommandResult Execute(Database database, BlockNumberingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockName))
        {
            return CommandResult.Fail("Thiếu tên block cần đánh số (blockName).");
        }

        if (request.Step == 0)
        {
            return CommandResult.Fail("Bước nhảy (step) phải khác 0.");
        }

        if (request.RowTolerance < 0)
        {
            return CommandResult.Fail("Dung sai gom hàng (rowToleranceMm) không được âm.");
        }

        using var transaction = database.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

        var items = new List<NumberingItem<ObjectId>>();
        // Đếm mọi block trong Model Space để khi sai tên thì nói được bản vẽ CÓ block gì (§57).
        var present = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (ObjectId entityId in modelSpace)
        {
            if (transaction.GetObject(entityId, OpenMode.ForRead) is not BlockReference blockRef)
            {
                continue;
            }

            var name = AcadHelpers.EffectiveBlockName(transaction, blockRef);
            present[name] = present.TryGetValue(name, out var n) ? n + 1 : 1;
            if (!string.Equals(name, request.BlockName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new NumberingItem<ObjectId>(blockRef.ObjectId, blockRef.Position.X, blockRef.Position.Y));
        }

        if (items.Count == 0)
        {
            transaction.Abort();
            return CommandResult.Fail(BlockMessages.BlockNotFound(request.BlockName, present.ToList()));
        }

        var ordered = NumberingPlanner.Order(items, request.Direction, request.RowTolerance);

        List<(ObjectId RefId, string Value)> plan;
        try
        {
            plan = NumberingPlanner
                .Assign(ordered, string.Empty, request.StartNumber, request.Step, 0)
                .Select(a => (a.Key, request.Label(a.Number)))
                .ToList();
        }
        catch (OverflowException)
        {
            transaction.Abort();
            return CommandResult.Fail($"Số thứ tự vượt giới hạn int khi bắt đầu từ {request.StartNumber} bước {request.Step} cho {items.Count} block.");
        }

        // Attribute đích của một block: khớp tag, hoặc attribute đầu tiên khi không khai tag. Xem trước và chạy thật dùng
        // CÙNG một hàm để hai con số khớp nhau.
        AttributeReference? Target(BlockReference blockRef)
        {
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);
                if (string.IsNullOrEmpty(request.AttributeTag)
                    || string.Equals(attRef.Tag, request.AttributeTag, StringComparison.OrdinalIgnoreCase))
                {
                    return attRef;
                }
            }

            return null;
        }

        var lockedLayers = AcadHelpers.LockedLayerIds(database, transaction);
        var lockedSkips = new LockedLayerSkips();
        var updated = 0;
        var unchanged = 0;
        var missing = 0;
        var tagsPresent = new List<string>();

        foreach (var (refId, value) in plan)
        {
            var blockRef = (BlockReference)transaction.GetObject(refId, OpenMode.ForRead);
            var attRef = Target(blockRef);
            if (attRef is null)
            {
                // Xem trước phải nói trước block nào KHÔNG có attribute cần ghi — chạy thật mới lộ thì xem trước vô nghĩa (§57).
                missing++;
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    tagsPresent.Add(((AttributeReference)transaction.GetObject(attId, OpenMode.ForRead)).Tag);
                }

                continue;
            }

            if (string.Equals(attRef.TextString, value, StringComparison.Ordinal))
            {
                // Trước đây đếm cả attribute không đổi → "Đã đánh số 200/200" khi không ghi gì.
                unchanged++;
                continue;
            }

            if (lockedLayers.Contains(attRef.LayerId))
            {
                // UpgradeOpen trên layer khoá ném eOnLockedLayer — bản cũ sập cả lệnh vì một attribute.
                lockedSkips.Add(attRef.Layer);
                continue;
            }

            if (!request.DryRun)
            {
                attRef.UpgradeOpen();
                attRef.TextString = value;
                attRef.AdjustAlignment(database);
            }

            updated++;
        }

        var notes = new List<string>();
        if (lockedSkips.Message("attribute") is { } lockedNote)
        {
            notes.Add(lockedNote);
        }

        if (request.DryRun)
        {
            transaction.Abort();
            // Đếm đúng số attribute SẼ ghi (bỏ block thiếu attribute, attribute đã đúng số, layer khoá) — như chạy thật.
            var preview = CommandResult.Ok(
                request.PreviewSummary(updated) + (unchanged > 0 ? $" {unchanged} attribute đã đúng số, sẽ không ghi." : string.Empty),
                updated);
            var warning = BlockMessages.AttributeMissingWarning(missing, plan.Count, request.AttributeTag, tagsPresent);
            if (warning != null)
            {
                preview.Messages.Add(warning);
            }

            preview.Messages.AddRange(notes);
            foreach (var (refId, value) in plan)
            {
                preview.Messages.Add($"{AcadHelpers.HandleOf(refId)}: \"{value}\"");
            }

            return preview;
        }

        transaction.Commit();

        var result = CommandResult.Ok(request.DoneSummary(updated, plan.Count) + (unchanged > 0 ? $" {unchanged} attribute đã đúng số, không ghi." : string.Empty), updated);
        if (missing > 0)
        {
            result.Messages.Add(string.IsNullOrEmpty(request.AttributeTag)
                ? $"{missing} block không có attribute nào — bỏ qua."
                : $"{missing} block không có attribute tag \"{request.AttributeTag}\" — bỏ qua.");
        }

        result.Messages.AddRange(notes);
        return result;
    }
}
