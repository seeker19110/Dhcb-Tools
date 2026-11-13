using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Gắn một file DWG làm xref (overlay mặc định, hoặc attach) vào Model Space tại điểm chèn, tỉ lệ, góc, layer cho
/// trước — như XATTACH nhưng gọi được qua Bridge. Phiên bóc khối lượng 2026-11-13 phải gắn overlay mặt bằng các tầng
/// bằng COM vì Bridge chưa có lệnh này.
/// <para>
/// Xem trước chỉ đọc: kiểm file, tên, xref trùng tên, layer — không gắn gì. Xref cùng tên và cùng file đã có thì chèn
/// thêm một bản (không nạp lại); cùng tên khác file thì từ chối. Lệnh không tự quy đổi đơn vị: INSUNITS hai file
/// khác nhau thì kết quả nói rõ để người gọi chỉnh <c>scale</c>.
/// </para>
/// </summary>
public sealed class XrefAttachCommand : ICoreCommand<XrefAttachConfig>
{
    public string CommandName => "XrefAttach";

    public CommandResult Execute(Database database, XrefAttachConfig config)
    {
        var request = new XrefAttachRequest
        {
            Path = config.Path,
            Name = config.Name,
            Position = new[] { config.X, config.Y, config.Z },
            Scale = config.Scale,
            Rotation = config.Rotation,
            Layer = config.Layer,
        };
        var plan = XrefAttachPlanner.Plan(request, File.Exists, database.Filename);
        if (plan.IsValid && !AcadHelpers.IsValidSymbolName(plan.Name))
        {
            plan.Errors.Add($"Tên xref \"{plan.Name}\" không hợp lệ với AutoCAD — đặt \"name\" khác.");
        }

        if (!plan.IsValid)
        {
            return CommandResult.Fail($"Không gắn xref — {string.Join(" ", plan.Errors)} Chưa đụng vào bản vẽ.", plan.Errors);
        }

        var kind = config.Overlay ? "overlay" : "attach";
        var messages = new List<string>();
        using var transaction = database.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var reuse = ObjectId.Null;
        if (blockTable.Has(plan.Name))
        {
            var existing = (BlockTableRecord)transaction.GetObject(blockTable[plan.Name], OpenMode.ForRead);
            var problem = XrefAttachPlanner.ExistingNameProblem(plan.Name, existing.IsFromExternalReference, existing.PathName, plan.FullPath);
            if (problem != null)
            {
                transaction.Abort();
                return CommandResult.Fail(problem);
            }

            reuse = existing.ObjectId;
            messages.Add($"Xref \"{plan.Name}\" đã có (cùng file) — chèn thêm một bản, không nạp lại.");
        }

        var layerName = string.IsNullOrWhiteSpace(config.Layer) ? null : config.Layer!.Trim();
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (layerName != null)
        {
            if (!AcadHelpers.IsValidSymbolName(layerName))
            {
                transaction.Abort();
                return CommandResult.Fail($"Tên layer \"{layerName}\" không hợp lệ với AutoCAD.");
            }

            if (layerTable.Has(layerName) && ((LayerTableRecord)transaction.GetObject(layerTable[layerName], OpenMode.ForRead)).IsLocked)
            {
                transaction.Abort();
                return CommandResult.Fail($"Layer \"{layerName}\" đang khoá.");
            }
        }

        var position = new Point3d(config.X, config.Y, config.Z);
        var scale = config.Scale ?? 1;
        var where = $"tại {EntityEditPlanner.Format(new[] { position.X, position.Y, position.Z })}, tỉ lệ {scale}, góc {config.Rotation ?? 0}°";
        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok($"[Xem trước] Sẽ gắn {kind} \"{plan.Name}\" ← \"{plan.FullPath}\" {where}"
                + (layerName != null ? $" trên layer \"{layerName}\"" : string.Empty)
                + (layerName != null && !layerTable.Has(layerName) ? " (layer sẽ được tạo)" : string.Empty)
                + ". Chưa ghi vào drawing.", 1);
            preview.Messages.AddRange(messages);
            return preview;
        }

        ObjectId definitionId;
        try
        {
            definitionId = !reuse.IsNull
                ? reuse
                : config.Overlay ? database.OverlayXref(plan.FullPath, plan.Name) : database.AttachXref(plan.FullPath, plan.Name);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            transaction.Abort();
            return CommandResult.Fail($"AutoCAD không gắn được \"{plan.FullPath}\" ({ex.ErrorStatus}) — file có thể hỏng, đang khoá, hoặc tạo vòng tham chiếu.");
        }

        if (definitionId.IsNull)
        {
            transaction.Abort();
            return CommandResult.Fail($"AutoCAD không gắn được \"{plan.FullPath}\".");
        }

        var reference = new BlockReference(position, definitionId);
        reference.SetDatabaseDefaults(database);
        reference.ScaleFactors = new Scale3d(scale);
        reference.Rotation = EntityEditPlanner.Radians(config.Rotation ?? 0);
        if (layerName != null)
        {
            reference.LayerId = EnsureLayer(transaction, database, layerName, messages);
        }

        var modelSpace = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
        var referenceId = modelSpace.AppendEntity(reference);
        transaction.AddNewlyCreatedDBObject(reference, true);

        var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
        if (definition.XrefStatus != XrefStatus.Resolved)
        {
            messages.Add($"Trạng thái xref sau khi gắn: {definition.XrefStatus} — kiểm lại bằng XrefAudit.");
        }

        NoteUnits(database, definition, messages);
        transaction.Commit();

        var result = CommandResult.Ok($"Đã gắn {kind} \"{plan.Name}\" ← \"{plan.FullPath}\" {where} — bản chèn {AcadHelpers.HandleOf(referenceId)}.", 1);
        result.WithChanged(new[] { referenceId.Handle.Value });
        result.Messages.AddRange(messages);
        result.Messages.Add($"Layer của xref mang tiền tố \"{plan.Name}|\" — đóng băng/tắt bằng LayerImport (CSV có cột Name = \"{plan.Name}|<layer>\").");
        return result;
    }

    private static ObjectId EnsureLayer(Transaction transaction, Database database, string name, List<string> messages)
    {
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (table.Has(name))
        {
            return table[name];
        }

        table.UpgradeOpen();
        var record = new LayerTableRecord { Name = name };
        var id = table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
        messages.Add($"Đã tạo layer \"{name}\".");
        return id;
    }

    /// <summary>INSUNITS hai file khác nhau thì nói ra — XATTACH tự nhân hệ số, lệnh này để người gọi quyết qua scale.</summary>
    private static void NoteUnits(Database host, BlockTableRecord definition, List<string> messages)
    {
        try
        {
            var xrefDb = definition.GetXrefDatabase(false);
            if (xrefDb != null && xrefDb.Insunits != host.Insunits
                && xrefDb.Insunits != UnitsValue.Undefined && host.Insunits != UnitsValue.Undefined)
            {
                messages.Add($"Đơn vị khác nhau: xref {xrefDb.Insunits}, bản vẽ {host.Insunits} — lệnh không tự quy đổi; chỉnh \"scale\" nếu cần.");
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Không đọc được database của xref (chưa nạp) — đã báo qua XrefStatus.
        }
    }
}
