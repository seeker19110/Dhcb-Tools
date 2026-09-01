using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DhcbTools.Core.AutoCAD.AutoNumbering;

/// <summary>
/// Đánh số hàng loạt Block Reference theo vị trí hình học — tương đương AutoNumberingCommand của Revit.
/// Sắp Block Reference theo InsertionPoint rồi ghi giá trị vào AttributeReference khớp tag.
/// </summary>
public sealed class AutoNumberingCommand : ICoreCommand<AutoNumberingConfig>
{
    public string CommandName => "AutoNumbering";

    public CommandResult Execute(Database database, AutoNumberingConfig config)
    {
        using var transaction = database.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

        // Thu thập tất cả BlockReference có tên khớp
        var inserts = new List<(BlockReference Insert, Point3d InsertPoint)>();

        foreach (ObjectId entityId in modelSpace)
        {
            var entity = transaction.GetObject(entityId, OpenMode.ForRead);
            if (entity is not BlockReference blockRef)
            {
                continue;
            }

            // Lấy tên block thực (xử lý cả dynamic block)
            var blockName = blockRef.IsDynamicBlock
                ? ((BlockTableRecord)transaction.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                : blockRef.Name;

            if (!string.Equals(blockName, config.BlockName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            inserts.Add((blockRef, blockRef.Position));
        }

        if (inserts.Count == 0)
        {
            transaction.Abort();
            return CommandResult.Fail($"Không tìm thấy Block \"{config.BlockName}\" trong Model Space.");
        }

        // Sắp xếp theo hướng đã chọn
        var ordered = config.Direction == NumberingDirection.LeftToRightThenTopToBottom
            ? inserts.OrderByDescending(t => t.InsertPoint.Y).ThenBy(t => t.InsertPoint.X)
            : inserts.OrderBy(t => t.InsertPoint.X).ThenByDescending(t => t.InsertPoint.Y);

        var plan = new List<(ObjectId RefId, string Value)>();
        var number = config.StartNumber;

        foreach (var (blockRef, _) in ordered)
        {
            var digits = number.ToString();
            if (config.PadWidth > 0)
            {
                digits = digits.PadLeft(config.PadWidth, '0');
            }

            plan.Add((blockRef.ObjectId, config.Prefix + digits));
            number += config.Step;
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đánh số {plan.Count} Block \"{config.BlockName}\" vào attribute \"{config.AttributeTag}\".",
                plan.Count);

            foreach (var (refId, value) in plan)
            {
                preview.Messages.Add($"{refId}: \"{value}\"");
            }

            return preview;
        }

        // Ghi thật vào attribute
        var updated = 0;
        var result = CommandResult.Ok(string.Empty);

        foreach (var (refId, value) in plan)
        {
            var blockRef = (BlockReference)transaction.GetObject(refId, OpenMode.ForRead);
            var written = false;

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);

                if (!string.Equals(attRef.Tag, config.AttributeTag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                attRef.UpgradeOpen();
                attRef.TextString = value;
                updated++;
                written = true;
                break;
            }

            if (!written)
            {
                // Không im lặng bỏ qua: kỹ sư cần biết block nào không có attribute đích (lỗi #2).
                result.Messages.Add(
                    $"Bỏ qua block {refId}: không có attribute \"{config.AttributeTag}\".");
            }
        }

        transaction.Commit();

        return result.With(
            $"Đã đánh số {updated}/{plan.Count} Block \"{config.BlockName}\".",
            updated);
    }
}
