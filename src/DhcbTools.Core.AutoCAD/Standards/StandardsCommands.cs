using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Geometry;

namespace DhcbTools.Core.AutoCAD.Standards;

/// <summary>Kiểm tra layer theo bộ quy tắc JSON (category "Layer", parameter Name/Color/Linetype/Lineweight/Plottable).</summary>
public sealed class LayerStandardCheckConfig
{
    public required string RulesPath { get; init; }

    public required string OutputPath { get; init; }
}

public sealed class LayerStandardCheckCommand : ICoreCommand<LayerStandardCheckConfig>
{
    public string CommandName => "LayerStandardCheck";

    public CommandResult Execute(Database database, LayerStandardCheckConfig config)
    {
        if (!File.Exists(config.RulesPath))
        {
            return CommandResult.Fail($"Không tìm thấy file quy tắc \"{config.RulesPath}\".");
        }

        List<ParameterRule> rules;
        try
        {
            rules = RuleChecker.ParseRules(File.ReadAllText(config.RulesPath)).Where(r => r.Category.Equals("Layer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(r.Category)).ToList();
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("File quy tắc không hợp lệ: " + ex.Message);
        }

        if (rules.Count == 0)
        {
            return CommandResult.Fail("Không có quy tắc nào cho category \"Layer\".");
        }

        var violations = new List<RuleViolation>();
        var checkedCount = 0;
        using (var tr = database.TransactionManager.StartTransaction())
        {
            var layerTable = (LayerTable)tr.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in layerTable)
            {
                var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (layer.IsDependent) continue; // layer của xref

                foreach (var rule in rules)
                {
                    var value = rule.Parameter.ToUpperInvariant() switch
                    {
                        "NAME" => layer.Name,
                        "COLOR" => layer.Color.IsByAci ? layer.Color.ColorIndex.ToString() : layer.Color.ColorValue.ToString(),
                        "LINETYPE" => LinetypeName(tr, database, layer.LinetypeObjectId),
                        "LINEWEIGHT" => layer.LineWeight.ToString(),
                        "PLOTTABLE" => layer.IsPlottable ? "true" : "false",
                        "DESCRIPTION" => layer.Description ?? string.Empty,
                        _ => null,
                    };
                    if (value == null)
                    {
                        continue;
                    }

                    checkedCount++;
                    var reason = RuleChecker.Check(rule, value);
                    if (reason != null)
                    {
                        violations.Add(new RuleViolation("Layer", id.Handle.ToString(), layer.Name, rule.Parameter, value, reason + (rule.Description != null ? " — " + rule.Description : string.Empty), rule.Severity));
                    }
                }
            }
            tr.Abort();
        }

        File.WriteAllText(config.OutputPath, RuleChecker.RenderHtml("DHCB - Kiểm tra chuẩn layer: " + Path.GetFileName(database.Filename), violations, checkedCount), Encoding.UTF8);
        var result = CommandResult.Ok($"Đã kiểm {checkedCount} giá trị, {violations.Count} vi phạm → \"{config.OutputPath}\".", violations.Count);
        result.Messages.AddRange(violations.Take(200).Select(v => $"{v.ElementName}.{v.Parameter} = \"{v.Value}\": {v.Reason}"));
        return result;
    }

    internal static string LinetypeName(Transaction tr, Database db, ObjectId id)
    {
        if (id == db.ContinuousLinetype) return "Continuous";
        try { return ((LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name; } catch { return "Continuous"; }
    }
}

/// <summary>Mục 2.3 — trích trục từ layer AXIS ra CSV cho Revit <c>GridFromCsv</c>.</summary>
public sealed class GridExtractConfig
{
    public string GridLayer { get; init; } = "AXIS";

    public required string OutputPath { get; init; }

    /// <summary>Dung sai gom đoạn cùng trục (đơn vị bản vẽ, thường mm).</summary>
    public double PositionTolerance { get; init; } = 50;

    public double MinLength { get; init; } = 500;

    /// <summary>Đặt tên theo quy tắc (A,B,C dọc; 1,2,3 ngang) thay vì đọc text gần trục.</summary>
    public bool NameByRule { get; init; } = true;

    /// <summary>Hệ số đổi đơn vị bản vẽ → mm (bản vẽ mét: 1000; bản vẽ mm: 1).</summary>
    public double UnitToMm { get; init; } = 1;
}

public sealed class GridExtractCommand : ICoreCommand<GridExtractConfig>
{
    public string CommandName => "GridExtract";

    public CommandResult Execute(Database database, GridExtractConfig config)
    {
        var segments = new List<Segment2D>();
        var texts = new List<(double X, double Y, string Text)>();
        using (var tr = database.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.Layer.IndexOf(config.GridLayer, StringComparison.OrdinalIgnoreCase) < 0) continue;

                switch (ent)
                {
                    case Line l:
                        segments.Add(new Segment2D(l.StartPoint.X * config.UnitToMm, l.StartPoint.Y * config.UnitToMm, l.EndPoint.X * config.UnitToMm, l.EndPoint.Y * config.UnitToMm));
                        break;
                    case Polyline pl:
                        for (var i = 0; i < pl.NumberOfVertices - 1; i++)
                        {
                            var a = pl.GetPoint2dAt(i);
                            var b = pl.GetPoint2dAt(i + 1);
                            segments.Add(new Segment2D(a.X * config.UnitToMm, a.Y * config.UnitToMm, b.X * config.UnitToMm, b.Y * config.UnitToMm));
                        }
                        break;
                    case DBText t:
                        texts.Add((t.Position.X * config.UnitToMm, t.Position.Y * config.UnitToMm, t.TextString));
                        break;
                    case BlockReference br:
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                            texts.Add((br.Position.X * config.UnitToMm, br.Position.Y * config.UnitToMm, att.TextString));
                        }
                        break;
                }
            }
            tr.Abort();
        }

        if (segments.Count == 0)
        {
            return CommandResult.Fail($"Không có Line/Polyline nào trên layer chứa \"{config.GridLayer}\" trong Model Space.");
        }

        var grids = GridClustering.Cluster(segments, config.PositionTolerance, minLength: config.MinLength);
        if (config.NameByRule || texts.Count == 0)
        {
            GridNaming.Apply(grids);
        }
        else
        {
            // Tên từ text/bubble gần đầu trục nhất (trong 3 m).
            foreach (var g in grids)
            {
                var ends = g.IsVertical ? new[] { (X: g.Position, Y: g.Start), (X: g.Position, Y: g.End) } : new[] { (X: g.Start, Y: g.Position), (X: g.End, Y: g.Position) };
                var best = texts.Select(t => (t, d: ends.Min(e => Math.Sqrt((e.X - t.X) * (e.X - t.X) + (e.Y - t.Y) * (e.Y - t.Y))))).Where(x => x.d < 3000).OrderBy(x => x.d).FirstOrDefault();
                g.Name = best.t.Text?.Trim() ?? string.Empty;
            }

            var unnamed = grids.Where(g => string.IsNullOrEmpty(g.Name)).ToList();
            if (unnamed.Count > 0)
            {
                GridNaming.Apply(unnamed);
            }
        }

        File.WriteAllText(config.OutputPath, GridNaming.ToCsv(grids), CsvText.Utf8WithBom);
        var result = CommandResult.Ok($"Đã trích {grids.Count} trục ({grids.Count(g => g.IsVertical)} dọc, {grids.Count(g => !g.IsVertical)} ngang) từ {segments.Count} đoạn → \"{config.OutputPath}\". Nhập vào Revit bằng GridFromCsv.", grids.Count);
        result.Messages.AddRange(grids.Select(g => $"{g.Name}: {(g.IsVertical ? "X" : "Y")}={NumericText.Format(g.Position, 1)} ({g.SegmentCount} đoạn)"));
        return result;
    }
}

/// <summary>Liệt kê xref: trạng thái, đường dẫn, thiếu file.</summary>
public sealed class XrefAuditConfig
{
    public string? OutputPath { get; init; }
}

public sealed class XrefAuditCommand : ICoreCommand<XrefAuditConfig>
{
    public string CommandName => "XrefAudit";

    public CommandResult Execute(Database database, XrefAuditConfig config)
    {
        var rows = new List<(string Name, string Path, string Status, bool Missing, bool Overlay)>();
        using (var tr = database.TransactionManager.StartTransaction())
        {
            var blockTable = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in blockTable)
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!btr.IsFromExternalReference) continue;

                var path = btr.PathName ?? string.Empty;
                var resolved = ResolvePath(database.Filename, path);
                var missing = !string.IsNullOrEmpty(path) && !File.Exists(resolved);
                rows.Add((btr.Name, path, btr.XrefStatus.ToString(), missing, btr.IsFromOverlayReference));
            }
            tr.Abort();
        }

        if (!string.IsNullOrEmpty(config.OutputPath))
        {
            var sb = new StringBuilder("Name,Path,Status,Missing,Overlay\n");
            foreach (var r in rows)
            {
                sb.Append(CsvText.JoinLine(new[] { r.Name, r.Path, r.Status, r.Missing ? "true" : "false", r.Overlay ? "true" : "false" })).Append('\n');
            }
            File.WriteAllText(config.OutputPath!, sb.ToString(), CsvText.Utf8WithBom);
        }

        var result = CommandResult.Ok($"{rows.Count} xref, {rows.Count(r => r.Missing)} thiếu file, {rows.Count(r => r.Status != "Resolved")} chưa resolve.", rows.Count);
        result.Messages.AddRange(rows.Select(r => $"{r.Name}: {r.Status}{(r.Missing ? " — THIẾU FILE" : string.Empty)} ({r.Path})"));
        return result;
    }

    private static string ResolvePath(string hostPath, string xrefPath)
    {
        if (Path.IsPathRooted(xrefPath)) return xrefPath;
        var dir = Path.GetDirectoryName(hostPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(dir, xrefPath));
    }
}

/// <summary>Mục 5.1 phía AutoCAD — map layer của drawing hiện tại → Revit type (danh sách type từ file text).</summary>
public sealed class CadLayerMapConfig
{
    /// <summary>File .txt mỗi dòng một "Family: Type" (xuất từ Revit) — danh sách type có thật.</summary>
    public required string RevitTypesPath { get; init; }

    public required string OutputPath { get; init; }

    public bool UseOllama { get; init; }

    public double MinConfidence { get; init; } = 0.3;
}

public sealed class CadLayerMapCommand : ICoreCommand<CadLayerMapConfig>
{
    public string CommandName => "CadLayerMap";

    public CommandResult Execute(Database database, CadLayerMapConfig config)
    {
        if (!File.Exists(config.RevitTypesPath))
        {
            return CommandResult.Fail($"Không tìm thấy \"{config.RevitTypesPath}\".");
        }

        var types = File.ReadAllLines(config.RevitTypesPath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
        if (types.Count == 0)
        {
            return CommandResult.Fail("Danh sách Revit type rỗng.");
        }

        var layers = new List<string>();
        using (var tr = database.TransactionManager.StartTransaction())
        {
            var layerTable = (LayerTable)tr.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in layerTable)
            {
                var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!layer.IsDependent) layers.Add(layer.Name);
            }
            tr.Abort();
        }

        var result = CommandResult.Ok(string.Empty);
        var mappings = LayerMappingSuggester.Suggest(layers, types, config.MinConfidence);
        var source = "heuristic offline";
        if (config.UseOllama)
        {
            var client = new OllamaClient(LocalAiSettings.Load());
            var rejected = new List<string>();
            var fromModel = client.IsUsable ? client.SuggestLayerMappings(layers, types, rejected) : null;
            if (fromModel != null)
            {
                var byLayer = fromModel.ToDictionary(m => m.Layer, m => m, StringComparer.OrdinalIgnoreCase);
                mappings = mappings.Select(h => byLayer.TryGetValue(h.Layer, out var m) && (h.RevitType == null || m.Confidence >= h.Confidence) ? m : h).ToList();
                result.Messages.AddRange(rejected.Select(r => "Model: " + r));
                source = "model local + heuristic";
            }
            else
            {
                result.Messages.Add("Model local không dùng được — dùng heuristic.");
            }
        }

        File.WriteAllText(config.OutputPath, LayerMappingSuggester.ToCsv(mappings), CsvText.Utf8WithBom);
        result.Summary = $"Đã gợi ý map {mappings.Count} layer ({mappings.Count(m => m.NeedsReview)} cần xem, nguồn: {source}) → \"{config.OutputPath}\".";
        result.AffectedCount = mappings.Count;
        return result;
    }
}
