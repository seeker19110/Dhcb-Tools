using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.LayerSync;

/// <summary>
/// Xuất toàn bộ layer trong drawing ra file CSV — tương đương ParameterExportCommand của Revit.
/// Các cột: Name, Color, Linetype, Lineweight, IsPlottable, Description.
/// </summary>
public sealed class LayerExportCommand : ICoreCommand<LayerExportConfig>
{
    public string CommandName => "LayerExport";

    public CommandResult Execute(Database database, LayerExportConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Color,Linetype,Lineweight,IsPlottable,Description");

        var count = 0;

        using var transaction = database.TransactionManager.StartTransaction();

        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        foreach (ObjectId layerId in layerTable)
        {
            var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);

            // Lọc theo tên nếu có filter
            if (!string.IsNullOrEmpty(config.FilterNameContains)
                && layer.Name.IndexOf(config.FilterNameContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var colorIndex = layer.Color.IsByAci ? layer.Color.ColorIndex.ToString(CultureInfo.InvariantCulture) : layer.Color.ColorValue.ToString();
            var linetype = GetLinetypeName(transaction, database, layer.LinetypeObjectId);
            var lineweight = layer.LineWeight.ToString();
            var plottable = layer.IsPlottable ? "true" : "false";
            var description = layer.Description ?? string.Empty;

            sb.Append(CsvText.Escape(layer.Name)).Append(',')
              .Append(CsvText.Escape(colorIndex)).Append(',')
              .Append(CsvText.Escape(linetype)).Append(',')
              .Append(CsvText.Escape(lineweight)).Append(',')
              .Append(plottable).Append(',')
              .AppendLine(CsvText.Escape(description));

            count++;
        }

        transaction.Commit();

        AcadHelpers.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        return CommandResult.Ok(
            $"Đã xuất {count} layer ra \"{config.OutputPath}\".",
            count);
    }

    private static string GetLinetypeName(Transaction transaction, Database database, ObjectId linetypeId)
    {
        if (linetypeId == database.ContinuousLinetype)
        {
            return "Continuous";
        }

        try
        {
            var ltype = (LinetypeTableRecord)transaction.GetObject(linetypeId, OpenMode.ForRead);
            return ltype.Name;
        }
        catch
        {
            return "Continuous";
        }
    }
}
