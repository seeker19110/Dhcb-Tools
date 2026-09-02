using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>Liệt kê mọi Xref trong drawing: tên, đường dẫn, trạng thái load (XrefStatus).</summary>
public sealed class XrefAuditCommand : ICoreCommand<XrefAuditConfig>
{
    public string CommandName => "XrefAudit";

    public CommandResult Execute(Database database, XrefAuditConfig config)
    {
        var rows = new List<(string Name, string Path, string Status)>();

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId blockId in blockTable)
            {
                var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
                if (!block.IsFromExternalReference)
                {
                    continue;
                }

                rows.Add((block.Name, block.PathName ?? string.Empty, block.XrefStatus.ToString()));
            }

            transaction.Commit();
        }

        var result = CommandResult.Ok(
            rows.Count == 0
                ? "Bản vẽ không có Xref nào."
                : $"Bản vẽ có {rows.Count} Xref.",
            rows.Count);

        foreach (var (name, path, status) in rows)
        {
            result.Messages.Add($"{name}: \"{path}\" — {status}");
        }

        if (!string.IsNullOrWhiteSpace(config.OutputPath))
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Name,Path,Status");
            foreach (var (name, path, status) in rows)
            {
                sb.Append(CsvText.JoinLine(new[] { name, path, status })).Append('\n');
            }

            File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);
            result.Messages.Add($"Đã ghi báo cáo ra \"{config.OutputPath}\".");
        }

        return result;
    }
}
