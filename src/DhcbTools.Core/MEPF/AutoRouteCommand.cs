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

    /// <summary>
    /// Xét cả vật cản ở model LIÊN KẾT (mặc định bật). Dầm, cột, tường của hồ sơ Việt Nam nằm ở model
    /// kết cấu/kiến trúc liên kết — tắt cái này là tuyến đề xuất xuyên thẳng qua chúng mà lệnh vẫn
    /// báo "tìm được tuyến".
    /// </summary>
    public bool IncludeLinkedModels { get; init; } = true;

    /// <summary>Chỉ xét link có tên chứa một trong các chuỗi này (rỗng = mọi link đã nạp).</summary>
    public List<string> LinkNameContains { get; init; } = new List<string>();

    public bool DryRun { get; init; } = true;
}

public sealed class AutoRouteCommand : ICoreCommand<AutoRouteConfig>
{
    public string CommandName => "AutoRoute";

    /// <summary>
    /// Hộp bao của phần tử ở toạ độ FILE CHỦ, tính bằng mm. Link xoay thì hộp dựng lại từ tám đỉnh —
    /// biến đổi hai điểm min/max cho ra hộp sai khi có xoay.
    /// </summary>
    private static Box3? BoxOf(Element element, Transform? transform)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;

        if (transform == null)
        {
            return new Box3(RevitCompat.FtToMm(bb.Min.X), RevitCompat.FtToMm(bb.Min.Y), RevitCompat.FtToMm(bb.Min.Z),
                            RevitCompat.FtToMm(bb.Max.X), RevitCompat.FtToMm(bb.Max.Y), RevitCompat.FtToMm(bb.Max.Z));
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (var i = 0; i < 8; i++)
        {
            var corner = transform.OfPoint(new XYZ(
                (i & 1) == 0 ? bb.Min.X : bb.Max.X,
                (i & 2) == 0 ? bb.Min.Y : bb.Max.Y,
                (i & 4) == 0 ? bb.Min.Z : bb.Max.Z));
            minX = Math.Min(minX, corner.X); maxX = Math.Max(maxX, corner.X);
            minY = Math.Min(minY, corner.Y); maxY = Math.Max(maxY, corner.Y);
            minZ = Math.Min(minZ, corner.Z); maxZ = Math.Max(maxZ, corner.Z);
        }

        return new Box3(RevitCompat.FtToMm(minX), RevitCompat.FtToMm(minY), RevitCompat.FtToMm(minZ),
                        RevitCompat.FtToMm(maxX), RevitCompat.FtToMm(maxY), RevitCompat.FtToMm(maxZ));
    }

    /// <summary>Hộp tìm kiếm đưa sang hệ toạ độ khác (dùng cả tám đỉnh, vì phép biến đổi có thể xoay).</summary>
    private static Outline TransformOutline(Outline outline, Transform transform)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (var i = 0; i < 8; i++)
        {
            var corner = transform.OfPoint(new XYZ(
                (i & 1) == 0 ? outline.MinimumPoint.X : outline.MaximumPoint.X,
                (i & 2) == 0 ? outline.MinimumPoint.Y : outline.MaximumPoint.Y,
                (i & 4) == 0 ? outline.MinimumPoint.Z : outline.MaximumPoint.Z));
            minX = Math.Min(minX, corner.X); maxX = Math.Max(maxX, corner.X);
            minY = Math.Min(minY, corner.Y); maxY = Math.Max(maxY, corner.Y);
            minZ = Math.Min(minZ, corner.Z); maxZ = Math.Max(maxZ, corner.Z);
        }

        return new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
    }

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
            var box = BoxOf(e, null);
            if (box != null) obstacles.Add(box);
        }

        var inDocument = obstacles.Count;

        // Dầm, cột, tường nằm ở model kết cấu/kiến trúc LIÊN KẾT. Không đọc chúng thì A* chạy trong một
        // không gian trống rỗng và luôn "tìm được tuyến" — tuyến xuyên thẳng qua dầm.
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

                // Category tra TRONG link (id category là của từng document), và hộp tìm kiếm phải đưa về
                // toạ độ LINK trước khi lọc — bộ lọc chạy trong document của link.
                ICollection<ElementId> idsLink = config.ObstacleCategories.Count > 0
                    ? ParameterSync.ParameterExportCommand.ResolveCategoryIds(linkDoc, config.ObstacleCategories, out _)
                    : DefaultObstacles.Select(c => new ElementId(c)).ToList();
                if (idsLink.Count == 0)
                {
                    linkSummary.Add($"{linkInstance.Name}: không có category vật cản nào");
                    continue;
                }

                var transform = linkInstance.GetTotalTransform();
                var inLinkCoords = TransformOutline(outline, transform.Inverse);
                var added = 0;
                foreach (var e in new FilteredElementCollector(linkDoc).WhereElementIsNotElementType()
                             .WherePasses(new ElementMulticategoryFilter(idsLink.ToList()))
                             .WherePasses(new BoundingBoxIntersectsFilter(inLinkCoords)).ToElements())
                {
                    var box = BoxOf(e, transform);
                    if (box == null) continue;
                    obstacles.Add(box);
                    added++;
                }
                linkSummary.Add($"{linkInstance.Name}: {added} vật cản");
            }
        }

        var inLinks = obstacles.Count - inDocument;

        var path = PathFinder3D.FindPath(start, goal, obstacles, bounds, new PathFinderOptions
        {
            StepMm = config.StepMm, ClearanceMm = config.ClearanceMm, TurnPenalty = config.TurnPenalty, AllowVertical = config.AllowVertical,
        });
        var source = $"{inDocument} trong file + {inLinks} từ model liên kết";
        if (!path.Found)
        {
            return CommandResult.Fail($"Không tìm được tuyến ({path.Reason}). Đã xét {obstacles.Count} chướng ngại ({source}), {path.ExpandedNodes} node.");
        }

        var segments = PolylineSimplifier.ToSegments(path.Polyline);
        var result = CommandResult.Ok(string.Empty);
        result.Messages.Add($"{obstacles.Count} chướng ngại trong hộp tìm kiếm ({source}), {path.ExpandedNodes} node, {path.Turns} lần rẽ, {segments.Count} đoạn, tổng {PolylineSimplifier.Length(path.Polyline) / 1000:F1} m.");
        foreach (var line in linkSummary)
        {
            result.Messages.Add("  Link — " + line);
        }

        // Tuyến đi qua một không gian TRỐNG là kết quả vô nghĩa nhưng trông y hệt kết quả tốt — nói ra
        // ngay, đừng để người đọc tự đoán vì sao tuyến thẳng băng.
        if (obstacles.Count == 0)
        {
            result.Messages.Add(config.IncludeLinkedModels
                ? "KHÔNG có vật cản nào trong hộp tìm kiếm, kể cả từ model liên kết — tuyến này chỉ là đường nối hai điểm. Kiểm lại link đã nạp chưa."
                : "KHÔNG có vật cản nào và includeLinkedModels đang tắt — bật lên nếu dầm/cột/tường nằm ở model liên kết.");
        }
        result.Messages.AddRange(segments.Take(50).Select(s => $"({s.Start.X:F0},{s.Start.Y:F0},{s.Start.Z:F0}) → ({s.End.X:F0},{s.End.Y:F0},{s.End.Z:F0})"));

        if (config.DryRun)
        {
            // Số vật cản phải nằm trong Summary chứ không chỉ Messages: báo cáo batch chỉ in Summary, mà
            // "tuyến đẹp" tìm trong không gian trống là kết quả vô nghĩa trông y hệt kết quả tốt.
            result.Summary = $"[Xem trước] Tuyến {segments.Count} đoạn, {path.Turns} lần rẽ, né {obstacles.Count} vật cản ({source}).";
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

        result.Summary = $"Đã vẽ {created} model line (line style \"{config.LineStyleName}\"), né {obstacles.Count} vật cản ({source}).";
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
