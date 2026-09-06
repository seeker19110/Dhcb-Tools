using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Setout;

namespace DhcbTools.Core.Export;

/// <summary>
/// Đề xuất A1 (<c>docs/nghien-cuu-chuoi-den-hoan-cong.md</c>): toạ độ định vị ra máy toàn đạc. Trắc đạc
/// đang đọc bản vẽ rồi <b>gõ tay</b> toạ độ tim cột / lỗ mở / giá đỡ vào máy — gõ nhầm một chữ số là
/// đục lại bê tông. Lệnh chỉ đọc, không mở transaction.
/// </summary>
public sealed class SetoutExportConfig
{
    /// <summary>File CSV cho máy toàn đạc.</summary>
    public required string OutputPath { get; init; }

    /// <summary>File DXF điểm cho phần mềm máy đời cũ (tuỳ chọn).</summary>
    public string? DxfPath { get; init; }

    /// <summary>Category lấy điểm; rỗng = Structural Columns + Columns (tim cột — thứ cắm đầu tiên).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Chỉ lấy đúng các ElementId này (ví dụ từ selection); rỗng = theo category.</summary>
    public List<long> ElementIds { get; init; } = new List<long>();

    /// <summary>Chỉ tầng này (tên Level); rỗng = mọi tầng. Với giao trục: cao độ của tầng này.</summary>
    public string? LevelName { get; init; }

    public string? FamilyContains { get; init; }

    public string? TypeContains { get; init; }

    /// <summary><c>Survey</c> (toạ độ chung theo điểm khảo sát — mặc định) hoặc <c>Internal</c> (gốc nội bộ Revit).</summary>
    public string CoordinateSystem { get; init; } = "Survey";

    /// <summary>Thứ tự cột theo chữ máy nhận: P tên, N Bắc, E Đông, Z cao độ, D mô tả, C mã, L tầng, I ElementId.</summary>
    public string Columns { get; init; } = SetoutColumns.Default;

    /// <summary><c>m</c> hoặc <c>mm</c>.</summary>
    public string Unit { get; init; } = "m";

    /// <summary>Số lẻ; null = 3 với m, 0 với mm.</summary>
    public int? Decimals { get; init; }

    public bool IncludeHeader { get; init; } = true;

    /// <summary>Mẫu tên điểm: {Code} {Category} {Family} {Type} {Level} {Mark} {Id} {Kind} {n:000}.</summary>
    public string NamePattern { get; init; } = "{Code}{n:000}";

    /// <summary>Mẫu tên điểm giao trục; mặc định chính cặp trục ({Grid} = A-1).</summary>
    public string GridNamePattern { get; init; } = "{Grid}";

    public string DescriptionPattern { get; init; } = "{Category} {Level}";

    /// <summary>Phần tử dạng đường (dầm, tường, ống): <c>Ends</c> (hai đầu — mặc định), <c>Mid</c>, <c>Both</c>.</summary>
    public string CurvePoints { get; init; } = "Ends";

    /// <summary>
    /// Điểm lấy cho phần tử đặt theo điểm (cột, thiết bị): <c>Centre</c> (tâm hình học trên mặt bằng — mặc định)
    /// hoặc <c>Insertion</c> (điểm chèn family). Họ cột "Off Center" có điểm chèn ở mép, lệch tim tới 305 mm (§52).
    /// </summary>
    public string PointMode { get; init; } = "Centre";

    /// <summary>Thêm giao điểm các trục thẳng (A-1, B-2…).</summary>
    public bool IncludeGridIntersections { get; init; }

    /// <summary>Giới hạn tên điểm của máy (Leica/Trimble: 16); 0 = không cắt.</summary>
    public int MaxNameLength { get; init; } = 16;

    /// <summary>Ghi BOM UTF-8 để Excel đọc tiếng Việt. Mặc định tắt: nhiều phần mềm máy đọc BOM thành ký tự lạ ở tên điểm đầu tiên.</summary>
    public bool Utf8Bom { get; init; }
}

public sealed class SetoutExportCommand : ICoreCommand<SetoutExportConfig>
{
    public string CommandName => "SetoutExport";

    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns,
    };

    public CommandResult Execute(Document document, SetoutExportConfig config)
    {
        // ── Kiểm config trước khi chạm mô hình — sai một chữ cột là báo rõ, không đoán ──
        if (!SetoutColumns.TryParse(config.Columns, out var columns, out var columnError))
        {
            return CommandResult.Fail(columnError);
        }

        if (!SetoutCsvFormat.TryParseUnit(config.Unit, out var metres, out var unitError))
        {
            return CommandResult.Fail(unitError);
        }

        if (!SetoutExportLogic.TryParseCurvePoints(config.CurvePoints, out var curveEnds, out var curveMid, out var curveError))
        {
            return CommandResult.Fail(curveError);
        }

        if (!SetoutExportLogic.TryParseCoordinateSystem(config.CoordinateSystem, out var useSurvey, out var systemError))
        {
            return CommandResult.Fail(systemError);
        }

        if (!SetoutExportLogic.TryParsePointMode(config.PointMode, out var useCentre, out var modeError))
        {
            return CommandResult.Fail(modeError);
        }

        var anchorKinds = SetoutExportLogic.CurveAnchorKinds(curveEnds, curveMid);

        Level? level = null;
        if (!string.IsNullOrWhiteSpace(config.LevelName))
        {
            level = RevitCompat.FindLevel(document, config.LevelName);
            if (level == null)
            {
                var names = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                    .Select(l => l.Name).OrderBy(n => n, NaturalComparer.Instance).ToList();
                return CommandResult.Fail($"Không có tầng tên \"{config.LevelName}\". Tầng có thật: {string.Join(", ", names)}.");
            }
        }

        var messages = new List<string>();
        var transform = useSurvey ? SurveyTransform(document, messages) : Transform.Identity;

        // ── Phần tử ──
        var elements = ResolveElements(document, config, messages, out var resolveError);
        if (resolveError != null)
        {
            return CommandResult.Fail(resolveError);
        }

        var sources = new List<SetoutSource>();
        var noGeometry = 0;
        var boxFallback = 0;
        var filteredOut = 0;
        var offCentre = 0;
        var maxOffsetMm = 0.0;
        foreach (var element in elements)
        {
            if (level != null && !string.Equals(LevelNameOf(document, element), level.Name, StringComparison.OrdinalIgnoreCase))
            {
                filteredOut++;
                continue;
            }

            var type = document.GetElement(element.GetTypeId()) as ElementType;
            var family = type?.FamilyName ?? string.Empty;
            var typeName = type?.Name ?? element.Name;
            if (!string.IsNullOrWhiteSpace(config.FamilyContains) && family.IndexOf(config.FamilyContains!, StringComparison.OrdinalIgnoreCase) < 0)
            {
                filteredOut++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(config.TypeContains) && typeName.IndexOf(config.TypeContains!, StringComparison.OrdinalIgnoreCase) < 0)
            {
                filteredOut++;
                continue;
            }

            var anchors = Anchors(element, anchorKinds, useCentre, out var fromBox, out var offsetMm);
            if (offsetMm > SetoutExportLogic.OffCentreToleranceMm)
            {
                offCentre++;
                maxOffsetMm = Math.Max(maxOffsetMm, offsetMm);
            }

            if (anchors.Count == 0)
            {
                noGeometry++;
                continue;
            }

            if (fromBox)
            {
                boxFallback++;
            }

            var levelName = LevelNameOf(document, element);
            var mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
            foreach (var anchor in anchors)
            {
                var p = transform.OfPoint(anchor.Value);
                sources.Add(new SetoutSource(anchor.Key, RevitCompat.FtToMm(p.X), RevitCompat.FtToMm(p.Y), RevitCompat.FtToMm(p.Z))
                {
                    ElementId = RevitCompat.IdValue(element.Id),
                    Category = element.Category?.Name ?? string.Empty,
                    Family = family,
                    TypeName = typeName,
                    Level = levelName,
                    Mark = mark,
                });
            }
        }

        // ── Giao trục ──
        var intersections = 0;
        var curvedGrids = 0;
        if (config.IncludeGridIntersections)
        {
            intersections = AddGridIntersections(document, transform, level, sources, out curvedGrids);
        }

        var elementPoints = sources.Count - intersections;
        var precondition = Precondition.NonEmptyInput(
            CommandName,
            SetoutExportLogic.InputDescription(config.ElementIds.Count > 0, config.IncludeGridIntersections),
            sources.Count,
            "Kiểm lại categories/levelName/familyContains, hoặc bật includeGridIntersections; tra phần tử có thật bằng query elements.");
        var result = CommandResult.Ok(string.Empty);
        if (RevitPrecondition.Blocks(precondition, result))
        {
            foreach (var m in messages)
            {
                result.Messages.Add(m);
            }

            return result;
        }

        // ── Đặt tên, ghi file ──
        var plan = SetoutPlanner.Plan(sources, new SetoutPlanOptions
        {
            NamePattern = string.IsNullOrWhiteSpace(config.NamePattern) ? "{Code}{n:000}" : config.NamePattern,
            GridNamePattern = string.IsNullOrWhiteSpace(config.GridNamePattern) ? "{Grid}" : config.GridNamePattern,
            DescriptionPattern = config.DescriptionPattern ?? string.Empty,
            MaxNameLength = config.MaxNameLength,
        });

        var format = new SetoutCsvFormat { Columns = columns, Metres = metres, Decimals = config.Decimals, IncludeHeader = config.IncludeHeader };
        var encoding = config.Utf8Bom ? CsvText.Utf8WithBom : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        RevitCompat.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, SetoutCsv.Write(plan.Points, format), encoding);

        if (!string.IsNullOrWhiteSpace(config.DxfPath))
        {
            RevitCompat.EnsureParentDirectory(config.DxfPath);
            File.WriteAllText(config.DxfPath!, SetoutDxf.Write(plan.Points, metres, format.EffectiveDecimals), new UTF8Encoding(false));
        }

        result = CommandResult.Ok(
            SetoutExportLogic.Summary(
                plan.Points.Count, elementPoints, elements.Count - filteredOut - noGeometry,
                config.IncludeGridIntersections, intersections,
                config.OutputPath, useSurvey, metres, columns),
            plan.Points.Count);

        foreach (var m in messages)
        {
            result.Messages.Add(m);
        }

        foreach (var kv in plan.CountByCode.OrderByDescending(k => k.Value))
        {
            result.Messages.Add($"{kv.Key}: {kv.Value} điểm");
        }

        result.Messages.AddRange(SetoutExportLogic.TrailingNotes(config.DxfPath, filteredOut, boxFallback, noGeometry, curvedGrids));
        var offCentreNote = SetoutExportLogic.OffCentreNote(offCentre, maxOffsetMm, useCentre);
        if (offCentreNote != null)
        {
            result.Messages.Add(offCentreNote);
        }


        foreach (var note in plan.Notes)
        {
            result.Messages.Add(note);
        }

        return result;
    }

    // ── Hệ toạ độ ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Transform nội bộ → Survey (toạ độ chung). Chiều của <c>GetTotalTransform()</c> được <b>tự kiểm</b>
    /// bằng <c>GetProjectPosition</c> tại hai điểm thay vì tin vào trí nhớ về API: chọn chiều nào đưa
    /// gốc nội bộ và điểm (1 m, 0, 0) về đúng E/N/Z mà Revit báo; không chiều nào khớp thì cảnh báo.
    /// </summary>
    private static Transform SurveyTransform(Document document, List<string> messages)
    {
        var location = document.ActiveProjectLocation;
        var total = location.GetTotalTransform();
        var probes = new[] { XYZ.Zero, new XYZ(RevitCompat.MmToFt(1000), 0, 0) };
        var expected = probes.Select(p =>
        {
            var pos = location.GetProjectPosition(p);
            return new XYZ(pos.EastWest, pos.NorthSouth, pos.Elevation);
        }).ToArray();

        // Ứng viên theo thứ tự ưu tiên: Inverse trước (đúng với API hiện hành), rồi chính total.
        var candidates = new[] { total.Inverse, total };
        var index = SetoutExportLogic.ChooseMatchingTransform(
            candidates.Select(c => (Func<(double X, double Y, double Z), (double X, double Y, double Z)>)(p =>
            {
                var q = c.OfPoint(new XYZ(p.X, p.Y, p.Z));
                return (q.X, q.Y, q.Z);
            })).ToList(),
            probes.Select(p => (p.X, p.Y, p.Z)).ToList(),
            expected.Select(p => (p.X, p.Y, p.Z)).ToList(),
            RevitCompat.MmToFt(1));

        var chosen = index >= 0 ? candidates[index] : total.Inverse;
        if (index < 0)
        {
            messages.Add(SetoutExportLogic.DirectionUnverifiedNote);
        }

        var origin = location.GetProjectPosition(XYZ.Zero);
        messages.AddRange(SetoutExportLogic.SiteNotes(
            location.Name,
            RevitCompat.FtToMm(origin.EastWest),
            RevitCompat.FtToMm(origin.NorthSouth),
            RevitCompat.FtToMm(origin.Elevation),
            origin.Angle * 180.0 / Math.PI));

        return chosen;
    }

    // ── Phần tử và điểm ──────────────────────────────────────────────────────

    private static List<Element> ResolveElements(Document document, SetoutExportConfig config, List<string> messages, out string? error)
    {
        error = null;
        var list = new List<Element>();

        if (config.ElementIds.Count > 0)
        {
            var missing = new List<long>();
            foreach (var id in config.ElementIds)
            {
                var element = document.GetElement(RevitCompat.MakeId(id));
                if (element == null)
                {
                    missing.Add(id);
                }
                else
                {
                    list.Add(element);
                }
            }

            var missingNote = SetoutExportLogic.MissingIdsNote(missing);
            if (missingNote != null)
            {
                messages.Add(missingNote);
            }

            return list;
        }

        ICollection<ElementId> categoryIds;
        if (config.Categories.Count > 0)
        {
            categoryIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
            if (unknown.Count > 0)
            {
                error = SetoutExportLogic.UnknownCategoriesError(unknown);
                return list;
            }
        }
        else
        {
            categoryIds = DefaultCategories.Select(c => new ElementId(c)).ToList();
        }

        list.AddRange(new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds.ToList()))
            .ToElements());
        return list;
    }

    /// <summary>
    /// Tâm mặt bằng của phần tử đặt theo điểm. Với family instance: TRỌNG TÂM các solid của hình học GỐC
    /// (trước join/cut) cân theo thể tích, đưa qua transform của instance. Vì sao không dùng hộp bao: cột nối
    /// vào tường thì hộp bao thường chỉ còn phần ngoài tường; hộp bao hình học gốc của họ "Off Center" lại lệch
    /// 117–160 mm so với thân cột thật (family chứa thêm hình học không phải thân) — cả hai đều lộ khi đối chiếu
    /// với IFC của Autodesk (§52). Không có solid → tâm hộp bao thường; không có gì → null.
    /// </summary>
    private static XYZ? PlanCentre(Element element)
    {
        if (element is FamilyInstance fi)
        {
            try
            {
                var volume = 0.0;
                var sum = XYZ.Zero;
                foreach (var g in fi.GetOriginalGeometry(new Options()))
                {
                    foreach (var solid in SolidsOf(g))
                    {
                        if (solid.Volume <= 1e-9)
                        {
                            continue;
                        }

                        sum += solid.ComputeCentroid() * solid.Volume;
                        volume += solid.Volume;
                    }
                }

                if (volume > 1e-9)
                {
                    return fi.GetTransform().OfPoint(sum / volume);
                }
            }
            catch (Exception)
            {
                // Family không có hình học 3D hay API từ chối tính trọng tâm → rơi về hộp bao thường.
            }
        }

        var bb = element.get_BoundingBox(null);
        return bb == null ? null : (bb.Min + bb.Max) / 2;
    }

    private static IEnumerable<Solid> SolidsOf(GeometryObject g)
    {
        switch (g)
        {
            case Solid s:
                yield return s;
                break;
            case GeometryInstance gi:
                foreach (var inner in gi.GetSymbolGeometry())
                {
                    foreach (var s in SolidsOf(inner))
                    {
                        yield return gi.Transform.IsIdentity ? s : SolidUtils.CreateTransformed(s, gi.Transform);
                    }
                }

                break;
        }
    }

    /// <summary>Điểm đặc trưng của phần tử theo <c>Location</c>: điểm đặt (tim), hai đầu/giữa đường, hay tâm hộp bao.</summary>
    private static List<KeyValuePair<string, XYZ>> Anchors(Element element, List<(string Kind, double T)> anchorKinds, bool useCentre, out bool fromBox, out double offsetMm)
    {
        fromBox = false;
        offsetMm = 0;
        var points = new List<KeyValuePair<string, XYZ>>();
        try
        {
            switch (element.Location)
            {
                case LocationPoint lp:
                    // Điểm chèn family KHÔNG chắc là tim: họ "Rectangular Column (Off Center)" của Snowdon lệch
                    // tới 305 mm, đối chiếu bằng IFC của Autodesk mới lộ (§52). Mặc định lấy trọng tâm solid
                    // trên mặt bằng (PlanCentre), giữ Z của điểm chèn (cao độ chân cột).
                    var anchor = lp.Point;
                    var centre = PlanCentre(element);
                    if (centre != null
                        && SetoutExportLogic.IsOffCentre(
                            RevitCompat.FtToMm(lp.Point.X), RevitCompat.FtToMm(lp.Point.Y),
                            RevitCompat.FtToMm(centre.X), RevitCompat.FtToMm(centre.Y), out offsetMm)
                        && useCentre)
                    {
                        anchor = new XYZ(centre.X, centre.Y, lp.Point.Z);
                    }

                    points.Add(new KeyValuePair<string, XYZ>("tim", anchor));
                    return points;

                case LocationCurve lc when lc.Curve != null:
                    var curve = lc.Curve;
                    foreach (var (kind, t) in anchorKinds)
                    {
                        // Hai đầu lấy đúng GetEndPoint (không nội suy) để trùng từng bit với toạ độ Revit hiển thị.
                        var point = t <= 0 ? curve.GetEndPoint(0) : t >= 1 ? curve.GetEndPoint(1) : curve.Evaluate(t, true);
                        points.Add(new KeyValuePair<string, XYZ>(kind, point));
                    }

                    return points;
            }

            var box = element.get_BoundingBox(null);
            if (box != null)
            {
                fromBox = true;
                points.Add(new KeyValuePair<string, XYZ>("tâm hộp bao", (box.Min + box.Max) / 2));
            }
        }
        catch (Exception)
        {
            // Phần tử không có hình học (nhóm, phần tử hệ thống…) → bỏ qua, đếm ở noGeometry.
        }

        return points;
    }

    private static int AddGridIntersections(Document document, Transform transform, Level? level, List<SetoutSource> sources, out int curved)
    {
        curved = 0;
        var segments = new List<NamedSegment2D>();
        var z = level?.Elevation ?? 0;
        foreach (var grid in new FilteredElementCollector(document).OfClass(typeof(Grid)).Cast<Grid>())
        {
            if (grid.Curve is not Line line)
            {
                curved++;
                continue;
            }

            var a = line.GetEndPoint(0);
            var b = line.GetEndPoint(1);
            segments.Add(new NamedSegment2D(grid.Name, new Segment2D(
                RevitCompat.FtToMm(a.X), RevitCompat.FtToMm(a.Y), RevitCompat.FtToMm(b.X), RevitCompat.FtToMm(b.Y))));
        }

        var found = GridIntersections.Find(segments, toleranceMm: 1.0);
        foreach (var hit in found)
        {
            // Giao điểm tính trong hệ nội bộ (2D, mm) rồi mới đổi hệ — phép quay True North áp cho cả điểm.
            var p = transform.OfPoint(new XYZ(RevitCompat.MmToFt(hit.X), RevitCompat.MmToFt(hit.Y), z));
            sources.Add(new SetoutSource("giao trục", RevitCompat.FtToMm(p.X), RevitCompat.FtToMm(p.Y), RevitCompat.FtToMm(p.Z))
            {
                Category = "Grids",
                Level = level?.Name ?? string.Empty,
                Grid = hit.Name,
                Code = SetoutCodes.For("Grids"),
            });
        }

        return found.Count;
    }

    private static string LevelNameOf(Document document, Element element)
    {
        try
        {
            if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId
                && document.GetElement(element.LevelId) is Level direct)
            {
                return direct.Name;
            }

            var parameter = RevitCompat.Lookup(element, "level")
                ?? element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (parameter?.StorageType == StorageType.ElementId && document.GetElement(parameter.AsElementId()) is Level viaParameter)
            {
                return viaParameter.Name;
            }
        }
        catch (Exception)
        {
        }

        return string.Empty;
    }
}
