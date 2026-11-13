using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Đặt block vào Model Space theo danh sách vị trí — kèm tỉ lệ, góc, layer và giá trị attribute. Định nghĩa block lấy
/// từ bản vẽ hiện tại, hoặc nạp từ thư viện: một DWG chứa nhiều block, một DWG mà cả file là ký hiệu, hay một thư mục
/// mỗi ký hiệu một file <c>&lt;tên&gt;.dwg</c> (cách tổ chức của các bộ ký hiệu P&amp;ID, thiết bị MEP, khung tên).
/// <para>
/// Kiểm hết trước khi ghi: block có tìm được không, tag attribute có trong định nghĩa không, layer có khoá không —
/// lỗi thì không đặt cái nào. Xem trước không nạp định nghĩa vào bản vẽ (chỉ đọc thư viện).
/// </para>
/// </summary>
public sealed class BlockInsertCommand : ICoreCommand<BlockInsertConfig>
{
    public string CommandName => "BlockInsert";

    public CommandResult Execute(Database database, BlockInsertConfig config)
    {
        var name = (config.BlockName ?? string.Empty).Trim();
        if (!AcadHelpers.IsValidSymbolName(name))
        {
            return CommandResult.Fail($"Tên block \"{config.BlockName}\" không hợp lệ.");
        }

        var insertions = new List<BlockInsertion>(config.Insertions ?? new List<BlockInsertion>());
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.InputPath))
        {
            if (!File.Exists(config.InputPath))
            {
                return CommandResult.Fail($"Không tìm thấy file: \"{config.InputPath}\".");
            }

            var csv = BlockInsertPlanner.FromCsv(CsvText.ReadRecords(config.InputPath));
            errors.AddRange(csv.Errors);
            insertions.AddRange(csv.Rows);
        }

        var plan = BlockInsertPlanner.Validate(insertions);
        errors.AddRange(plan.Errors);
        if (errors.Count > 0)
        {
            return CommandResult.Fail($"Danh sách vị trí không hợp lệ ({errors.Count} lỗi) — chưa đặt block nào.", errors.Distinct());
        }

        var report = new List<string>();
        bool existing;
        using (var tr = database.TransactionManager.StartTransaction())
        {
            existing = ((BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead)).Has(name);
            tr.Abort();
        }

        // ── Nguồn định nghĩa ──
        Database? source = null;
        string? sourceBlock = null;
        try
        {
            if (!existing || config.Redefine)
            {
                if (string.IsNullOrWhiteSpace(config.LibraryPath))
                {
                    return CommandResult.Fail(existing
                        ? "redefine cần libraryPath (DWG hoặc thư mục thư viện)."
                        : $"Bản vẽ chưa có block \"{name}\" — cần libraryPath (DWG chứa block, DWG là ký hiệu, hoặc thư mục có {name}.dwg).");
                }

                var file = LibraryFile(config.LibraryPath!, name, out var findError);
                if (file == null)
                {
                    return CommandResult.Fail(findError!);
                }

                source = new Database(false, true);
                try
                {
                    source.ReadDwgFile(file, FileOpenMode.OpenForReadAndAllShare, true, null);
                    source.CloseInput(true);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    return CommandResult.Fail($"Không đọc được thư viện \"{file}\" ({ex.ErrorStatus}).");
                }

                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                    sourceBlock = table.Has(name) ? name : null; // null = cả file là ký hiệu
                    tr.Abort();
                }

                report.Add(sourceBlock != null
                    ? $"Định nghĩa \"{name}\" lấy từ block cùng tên trong \"{file}\"{(existing ? " (định nghĩa lại)" : "")}."
                    : $"Định nghĩa \"{name}\" lấy từ cả file \"{file}\"{(existing ? " (định nghĩa lại)" : "")}.");
            }
            else
            {
                report.Add($"Dùng định nghĩa \"{name}\" có sẵn trong bản vẽ.");
            }

            // ── Kiểm attribute trên định nghĩa sẽ dùng (chưa ghi gì) ──
            var tags = source != null ? AttributeTags(source, sourceBlock) : AttributeTags(database, name);
            if (tags == null)
            {
                return CommandResult.Fail($"\"{name}\" là block đặc biệt (layout/xref/anonymous) — không đặt được.");
            }

            var unknown = insertions.SelectMany((ins, i) => (ins.Attributes ?? new Dictionary<string, string>()).Keys
                    .Where(t => !tags.Contains(t)).Select(t => $"#{i + 1}: tag \"{t}\""))
                .ToList();
            if (unknown.Count > 0)
            {
                var known = tags.Count == 0 ? "block không có attribute" : "tag có: " + string.Join(", ", tags);
                return CommandResult.Fail($"Có tag attribute không thuộc block \"{name}\" ({known}).", unknown);
            }

            // ── Layer: tạo/kiểm khoá (xem trước thì huỷ giao dịch sau khi kiểm) ──
            using var write = database.TransactionManager.StartTransaction();
            foreach (var layer in insertions.Select(i => i.Layer?.Trim()).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                EnsureLayer(database, write, layer!, report, config.DryRun, out var layerError);
                if (layerError != null)
                {
                    write.Abort();
                    return CommandResult.Fail(layerError + " Chưa đặt block nào.");
                }
            }

            if (config.DryRun)
            {
                write.Abort();
                var preview = CommandResult.Ok($"[Xem trước] Sẽ đặt {insertions.Count} block \"{name}\" vào Model Space.", insertions.Count);
                preview.Messages.AddRange(report);
                foreach (var layer in insertions.Select(i => i.Layer?.Trim()).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    preview.Messages.Add($"Layer \"{layer}\": {insertions.Count(i => string.Equals(i.Layer?.Trim(), layer, StringComparison.OrdinalIgnoreCase))} block.");
                }

                return preview;
            }

            write.Commit();

            var blockId = ImportDefinition(database, name, source, sourceBlock, config.Redefine && existing);
            using var transaction = database.TransactionManager.StartTransaction();
            var definition = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            var attributeDefinitions = new List<AttributeDefinition>();
            foreach (ObjectId id in definition)
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is AttributeDefinition { Constant: false } ad)
                {
                    attributeDefinitions.Add(ad);
                }
            }

            var modelSpace = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            var result = CommandResult.Ok(string.Empty);
            foreach (var insertion in insertions)
            {
                var p = insertion.Position!;
                var reference = new BlockReference(new Point3d(p[0], p[1], EntityEditPlanner.Z(p)), blockId);
                reference.SetDatabaseDefaults(database);
                if (!string.IsNullOrWhiteSpace(insertion.Layer)) reference.LayerId = layerTable[insertion.Layer!.Trim()];
                reference.ScaleFactors = new Scale3d(insertion.Scale ?? 1);
                reference.Rotation = EntityEditPlanner.Radians(insertion.Rotation ?? 0);
                var refId = modelSpace.AppendEntity(reference);
                transaction.AddNewlyCreatedDBObject(reference, true);

                foreach (var ad in attributeDefinitions)
                {
                    var attribute = new AttributeReference();
                    attribute.SetAttributeFromBlock(ad, reference.BlockTransform);
                    if (insertion.Attributes != null && insertion.Attributes.TryGetValue(ad.Tag, out var value))
                    {
                        attribute.TextString = value;
                    }

                    if (ad.IsMTextAttributeDefinition) attribute.UpdateMTextAttribute();
                    else if (ad.Justify != AttachmentPoint.BaseLeft) attribute.AdjustAlignment(database);
                    reference.AttributeCollection.AppendAttribute(attribute);
                    transaction.AddNewlyCreatedDBObject(attribute, true);
                }

                result.WithChanged(refId.Handle.Value);
            }

            transaction.Commit();
            result.Summary = $"Đã đặt {insertions.Count} block \"{name}\" vào Model Space.";
            result.AffectedCount = insertions.Count;
            result.Messages.AddRange(report);
            result.Messages.Add("Handle: " + string.Join(", ", result.ChangedIds.Take(50).Select(HandleText.ToText)) + (insertions.Count > 50 ? ", …" : ""));
            return result;
        }
        finally
        {
            source?.Dispose();
        }
    }

    /// <summary>File thư viện: đường dẫn DWG trực tiếp, hoặc <c>&lt;thư mục&gt;/&lt;tên&gt;.dwg</c> (không phân biệt hoa thường).</summary>
    private static string? LibraryFile(string path, string name, out string? error)
    {
        error = null;
        path = path.Trim().Trim('"');
        if (Directory.Exists(path))
        {
            var match = Directory.EnumerateFiles(path, "*.dwg", SearchOption.AllDirectories)
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            if (match == null) error = $"Thư mục \"{path}\" không có {name}.dwg (tìm cả thư mục con).";
            return match;
        }

        if (File.Exists(path) && path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        error = $"libraryPath \"{path}\" không phải file .dwg hay thư mục có thật.";
        return null;
    }

    /// <summary>Tag attribute (không kể hằng) của định nghĩa; <paramref name="block"/> null = cả Model Space của file. Null nếu block đặc biệt.</summary>
    private static HashSet<string>? AttributeTags(Database db, string? block)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var id = block == null ? SymbolUtilityServices.GetBlockModelSpaceId(db) : ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[block];
        var record = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
        if (block != null && (record.IsLayout || record.IsFromExternalReference || record.IsAnonymous))
        {
            return null;
        }

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId child in record)
        {
            if (tr.GetObject(child, OpenMode.ForRead) is AttributeDefinition { Constant: false } ad) tags.Add(ad.Tag);
        }

        tr.Abort();
        return tags;
    }

    private static ObjectId ImportDefinition(Database target, string name, Database? source, string? sourceBlock, bool redefine)
    {
        if (source == null)
        {
            using var tr = target.TransactionManager.StartTransaction();
            var id = ((BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead))[name];
            tr.Abort();
            return id;
        }

        if (sourceBlock == null)
        {
            // Cả file là ký hiệu — như INSERT một DWG; trùng tên thì Insert định nghĩa lại.
            return target.Insert(name, source, true);
        }

        ObjectId sourceId;
        using (var tr = source.TransactionManager.StartTransaction())
        {
            sourceId = ((BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead))[sourceBlock];
            tr.Abort();
        }

        var mapping = new IdMapping();
        source.WblockCloneObjects(new ObjectIdCollection { sourceId }, target.BlockTableId, mapping,
            redefine ? DuplicateRecordCloning.Replace : DuplicateRecordCloning.Ignore, false);
        return mapping[sourceId].Value;
    }

    private static void EnsureLayer(Database db, Transaction tr, string name, List<string> report, bool dryRun, out string? error)
    {
        error = null;
        if (!AcadHelpers.IsValidSymbolName(name))
        {
            error = $"Tên layer \"{name}\" không hợp lệ.";
            return;
        }

        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (table.Has(name))
        {
            var record = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
            if (record.IsLocked) error = $"Layer \"{name}\" đang khoá.";
            return;
        }

        if (dryRun)
        {
            // Xem trước chỉ đọc: không ghi bảng layer (kể cả trong giao dịch sẽ huỷ) để không phát sự kiện ghi tạm.
            report.Add($"[Xem trước] Sẽ tạo layer \"{name}\".");
            return;
        }

        table.UpgradeOpen();
        var layer = new LayerTableRecord { Name = name };
        table.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
        report.Add($"Đã tạo layer \"{name}\".");
    }
}
