using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>
/// Gán attribute tăng dần theo mẫu (kiểu Lee Mac BATTE) cho các Block Reference cùng tên trong Model Space,
/// theo thứ tự vị trí: từ trên xuống dưới (Y giảm dần), trái sang phải (X tăng dần) — quy ước bản vẽ kỹ thuật.
/// </summary>
public sealed class AttributeIncrementCommand : ICoreCommand<AttributeIncrementConfig>
{
    private static readonly Regex PatternToken = new(@"\{n(:(\d+))?\}", RegexOptions.Compiled);

    public string CommandName => "AttributeIncrement";

    public CommandResult Execute(Database database, AttributeIncrementConfig config)
    {
        using var transaction = database.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

        var inserts = new List<(BlockReference Insert, Point3d Position)>();

        foreach (ObjectId entityId in modelSpace)
        {
            var entity = transaction.GetObject(entityId, OpenMode.ForRead);
            if (entity is not BlockReference blockRef)
            {
                continue;
            }

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

        var ordered = inserts
            .OrderByDescending(t => t.Position.Y)
            .ThenBy(t => t.Position.X)
            .ToList();

        var plan = new List<(ObjectId RefId, string Value)>();
        var number = config.StartNumber;

        foreach (var (blockRef, _) in ordered)
        {
            plan.Add((blockRef.ObjectId, FormatPattern(config.Pattern, number)));
            number++;
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ gán {plan.Count} giá trị vào attribute \"{config.AttributeTag}\" của block \"{config.BlockName}\".",
                plan.Count);

            foreach (var (refId, value) in plan)
            {
                preview.Messages.Add($"{refId}: \"{value}\"");
            }

            return preview;
        }

        var updated = 0;

        foreach (var (refId, value) in plan)
        {
            var blockRef = (BlockReference)transaction.GetObject(refId, OpenMode.ForRead);

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
                break;
            }
        }

        transaction.Commit();

        return CommandResult.Ok(
            $"Đã gán {updated}/{plan.Count} giá trị attribute \"{config.AttributeTag}\" cho block \"{config.BlockName}\".",
            updated);
    }

    /// <summary>Thay "{n}" hoặc "{n:000}" trong mẫu bằng số, độ rộng theo số ký tự trong ":000".</summary>
    private static string FormatPattern(string pattern, int number)
    {
        return PatternToken.Replace(pattern, match =>
        {
            var digitsGroup = match.Groups[2].Value;
            if (digitsGroup.Length > 0)
            {
                return number.ToString("D" + digitsGroup.Length);
            }

            return number.ToString();
        });
    }
}
