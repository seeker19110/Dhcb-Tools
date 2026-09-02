using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>
/// Xuất mọi attribute của Block Reference trong Model Space ra CSV dạng hàng dài
/// (một hàng mỗi attribute) — đơn giản hơn pivot cột động, và khớp trực tiếp với AttributeImport.
/// Cột: BlockName, Handle, AttributeTag, AttributeValue.
/// </summary>
public sealed class AttributeExportCommand : ICoreCommand<AttributeExportConfig>
{
    public string CommandName => "AttributeExport";

    public CommandResult Execute(Database database, AttributeExportConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BlockName,Handle,AttributeTag,AttributeValue");

        var rowCount = 0;
        var blockCount = 0;

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

            if (blockRef.AttributeCollection.Count == 0)
            {
                continue;
            }

            var blockName = blockRef.IsDynamicBlock
                ? ((BlockTableRecord)transaction.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                : blockRef.Name;

            if (!string.IsNullOrEmpty(config.BlockName)
                && !string.Equals(blockName, config.BlockName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hasAttribute = false;

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);

                sb.Append(CsvText.JoinLine(new[]
                {
                    blockName,
                    blockRef.Handle.ToString(),
                    attRef.Tag,
                    attRef.TextString,
                })).Append('\n');

                rowCount++;
                hasAttribute = true;
            }

            if (hasAttribute)
            {
                blockCount++;
            }
        }

        transaction.Commit();

        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        return CommandResult.Ok(
            $"Đã xuất {rowCount} attribute từ {blockCount} block ra \"{config.OutputPath}\".",
            rowCount);
    }
}
