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
    /// <summary>
    /// Xét cả phòng ở model LIÊN KẾT (mặc định bật). File MEP thuần không có phòng nào — phòng nằm ở
    /// model kiến trúc, nên tắt cái này là lệnh dừng ngay ở bước tìm phòng.
    /// </summary>
    public bool IncludeLinkedModels { get; init; } = true;

    /// <summary>Chỉ xét link có tên chứa một trong các chuỗi này (rỗng = mọi link đã nạp).</summary>
    public List<string> LinkNameContains { get; init; } = new List<string>();

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

        // Room hầu như luôn nằm ở model KIẾN TRÚC liên kết: file MEP thuần không có phòng nào. Bản trước
        // chỉ quét document đang mở nên trên đúng cấu hình phổ biến nhất, lệnh dừng ở "không có phòng nào
        // khớp bộ lọc" — câu đó đổ lỗi cho bộ lọc, trong khi vấn đề là chỗ tìm.
        var inDocument = RoomsIn(document, config, null, null);
        var rooms = new List<RoomSource>(inDocument);
        var resolvedType = $"{symbol.FamilyName}: {symbol.Name}";

        var linkSummary = new List<string>();
        if (config.IncludeLinkedModels)
        {
            foreach (var linkInstance in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null)
                {
                    linkSummary.Add($"{linkInstance.Name}: chưa nạp (unloaded) — bỏ qua");
                    continue;
                }

                if (config.LinkNameContains.Count > 0 &&
                    !config.LinkNameContains.Any(f => linkInstance.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                var fromLink = RoomsIn(linkDoc, config, linkInstance.GetTotalTransform(), linkInstance.Name);
                rooms.AddRange(fromLink);
                linkSummary.Add($"{linkInstance.Name}: {fromLink.Count} phòng khớp bộ lọc");
            }
        }

        if (rooms.Count == 0)
        {
            var roomsInFile = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().GetElementCount();
            return CommandResult.Fail(roomsInFile == 0
                ? (config.IncludeLinkedModels
                    ? "Không có phòng nào — cả trong file này lẫn trong model liên kết. Kiểm lại link đã nạp chưa (Manage → Manage Links)."
                    : "File này không có phòng nào và includeLinkedModels đang tắt — bật lên nếu phòng nằm ở model kiến trúc liên kết.")
                : $"File có {roomsInFile} phòng nhưng không phòng nào khớp bộ lọc (tầng/tên/diện tích tối thiểu).");
        }

        var obstacles = CollectObstacles(document, config.ObstacleCategories);
        var options = new GridPatternOptions
        {
            SpacingX = config.Pattern.SpacingXMm,
            SpacingY = config.Pattern.SpacingYMm,
            Margin = config.Pattern.MarginMm,
            CoverageRadius = config.MaxCoverageRadiusMm,
        };

        var plans = new List<(Room Room, DevicePlacementPlan Plan, double ZFt, Level HostLevel)>();
        var result = CommandResult.Ok(string.Empty);

        // Level của file chủ, dùng để gắn thiết bị và để đối chiếu cao độ phòng ở model liên kết.
        var hostLevels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().ToList();

        // Nói rõ đã chọn type nào: tra theo tên family có thể ra nhiều type, người dùng phải thấy cái
        // thực sự được dùng thay vì đoán.
        if (!string.Equals(resolvedType, config.DeviceFamily, StringComparison.OrdinalIgnoreCase))
        {
            result.Messages.Add($"Dùng type \"{resolvedType}\" cho \"{config.DeviceFamily}\".");
        }

        foreach (var source in rooms)
        {
            var room = source.Room;
            var boundary = OuterBoundaryMm(room, source.Transform);
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

            // Phòng ở model liên kết: room.Level thuộc document của LINK — không dùng được để gắn thiết bị
            // trong file chủ, và cao độ của nó ở toạ độ link. Đưa cao độ qua phép biến đổi của link (Z cũng
            // phải qua transform như XY) rồi tìm Level file chủ gần nhất.
            Level? hostLevel;
            if (source.Transform == null)
            {
                hostLevel = room.Level;
            }
            else
            {
                var linkLevelZ = room.Level == null
                    ? (double?)null
                    : source.Transform.OfPoint(new XYZ(0, 0, room.Level.Elevation)).Z;
                var deltaFt = 0.0;
                hostLevel = linkLevelZ.HasValue ? NearestLevel(hostLevels, linkLevelZ.Value, out deltaFt) : null;
                if (hostLevel == null)
                {
                    result.Messages.Add($"Phòng {room.Name} ({room.Id}, link \"{source.LinkName}\"): không có Level nào trong file chủ để đối chiếu cao độ — bỏ qua.");
                    continue;
                }

                if (Math.Abs(deltaFt) > LevelMatchToleranceFt)
                {
                    result.Messages.Add($"Phòng {room.Name} ({room.Id}, link \"{source.LinkName}\"): cao độ tầng {RevitCompat.FtToMm(linkLevelZ!.Value):F0} mm "
                                        + $"không khớp Level nào của file chủ (gần nhất \"{hostLevel.Name}\" lệch {RevitCompat.FtToMm(Math.Abs(deltaFt)):F0} mm) — bỏ qua.");
                    continue;
                }
            }

            if (hostLevel == null)
            {
                result.Messages.Add($"Phòng {room.Name} ({room.Id}): không có Level — bỏ qua.");
                continue;
            }

            var z = hostLevel.Elevation + RevitCompat.MmToFt(config.MountHeightMm ?? (room.UnboundedHeight > 0 ? RevitCompat.FtToMm(room.UnboundedHeight) : 0));
            plans.Add((room, plan, z, hostLevel));
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

        foreach (var (room, plan, z, hostLevel) in plans)
        {
            foreach (var p in plan.Points)
            {
                try
                {
                    var xyz = new XYZ(RevitCompat.MmToFt(p.X), RevitCompat.MmToFt(p.Y), z);
                    document.Create.NewFamilyInstance(xyz, symbol, hostLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
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

    /// <summary>Sai số cao độ tối đa (ft) để coi Level file chủ "khớp" Level của link — 500 mm.</summary>
    private static readonly double LevelMatchToleranceFt = RevitCompat.MmToFt(500);

    /// <summary>Level có cao độ gần <paramref name="elevationFt"/> nhất; <paramref name="deltaFt"/> = level − mục tiêu.</summary>
    private static Level? NearestLevel(List<Level> levels, double elevationFt, out double deltaFt)
    {
        Level? best = null;
        deltaFt = double.MaxValue;
        foreach (var level in levels)
        {
            var d = level.Elevation - elevationFt;
            if (Math.Abs(d) < Math.Abs(deltaFt))
            {
                deltaFt = d;
                best = level;
            }
        }

        return best;
    }

    /// <summary>Biên ngoài của phòng (vòng dài nhất) đổi sang mm, XY.</summary>
    internal static List<Point2>? OuterBoundaryMm(Room room) => OuterBoundaryMm(room, null);

    /// <summary>
    /// Biên ngoài của phòng, tính bằng mm ở toạ độ FILE CHỦ. Phòng ở model liên kết có toạ độ riêng —
    /// không đưa về toạ độ file chủ thì thiết bị rơi lệch đúng bằng độ lệch gốc của link.
    /// </summary>
    internal static List<Point2>? OuterBoundaryMm(Room room, Transform? transform)
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
                    var pt = transform == null ? tess[i] : transform.OfPoint(tess[i]);
                    pts.Add(new Point2(RevitCompat.FtToMm(pt.X), RevitCompat.FtToMm(pt.Y)));
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

    /// <summary>Một phòng cùng phép biến đổi về toạ độ file chủ (<c>null</c> = phòng cùng file).</summary>
    private sealed record RoomSource(Room Room, Transform? Transform, string? LinkName);

    /// <summary>Phòng trong một document (file chủ hoặc link) đã lọc theo tầng/tên/diện tích.</summary>
    private static List<RoomSource> RoomsIn(Document doc, DevicePlacementConfig config, Transform? transform, string? linkName) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(r => r.Area > 0 && r.Location != null)
            .Where(r => string.IsNullOrEmpty(config.RoomFilter.LevelName) || string.Equals(r.Level?.Name, config.RoomFilter.LevelName, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(config.RoomFilter.NameContains) || (r.Name ?? string.Empty).IndexOf(config.RoomFilter.NameContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(r => config.RoomFilter.MinAreaM2 <= 0 || RevitCompat.SqFtToSqm(r.Area) >= config.RoomFilter.MinAreaM2)
            .Select(r => new RoomSource(r, transform, linkName))
            .ToList();

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
