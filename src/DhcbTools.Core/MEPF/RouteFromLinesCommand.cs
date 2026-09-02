using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>Kích thước tiết diện (mm): tròn dùng Diameter, chữ nhật dùng Width×Height.</summary>
public sealed class RouteSizeMm
{
    public double? Diameter { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }
}

/// <summary>Cấu hình routing mức A (mục 3.1): dựng MEPCurve từ model/detail line vẽ tay.</summary>
public sealed class RouteFromLinesConfig
{
    /// <summary>Line style của các đường tuyến (ví dụ "DHCB-Route").</summary>
    public string LineStyleName { get; init; } = "DHCB-Route";

    /// <summary>Duct | Pipe | CableTray | Conduit.</summary>
    public string ElementType { get; init; } = "Duct";

    /// <summary>Tên type (DuctType/PipeType/CableTrayType/ConduitType), "Family: Type" hoặc chỉ Type.</summary>
    public string? TypeName { get; init; }

    /// <summary>Tên MechanicalSystemType/PipingSystemType, ví dụ "Supply Air", "Domestic Cold Water".</summary>
    public string? SystemType { get; init; }

    public string? LevelName { get; init; }

    public RouteSizeMm SizeMm { get; init; } = new RouteSizeMm { Width = 400, Height = 200 };

    /// <summary>Cao độ tim tuyến so với level (mm). null = giữ Z của line.</summary>
    public double? OffsetMm { get; init; }

    /// <summary>Dung sai gộp đầu mút line (mm).</summary>
    public double JoinToleranceMm { get; init; } = 1.0;

    /// <summary>Sau khi dựng, nối connector hở vào connector có sẵn trong bán kính này (mm); 0 = không.</summary>
    public double ConnectToNearestMm { get; init; } = 300;

    /// <summary>Xoá line sau khi dựng thành công.</summary>
    public bool DeleteLines { get; init; } = false;

    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Routing mức A: gom line thành graph (<see cref="RouteGraph{TKey}"/>), dựng MEPCurve cho từng cạnh theo type/size,
/// dựng elbow/tee/cross tại đỉnh theo bậc; fitting hỏng thì bỏ riêng chỗ đó (không rollback), ghi ElementId + toạ độ.
/// Fitting lấy từ routing preference của type qua <c>Document.Create.NewElbowFitting</c>… — không hard-code family.
/// </summary>
public sealed class RouteFromLinesCommand : ICoreCommand<RouteFromLinesConfig>
{
    public string CommandName => "RouteFromLines";

    public CommandResult Execute(Document document, RouteFromLinesConfig config)
    {
        var lines = CollectLines(document, config.LineStyleName);
        if (lines.Count == 0)
        {
            return CommandResult.Fail($"Không có model/detail line nào dùng line style \"{config.LineStyleName}\".");
        }

        var level = RevitCompat.FindLevel(document, config.LevelName)
                    ?? new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
        if (level == null)
        {
            return CommandResult.Fail("Mô hình không có Level nào.");
        }

        var kind = config.ElementType.Trim().ToUpperInvariant();
        ElementType? curveType = kind switch
        {
            "DUCT" => RevitCompat.FindType<DuctType>(document, config.TypeName) ?? new FilteredElementCollector(document).OfClass(typeof(DuctType)).Cast<DuctType>().FirstOrDefault(),
            "PIPE" => RevitCompat.FindType<PipeType>(document, config.TypeName) ?? new FilteredElementCollector(document).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault(),
            "CABLETRAY" => RevitCompat.FindType<CableTrayType>(document, config.TypeName) ?? new FilteredElementCollector(document).OfClass(typeof(CableTrayType)).Cast<CableTrayType>().FirstOrDefault(),
            "CONDUIT" => RevitCompat.FindType<ConduitType>(document, config.TypeName) ?? new FilteredElementCollector(document).OfClass(typeof(ConduitType)).Cast<ConduitType>().FirstOrDefault(),
            _ => null,
        };
        if (curveType == null)
        {
            return CommandResult.Fail($"Không tìm thấy type \"{config.TypeName}\" cho {config.ElementType} (hợp lệ: Duct, Pipe, CableTray, Conduit).");
        }

        ElementId systemTypeId = ElementId.InvalidElementId;
        if (kind == "DUCT")
        {
            var st = FindSystemType<MechanicalSystemType>(document, config.SystemType);
            if (st == null) return CommandResult.Fail($"Không tìm thấy Mechanical System Type \"{config.SystemType}\".");
            systemTypeId = st.Id;
        }
        else if (kind == "PIPE")
        {
            var st = FindSystemType<PipingSystemType>(document, config.SystemType);
            if (st == null) return CommandResult.Fail($"Không tìm thấy Piping System Type \"{config.SystemType}\".");
            systemTypeId = st.Id;
        }

        // Graph thuần
        var tol = RevitCompat.MmToFt(config.JoinToleranceMm);
        var segments = lines.Select(l =>
        {
            var c = ((LocationCurve)l.Location).Curve;
            var p0 = c.GetEndPoint(0);
            var p1 = c.GetEndPoint(1);
            if (config.OffsetMm.HasValue)
            {
                var z = level.Elevation + RevitCompat.MmToFt(config.OffsetMm.Value);
                p0 = new XYZ(p0.X, p0.Y, z);
                p1 = new XYZ(p1.X, p1.Y, z);
            }
            return new RouteSegment<ElementId>(l.Id, new Point3(p0.X, p0.Y, p0.Z), new Point3(p1.X, p1.Y, p1.Z));
        }).ToList();

        var graph = RouteGraph<ElementId>.Build(segments, tol);
        var result = CommandResult.Ok(string.Empty);
        result.Messages.AddRange(graph.Warnings);

        var fittingPlan = graph.Nodes.Select(n => (Node: n, Kind: graph.FittingAt(n.Id))).Where(t => t.Kind != FittingKind.None).ToList();

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ dựng {graph.Edges.Count} đoạn {config.ElementType} ({curveType.Name}), " +
                             $"{fittingPlan.Count(f => f.Kind == FittingKind.Elbow)} elbow, {fittingPlan.Count(f => f.Kind == FittingKind.Tee)} tee, " +
                             $"{fittingPlan.Count(f => f.Kind == FittingKind.Cross)} cross; {graph.Rejected.Count} đoạn bị bỏ.";
            result.AffectedCount = graph.Edges.Count;
            foreach (var e in graph.Edges)
            {
                result.Messages.Add($"Đoạn {e.Key}: {graph.Nodes[e.StartNode].Position} → {graph.Nodes[e.EndNode].Position} (ft)");
            }
            return result;
        }

        var created = new Dictionary<int, MEPCurve>();
        var fittingsOk = 0;
        var fittingsFailed = 0;

        using var tx = RevitCompat.StartTransaction(document, $"DHCB - Routing {config.ElementType}");

        foreach (var e in graph.EdgesInBuildOrder())
        {
            var a = ToXyz(graph.Nodes[e.StartNode].Position);
            var b = ToXyz(graph.Nodes[e.EndNode].Position);
            try
            {
                MEPCurve curve = kind switch
                {
                    "DUCT" => Duct.Create(document, systemTypeId, curveType.Id, level.Id, a, b),
                    "PIPE" => Pipe.Create(document, systemTypeId, curveType.Id, level.Id, a, b),
                    "CABLETRAY" => CableTray.Create(document, curveType.Id, a, b, level.Id),
                    _ => Conduit.Create(document, curveType.Id, a, b, level.Id),
                };
                ApplySize(curve, kind, config.SizeMm, result);
                created[e.Id] = curve;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Không dựng được đoạn từ line {e.Key}: {ex.Message}");
            }
        }

        document.Regenerate();

        foreach (var (node, fkind) in fittingPlan)
        {
            var curves = node.EdgeIds.Where(created.ContainsKey).Select(id => created[id]).ToList();
            if (curves.Count != node.Degree)
            {
                fittingsFailed++;
                result.Errors.Add($"Đỉnh {node.Position}: thiếu đoạn để dựng {fkind} — để hở, nối tay.");
                continue;
            }

            var connectors = curves.Select(c => NearestConnector(c, ToXyz(node.Position))).ToList();
            if (connectors.Any(c => c == null))
            {
                fittingsFailed++;
                result.Errors.Add($"Đỉnh {node.Position}: không tìm được connector — để hở.");
                continue;
            }

            try
            {
                using var sub = new SubTransaction(document);
                sub.Start();
                switch (fkind)
                {
                    case FittingKind.Elbow:
                        document.Create.NewElbowFitting(connectors[0]!, connectors[1]!);
                        break;
                    case FittingKind.Tee:
                        // Hai nhánh thẳng hàng làm thân tee, nhánh còn lại là nhánh rẽ.
                        var (main1, main2, branch) = PickTeeOrder(connectors!, ToXyz(node.Position));
                        document.Create.NewTeeFitting(main1, main2, branch);
                        break;
                    case FittingKind.Cross:
                        document.Create.NewCrossFitting(connectors[0]!, connectors[1]!, connectors[2]!, connectors[3]!);
                        break;
                    default:
                        sub.RollBack();
                        fittingsFailed++;
                        result.Errors.Add($"Đỉnh {node.Position}: {node.Degree} nhánh — không có fitting, để hở.");
                        continue;
                }
                sub.Commit();
                fittingsOk++;
            }
            catch (Exception ex)
            {
                fittingsFailed++;
                result.Errors.Add($"Đỉnh {node.Position}: {fkind} thất bại ({ex.Message}) — connector để hở, kỹ sư nối tay. Đoạn: {string.Join(", ", curves.Select(c => c.Id))}");
            }
        }

        var autoConnected = 0;
        if (config.ConnectToNearestMm > 0 && created.Count > 0)
        {
            autoConnected = ConnectToNearest(document, created.Values, RevitCompat.MmToFt(config.ConnectToNearestMm), result);
        }

        if (config.DeleteLines && result.Errors.Count == 0)
        {
            document.Delete(lines.Select(l => l.Id).ToList());
        }

        tx.Commit();

        result.Summary = $"Đã dựng {created.Count}/{graph.Edges.Count} đoạn {config.ElementType}, {fittingsOk} fitting OK, {fittingsFailed} fitting lỗi, {autoConnected} mối nối tự động.";
        result.AffectedCount = created.Count;
        result.Success = created.Count > 0;
        return result;
    }

    private static List<CurveElement> CollectLines(Document doc, string lineStyleName)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(CurveElement))
            .Cast<CurveElement>()
            .Where(c => c.Location is LocationCurve lc && lc.Curve is Line
                        && c.LineStyle != null
                        && string.Equals(c.LineStyle.Name, lineStyleName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static T? FindSystemType<T>(Document doc, string? name) where T : MEPSystemType
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToList();
        if (string.IsNullOrWhiteSpace(name))
        {
            return all.FirstOrDefault();
        }

        return all.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(t => t.Name.IndexOf(name!, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void ApplySize(MEPCurve curve, string kind, RouteSizeMm size, CommandResult result)
    {
        try
        {
            if (size.Diameter.HasValue)
            {
                var p = curve.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM) ?? curve.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                p?.Set(RevitCompat.MmToFt(size.Diameter.Value));
            }
            else
            {
                if (size.Width.HasValue)
                {
                    curve.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(RevitCompat.MmToFt(size.Width.Value));
                }

                if (size.Height.HasValue)
                {
                    curve.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(RevitCompat.MmToFt(size.Height.Value));
                }
            }
        }
        catch (Exception ex)
        {
            result.Messages.Add($"Đoạn {curve.Id}: không đặt được kích thước ({ex.Message}) — dùng kích thước mặc định của type.");
        }
    }

    private static Connector? NearestConnector(MEPCurve curve, XYZ point)
    {
        Connector? best = null;
        var bestD = double.MaxValue;
        foreach (Connector c in curve.ConnectorManager.Connectors)
        {
            var d = c.Origin.DistanceTo(point);
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }
        return best;
    }

    private static (Connector, Connector, Connector) PickTeeOrder(List<Connector?> connectors, XYZ node)
    {
        // Cặp connector có hướng đối nhau nhất (dot ≈ -1) là thân tee.
        var best = (i: 0, j: 1, dot: double.MaxValue);
        for (var i = 0; i < 3; i++)
        {
            for (var j = i + 1; j < 3; j++)
            {
                var dot = connectors[i]!.CoordinateSystem.BasisZ.DotProduct(connectors[j]!.CoordinateSystem.BasisZ);
                if (dot < best.dot)
                {
                    best = (i, j, dot);
                }
            }
        }

        var k = Enumerable.Range(0, 3).First(x => x != best.i && x != best.j);
        return (connectors[best.i]!, connectors[best.j]!, connectors[k]!);
    }

    private static int ConnectToNearest(Document doc, IEnumerable<MEPCurve> created, double radiusFt, CommandResult result)
    {
        var createdIds = new HashSet<ElementId>(created.Select(c => c.Id));
        var candidates = new List<Connector>();
        foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements())
        {
            if (createdIds.Contains(el.Id))
            {
                continue;
            }

            ConnectorManager? cm = el is MEPCurve mc ? mc.ConnectorManager : (el as FamilyInstance)?.MEPModel?.ConnectorManager;
            if (cm == null)
            {
                continue;
            }

            foreach (Connector c in cm.Connectors)
            {
                if (!c.IsConnected && c.ConnectorType != ConnectorType.End)
                {
                    candidates.Add(c);
                }
            }
        }

        var connected = 0;
        foreach (var curve in created)
        {
            foreach (Connector open in curve.ConnectorManager.Connectors)
            {
                if (open.IsConnected)
                {
                    continue;
                }

                var target = candidates
                    .Where(c => !c.IsConnected && c.Domain == open.Domain && c.Origin.DistanceTo(open.Origin) <= radiusFt)
                    .OrderBy(c => c.Origin.DistanceTo(open.Origin))
                    .FirstOrDefault();
                if (target == null)
                {
                    continue;
                }

                try
                {
                    open.ConnectTo(target);
                    connected++;
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Không nối được {curve.Id} vào {target.Owner.Id}: {ex.Message}");
                }
            }
        }

        return connected;
    }

    private static XYZ ToXyz(Point3 p) => new XYZ(p.X, p.Y, p.Z);
}
