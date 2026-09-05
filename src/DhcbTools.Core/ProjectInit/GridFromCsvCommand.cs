using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Geometry;

namespace DhcbTools.Core.ProjectInit;

/// <summary>Mục 2.3 — trục/level từ CSV (Excel hoặc CSV do AutoCAD <c>GridExtract</c> sinh từ layer AXIS).</summary>
public sealed class GridFromCsvConfig
{
    /// <summary>CSV <c>Name,X1,Y1,X2,Y2</c> (mm). Rỗng = không tạo trục.</summary>
    public string? GridCsvPath { get; init; }

    /// <summary>CSV <c>Name,Elevation</c> (mm). Rỗng = không tạo level.</summary>
    public string? LevelCsvPath { get; init; }

    /// <summary>Đặt lại tên trục theo quy tắc (chữ dọc, số ngang) thay cho tên trong CSV.</summary>
    public bool RenameByRule { get; init; } = false;

    /// <summary>Dời gốc CAD → gốc Revit (mm).</summary>
    public double OffsetXMm { get; init; }

    public double OffsetYMm { get; init; }

    public bool SkipExisting { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

public sealed class GridFromCsvCommand : ICoreCommand<GridFromCsvConfig>
{
    public string CommandName => "GridFromCsv";

    public CommandResult Execute(Document document, GridFromCsvConfig config)
    {
        if (string.IsNullOrEmpty(config.GridCsvPath) && string.IsNullOrEmpty(config.LevelCsvPath))
        {
            return CommandResult.Fail("Cần ít nhất gridCsvPath hoặc levelCsvPath.");
        }

        var result = CommandResult.Ok(string.Empty);
        var grids = new List<GridLine>();
        var levels = new List<(string Name, double Mm)>();

        if (!string.IsNullOrEmpty(config.GridCsvPath))
        {
            if (!File.Exists(config.GridCsvPath)) return CommandResult.Fail($"Không tìm thấy \"{config.GridCsvPath}\".");
            var errors = new List<string>();
            grids = GridNaming.FromCsv(File.ReadAllText(config.GridCsvPath, CsvText.Utf8WithBom), errors);
            result.Messages.AddRange(errors);
            if (config.RenameByRule)
            {
                GridNaming.Apply(grids);
            }
        }

        if (!string.IsNullOrEmpty(config.LevelCsvPath))
        {
            if (!File.Exists(config.LevelCsvPath)) return CommandResult.Fail($"Không tìm thấy \"{config.LevelCsvPath}\".");
            // Đọc theo RFC 4180 (CsvText.ReadRecords): ô có nháy được phép chứa dấu phẩy và xuống dòng —
            // ReadAllLines + SplitLine cắt nhầm đúng những ô mà CsvText.Escape ghi ra.
            var rows = CsvText.ReadRecords(config.LevelCsvPath!).ToList();
            for (var i = 1; i < rows.Count; i++)
            {
                var cells = rows[i];
                if (cells.All(string.IsNullOrWhiteSpace)) continue;
                if (cells.Length < 2 || !NumericText.TryParseDouble(cells[1], out var mm))
                {
                    result.Messages.Add($"Level CSV dòng {i + 1}: cần Name,Elevation — bỏ qua.");
                    continue;
                }
                levels.Add((cells[0].Trim(), mm));
            }
        }

        var existingGrids = new FilteredElementCollector(document).OfClass(typeof(Grid)).Cast<Grid>().Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingLevels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var gridPlan = grids.Where(g => !(config.SkipExisting && existingGrids.Contains(g.Name))).ToList();
        var levelPlan = levels.Where(l => !(config.SkipExisting && existingLevels.Contains(l.Name))).ToList();
        foreach (var g in grids.Except(gridPlan)) result.Messages.Add($"Trục \"{g.Name}\" đã có — bỏ qua.");
        foreach (var l in levels.Except(levelPlan)) result.Messages.Add($"Level \"{l.Name}\" đã có — bỏ qua.");

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ tạo {gridPlan.Count} trục và {levelPlan.Count} level.";
            result.Messages.AddRange(gridPlan.Select(g => $"Trục {g.Name}: {(g.IsVertical ? "X" : "Y")}={NumericText.Format(g.Position, 1)} mm"));
            result.Messages.AddRange(levelPlan.Select(l => $"Level {l.Name}: {NumericText.Format(l.Mm, 1)} mm"));
            result.AffectedCount = gridPlan.Count + levelPlan.Count;
            return result;
        }

        var created = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Trục/Level từ CSV");
        foreach (var g in gridPlan)
        {
            try
            {
                var ox = RevitCompat.MmToFt(config.OffsetXMm);
                var oy = RevitCompat.MmToFt(config.OffsetYMm);
                Line line = g.IsVertical
                    ? Line.CreateBound(new XYZ(RevitCompat.MmToFt(g.Position) + ox, RevitCompat.MmToFt(g.Start) + oy, 0), new XYZ(RevitCompat.MmToFt(g.Position) + ox, RevitCompat.MmToFt(g.End) + oy, 0))
                    : Line.CreateBound(new XYZ(RevitCompat.MmToFt(g.Start) + ox, RevitCompat.MmToFt(g.Position) + oy, 0), new XYZ(RevitCompat.MmToFt(g.End) + ox, RevitCompat.MmToFt(g.Position) + oy, 0));
                var grid = Grid.Create(document, line);
                if (!string.IsNullOrWhiteSpace(g.Name))
                {
                    grid.Name = g.Name;
                }
                created++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Trục {g.Name}: {ex.Message}");
            }
        }

        foreach (var (name, mm) in levelPlan)
        {
            try
            {
                var level = Level.Create(document, RevitCompat.MmToFt(mm));
                level.Name = name;
                created++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Level {name}: {ex.Message}");
            }
        }

        tx.Commit();
        result.Summary = $"Đã tạo {created}/{gridPlan.Count + levelPlan.Count} trục/level.";
        result.AffectedCount = created;
        return result;
    }
}
