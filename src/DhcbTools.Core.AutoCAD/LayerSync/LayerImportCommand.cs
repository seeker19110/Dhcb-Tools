using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerSync;

/// <summary>
/// Đọc lại file CSV do <see cref="LayerExportCommand"/> tạo ra (đã chỉnh sửa trong Excel) và ghi giá trị
/// ngược vào drawing: color, linetype, lineweight, plottable, description, và trạng thái đóng băng / tắt / khoá.
/// Tuỳ chọn tạo layer mới nếu CSV có layer chưa tồn tại (<c>createMissing</c>). Layer phụ thuộc xref
/// (<c>XREF|LAYER</c>) đã có thì sửa được như layer thường (đóng băng, tắt, khoá, màu…) — không bao giờ tạo mới.
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
        var visibilityChanged = false;
        var xrefChanged = 0;
        var result = CommandResult.Ok(string.Empty);

        using var transaction = database.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        // Lượt 1 — chỉ đọc CSV: cảnh báo từng ô, bỏ dòng rỗng và tên không hợp lệ. Dòng trùng tên layer được
        // LayerImportPlanner gom lại TRƯỚC khi áp, nên kết quả không còn phụ thuộc thứ tự dòng.
        var invalid = 0;
        var rows = new List<(int Row, LayerCsvRow Layer)>();

        for (var i = 1; i < lines.Count; i++)
        {
            var row = LayerCsvRow.FromCells(lines[i], i + 1);
            if (row.IsEmpty)
            {
                continue;
            }

            // Ô không đọc được (màu, lineweight, plot sai định dạng) phải nói ra, không bỏ im lặng.
            result.Messages.AddRange(row.Warnings);

            // Tên có ký tự cấm (< > / \ " : ; ? * | , = `) làm LayerTableRecord.Name ném exception giữa
            // transaction — báo dòng lỗi rồi đi tiếp thay vì sập cả lệnh. Riêng dạng XREF|LAYER là layer phụ thuộc
            // xref: hợp lệ nếu hai phía hợp lệ — lượt 2 chỉ cho SỬA layer xref đã có, không bao giờ tạo.
            var validName = XrefLayerName.TrySplit(row.Name, out var xrefPart, out var layerPart)
                ? AcadHelpers.IsValidSymbolName(xrefPart) && AcadHelpers.IsValidSymbolName(layerPart)
                : AcadHelpers.IsValidSymbolName(row.Name);
            if (!validName)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: tên layer \"{row.Name}\" không hợp lệ với AutoCAD.");
                invalid++;
                continue;
            }

            rows.Add((i + 1, row));
        }

        var plan = LayerImportPlanner.Plan(rows);
        result.Messages.AddRange(plan.Notes);

        // Lượt 2 — áp từng dòng kế hoạch chọn.
        foreach (var (rowNumber, row) in plan.Rows)
        {
            var layerName = row.Name;
            var isNew = !layerTable.Has(layerName);
            var isXref = XrefLayerName.IsDependentName(layerName);
            if (isXref)
            {
                var problem = XrefLayerName.ImportProblem(
                    layerName,
                    !isNew,
                    !isNew && ((LayerTableRecord)transaction.GetObject(layerTable[layerName], OpenMode.ForRead)).IsDependent);
                if (problem != null)
                {
                    result.Messages.Add($"Bỏ qua dòng {rowNumber}: {problem}");
                    continue;
                }
            }

            if (isNew && !config.CreateMissing)
            {
                result.Messages.Add($"Bỏ qua dòng {rowNumber}: layer \"{layerName}\" không tồn tại.");
                continue;
            }

            if (isNew)
            {
                // Layer mới đếm MỘT lần là "tạo mới" ở cả hai lượt. Bản cũ lúc chạy thật đếm thêm "cập nhật" (hoặc
                // "giữ nguyên") cho chính layer vừa tạo: "Đã nhập 2 layer (1 cập nhật, 1 tạo mới)" cho một layer, gấp
                // đôi con số xem trước.
                created++;
                if (config.DryRun)
                {
                    result.Messages.Add($"[Xem trước] Sẽ tạo layer mới: \"{layerName}\".");
                    // Cùng những câu PlanChanges sẽ nói lúc chạy thật — bản cũ chỉ lộ ra khi đã ghi.
                    NoteUnusableCells(transaction, database, row, result.Messages);
                    continue;
                }

                var ltWrite = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
                var newLayer = new LayerTableRecord { Name = layerName };
                ltWrite.Add(newLayer);
                transaction.AddNewlyCreatedDBObject(newLayer, true);
                foreach (var apply in PlanChanges(transaction, database, newLayer, row, result.Messages, out _))
                {
                    apply(newLayer);
                }

                result.Messages.Add($"Đã tạo layer mới: \"{layerName}\".");
                continue;
            }

            // Mở ở chế độ ĐỌC trước để so sánh; chỉ nâng lên ghi khi thật sự có ô khác.
            var layer = (LayerTableRecord)transaction.GetObject(layerTable[layerName], OpenMode.ForRead);
            var changes = PlanChanges(transaction, database, layer, row, result.Messages, out var hidesOrShows);
            if (changes.Count == 0)
            {
                unchanged++;
                continue;
            }

            // Xem trước chỉ đọc: ghi tạm rồi Abort vẫn phát ObjectModified và làm revision Bridge đổi,
            // khiến preview hợp lệ bị từ chối token (E-PREVIEW-CHANGED).
            if (!config.DryRun)
            {
                layer.UpgradeOpen();
                foreach (var apply in changes) apply(layer);
                visibilityChanged |= hidesOrShows;
            }

            updated++;
            xrefChanged += isXref ? 1 : 0;
            result.Messages.Add($"Layer \"{layerName}\": {changes.Count} thuộc tính đổi.");
        }

        if (unchanged > 0)
        {
            result.Messages.Insert(0, $"{unchanged} layer giữ nguyên vì mọi giá trị trong CSV trùng với drawing.");
        }

        // Layer xref: VISRETAIN = 0 thì lần nạp lại xref sau ghi đè trạng thái vừa đổi — nói trước, không để kỹ sư ngỡ lệnh hỏng.
        if (xrefChanged > 0 && !database.Visretain)
        {
            result.Messages.Insert(0, XrefLayerName.VisretainWarning(xrefChanged));
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
            return WithConflicts(preview, plan);
        }

        transaction.Commit();
        var final = CommandResult.Ok(
            $"Đã nhập {updated + created} layer từ \"{config.InputPath}\" ({updated} cập nhật, {created} tạo mới).",
            updated + created);
        final.Messages.AddRange(result.Messages);

        // Gán IsFrozen/IsOff qua API không tự vẽ lại màn hình: không Regen thì kỹ sư thấy bản vẽ y như cũ
        // dù lệnh báo thành công (2026-11-13, rã băng 30 layer mà "chưa thấy hiện"). Phải sau Commit.
        if (visibilityChanged && !AcadDisplay.RequestRegen(database))
        {
            final.Messages.Add("Đã đổi trạng thái đóng băng/tắt layer — nếu màn hình chưa đổi, gõ REGEN để vẽ lại.");
        }

        return WithConflicts(final, plan);
    }

    /// <summary>
    /// Layer có nhiều dòng mâu thuẫn: không áp dòng nào (LayerImportPlanner), báo vào Errors và đánh dấu
    /// PartialSuccess — báo cáo batch không được gọi là xanh, và Bridge không cấp preview token cho lần xem trước đó.
    /// </summary>
    private static CommandResult WithConflicts(CommandResult result, LayerImportPlan plan)
    {
        result.Errors.AddRange(plan.Conflicts);
        if (plan.Conflicts.Count > 0)
        {
            result.PartialSuccess = true;
        }

        return result;
    }

    /// <summary>Những ô của một layer SẮP TẠO mà lúc tạo sẽ không áp được — đúng câu <see cref="PlanChanges"/> nói.</summary>
    private static void NoteUnusableCells(Transaction transaction, Database database, LayerCsvRow row, List<string> notes)
    {
        if (!string.IsNullOrEmpty(row.Linetype))
        {
            var linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
            if (!linetypeTable.Has(row.Linetype))
            {
                notes.Add(LinetypeMissing(row.Name, row.Linetype!));
            }
        }

        if (row.LineWeight.HasValue && !Enum.IsDefined(typeof(LineWeight), (LineWeight)row.LineWeight.Value))
        {
            notes.Add(LineWeightInvalid(row.Name, row.LineWeight.Value));
        }
    }

    private static string LinetypeMissing(string layer, string linetype) =>
        $"Layer \"{layer}\": linetype \"{linetype}\" chưa có trong drawing, giữ nguyên nét cũ.";

    private static string LineWeightInvalid(string layer, int value) =>
        $"Layer \"{layer}\": lineweight {value} không có trong bảng chuẩn của AutoCAD, giữ nguyên.";

    /// <summary>
    /// Danh sách thay đổi thật sự cần ghi cho một layer. Rỗng = dòng CSV trùng khớp hoàn toàn với
    /// drawing, không đụng vào bản ghi. <paramref name="changesVisibility"/> = có đổi đóng băng/tắt, tức là
    /// sau khi ghi phải vẽ lại màn hình.
    /// </summary>
    private static List<Action<LayerTableRecord>> PlanChanges(
        Transaction transaction, Database database, LayerTableRecord layer, LayerCsvRow row, List<string> notes,
        out bool changesVisibility)
    {
        var changes = new List<Action<LayerTableRecord>>();
        changesVisibility = false;

        // Cột: Name, Color, Linetype, Lineweight, IsPlottable, Description, IsFrozen, IsOff, IsLocked
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
                notes.Add(LinetypeMissing(layer.Name, wanted));
            }
        }

        if (row.LineWeight.HasValue)
        {
            var lineWeight = (LineWeight)row.LineWeight.Value;
            if (!Enum.IsDefined(typeof(LineWeight), lineWeight))
            {
                notes.Add(LineWeightInvalid(layer.Name, row.LineWeight.Value));
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

        if (row.Frozen.HasValue && layer.IsFrozen != row.Frozen.Value)
        {
            var frozen = row.Frozen.Value;
            // AutoCAD không cho đóng băng layer hiện hành (gán IsFrozen ném eInvalidLayer giữa transaction).
            // Layer mới tạo chưa có ObjectId lúc lập kế hoạch nên không thể là layer hiện hành.
            if (frozen && !layer.ObjectId.IsNull && layer.ObjectId == database.Clayer)
            {
                notes.Add($"Layer \"{layer.Name}\" đang là layer hiện hành — AutoCAD không cho đóng băng, giữ nguyên. "
                    + "Đổi layer hiện hành (CLAYER) rồi nhập lại.");
            }
            else
            {
                changes.Add(l => l.IsFrozen = frozen);
                changesVisibility = true;
            }
        }

        if (row.Off.HasValue && layer.IsOff != row.Off.Value)
        {
            var off = row.Off.Value;
            changes.Add(l => l.IsOff = off);
            changesVisibility = true;
        }

        if (row.Locked.HasValue && layer.IsLocked != row.Locked.Value)
        {
            var locked = row.Locked.Value;
            changes.Add(l => l.IsLocked = locked);
        }

        return changes;
    }
}
