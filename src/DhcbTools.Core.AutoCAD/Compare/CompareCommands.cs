using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.Compare;

/// <summary>Mục 7.9 — so bản vẽ hiện tại với một DWG khác (đọc offline bằng side database).</summary>
public sealed class DrawingCompareConfig
{
    public required string OtherPath { get; init; }

    /// <summary>.csv hoặc .html theo đuôi file.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Dung sai dời (đơn vị bản vẽ).</summary>
    public double MoveToleranceMm { get; init; } = 1.0;

    /// <summary>Bản vẽ hiện tại là bản MỚI (mặc định) — "Added" nghĩa là có trong hiện tại, không có trong file khác.</summary>
    public bool CurrentIsNewer { get; init; } = true;
}

public sealed class DrawingCompareCommand : ICoreCommand<DrawingCompareConfig>
{
    public string CommandName => "DrawingCompare";

    public CommandResult Execute(Database database, DrawingCompareConfig config)
    {
        if (!File.Exists(config.OtherPath))
        {
            return CommandResult.Fail($"Không tìm thấy \"{config.OtherPath}\".");
        }

        var current = Snapshot(database);
        List<EntitySnapshot> other;
        using (var side = new Database(false, true))
        {
            try
            {
                side.ReadDwgFile(config.OtherPath, FileOpenMode.OpenForReadAndAllShare, true, null);
                side.CloseInput(true);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("Không đọc được DWG: " + ex.Message);
            }
            other = Snapshot(side);
        }

        var diff = config.CurrentIsNewer ? DiffSummary.Compare(other, current, config.MoveToleranceMm) : DiffSummary.Compare(current, other, config.MoveToleranceMm);
        var dir = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (config.OutputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(config.OutputPath, DiffSummary.ToHtml($"So sánh: {Path.GetFileName(database.Filename)} ↔ {Path.GetFileName(config.OtherPath)}", diff), Encoding.UTF8);
        }
        else
        {
            File.WriteAllText(config.OutputPath, DiffSummary.ToCsv(diff), CsvText.Utf8WithBom);
        }

        var counts = DiffSummary.Count(diff);
        var result = CommandResult.Ok($"{diff.Count} khác biệt ({string.Join(", ", counts.Select(c => c.Key + "=" + c.Value))}) → \"{config.OutputPath}\".", diff.Count);
        result.Messages.AddRange(diff.Take(200).Select(d => $"{d.Kind} {d.Handle} {d.Type}: {d.Detail}"));
        return result;
    }

    internal static List<EntitySnapshot> Snapshot(Database db)
    {
        var list = new List<EntitySnapshot>();
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!btr.IsLayout) continue;
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                double x = 0, y = 0;
                string? text = null;
                switch (e)
                {
                    case BlockReference br: x = br.Position.X; y = br.Position.Y; text = AttributeText(tr, br); break;
                    case DBText t: x = t.Position.X; y = t.Position.Y; text = t.TextString; break;
                    case MText m: x = m.Location.X; y = m.Location.Y; text = m.Contents; break;
                    default:
                        try
                        {
                            var ext = e.GeometricExtents;
                            x = (ext.MinPoint.X + ext.MaxPoint.X) / 2;
                            y = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2;
                        }
                        catch { /* entity không có extents */ }
                        break;
                }
                list.Add(new EntitySnapshot(e.Handle.ToString(), e.GetType().Name, e.Layer, x, y, text));
            }
        }
        tr.Abort();
        return list;
    }

    private static string AttributeText(Transaction tr, BlockReference br)
    {
        var parts = new List<string> { br.Name };
        foreach (ObjectId attId in br.AttributeCollection)
        {
            var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
            parts.Add(att.Tag + "=" + att.TextString);
        }
        return string.Join("|", parts);
    }
}

/// <summary>Mục 7.10 — đếm block ra BOM (Data Extraction rút gọn).</summary>
public sealed class BlockQuantityConfig
{
    public required string OutputPath { get; init; }

    /// <summary>Nhóm theo giá trị attribute này (ví dụ SIZE) ngoài tên block.</summary>
    public string? GroupByAttribute { get; init; }

    public string? BlockNameContains { get; init; }

    public bool ModelSpaceOnly { get; init; } = true;
}

public sealed class BlockQuantityCommand : ICoreCommand<BlockQuantityConfig>
{
    public string CommandName => "BlockQuantity";

    public CommandResult Execute(Database database, BlockQuantityConfig config)
    {
        var counts = new Dictionary<(string Block, string Group, string Layer), int>();
        using (var tr = database.TransactionManager.StartTransaction())
        {
            foreach (var br in Attributes.AttributeExportCommand.EnumerateInserts(database, tr, config.ModelSpaceOnly))
            {
                var name = Attributes.AttributeExportCommand.BlockName(tr, br);
                if (!string.IsNullOrEmpty(config.BlockNameContains) && name.IndexOf(config.BlockNameContains!, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var group = string.Empty;
                if (!string.IsNullOrEmpty(config.GroupByAttribute))
                {
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                        if (att.Tag.Equals(config.GroupByAttribute, StringComparison.OrdinalIgnoreCase)) { group = att.TextString; break; }
                    }
                }
                var key = (name, group, br.Layer);
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            tr.Abort();
        }

        var sb = new StringBuilder(CsvText.JoinLine(new[] { "BlockName", config.GroupByAttribute ?? "Group", "Layer", "Count" }) + "\n");
        foreach (var kv in counts.OrderBy(k => k.Key.Block, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Key.Group, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(CsvText.JoinLine(new[] { kv.Key.Block, kv.Key.Group, kv.Key.Layer, kv.Value.ToString() })).Append('\n');
        }
        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        var result = CommandResult.Ok($"{counts.Values.Sum()} block, {counts.Count} dòng BOM → \"{config.OutputPath}\".", counts.Values.Sum());
        result.Messages.AddRange(counts.OrderByDescending(k => k.Value).Take(50).Select(k => $"{k.Key.Block}{(k.Key.Group.Length > 0 ? " [" + k.Key.Group + "]" : string.Empty)}: {k.Value}"));
        return result;
    }
}

/// <summary>Mục 7.11 — attribute tăng dần theo mẫu (Lee Mac BATTE), thứ tự theo vị trí như AutoNumbering.</summary>
public sealed class AttributeIncrementConfig
{
    public required string BlockName { get; init; }

    public string AttributeTag { get; init; } = "MARK";

    /// <summary>Mẫu giá trị, ví dụ <c>P-{n:000}</c>; token {Layer} {Block} {Old} dùng được.</summary>
    public string Pattern { get; init; } = "{n}";

    public int StartNumber { get; init; } = 1;

    public int Step { get; init; } = 1;

    /// <summary>LeftToRightThenTopToBottom | TopToBottomThenLeftToRight.</summary>
    public string Direction { get; init; } = "LeftToRightThenTopToBottom";

    public double RowTolerance { get; init; } = 300;

    public bool DryRun { get; init; } = true;
}

public sealed class AttributeIncrementCommand : ICoreCommand<AttributeIncrementConfig>
{
    public string CommandName => "AttributeIncrement";

    public CommandResult Execute(Database database, AttributeIncrementConfig config)
    {
        using var tr = database.TransactionManager.StartTransaction();
        var refs = Attributes.AttributeExportCommand.EnumerateInserts(database, tr, true)
            .Where(br => string.Equals(Attributes.AttributeExportCommand.BlockName(tr, br), config.BlockName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (refs.Count == 0)
        {
            tr.Abort();
            return CommandResult.Fail($"Không có block \"{config.BlockName}\" trong Model Space.");
        }

        var items = refs.Select(br => new NumberingItem<BlockReference>(br, br.Position.X, br.Position.Y)).ToList();
        var direction = config.Direction.Equals("TopToBottomThenLeftToRight", StringComparison.OrdinalIgnoreCase) ? ScanDirection.TopToBottomThenLeftToRight : ScanDirection.LeftToRightThenTopToBottom;
        var ordered = NumberingPlanner.Order(items, direction, config.RowTolerance);

        var pattern = new NamePattern(config.Pattern) { CounterStart = config.StartNumber, CounterStep = config.Step };
        var plan = new List<(BlockReference Ref, AttributeReference? Att, string Value)>();
        var result = CommandResult.Ok(string.Empty);
        for (var i = 0; i < ordered.Count; i++)
        {
            var br = ordered[i].Key;
            AttributeReference? target = null;
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                if (att.Tag.Equals(config.AttributeTag, StringComparison.OrdinalIgnoreCase)) { target = att; break; }
            }
            if (target == null)
            {
                result.Messages.Add($"{br.Handle}: không có attribute \"{config.AttributeTag}\" — bỏ qua.");
                continue;
            }
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Layer"] = br.Layer, ["Block"] = config.BlockName, ["Old"] = target.TextString };
            plan.Add((br, target, pattern.Apply(i, values)));
        }

        if (config.DryRun)
        {
            tr.Abort();
            result.Summary = $"[Xem trước] Sẽ ghi {plan.Count} giá trị \"{config.AttributeTag}\" theo mẫu \"{config.Pattern}\".";
            result.Messages.AddRange(plan.Take(300).Select(p => $"{p.Ref.Handle}: {p.Att!.TextString} → {p.Value}"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var done = 0;
        foreach (var (_, att, value) in plan)
        {
            try { att!.UpgradeOpen(); att.TextString = value; done++; }
            catch (Exception ex) { result.Errors.Add(ex.Message); }
        }
        tr.Commit();
        result.Summary = $"Đã ghi {done}/{plan.Count} attribute.";
        result.AffectedCount = done;
        return result;
    }
}
