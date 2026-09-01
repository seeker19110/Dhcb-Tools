using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>Xuất attribute của Block Reference ra CSV — tương đương ParameterExport của Revit.</summary>
public sealed class AttributeExportConfig
{
    /// <summary>Tên block (không phân biệt hoa thường). Rỗng = mọi block có attribute.</summary>
    public string? BlockName { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>Chỉ Model Space (mặc định) hay cả layout.</summary>
    public bool ModelSpaceOnly { get; init; } = true;
}

public sealed class AttributeImportConfig
{
    public required string InputPath { get; init; }

    public bool DryRun { get; init; } = true;
}

/// <summary>Cột: <c>Handle,BlockName,Layer,X,Y,&lt;TAG1&gt;,&lt;TAG2&gt;…</c> (tập tag hợp nhất của các block đã chọn).</summary>
public sealed class AttributeExportCommand : ICoreCommand<AttributeExportConfig>
{
    public string CommandName => "AttributeExport";

    public CommandResult Execute(Database database, AttributeExportConfig config)
    {
        using var tr = database.TransactionManager.StartTransaction();
        var rows = new List<(string Handle, string Block, string Layer, double X, double Y, Dictionary<string, string> Attrs)>();
        var tags = new List<string>();

        foreach (var br in EnumerateInserts(database, tr, config.ModelSpaceOnly))
        {
            var name = BlockName(tr, br);
            if (!string.IsNullOrEmpty(config.BlockName) && !string.Equals(name, config.BlockName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (br.AttributeCollection.Count == 0)
            {
                continue;
            }

            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                attrs[att.Tag] = att.TextString;
                if (!tags.Contains(att.Tag, StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add(att.Tag);
                }
            }

            rows.Add((br.Handle.ToString(), name, br.Layer, br.Position.X, br.Position.Y, attrs));
        }

        tr.Abort();

        var sb = new StringBuilder();
        sb.Append(CsvText.JoinLine(new[] { "Handle", "BlockName", "Layer", "X", "Y" }.Concat(tags))).Append('\n');
        foreach (var r in rows)
        {
            var cells = new List<string> { r.Handle, r.Block, r.Layer, NumericText.Format(r.X, 3), NumericText.Format(r.Y, 3) };
            cells.AddRange(tags.Select(t => r.Attrs.TryGetValue(t, out var v) ? v : string.Empty));
            sb.Append(CsvText.JoinLine(cells)).Append('\n');
        }

        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);
        return CommandResult.Ok($"Đã xuất {rows.Count} block ({tags.Count} attribute) ra \"{config.OutputPath}\".", rows.Count);
    }

    internal static IEnumerable<BlockReference> EnumerateInserts(Database db, Transaction tr, bool modelSpaceOnly)
    {
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!btr.IsLayout)
            {
                continue;
            }

            if (modelSpaceOnly && btrId != SymbolUtilityServices.GetBlockModelSpaceId(db))
            {
                continue;
            }

            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                {
                    yield return br;
                }
            }
        }
    }

    internal static string BlockName(Transaction tr, BlockReference br)
        => br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
}

/// <summary>Ghi ngược CSV (đã sửa trong Excel) vào attribute theo Handle. Mọi ô bị bỏ qua đều có một dòng lý do.</summary>
public sealed class AttributeImportCommand : ICoreCommand<AttributeImportConfig>
{
    public string CommandName => "AttributeImport";

    public CommandResult Execute(Database database, AttributeImportConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy file \"{config.InputPath}\".");
        }

        var lines = File.ReadAllLines(config.InputPath, CsvText.Utf8WithBom);
        if (lines.Length < 2)
        {
            return CommandResult.Fail("File CSV chỉ có tiêu đề hoặc rỗng.");
        }

        var header = CsvText.SplitLine(lines[0]);
        var handleCol = header.FindIndex(h => h.Equals("Handle", StringComparison.OrdinalIgnoreCase));
        if (handleCol < 0)
        {
            return CommandResult.Fail("CSV thiếu cột Handle.");
        }

        var fixedCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Handle", "BlockName", "Layer", "X", "Y" };
        var tagCols = header.Select((h, i) => (Tag: h, Index: i)).Where(t => !fixedCols.Contains(t.Tag) && t.Tag.Length > 0).ToList();

        var result = CommandResult.Ok(string.Empty);
        var plan = new List<(ObjectId Id, string Handle, Dictionary<string, string> Values)>();

        using var tr = database.TransactionManager.StartTransaction();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = CsvText.SplitLine(lines[i]);
            if (cells.Count <= handleCol)
            {
                result.Messages.Add($"Dòng {i + 1}: thiếu Handle — bỏ qua.");
                continue;
            }

            ObjectId id;
            try
            {
                id = database.GetObjectId(false, new Handle(Convert.ToInt64(cells[handleCol], 16)), 0);
            }
            catch
            {
                result.Messages.Add($"Dòng {i + 1}: Handle \"{cells[handleCol]}\" không tồn tại — bỏ qua.");
                continue;
            }

            if (!id.IsValid || tr.GetObject(id, OpenMode.ForRead) is not BlockReference)
            {
                result.Messages.Add($"Dòng {i + 1}: Handle \"{cells[handleCol]}\" không phải Block Reference — bỏ qua.");
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tag, index) in tagCols)
            {
                if (index < cells.Count)
                {
                    values[tag] = cells[index];
                }
            }
            plan.Add((id, cells[handleCol], values));
        }

        if (config.DryRun)
        {
            tr.Abort();
            result.Summary = $"[Xem trước] Sẽ ghi attribute cho {plan.Count} block ({tagCols.Count} tag).";
            result.Messages.AddRange(plan.Take(200).Select(p => $"{p.Handle}: {string.Join(", ", p.Values.Select(kv => kv.Key + "=" + kv.Value))}"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var updated = 0;
        var cells_ = 0;
        foreach (var (id, handle, values) in plan)
        {
            try
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var any = false;
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                    found.Add(att.Tag);
                    if (!values.TryGetValue(att.Tag, out var v) || string.Equals(att.TextString, v, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    att.UpgradeOpen();
                    att.TextString = v;
                    any = true;
                    cells_++;
                }

                foreach (var missing in values.Keys.Where(k => !found.Contains(k)))
                {
                    result.Messages.Add($"{handle}: block không có attribute \"{missing}\" — bỏ qua ô.");
                }

                if (any) updated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{handle}: {ex.Message}");
            }
        }

        tr.Commit();
        result.Summary = $"Đã cập nhật {cells_} attribute trên {updated}/{plan.Count} block.";
        result.AffectedCount = updated;
        return result;
    }
}
