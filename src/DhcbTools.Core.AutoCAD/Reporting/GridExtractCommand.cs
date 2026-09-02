using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// Trích các đường Line trên layer trục (mặc định "AXIS") trong Model Space ra CSV cho lệnh
/// GridFromCsv bên Revit. Tên trục: dùng số thứ tự "AXIS-n" (không tìm DBText gần đó — đơn giản hoá
/// nêu trong đặc tả, vì việc khớp text gần một Line theo khoảng cách hình học dễ sai khi bản vẽ dày đặc).
/// </summary>
public sealed class GridExtractCommand : ICoreCommand<GridExtractConfig>
{
    public string CommandName => "GridExtract";

    public CommandResult Execute(Database database, GridExtractConfig config)
    {
        var gridLayer = string.IsNullOrWhiteSpace(config.GridLayer) ? "AXIS" : config.GridLayer;

        var sb = new StringBuilder();
        sb.AppendLine("Name,StartX,StartY,EndX,EndY");

        var count = 0;

        using var transaction = database.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

        foreach (ObjectId entityId in modelSpace)
        {
            var entity = transaction.GetObject(entityId, OpenMode.ForRead);
            if (entity is not Line line || !string.Equals(line.Layer, gridLayer, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
            var name = "AXIS-" + count;

            sb.Append(CsvText.JoinLine(new[]
            {
                name,
                NumericText.Format(line.StartPoint.X),
                NumericText.Format(line.StartPoint.Y),
                NumericText.Format(line.EndPoint.X),
                NumericText.Format(line.EndPoint.Y),
            })).Append('\n');
        }

        transaction.Commit();

        if (count == 0)
        {
            return CommandResult.Fail($"Không tìm thấy đường Line nào trên layer \"{gridLayer}\".");
        }

        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        return CommandResult.Ok(
            $"Đã trích {count} trục từ layer \"{gridLayer}\" ra \"{config.OutputPath}\".",
            count);
    }
}
