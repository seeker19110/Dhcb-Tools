using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;
using DhcbTools.Shared.Logic.Cad;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình <c>ModelLinesFromCad</c> (đề xuất C4): DWG → model line cho <c>RouteFromLines</c>.</summary>
public sealed class ModelLinesFromCadConfig
{
    /// <summary>Chỉ đọc bản vẽ CAD có tên chứa chuỗi này (rỗng = mọi DWG đã import/link trong mô hình).</summary>
    public string? DwgNameContains { get; init; }

    /// <summary>Layer lấy đường; hỗ trợ wildcard <c>*</c>, <c>?</c>, <c>~</c>. Rỗng = mọi layer.</summary>
    public List<string> IncludeLayers { get; init; } = new List<string>();

    /// <summary>Layer bỏ, xét sau danh sách lấy (ví dụ bỏ <c>*-TEXT</c>, <c>*-DIM</c>).</summary>
    public List<string> ExcludeLayers { get; init; } = new List<string>();

    /// <summary>Line style của model line sinh ra — đặt trùng <c>RouteFromLines.lineStyleName</c> để nối tiếp được.</summary>
    public string LineStyleName { get; init; } = "DHCB-Route";

    /// <summary>Tầng đặt model line (rỗng = tầng thấp nhất).</summary>
    public string? LevelName { get; init; }

    /// <summary>Cao độ so với tầng (mm). Mọi đường bị ép về đúng cao độ này khi <see cref="Flatten"/> bật.</summary>
    public double OffsetMm { get; init; } = 0;

    /// <summary>
    /// Ép mọi đường về một cao độ. Mặc định bật: mặt bằng CAD hay có Z rác (0, cao độ cũ, hoặc từng
    /// đoạn một Z khác nhau) — dựng nguyên Z đó ra là tuyến gãy khúc lên xuống mà nhìn mặt bằng không thấy.
    /// </summary>
    public bool Flatten { get; init; } = true;

    public double MinLengthMm { get; init; } = 50;

    public double WeldToleranceMm { get; init; } = 1.0;

    public bool MergeCollinear { get; init; } = true;

    public bool IncludeArcs { get; init; } = true;

    /// <summary>Giới hạn số model line tạo trong một lượt (0 = không giới hạn).</summary>
    public int MaxLines { get; init; } = 5000;

    /// <summary>Xem trước: đếm và liệt kê nhưng KHÔNG tạo model line nào.</summary>
    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Mắt xích còn thiếu ở chặng 1: <c>CadLayerMap</c> map được layer, <c>RouteFromLines</c> dựng được ống
/// từ model line — nhưng **không ai dựng model line từ DWG**, nên kỹ sư vẫn ngồi vẽ lại tuyến bằng tay
/// đè lên bản vẽ CAD.
/// <para>
/// Chạy lại **không sinh bản sao**: đường nào đã có model line trùng (cùng line style, cùng hai đầu mút
/// trong dung sai) thì bỏ qua và đếm riêng — theo đúng cách chốt tính idempotent của §12 thay vì dọn lại.
/// </para>
/// </summary>
public sealed class ModelLinesFromCadCommand : ICoreCommand<ModelLinesFromCadConfig>
{
    public string CommandName => "ModelLinesFromCad";

    public CommandResult Execute(Document document, ModelLinesFromCadConfig config)
    {
        var result = CommandResult.Ok(string.Empty);

        var imports = new FilteredElementCollector(document).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
            .Where(i => string.IsNullOrWhiteSpace(config.DwgNameContains)
                        || SafeName(i).IndexOf(config.DwgNameContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (RevitPrecondition.Blocks(Shared.Logic.Checks.Precondition.NonEmptyInput(
                CommandName,
                string.IsNullOrWhiteSpace(config.DwgNameContains) ? "bản vẽ CAD đã import/link" : $"bản vẽ CAD có tên chứa \"{config.DwgNameContains}\"",
                imports.Count,
                "Import hoặc link file DWG vào mô hình trước (Insert → Link CAD), rồi chạy lại; kiểm cả dwgNameContains."), result))
        {
            return result;
        }

        var level = RevitCompat.FindLevel(document, config.LevelName)
                    ?? new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
        if (level == null)
        {
            return CommandResult.Fail("Mô hình không có Level nào để đặt model line.");
        }

        var elevationFt = level.Elevation + RevitCompat.MmToFt(config.OffsetMm);

        // ── Đọc hình học DWG ────────────────────────────────────────────────
        var raw = new List<CadCurve>();
        var unreadable = 0;
        foreach (var import in imports)
        {
            try
            {
                var geometry = import.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
                if (geometry == null) continue;
                foreach (var obj in geometry)
                {
                    if (obj is GeometryInstance instance)
                    {
                        // GetInstanceGeometry() đã đưa về toạ độ file chủ — không tự nhân transform lần nữa.
                        Collect(document, instance.GetInstanceGeometry(), raw, ref unreadable);
                    }
                    else
                    {
                        Collect(document, new[] { obj }, raw, ref unreadable);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Không đọc được hình học của \"{SafeName(import)}\": {ex.Message}");
            }
        }

        var options = new CadCurveFilterOptions
        {
            MinLengthMm = config.MinLengthMm,
            WeldToleranceMm = config.WeldToleranceMm,
            MergeCollinear = config.MergeCollinear,
            IncludeArcs = config.IncludeArcs,
            FlattenToZMm = config.Flatten ? RevitCompat.FtToMm(elevationFt) : (double?)null,
        };
        options.IncludeLayers.AddRange(config.IncludeLayers);
        options.ExcludeLayers.AddRange(config.ExcludeLayers);

        var filtered = CadCurveFilter.Filter(raw, options);
        result.Messages.Add($"Đọc {raw.Count} đường từ {imports.Count} bản vẽ CAD → {filtered.Summary()}.");
        if (unreadable > 0)
        {
            result.Messages.Add($"{unreadable} đối tượng không phải đoạn thẳng/cung (spline, ellipse, text…) — bỏ qua.");
        }

        foreach (var pair in filtered.ByLayer.OrderByDescending(p => p.Value))
        {
            result.Messages.Add($"  Layer {pair.Key}: {pair.Value} đường");
        }

        // Lọc ra hết là một câu nói về BỘ LỌC, không phải về bản vẽ — không được báo thành công 0 đường.
        if (RevitPrecondition.Blocks(Shared.Logic.Checks.Precondition.NonEmptyInput(
                CommandName, "đường CAD hợp lệ sau bộ lọc", filtered.Curves.Count,
                raw.Count == 0
                    ? "Bản vẽ không có đoạn thẳng/cung nào đọc được — kiểm xem DWG có bị explode thành block lồng nhau không."
                    : $"Đọc được {raw.Count} đường nhưng bộ lọc bỏ hết: kiểm includeLayers/excludeLayers và minLengthMm (danh sách layer thật in ở trên)."), result))
        {
            return result;
        }

        // ── Đường đã có: chạy lại không đẻ bản sao ──────────────────────────
        var style = FindLineStyle(document, config.LineStyleName);
        var existing = ExistingModelCurves(document, style);
        var toCreate = new List<CadCurve>();
        var already = 0;
        foreach (var curve in filtered.Curves)
        {
            if (existing.Any(e => CadCurveFilter.SameShape(e, curve, Math.Max(config.WeldToleranceMm, 1e-9))))
            {
                already++;
                continue;
            }

            toCreate.Add(curve);
            if (config.MaxLines > 0 && toCreate.Count >= config.MaxLines)
            {
                result.Messages.Add($"Đạt giới hạn {config.MaxLines} model line — phần còn lại chưa dựng.");
                break;
            }
        }

        if (already > 0)
        {
            result.Messages.Add($"{already} đường đã có model line trùng — bỏ qua (chạy lại không tạo bản sao).");
        }

        if (style == null)
        {
            result.Messages.Add($"Chưa có line style \"{config.LineStyleName}\" — model line sẽ mang style mặc định; "
                                + "tạo subcategory cùng tên trong Manage → Object Styles → Lines nếu muốn RouteFromLines lọc đúng.");
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ tạo {toCreate.Count} model line ở tầng \"{level.Name}\" (+{config.OffsetMm:F0} mm)"
                             + (already > 0 ? $"; {already} đường đã có." : ".");
            result.AffectedCount = toCreate.Count;
            return result;
        }

        // ── Ghi ─────────────────────────────────────────────────────────────
        var created = 0;
        var failed = 0;
        try
        {
            using var tx = RevitCompat.StartTransaction(document, "DHCB - Model line từ CAD");
            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevationFt));
            var sketch = SketchPlane.Create(document, plane);

            foreach (var curve in toCreate)
            {
                try
                {
                    var geometry = ToRevit(curve, config.Flatten ? elevationFt : (double?)null);
                    if (geometry == null)
                    {
                        failed++;
                        continue;
                    }

                    var modelCurve = document.Create.NewModelCurve(geometry, sketch);
                    if (style != null)
                    {
                        try { modelCurve.LineStyle = style; }
                        catch (Exception ex) { result.Messages.Add($"Không gán được line style: {ex.Message}"); }
                    }

                    created++;
                }
                catch (Exception ex)
                {
                    // Một đường hỏng (quá ngắn theo dung sai của Revit, nằm ngoài giới hạn mô hình) không
                    // được kéo đổ cả lượt: ghi lại đúng chỗ đó rồi đi tiếp.
                    failed++;
                    if (failed <= 20)
                    {
                        result.Messages.Add($"Không dựng được đường trên layer {curve.Layer} tại ({curve.Start.X:F0},{curve.Start.Y:F0}) mm: {ex.Message}");
                    }
                }
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("Không dựng được model line: " + ex.Message);
        }

        result.Summary = $"Đã tạo {created} model line (style \"{config.LineStyleName}\") ở tầng \"{level.Name}\""
                         + (already > 0 ? $"; {already} đã có" : string.Empty)
                         + (failed > 0 ? $"; {failed} không dựng được" : string.Empty) + ".";
        result.AffectedCount = created;
        return result;
    }

    private static string SafeName(Element element)
    {
        try { return element.Name ?? string.Empty; }
        catch (Exception) { return string.Empty; }
    }

    /// <summary>Gom Line/Arc trong một tập hình học, kèm tên layer lấy từ GraphicsStyle.</summary>
    private static void Collect(Document document, IEnumerable<GeometryObject> objects, List<CadCurve> into, ref int unreadable)
    {
        foreach (var obj in objects)
        {
            switch (obj)
            {
                case Line line:
                    into.Add(new CadCurve(LayerOf(document, line.GraphicsStyleId), ToMm(line.GetEndPoint(0)), ToMm(line.GetEndPoint(1))));
                    break;

                case Arc arc:
                    into.Add(new CadCurve(
                        LayerOf(document, arc.GraphicsStyleId),
                        ToMm(arc.GetEndPoint(0)),
                        ToMm(arc.GetEndPoint(1)),
                        CadCurveKind.Arc,
                        ToMm(arc.Evaluate(0.5, true))));
                    break;

                case PolyLine polyline:
                    var points = polyline.GetCoordinates();
                    var layer = LayerOf(document, polyline.GraphicsStyleId);
                    for (var i = 1; i < points.Count; i++)
                    {
                        into.Add(new CadCurve(layer, ToMm(points[i - 1]), ToMm(points[i])));
                    }

                    break;

                case GeometryInstance nested:
                    // Block lồng trong block: hình học thật nằm sâu bên trong, không lấy thì mất cả tuyến.
                    Collect(document, nested.GetInstanceGeometry(), into, ref unreadable);
                    break;

                default:
                    unreadable++;
                    break;
            }
        }
    }

    private static Point3 ToMm(XYZ point)
        => new Point3(RevitCompat.FtToMm(point.X), RevitCompat.FtToMm(point.Y), RevitCompat.FtToMm(point.Z));

    private static XYZ ToFt(Point3 point, double? zFt)
        => new XYZ(RevitCompat.MmToFt(point.X), RevitCompat.MmToFt(point.Y), zFt ?? RevitCompat.MmToFt(point.Z));

    private static Curve? ToRevit(CadCurve curve, double? zFt)
    {
        var start = ToFt(curve.Start, zFt);
        var end = ToFt(curve.End, zFt);
        if (curve.Kind == CadCurveKind.Arc && curve.Middle != null)
        {
            return Arc.Create(start, end, ToFt(curve.Middle.Value, zFt));
        }

        return Line.CreateBound(start, end);
    }

    private static string LayerOf(Document document, ElementId graphicsStyleId)
    {
        try
        {
            return document.GetElement(graphicsStyleId) is GraphicsStyle style
                ? style.GraphicsStyleCategory?.Name ?? string.Empty
                : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static GraphicsStyle? FindLineStyle(Document document, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var lines = document.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        foreach (Category sub in lines.SubCategories)
        {
            if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
        }

        return null;
    }

    /// <summary>Model line đã có mang đúng line style, đọc về cùng dạng dữ liệu để so trùng.</summary>
    private static List<CadCurve> ExistingModelCurves(Document document, GraphicsStyle? style)
    {
        var existing = new List<CadCurve>();
        foreach (var element in new FilteredElementCollector(document).OfClass(typeof(CurveElement)).ToElements())
        {
            if (element is not CurveElement curveElement) continue;

            try
            {
                if (style != null && RevitCompat.IdValue(curveElement.LineStyle.Id) != RevitCompat.IdValue(style.Id)) continue;

                var geometry = curveElement.GeometryCurve;
                var layer = style?.GraphicsStyleCategory?.Name ?? string.Empty;
                if (geometry is Arc arc)
                {
                    existing.Add(new CadCurve(layer, ToMm(arc.GetEndPoint(0)), ToMm(arc.GetEndPoint(1)), CadCurveKind.Arc, ToMm(arc.Evaluate(0.5, true))));
                }
                else if (geometry != null)
                {
                    existing.Add(new CadCurve(layer, ToMm(geometry.GetEndPoint(0)), ToMm(geometry.GetEndPoint(1))));
                }
            }
            catch (Exception)
            {
                // Curve element không đọc được thì coi như chưa có — cùng lắm là tạo thêm một đường.
            }
        }

        return existing;
    }
}
