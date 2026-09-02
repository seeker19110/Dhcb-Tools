using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD.LayerSync;

/// <summary>
/// Đọc lại file CSV do <see cref="LayerExportCommand"/> tạo ra (đã chỉnh sửa trong Excel)
/// và ghi giá trị ngược vào drawing: cập nhật color, linetype, lineweight, description.
/// Optionally tạo layer mới nếu trong CSV có layer chưa tồn tại (CreateMissing=true).
/// </summary>
public sealed class LayerImportCommand : ICoreCommand<LayerImportConfig>
{
    public string CommandName => "LayerImport";

    public CommandResult Execute(Database database, LayerImportConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy file: \"{config.InputPath}\".");
        }

        var lines = File.ReadAllLines(config.InputPath);
        if (lines.Length < 2)
        {
            return CommandResult.Fail("File CSV không có dữ liệu (chỉ có dòng tiêu đề hoặc rỗng).");
        }

        var updated = 0;
        var created = 0;
        var result = CommandResult.Ok(string.Empty);

        using var transaction = database.TransactionManager.StartTransaction();

        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        // Đọc từ dòng 1 (bỏ header)
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = SplitCsvLine(lines[i]);
            if (cells.Count < 1)
            {
                continue;
            }

            var layerName = cells[0];
            if (string.IsNullOrWhiteSpace(layerName))
            {
                continue;
            }

            LayerTableRecord? layer = null;

            if (layerTable.Has(layerName))
            {
                var layerId = layerTable[layerName];
                layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForWrite);
            }
            else if (config.CreateMissing)
            {
                if (config.DryRun)
                {
                    result.Messages.Add($"[Xem trước] Sẽ tạo layer mới: \"{layerName}\".");
                    created++;
                    continue;
                }

                // Mở table để ghi, thêm layer mới
                var ltWrite = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
                layer = new LayerTableRecord { Name = layerName };
                ltWrite.Add(layer);
                transaction.AddNewlyCreatedDBObject(layer, true);
                created++;
            }
            else
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: layer \"{layerName}\" không tồn tại.");
                continue;
            }

            if (layer is null)
            {
                continue;
            }

            // Ghi color
            if (cells.Count > 1 && !string.IsNullOrEmpty(cells[1]))
            {
                if (short.TryParse(cells[1], out var aci))
                {
                    layer.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
                }
            }

            // Ghi description
            if (cells.Count > 5)
            {
                layer.Description = cells[5];
            }

            // Ghi IsPlottable
            if (cells.Count > 4 && bool.TryParse(cells[4], out var plottable))
            {
                layer.IsPlottable = plottable;
            }

            updated++;

            if (config.DryRun)
            {
                result.Messages.Add($"[Xem trước] Sẽ cập nhật layer \"{layerName}\".");
            }
        }

        if (config.DryRun)
        {
            transaction.Abort();
            return CommandResult.Ok(
                $"[Xem trước] Sẽ cập nhật {updated} layer, tạo mới {created} layer (chưa ghi vào drawing).",
                updated + created);
        }

        transaction.Commit();
        result.Messages.Add($"Đã cập nhật {updated} layer, tạo mới {created} layer.");
        return CommandResult.Ok(
            $"Đã nhập {updated + created} layer từ \"{config.InputPath}\".",
            updated + created);
    }

    private static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        cells.Add(current.ToString());
        return cells;
    }
}
