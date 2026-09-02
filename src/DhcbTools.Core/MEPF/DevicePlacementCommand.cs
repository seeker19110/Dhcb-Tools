using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

public sealed class RoomFilterConfig
{
    public string? LevelName { get; init; }

    public string? NameContains { get; init; }

    /// <summary>Chỉ phòng có diện tích ≥ (m²). 0 = mọi phòng.</summary>
    public double MinAreaM2 { get; init; }
}

public sealed class DevicePatternConfig
{
    /// <summary>Hiện chỉ hỗ trợ "grid".</summary>
    public string Type { get; init; } = "grid";

    public double SpacingXMm { get; init; } = 3000;

    public double SpacingYMm { get; init; } = 3000;

    public double MarginMm { get; init; } = 1500;
}

/// <summary>Cấu hình routing mức B (mục 3.2): rải thiết bị đầu cuối theo phòng.</summary>
public sealed class DevicePlacementConfig
{
    /// <summary>"Family: Type" hoặc tên type của FamilySymbol (sprinkler, miệng gió, đèn…).</summary>
    public required string DeviceFamily { get; init; }

    public RoomFilterConfig RoomFilter { get; init; } = new RoomFilterConfig();

    public DevicePatternConfig Pattern { get; init; } = new DevicePatternConfig();

    /// <summary>Bán kính phủ (mm); 0 = không kiểm tra phủ.</summary>
    public double MaxCoverageRadiusMm { get; init; } = 2300;

    /// <summary>Cao độ đặt so với level (mm). null = cao độ trần phòng (Unbounded Height) hoặc 0.</summary>
    public double? MountHeightMm { get; init; }

    /// <summary>Loại phần tử làm "lỗ" (cột, hộp gen) — tên category; rỗng = Structural Columns + Columns.</summary>
    public List<string> ObstacleCategories { get; init; } = new List<string>();

    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Rải thiết bị đầu cuối theo lưới trong biên phòng (<see cref="DevicePattern"/>), loại điểm trong cột, kiểm tra phủ,
/// đặt FamilyInstance. Nối về trục chính là bước tiếp theo bằng <see cref="RouteFromLinesCommand"/> (mục 3.2 nói rõ
/// dùng lại thuật toán 3.1).
/// </summary>
public sealed class DevicePlacementCommand : ICoreCommand<DevicePlacementConfig>
{
    public string CommandName => "DevicePlacement";

    public CommandResult Execute(Document document, DevicePlacementConfig config)
    {
        var symbol = RevitCompat.FindType<FamilySymbol>(document, config.DeviceFamily);
        if (symbol == null)
        {
            return CommandResult.Fail($"Không tìm thấy family/type \"{config.DeviceFamily}\" trong mô hình (đã load chưa?).");
        }

        var rooms = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(r => r.Area > 0 && r.Location != null)
            .Where(r => string.IsNullOrEmpty(config.RoomFilter.LevelName) || string.Equals(r.Level?.Name, config.RoomFilter.LevelName, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(config.RoomFilter.NameContains) || (r.Name ?? string.Empty).IndexOf(config.RoomFilter.NameContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(r => config.RoomFilter.MinAreaM2 <= 0 || RevitCompat.SqFtToSqm(r.Area) >= config.RoomFilter.MinAreaM2)
            .ToList();

        if (rooms.Count == 0)
        {
            return CommandResult.Fail("Không có phòng nào khớp bộ lọc.");
        }

        var obstacles = CollectObstacles(document, config.ObstacleCategories);
        var options = new GridPatternOptions
        {
            SpacingX = config.Pattern.SpacingXMm,
            SpacingY = config.Pattern.SpacingYMm,
            Margin = config.Pattern.MarginMm,
            CoverageRadius = config.MaxCoverageRadiusMm,
        };

        var plans = new List<(Room Room, DevicePlacementPlan Plan, double ZFt)>();
        var result = CommandResult.Ok(string.Empty);

        foreach (var room in rooms)
        {
            var boundary = OuterBoundaryMm(room);
            if (boundary == null || boundary.Count < 3)
            {
                result.Messages.Add($"Phòng {room.Name} ({room.Id}): không lấy được biên — bỏ qua.");
                continue;
            }

            var holes = obstacles.Where(o => DevicePattern.Contains(boundary, DevicePattern.Centroid(o))).ToList();
            DevicePlacementPlan plan;
            try
            {
                plan = DevicePattern.GridInPolygon(boundary, options, holes);
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Phòng {room.Name}: {ex.Message}");
                continue;
            }

            var z = room.Level!.Elevation + RevitCompat.MmToFt(config.MountHeightMm ?? (room.UnboundedHeight > 0 ? RevitCompat.FtToMm(room.UnboundedHeight) : 0));
            plans.Add((room, plan, z));
            result.Messages.Add($"Phòng {room.Name}: {plan.Points.Count} thiết bị ({plan.AddedForCoverage.Count} chèn thêm để phủ, {plan.Uncovered.Count} điểm chưa phủ).");
            result.Messages.AddRange(plan.Messages.Select(m => "  " + m));
        }

        var total = plans.Sum(p => p.Plan.Points.Count);
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ đặt {total} \"{symbol.FamilyName}: {symbol.Name}\" trong {plans.Count} phòng.";
            result.AffectedCount = total;
            return result;
        }

        var placed = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Rải thiết bị theo phòng");
        if (!symbol.IsActive)
        {
            symbol.Activate();
            document.Regenerate();
        }

        foreach (var (room, plan, z) in plans)
        {
            foreach (var p in plan.Points)
            {
                try
                {
                    var xyz = new XYZ(RevitCompat.MmToFt(p.X), RevitCompat.MmToFt(p.Y), z);
                    document.Create.NewFamilyInstance(xyz, symbol, room.Level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    placed++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Phòng {room.Name} tại {p}: {ex.Message}");
                }
            }
        }

        tx.Commit();
        result.Summary = $"Đã đặt {placed}/{total} thiết bị trong {plans.Count} phòng.";
        result.AffectedCount = placed;
        return result;
    }

    /// <summary>Biên ngoài của phòng (vòng dài nhất) đổi sang mm, XY.</summary>
    internal static List<Point2>? OuterBoundaryMm(Room room)
    {
        var loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish });
        if (loops == null || loops.Count == 0)
        {
            return null;
        }

        List<Point2>? best = null;
        var bestArea = -1.0;
        foreach (var loop in loops)
        {
            var pts = new List<Point2>();
            foreach (var seg in loop)
            {
                var curve = seg.GetCurve();
                var tess = curve.Tessellate();
                for (var i = 0; i < tess.Count - 1; i++)
                {
                    pts.Add(new Point2(RevitCompat.FtToMm(tess[i].X), RevitCompat.FtToMm(tess[i].Y)));
                }
            }

            if (pts.Count < 3)
            {
                continue;
            }

            var area = DevicePattern.Area(pts);
            if (area > bestArea)
            {
                bestArea = area;
                best = pts;
            }
        }

        return best;
    }

    private static List<List<Point2>> CollectObstacles(Document doc, List<string> categoryNames)
    {
        var cats = new List<BuiltInCategory>();
        if (categoryNames.Count == 0)
        {
            cats.Add(BuiltInCategory.OST_StructuralColumns);
            cats.Add(BuiltInCategory.OST_Columns);
        }

        var collector = categoryNames.Count == 0
            ? new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(cats)).WhereElementIsNotElementType()
            : new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(ParameterSync.ParameterExportCommand.ResolveCategoryIds(doc, categoryNames, out _).ToList()));

        var result = new List<List<Point2>>();
        foreach (var el in collector)
        {
            var bb = el.get_BoundingBox(null);
            if (bb == null)
            {
                continue;
            }

            result.Add(new List<Point2>
            {
                new Point2(RevitCompat.FtToMm(bb.Min.X), RevitCompat.FtToMm(bb.Min.Y)),
                new Point2(RevitCompat.FtToMm(bb.Max.X), RevitCompat.FtToMm(bb.Min.Y)),
                new Point2(RevitCompat.FtToMm(bb.Max.X), RevitCompat.FtToMm(bb.Max.Y)),
                new Point2(RevitCompat.FtToMm(bb.Min.X), RevitCompat.FtToMm(bb.Max.Y)),
            });
        }
        return result;
    }
}
