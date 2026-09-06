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

    /// <summary>Biên hộp tìm kiếm quanh hai điểm theo mặt bằng X/Y (mm).</summary>
    public double SearchMarginMm { get; init; } = 3000;

    /// <summary>
    /// Biên hộp theo cao độ Z (mm), tách khỏi biên mặt bằng. Số lớp cao độ là HỆ SỐ NHÂN của không gian A*
    /// tìm: cùng một hộp 30 × 30 m, một lớp cần 460.000 node, 61 lớp (biên 3 m, bước 100) thì 20 triệu node
    /// vẫn chưa xong. Tuyến đi trong trần kỹ thuật chỉ cần lên xuống trong khoảng ~1 m, nên mặc định 1000.
    /// </summary>
    public double SearchMarginZMm { get; init; } = 1000;

    public double StepMm { get; init; } = 100;

    public double ClearanceMm { get; init; } = 100;

    public double TurnPenalty { get; init; } = 20;

    /// <summary>Phạt mỗi bước đi sát vật cản (trong vùng 2×clearance) — ưu tiên tuyến "thoáng". 0 = tắt.</summary>
    public double NearObstaclePenalty { get; init; } = 2;

    public bool AllowVertical { get; init; } = true;

    /// <summary>
    /// Trần số ô A* được mở rộng. Rỗng = tự chọn theo cỡ lưới (xem <see cref="PathFinderOptions.MaxExpandedNodes"/>).
    /// Chỉ đặt tay khi thông báo "hết ngân sách" nói rõ hai điểm CÓ nối thông nhau.
    /// </summary>
    public int? MaxExpandedNodes { get; init; }

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

    /// <summary>
    /// Routing mức D (mặc định bật): tường/sàn/mái có lỗ mở (shaft, opening, lỗ chờ) thì lỗ được đục
    /// khỏi hộp bao — tuyến chui qua lỗ được, và CHỈ qua lỗ. Tắt thì về mức C: mọi tường là rào kín.
    /// </summary>
    public bool RespectOpenings { get; init; } = true;

    /// <summary>Coi cửa đi/cửa sổ cũng là lỗ mở (mặc định tắt — duct không đi qua cửa).</summary>
    public bool IncludeDoorsWindows { get; init; } = false;

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

        var moved = AutoRoutePlanner.Corners(bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z)
            .Select(c => transform.OfPoint(new XYZ(c.X, c.Y, c.Z)))
            .Select(c => new Point3(RevitCompat.FtToMm(c.X), RevitCompat.FtToMm(c.Y), RevitCompat.FtToMm(c.Z)));
        return AutoRoutePlanner.BoundsOf(moved);
    }

    /// <summary>Hộp tìm kiếm đưa sang hệ toạ độ khác (dùng cả tám đỉnh, vì phép biến đổi có thể xoay).</summary>
    private static Outline TransformOutline(Outline outline, Transform transform)
    {
        var box = AutoRoutePlanner.BoundsOf(
            AutoRoutePlanner.Corners(outline.MinimumPoint.X, outline.MinimumPoint.Y, outline.MinimumPoint.Z,
                                     outline.MaximumPoint.X, outline.MaximumPoint.Y, outline.MaximumPoint.Z)
                .Select(c => transform.OfPoint(new XYZ(c.X, c.Y, c.Z)))
                .Select(c => new Point3(c.X, c.Y, c.Z)));
        return new Outline(new XYZ(box.MinX, box.MinY, box.MinZ), new XYZ(box.MaxX, box.MaxY, box.MaxZ));
    }

    /// <summary>
    /// Thêm một phần tử vào danh sách vật cản. Mức D: nếu là vật chủ (tường/sàn/mái/trần) và
    /// <see cref="AutoRouteConfig.RespectOpenings"/> bật thì các insert của nó — shaft, opening, lỗ chờ,
    /// (tuỳ chọn) cửa — được đục khỏi hộp bao bằng <see cref="BoxSubtract"/>; hộp bao của tường vì thế
    /// thành vài mảnh quanh lỗ. Trả về <c>true</c> nếu phần tử có hộp bao (đếm là một vật cản).
    /// </summary>
    private static bool AddObstacle(Element e, Transform? transform, AutoRouteConfig config, List<Box3> obstacles, List<string> openingLog)
    {
        var box = BoxOf(e, transform);
        if (box == null) return false;

        if (config.RespectOpenings && e is HostObject host)
        {
            var holes = new List<Box3>();
            ICollection<ElementId> inserts;
            try
            {
                inserts = host.FindInserts(addRectOpenings: true, includeShadows: false, includeEmbeddedWalls: false, includeSharedEmbeddedInserts: true);
            }
            catch (Exception)
            {
                inserts = Array.Empty<ElementId>();
            }

            foreach (var id in inserts)
            {
                var insert = e.Document.GetElement(id);
                if (insert == null) continue;
                var cat = insert.Category == null ? 0 : RevitCompat.IdValue(insert.Category.Id);
                var isDoorOrWindow = cat == (long)BuiltInCategory.OST_Doors || cat == (long)BuiltInCategory.OST_Windows;
                // Tường nhúng (curtain wall trong tường chủ) không phải lỗ để đi qua; liên kết kết cấu
                // (Structural Connections) gắn vào tường cũng về qua FindInserts nhưng là thép đặc, không phải lỗ.
                if (!AutoRoutePlanner.IsPassableOpening(isDoorOrWindow, config.IncludeDoorsWindows, insert is Wall,
                        cat == (long)BuiltInCategory.OST_StructConnections)) continue;

                var hole = BoxOf(insert, transform);
                // Chỉ tính lỗ thật sự nằm trên vật chủ trong hộp tìm kiếm — insert ở đầu kia của bức tường
                // dài không phải lỗ để đi qua ở đây.
                if (!AutoRoutePlanner.HoleTouchesHost(hole, box)) continue;
                holes.Add(hole!);
                openingLog.Add(AutoRoutePlanner.OpeningLine(insert.Category?.Name, hole!, e.Category?.Name, RevitCompat.IdValue(e.Id)));
            }

            obstacles.AddRange(AutoRoutePlanner.ObstaclePieces(box, holes));
            return true;
        }

        obstacles.Add(box);
        return true;
    }

    /// <summary>Dòng chi tiết chung cho cả hai nhánh: từng link đóng góp bao nhiêu vật cản, và lỗ mở nào đã đục (mức D, tối đa 30 dòng).</summary>
    private static void AppendObstacleDetails(CommandResult result, List<string> linkSummary, List<string> openingLog)
    {
        result.Messages.AddRange(AutoRoutePlanner.ObstacleDetails(linkSummary, openingLog));
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
        var bounds = AutoRoutePlanner.SearchBounds(start, goal, config.SearchMarginMm, config.SearchMarginZMm);

        ICollection<ElementId> catIds = config.ObstacleCategories.Count > 0
            ? ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.ObstacleCategories, out _)
            : DefaultObstacles.Select(c => new ElementId(c)).ToList();

        var outline = new Outline(new XYZ(RevitCompat.MmToFt(bounds.MinX), RevitCompat.MmToFt(bounds.MinY), RevitCompat.MmToFt(bounds.MinZ)),
                                  new XYZ(RevitCompat.MmToFt(bounds.MaxX), RevitCompat.MmToFt(bounds.MaxY), RevitCompat.MmToFt(bounds.MaxZ)));
        var obstacles = new List<Box3>();
        var openingLog = new List<string>();
        var inDocument = 0;
        foreach (var e in new FilteredElementCollector(document).WhereElementIsNotElementType()
                     .WherePasses(new ElementMulticategoryFilter(catIds.ToList()))
                     .WherePasses(new BoundingBoxIntersectsFilter(outline)).ToElements())
        {
            if (AddObstacle(e, null, config, obstacles, openingLog)) inDocument++;
        }

        // Dầm, cột, tường nằm ở model kết cấu/kiến trúc LIÊN KẾT. Không đọc chúng thì A* chạy trong một
        // không gian trống rỗng và luôn "tìm được tuyến" — tuyến xuyên thẳng qua dầm.
        // Link chưa nạp mà vẫn chạy tiếp thì con số trả ra nói về trạng thái link, không nói về mô hình
        // — đúng lớp lỗi của bug #14. Dừng ở đây, trước mọi transaction.
        var linkPre = Checks.RevitPrecondition.LinkedModels(document, CommandName);
        if (config.IncludeLinkedModels && linkPre.Blocks)
        {
            return CommandResult.Fail(linkPre.Message);
        }

        var linkSummary = new List<string>();
        var inLinks = 0;
        if (config.IncludeLinkedModels)
        {
            foreach (var linkInstance in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null)
                {
                    linkSummary.Add(AutoRoutePlanner.LinkUnloadedLine(linkInstance.Name));
                    continue;
                }

                if (!AutoRoutePlanner.LinkSelected(linkInstance.Name, config.LinkNameContains))
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
                    linkSummary.Add(AutoRoutePlanner.LinkNoCategoryLine(linkInstance.Name));
                    continue;
                }

                var transform = linkInstance.GetTotalTransform();
                var inLinkCoords = TransformOutline(outline, transform.Inverse);
                var added = 0;
                foreach (var e in new FilteredElementCollector(linkDoc).WhereElementIsNotElementType()
                             .WherePasses(new ElementMulticategoryFilter(idsLink.ToList()))
                             .WherePasses(new BoundingBoxIntersectsFilter(inLinkCoords)).ToElements())
                {
                    if (AddObstacle(e, transform, config, obstacles, openingLog)) added++;
                }
                linkSummary.Add(AutoRoutePlanner.LinkCountLine(linkInstance.Name, added));
                inLinks += added;
            }
        }

        var path = PathFinder3D.FindPath(start, goal, obstacles, bounds, new PathFinderOptions
        {
            StepMm = config.StepMm, ClearanceMm = config.ClearanceMm, TurnPenalty = config.TurnPenalty, AllowVertical = config.AllowVertical,
            NearObstaclePenalty = config.NearObstaclePenalty, MaxExpandedNodes = config.MaxExpandedNodes,
        });
        var elements = inDocument + inLinks;
        var source = AutoRoutePlanner.SourceText(inDocument, inLinks, config.RespectOpenings, openingLog.Count);
        if (!path.Found)
        {
            // `path.Reason` đã nói rõ thua vì bị bịt kín hay vì hết ngân sách — hai thứ cần cách chữa khác
            // hẳn nhau, nên đừng nuốt mất; kèm cỡ lưới để người đọc biết bước lưới có hợp với hộp không.
            var fail = CommandResult.Fail(AutoRoutePlanner.FailSummary(path, elements, source, config.StepMm));
            // Thua thì càng cần biết đã đục lỗ nào — kỹ sư đối chiếu ngay "lỗ có nhưng không đúng chỗ" với
            // "không có lỗ nào", hai kết luận dẫn tới hai việc khác nhau (sửa điểm, hay vẽ lỗ chờ vào model).
            AppendObstacleDetails(fail, linkSummary, openingLog);
            return fail;
        }

        var segments = PolylineSimplifier.ToSegments(path.Polyline);
        var result = CommandResult.Ok(string.Empty);
        result.Messages.Add(AutoRoutePlanner.FoundMessage(path, elements, source, segments.Count));
        AppendObstacleDetails(result, linkSummary, openingLog);

        // Tuyến đi qua một không gian TRỐNG là kết quả vô nghĩa nhưng trông y hệt kết quả tốt — nói ra
        // ngay, đừng để người đọc tự đoán vì sao tuyến thẳng băng.
        var warning = AutoRoutePlanner.NoObstacleWarning(elements, config.IncludeLinkedModels);
        if (warning != null)
        {
            result.Messages.Add(warning);
        }
        result.Messages.AddRange(segments.Take(50).Select(s => AutoRoutePlanner.SegmentLine(s.Start, s.End)));

        if (config.DryRun)
        {
            // Số vật cản phải nằm trong Summary chứ không chỉ Messages: báo cáo batch chỉ in Summary, mà
            // "tuyến đẹp" tìm trong không gian trống là kết quả vô nghĩa trông y hệt kết quả tốt.
            result.Summary = AutoRoutePlanner.PreviewSummary(segments.Count, path, elements, source);
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

        result.Summary = AutoRoutePlanner.WrittenSummary(created, config.LineStyleName, path, elements, source);
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
