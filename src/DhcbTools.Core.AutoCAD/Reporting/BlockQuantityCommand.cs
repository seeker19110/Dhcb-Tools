using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// Đếm Block Reference trong Model Space theo (BlockName, giá trị attribute GroupByAttribute nếu có)
/// → CSV BOM: BlockName,GroupValue,Count.
/// </summary>
public sealed class BlockQuantityCommand : ICoreCommand<BlockQuantityConfig>
{
    public string CommandName => "BlockQuantity";

    public CommandResult Execute(Database database, BlockQuantityConfig config)
    {
        var counts = new Dictionary<(string BlockName, string GroupValue), int>();

        using var transaction = database.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

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

            if (!string.IsNullOrEmpty(config.BlockNameContains)
                && blockName.IndexOf(config.BlockNameContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var groupValue = string.Empty;

            if (!string.IsNullOrEmpty(config.GroupByAttribute))
            {
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);
                    if (string.Equals(attRef.Tag, config.GroupByAttribute, StringComparison.OrdinalIgnoreCase))
                    {
                        groupValue = attRef.TextString;
                        break;
                    }
                }
            }

            var key = (blockName, groupValue);
            counts[key] = counts.TryGetValue(key, out var existing) ? existing + 1 : 1;
        }

        transaction.Commit();

        var sb = new StringBuilder();
        sb.AppendLine("BlockName,GroupValue,Count");

        foreach (var kv in counts.OrderBy(k => k.Key.BlockName, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Key.GroupValue, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(CsvText.JoinLine(new[] { kv.Key.BlockName, kv.Key.GroupValue, NumericText.Format(kv.Value) })).Append('\n');
        }

        AcadHelpers.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        var totalBlocks = counts.Values.Sum();

        return CommandResult.Ok(
            $"Đã đếm {totalBlocks} block ({counts.Count} nhóm) ra \"{config.OutputPath}\".",
            totalBlocks);
    }
}
