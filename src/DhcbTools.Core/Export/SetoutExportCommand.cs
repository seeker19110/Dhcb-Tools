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

        var curvePoints = (config.CurvePoints ?? "Ends").Trim();
        if (!curvePoints.Equals("Ends", StringComparison.OrdinalIgnoreCase)
            && !curvePoints.Equals("Mid", StringComparison.OrdinalIgnoreCase)
            && !curvePoints.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail($"curvePoints \"{config.CurvePoints}\" không hợp lệ. Hợp lệ: Ends (hai đầu), Mid (điểm giữa), Both.");
        }

        var system = (config.CoordinateSystem ?? "Survey").Trim();
        var useSurvey = system.Equals("Survey", StringComparison.OrdinalIgnoreCase) || system.Equals("Shared", StringComparison.OrdinalIgnoreCase);
        if (!useSurvey && !system.Equals("Internal", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail($"coordinateSystem \"{config.CoordinateSystem}\" không hợp lệ. Hợp lệ: Survey (toạ độ chung) hoặc Internal (gốc nội bộ).");
        }

        Level? level = null;
        if (!string.IsNullOrWhiteSpace(config.LevelName))
        {
            level = RevitCompat.FindLevel(document, config.LevelName);
            if (level == null)
            {
                var names = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                    .Select(l => l.Name).OrderBy(n => n, SetoutPlanner.NaturalComparer.Instance).ToList();
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

            var anchors = Anchors(element, curvePoints, out var fromBox);
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
            config.ElementIds.Count > 0 ? "phần tử theo elementIds" : "điểm định vị (phần tử theo bộ lọc" + (config.IncludeGridIntersections ? ", giao trục" : string.Empty) + ")",
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

        var unitLabel = metres ? "m" : "mm";
        result = CommandResult.Ok(
            $"Xuất {plan.Points.Count} điểm định vị ({elementPoints} điểm của {elements.Count - filteredOut - noGeometry} phần tử"
            + (config.IncludeGridIntersections ? $", {intersections} giao trục" : string.Empty)
            + $") → \"{config.OutputPath}\" — hệ {(useSurvey ? "Survey" : "Internal")}, {unitLabel}, cột {string.Concat(columns.Select(LetterOf))}.",
            plan.Points.Count);

        foreach (var m in messages)
        {
            result.Messages.Add(m);
        }

        foreach (var kv in plan.CountByCode.OrderByDescending(k => k.Value))
        {
            result.Messages.Add($"{kv.Key}: {kv.Value} điểm");
        }

        if (!string.IsNullOrWhiteSpace(config.DxfPath))
        {
            result.Messages.Add($"DXF điểm: \"{config.DxfPath}\" (layer DHCB-<mã> và DHCB-<mã>-TEN).");
        }

        if (filteredOut > 0)
        {
            result.Messages.Add($"{filteredOut} phần tử ngoài bộ lọc tầng/family/type.");
        }

        if (boxFallback > 0)
        {
            result.Messages.Add($"{boxFallback} phần tử không có điểm/đường đặt (Location) nên lấy tâm hộp bao — kiểm lại trước khi cắm.");
        }

        if (noGeometry > 0)
        {
            result.Messages.Add($"{noGeometry} phần tử không có hình học nào để lấy điểm, đã bỏ qua.");
        }

        if (curvedGrids > 0)
        {
            result.Messages.Add($"{curvedGrids} trục cong bị bỏ qua khi tính giao trục (chỉ xét trục thẳng).");
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

        var tolerance = RevitCompat.MmToFt(1);
        Transform? chosen = null;
        foreach (var candidate in new[] { total.Inverse, total })
        {
            var ok = true;
            for (var i = 0; i < probes.Length; i++)
            {
                if (candidate.OfPoint(probes[i]).DistanceTo(expected[i]) > tolerance)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                chosen = candidate;
                break;
            }
        }

        if (chosen == null)
        {
            chosen = total.Inverse;
            messages.Add("Không đối chiếu được chiều của GetTotalTransform với GetProjectPosition — toạ độ Survey có thể sai, kiểm lại một điểm bằng Spot Coordinate trước khi đưa ra hiện trường.");
        }

        var origin = location.GetProjectPosition(XYZ.Zero);
        var e = RevitCompat.FtToMm(origin.EastWest);
        var n = RevitCompat.FtToMm(origin.NorthSouth);
        var z = RevitCompat.FtToMm(origin.Elevation);
        var angle = origin.Angle * 180.0 / Math.PI;
        messages.Add($"Site \"{location.Name}\": gốc nội bộ ở E={e:F0} N={n:F0} Z={z:F0} mm, True North xoay {angle:F4}°.");
        if (Math.Abs(e) < 0.5 && Math.Abs(n) < 0.5 && Math.Abs(z) < 0.5 && Math.Abs(angle) < 1e-6)
        {
            messages.Add("Hệ Survey trùng hệ nội bộ (mô hình chưa khai toạ độ chung) — toạ độ ra file là toạ độ Revit, không phải toạ độ khảo sát; đối chiếu với tổ trắc đạc trước khi dùng.");
        }

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

            if (missing.Count > 0)
            {
                messages.Add($"{missing.Count} Id không có trong mô hình: {string.Join(", ", missing.Take(20))}{(missing.Count > 20 ? ", …" : string.Empty)}.");
            }

            return list;
        }

        ICollection<ElementId> categoryIds;
        if (config.Categories.Count > 0)
        {
            categoryIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
            if (unknown.Count > 0)
            {
                error = "Category không có: " + string.Join(", ", unknown) + ". Tra tên category có thật bằng query categories hoặc parameters_of.";
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

    /// <summary>Điểm đặc trưng của phần tử theo <c>Location</c>: điểm đặt (tim), hai đầu/giữa đường, hay tâm hộp bao.</summary>
    private static List<KeyValuePair<string, XYZ>> Anchors(Element element, string curvePoints, out bool fromBox)
    {
        fromBox = false;
        var points = new List<KeyValuePair<string, XYZ>>();
        try
        {
            switch (element.Location)
            {
                case LocationPoint lp:
                    points.Add(new KeyValuePair<string, XYZ>("tim", lp.Point));
                    return points;

                case LocationCurve lc when lc.Curve != null:
                    var curve = lc.Curve;
                    var ends = !curvePoints.Equals("Mid", StringComparison.OrdinalIgnoreCase);
                    var mid = !curvePoints.Equals("Ends", StringComparison.OrdinalIgnoreCase);
                    if (ends)
                    {
                        points.Add(new KeyValuePair<string, XYZ>("đầu", curve.GetEndPoint(0)));
                    }

                    if (mid)
                    {
                        points.Add(new KeyValuePair<string, XYZ>("giữa", curve.Evaluate(0.5, true)));
                    }

                    if (ends)
                    {
                        points.Add(new KeyValuePair<string, XYZ>("cuối", curve.GetEndPoint(1)));
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

    private static string LetterOf(SetoutColumn column) => column switch
    {
        SetoutColumn.Name => "P",
        SetoutColumn.North => "N",
        SetoutColumn.East => "E",
        SetoutColumn.Elevation => "Z",
        SetoutColumn.Description => "D",
        SetoutColumn.Code => "C",
        SetoutColumn.Level => "L",
        SetoutColumn.ElementId => "I",
        _ => "?",
    };
}
