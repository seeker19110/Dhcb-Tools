using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

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

        foreach (ObjectId entityId in modelSpace)
        {
            if (transaction.GetObject(entityId, OpenMode.ForRead) is not BlockReference blockRef)
            {
                continue;
            }

            if (!string.Equals(AcadHelpers.EffectiveBlockName(transaction, blockRef), request.BlockName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new NumberingItem<ObjectId>(blockRef.ObjectId, blockRef.Position.X, blockRef.Position.Y));
        }

        if (items.Count == 0)
        {
            transaction.Abort();
            return CommandResult.Fail($"Không tìm thấy Block \"{request.BlockName}\" trong Model Space.");
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

        if (request.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(request.PreviewSummary(plan.Count), plan.Count);
            foreach (var (refId, value) in plan)
            {
                preview.Messages.Add($"{AcadHelpers.HandleOf(refId)}: \"{value}\"");
            }

            return preview;
        }

        var updated = 0;
        var missing = 0;

        foreach (var (refId, value) in plan)
        {
            var blockRef = (BlockReference)transaction.GetObject(refId, OpenMode.ForRead);
            var written = false;

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);

                var matchByTag = !string.IsNullOrEmpty(request.AttributeTag)
                    && string.Equals(attRef.Tag, request.AttributeTag, StringComparison.OrdinalIgnoreCase);
                var matchFirst = string.IsNullOrEmpty(request.AttributeTag);

                if (!matchByTag && !matchFirst)
                {
                    continue;
                }

                if (!string.Equals(attRef.TextString, value, StringComparison.Ordinal))
                {
                    attRef.UpgradeOpen();
                    attRef.TextString = value;
                }

                updated++;
                written = true;
                break;
            }

            if (!written)
            {
                missing++;
            }
        }

        transaction.Commit();

        var result = CommandResult.Ok(request.DoneSummary(updated, plan.Count), updated);
        if (missing > 0)
        {
            result.Messages.Add(string.IsNullOrEmpty(request.AttributeTag)
                ? $"{missing} block không có attribute nào — bỏ qua."
                : $"{missing} block không có attribute tag \"{request.AttributeTag}\" — bỏ qua.");
        }

        return result;
    }
}
