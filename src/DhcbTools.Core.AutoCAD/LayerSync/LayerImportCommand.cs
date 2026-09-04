using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerSync;

/// <summary>
/// Đọc lại file CSV do <see cref="LayerExportCommand"/> tạo ra (đã chỉnh sửa trong Excel) và ghi giá trị
/// ngược vào drawing: color, linetype, lineweight, plottable, description.
/// Tuỳ chọn tạo layer mới nếu CSV có layer chưa tồn tại (<c>createMissing</c>).
/// <para>
/// <b>Chỉ ghi ô đã đổi.</b> Bản trước mở MỌI layer trong CSV ở chế độ ghi rồi gán lại y nguyên giá trị
/// cũ, nên nhập lại chính file vừa xuất vẫn báo "cập nhật 70 layer" — không phân biệt được với việc kỹ
/// sư sửa thật 70 layer, và làm bẩn drawing (undo, dirty flag) mà không đổi gì. Cùng một lỗi đã sửa cho
/// <c>ParameterImport</c> bên Revit ở PR #29; lộ ra ở vòng kiểm thử AutoCAD đầu tiên 2026-09-03 nhờ ca
/// "nhập lại chính CSV vừa xuất — phải không đổi layer nào".
/// </para>
/// <para>
/// Bản trước cũng <b>bỏ qua hoàn toàn</b> cột Linetype và Lineweight dù tài liệu và header CSV đều nói
/// có — sửa nét đứt trong Excel rồi nhập lại thì không có gì xảy ra, mà lệnh vẫn báo thành công.
/// </para>
/// <para>
/// Phần đọc một dòng CSV nằm ở <see cref="LayerCsvRow"/> (Shared.Logic, không có API Autodesk) nên có test
/// tự động; ở đây chỉ còn phần so sánh với drawing và ghi. Nhờ đó cũng đọc lại được màu true color
/// (<c>ColorValue</c>) mà lệnh xuất ghi ra cho layer không dùng màu ACI — bản trước chỉ nhận màu ACI.
/// </para>
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

        // Đọc theo bản ghi RFC 4180: cột Description nhiều dòng (Escape đã bọc nháy) không bị cắt đôi như ReadAllLines.
        var lines = CsvText.ReadRecords(config.InputPath).ToList();
        if (lines.Count < 2)
        {
            return CommandResult.Fail("File CSV không có dữ liệu (chỉ có dòng tiêu đề hoặc rỗng).");
        }

        var updated = 0;
        var created = 0;
        var unchanged = 0;
        var result = CommandResult.Ok(string.Empty);

        using var transaction = database.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        var invalid = 0;

        for (var i = 1; i < lines.Count; i++)
        {
            var row = LayerCsvRow.FromCells(lines[i], i + 1);
            if (row.IsEmpty)
            {
                continue;
            }

            // Ô không đọc được (màu, lineweight, plot sai định dạng) phải nói ra, không bỏ im lặng.
            result.Messages.AddRange(row.Warnings);

            var layerName = row.Name;

            // Tên có ký tự cấm (< > / \ " : ; ? * | , = `) làm LayerTableRecord.Name ném exception giữa
            // transaction — báo dòng lỗi rồi đi tiếp thay vì sập cả lệnh.
            if (!AcadHelpers.IsValidSymbolName(layerName))
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: tên layer \"{layerName}\" không hợp lệ với AutoCAD.");
                invalid++;
                continue;
            }

            var isNew = !layerTable.Has(layerName);
            if (isNew && !config.CreateMissing)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: layer \"{layerName}\" không tồn tại.");
                continue;
            }

            if (isNew && config.DryRun)
            {
                result.Messages.Add($"[Xem trước] Sẽ tạo layer mới: \"{layerName}\".");
                created++;
                continue;
            }

            // Mở ở chế độ ĐỌC trước để so sánh; chỉ nâng lên ghi khi thật sự có ô khác.
            LayerTableRecord layer;
            if (isNew)
            {
                var ltWrite = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
                layer = new LayerTableRecord { Name = layerName };
                ltWrite.Add(layer);
                transaction.AddNewlyCreatedDBObject(layer, true);
                created++;
            }
            else
            {
                layer = (LayerTableRecord)transaction.GetObject(layerTable[layerName], OpenMode.ForRead);
            }

            var changes = PlanChanges(transaction, database, layer, row, result.Messages);
            if (changes.Count == 0)
            {
                unchanged++;
                continue;
            }

            if (!isNew)
            {
                layer.UpgradeOpen();
            }

            foreach (var apply in changes)
            {
                apply(layer);
            }

            updated++;
            result.Messages.Add($"Layer \"{layerName}\": {changes.Count} thuộc tính đổi.");
        }

        if (unchanged > 0)
        {
            result.Messages.Insert(0, $"{unchanged} layer giữ nguyên vì mọi giá trị trong CSV trùng với drawing.");
        }

        if (invalid > 0)
        {
            result.Messages.Insert(0, $"{invalid} dòng có tên layer không hợp lệ — bỏ qua (xem chi tiết bên dưới).");
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ cập nhật {updated} layer, tạo mới {created} layer (chưa ghi vào drawing).",
                updated + created);
            preview.Messages.AddRange(result.Messages);
            return preview;
        }

        transaction.Commit();
        var final = CommandResult.Ok(
            $"Đã nhập {updated + created} layer từ \"{config.InputPath}\" ({updated} cập nhật, {created} tạo mới).",
            updated + created);
        final.Messages.AddRange(result.Messages);
        return final;
    }

    /// <summary>
    /// Danh sách thay đổi thật sự cần ghi cho một layer. Rỗng = dòng CSV trùng khớp hoàn toàn với
    /// drawing, không đụng vào bản ghi.
    /// </summary>
    private static List<Action<LayerTableRecord>> PlanChanges(
        Transaction transaction, Database database, LayerTableRecord layer, LayerCsvRow row, List<string> notes)
    {
        var changes = new List<Action<LayerTableRecord>>();

        // Cột: Name, Color, Linetype, Lineweight, IsPlottable, Description
        if (row.ColorAci.HasValue)
        {
            var aci = row.ColorAci.Value;
            var current = layer.Color.IsByAci ? layer.Color.ColorIndex : (short)-1;
            if (current != aci)
            {
                changes.Add(l => l.Color = Color.FromColorIndex(ColorMethod.ByAci, aci));
            }
        }
        else if (row.ColorRgb.HasValue)
        {
            var rgb = row.ColorRgb.Value;
            // ColorValue là System.Drawing.Color, phải qua ToArgb() mới so được với RGB đọc từ CSV.
            var current = layer.Color.IsByAci ? -1 : layer.Color.ColorValue.ToArgb() & 0xFFFFFF;
            if (current != rgb)
            {
                changes.Add(l => l.Color = Color.FromRgb(
                    (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF)));
            }
        }

        if (!string.IsNullOrEmpty(row.Linetype))
        {
            var wanted = row.Linetype!;
            var linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
            if (linetypeTable.Has(wanted))
            {
                var wantedId = linetypeTable[wanted];
                if (layer.LinetypeObjectId != wantedId)
                {
                    changes.Add(l => l.LinetypeObjectId = wantedId);
                }
            }
            else
            {
                // Không tự nạp linetype từ file .lin — nhưng phải NÓI, không bỏ im lặng rồi báo thành công.
                notes.Add($"Layer \"{layer.Name}\": linetype \"{wanted}\" chưa có trong drawing, giữ nguyên nét cũ.");
            }
        }

        if (row.LineWeight.HasValue)
        {
            var lineWeight = (LineWeight)row.LineWeight.Value;
            if (!Enum.IsDefined(typeof(LineWeight), lineWeight))
            {
                notes.Add($"Layer \"{layer.Name}\": lineweight {row.LineWeight.Value} không có trong bảng chuẩn của AutoCAD, giữ nguyên.");
            }
            else if (layer.LineWeight != lineWeight)
            {
                changes.Add(l => l.LineWeight = lineWeight);
            }
        }

        if (row.Plottable.HasValue && layer.IsPlottable != row.Plottable.Value)
        {
            var plottable = row.Plottable.Value;
            changes.Add(l => l.IsPlottable = plottable);
        }

        if (row.Description != null
            && !string.Equals(layer.Description ?? string.Empty, row.Description, StringComparison.Ordinal))
        {
            var description = row.Description;
            changes.Add(l => l.Description = description);
        }

        return changes;
    }
}
