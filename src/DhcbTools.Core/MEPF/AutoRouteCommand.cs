using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

public sealed class PointMm
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
}

/// <summary>
/// P2 — Routing mức C nối vào mức A: A* né chướng ngại (<see cref="PathFinder3D"/>) từ 2 điểm → model line theo line style
/// → tuỳ chọn gọi luôn <see cref="RouteFromLinesCommand"/> để dựng duct/pipe. Giới hạn phạm vi bằng hộp tìm kiếm quanh
/// hai điểm (searchMarginMm) — đúng khuyến nghị "một hệ, một tầng, một hành lang".
/// </summary>
public sealed class AutoRouteConfig
{
    public required PointMm StartMm { get; init; }

    public required PointMm EndMm { get; init; }

    /// <summary>Biên hộp tìm kiếm quanh hai điểm (mm).</summary>
    public double SearchMarginMm { get; init; } = 3000;

    public double StepMm { get; init; } = 100;

    public double ClearanceMm { get; init; } = 100;

    public double TurnPenalty { get; init; } = 20;

    public bool AllowVertical { get; init; } = true;

    /// <summary>Category chướng ngại (rỗng = Structural Framing, Structural Columns, Walls, Floors, Ducts, Pipes, Cable Trays).</summary>
    public List<string> ObstacleCategories { get; init; } = new List<string>();

    /// <summary>Line style cho model line sinh ra; tạo mới nếu chưa có.</summary>
    public string LineStyleName { get; init; } = "DHCB-Route";

    /// <summary>Dựng luôn duct/pipe từ line vừa vẽ bằng RouteFromLines (config bên dưới); false = chỉ vẽ line để kỹ sư sửa.</summary>
    public bool BuildRoute { get; init; } = false;

    public RouteFromLinesConfig? RouteConfig { get; init; }

    public bool DryRun { get; init; } = true;
}

public sealed class AutoRouteCommand : ICoreCommand<AutoRouteConfig>
{
    public string CommandName => "AutoRoute";

    private static readonly BuiltInCategory[] DefaultObstacles =
    {
        BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_CableTray,
    };

    public CommandResult Execute(Document document, AutoRouteConfig config)
    {
        var start = new Point3(config.StartMm.X, config.StartMm.Y, config.StartMm.Z);
        var goal = new Point3(config.EndMm.X, config.EndMm.Y, config.EndMm.Z);
        var m = config.SearchMarginMm;
        var bounds = new Box3(Math.Min(start.X, goal.X) - m, Math.Min(start.Y, goal.Y) - m, Math.Min(start.Z, goal.Z) - m,
                              Math.Max(start.X, goal.X) + m, Math.Max(start.Y, goal.Y) + m, Math.Max(start.Z, goal.Z) + m);

        ICollection<ElementId> catIds = config.ObstacleCategories.Count > 0
            ? ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.ObstacleCategories, out _)
            : DefaultObstacles.Select(c => new ElementId(c)).ToList();

        var outline = new Outline(new XYZ(RevitCompat.MmToFt(bounds.MinX), RevitCompat.MmToFt(bounds.MinY), RevitCompat.MmToFt(bounds.MinZ)),
                                  new XYZ(RevitCompat.MmToFt(bounds.MaxX), RevitCompat.MmToFt(bounds.MaxY), RevitCompat.MmToFt(bounds.MaxZ)));
        var obstacles = new List<Box3>();
        foreach (var e in new FilteredElementCollector(document).WhereElementIsNotElementType()
                     .WherePasses(new ElementMulticategoryFilter(catIds.ToList()))
                     .WherePasses(new BoundingBoxIntersectsFilter(outline)).ToElements())
        {
            var bb = e.get_BoundingBox(null);
            if (bb == null) continue;
            obstacles.Add(new Box3(RevitCompat.FtToMm(bb.Min.X), RevitCompat.FtToMm(bb.Min.Y), RevitCompat.FtToMm(bb.Min.Z),
                                   RevitCompat.FtToMm(bb.Max.X), RevitCompat.FtToMm(bb.Max.Y), RevitCompat.FtToMm(bb.Max.Z)));
        }

        var path = PathFinder3D.FindPath(start, goal, obstacles, bounds, new PathFinderOptions
        {
            StepMm = config.StepMm, ClearanceMm = config.ClearanceMm, TurnPenalty = config.TurnPenalty, AllowVertical = config.AllowVertical,
        });
        if (!path.Found)
        {
            return CommandResult.Fail($"Không tìm được tuyến ({path.Reason}). Đã xét {obstacles.Count} chướng ngại, {path.ExpandedNodes} node.");
        }

        var segments = PolylineSimplifier.ToSegments(path.Polyline);
        var result = CommandResult.Ok(string.Empty);
        result.Messages.Add($"{obstacles.Count} chướng ngại trong hộp tìm kiếm, {path.ExpandedNodes} node, {path.Turns} lần rẽ, {segments.Count} đoạn, tổng {PolylineSimplifier.Length(path.Polyline) / 1000:F1} m.");
        result.Messages.AddRange(segments.Take(50).Select(s => $"({s.Start.X:F0},{s.Start.Y:F0},{s.Start.Z:F0}) → ({s.End.X:F0},{s.End.Y:F0},{s.End.Z:F0})"));

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Tuyến {segments.Count} đoạn, {path.Turns} lần rẽ.";
            result.AffectedCount = segments.Count;
            return result;
        }

        var created = 0;
        using (var tx = RevitCompat.StartTransaction(document, "DHCB - Vẽ tuyến tự động"))
        {
            var style = FindOrCreateLineStyle(document, config.LineStyleName);
            foreach (var (s, e) in segments)
            {
                var a = new XYZ(RevitCompat.MmToFt(s.X), RevitCompat.MmToFt(s.Y), RevitCompat.MmToFt(s.Z));
                var b = new XYZ(RevitCompat.MmToFt(e.X), RevitCompat.MmToFt(e.Y), RevitCompat.MmToFt(e.Z));
                if (a.DistanceTo(b) < document.Application.ShortCurveTolerance) continue;
                var line = Line.CreateBound(a, b);
                var dir = (b - a).Normalize();
                var normal = Math.Abs(dir.Z) > 0.9 ? XYZ.BasisX : XYZ.BasisZ.CrossProduct(dir).Normalize();
                if (normal.GetLength() < 1e-9) normal = XYZ.BasisY;
                var plane = Plane.CreateByNormalAndOrigin(normal, a);
                var sketch = SketchPlane.Create(document, plane);
                var mc = document.Create.NewModelCurve(line, sketch);
                if (style != null)
                {
                    try { mc.LineStyle = style; } catch { /* style không áp được */ }
                }
                created++;
            }
            tx.Commit();
        }

        result.Summary = $"Đã vẽ {created} model line (line style \"{config.LineStyleName}\").";
        result.AffectedCount = created;

        if (config.BuildRoute)
        {
            var rc = config.RouteConfig ?? new RouteFromLinesConfig();
            var routeCfg = new RouteFromLinesConfig
            {
                LineStyleName = config.LineStyleName, ElementType = rc.ElementType, TypeName = rc.TypeName, SystemType = rc.SystemType,
                LevelName = rc.LevelName, SizeMm = rc.SizeMm, OffsetMm = null, JoinToleranceMm = rc.JoinToleranceMm,
                ConnectToNearestMm = rc.ConnectToNearestMm, DeleteLines = rc.DeleteLines, DryRun = false,
            };
            var routed = new RouteFromLinesCommand().Execute(document, routeCfg);
            result.Summary += " " + routed.Summary;
            result.Messages.AddRange(routed.Messages);
            result.Errors.AddRange(routed.Errors);
        }

        return result;
    }

    internal static GraphicsStyle? FindOrCreateLineStyle(Document doc, string name)
    {
        var lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
        if (lines == null) return null;
        foreach (Category sub in lines.SubCategories)
        {
            if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
        }
        try
        {
            var created = doc.Settings.Categories.NewSubcategory(lines, name);
            created.LineColor = new Color(255, 0, 255);
            return created.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
        catch
        {
            return null;
        }
    }
}
