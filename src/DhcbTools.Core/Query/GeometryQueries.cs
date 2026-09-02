using Autodesk.Revit.DB;

namespace DhcbTools.Core.Query;

/// <summary>
/// Giai đoạn 10.1 — phần đọc "sâu" mà agent cần để <b>nhìn, chỉ và kiểm</b> được kết quả, chứ không
/// chỉ đếm được phần tử.
/// <para>
/// Trước đây <c>/query elements</c> chỉ trả về một điểm tâm duy nhất cho mỗi phần tử: agent tả được
/// mô hình nhưng không biết phần tử nằm ở đâu, dài bao nhiêu, nối vào đâu — nên không kiểm được việc
/// mình vừa làm có đúng không.
/// </para>
/// <para>Chỉ đọc <c>Document</c>, không đụng UI, nên batch và Bridge dùng chung.</para>
/// </summary>
internal static class GeometryQueries
{
    /// <summary>Hình học chi tiết của phần tử: hộp bao, đường tâm, connector, host, level.</summary>
    public static object ElementGeometry(Document doc, QueryParams p)
    {
        var elements = Resolve(doc, p).ToList();
        if (elements.Count == 0)
        {
            return new { error = "Không có phần tử nào khớp. Truyền elementIds hoặc categories." };
        }

        var items = elements.Select(e => new
        {
            id = RevitCompat.IdValue(e.Id),
            category = e.Category?.Name,
            name = e.Name,
            typeName = SafeName(doc.GetElement(e.GetTypeId())),
            levelName = LevelName(doc, e),
            boundingBoxMm = BoundingBox(e),
            curveMm = CurveOf(e),
            connectors = Connectors(e),
            host = HostOf(e),
        }).ToList();

        return new { count = items.Count, elements = items };
    }

    /// <summary>Tham số của một category: tên, kiểu lưu, đơn vị, chỉ đọc — để agent dựng config đúng.</summary>
    public static object ParametersOf(Document doc, QueryParams p)
    {
        if (p.Categories.Count == 0)
        {
            return new { error = "Cần truyền categories, ví dụ {\"categories\":[\"Doors\"]}." };
        }

        var ids = ParameterSync.ParameterExportCommand.ResolveCategoryIds(doc, p.Categories, out var unknown);
        if (ids.Count == 0)
        {
            return new { error = $"Không có category nào khớp: {string.Join(", ", p.Categories)}." };
        }

        var sample = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(ids.ToList()))
            .Take(50)
            .ToList();

        var seen = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        void Collect(Element element, bool isType)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                var name = parameter.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name) || seen.ContainsKey(name!))
                {
                    continue;
                }

                if (p.WritableOnly && parameter.IsReadOnly)
                {
                    continue;
                }

                seen[name!] = new
                {
                    name,
                    storageType = parameter.StorageType.ToString(),
                    readOnly = parameter.IsReadOnly,
                    onType = isType,
                    isShared = parameter.IsShared,
                    sampleValue = SampleValue(parameter),
                };
            }
        }

        foreach (var element in sample)
        {
            Collect(element, isType: false);

            var type = doc.GetElement(element.GetTypeId());
            if (type != null)
            {
                Collect(type, isType: true);
            }
        }

        return new
        {
            categories = p.Categories,
            unknownCategories = unknown,
            sampledElements = sample.Count,
            count = seen.Count,
            parameters = seen.Values.ToList(),
        };
    }

    /// <summary>
    /// Bảng thống kê dạng hàng — đúng cột/hàng đang hiển thị. Khác <c>ScheduleExport</c> ở chỗ trả
    /// thẳng dữ liệu cho agent thay vì ghi CSV ra đĩa rồi bảo agent tự đi đọc file.
    /// </summary>
    public static object ScheduleRows(Document doc, QueryParams p)
    {
        var schedules = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .Where(s => !s.IsTemplate)
            .ToList();

        if (string.IsNullOrWhiteSpace(p.ScheduleName))
        {
            return new
            {
                count = schedules.Count,
                schedules = schedules.Select(s => new { id = RevitCompat.IdValue(s.Id), name = s.Name }).ToList(),
                hint = "Truyền scheduleName để lấy nội dung bảng.",
            };
        }

        var schedule = schedules.FirstOrDefault(s => string.Equals(s.Name, p.ScheduleName, StringComparison.OrdinalIgnoreCase))
            ?? schedules.FirstOrDefault(s => s.Name.IndexOf(p.ScheduleName!, StringComparison.OrdinalIgnoreCase) >= 0);

        if (schedule == null)
        {
            return new
            {
                error = $"Không có schedule tên \"{p.ScheduleName}\".",
                available = schedules.Select(s => s.Name).ToList(),
            };
        }

        try
        {
            var body = schedule.GetTableData().GetSectionData(SectionType.Body);
            var rowCount = body.NumberOfRows;
            var columnCount = body.NumberOfColumns;
            var limit = p.Limit > 0 ? Math.Min(p.Limit, rowCount) : rowCount;

            var rows = new List<List<string>>();
            for (var r = 0; r < limit; r++)
            {
                var row = new List<string>(columnCount);
                for (var c = 0; c < columnCount; c++)
                {
                    row.Add(schedule.GetCellText(SectionType.Body, r, c) ?? string.Empty);
                }

                rows.Add(row);
            }

            return new
            {
                name = schedule.Name,
                id = RevitCompat.IdValue(schedule.Id),
                rowCount,
                columnCount,
                returned = rows.Count,
                rows,
            };
        }
        catch (Exception ex)
        {
            return new { error = $"Không đọc được schedule \"{schedule.Name}\": {ex.Message}" };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<Element> Resolve(Document doc, QueryParams p)
    {
        if (p.ElementIds.Count > 0)
        {
            foreach (var id in p.ElementIds)
            {
                var element = doc.GetElement(RevitCompat.MakeId(id));
                if (element != null)
                {
                    yield return element;
                }
            }

            yield break;
        }

        if (p.Categories.Count == 0)
        {
            yield break;
        }

        var ids = ParameterSync.ParameterExportCommand.ResolveCategoryIds(doc, p.Categories, out _);
        if (ids.Count == 0)
        {
            yield break;
        }

        var collector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(ids.ToList()))
            .AsEnumerable();

        if (p.Limit > 0)
        {
            collector = collector.Take(p.Limit);
        }

        foreach (var element in collector)
        {
            yield return element;
        }
    }

    private static object? BoundingBox(Element element)
    {
        try
        {
            var box = element.get_BoundingBox(null);
            if (box == null)
            {
                return null;
            }

            return new
            {
                minX = Round(box.Min.X),
                minY = Round(box.Min.Y),
                minZ = Round(box.Min.Z),
                maxX = Round(box.Max.X),
                maxY = Round(box.Max.Y),
                maxZ = Round(box.Max.Z),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? CurveOf(Element element)
    {
        try
        {
            if (element.Location is not LocationCurve location || location.Curve is not Curve curve)
            {
                return null;
            }

            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);

            return new
            {
                startX = Round(start.X),
                startY = Round(start.Y),
                startZ = Round(start.Z),
                endX = Round(end.X),
                endY = Round(end.Y),
                endZ = Round(end.Z),
                lengthMm = Round(curve.Length),
                isLine = curve is Line,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Connector và tình trạng nối — thứ agent cần để biết tuyến ống đã liền chưa.</summary>
    private static object? Connectors(Element element)
    {
        try
        {
            var manager = (element as MEPCurve)?.ConnectorManager
                ?? (element as FamilyInstance)?.MEPModel?.ConnectorManager;

            if (manager == null)
            {
                return null;
            }

            var list = new List<object>();
            foreach (Connector connector in manager.Connectors)
            {
                list.Add(new
                {
                    origin = new { x = Round(connector.Origin.X), y = Round(connector.Origin.Y), z = Round(connector.Origin.Z) },
                    isConnected = connector.IsConnected,
                    domain = connector.Domain.ToString(),
                    shape = SafeGet(() => connector.Shape.ToString()),
                });
            }

            return list;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? HostOf(Element element)
    {
        try
        {
            if (element is not FamilyInstance instance || instance.Host == null)
            {
                return null;
            }

            return new
            {
                id = RevitCompat.IdValue(instance.Host.Id),
                category = instance.Host.Category?.Name,
                name = instance.Host.Name,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? LevelName(Document doc, Element element)
    {
        try
        {
            var parameter = RevitCompat.Lookup(element, "level")
                ?? element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.LEVEL_PARAM);

            if (parameter?.StorageType != StorageType.ElementId)
            {
                return null;
            }

            return (doc.GetElement(parameter.AsElementId()) as Level)?.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? SampleValue(Parameter parameter)
    {
        try
        {
            if (!parameter.HasValue)
            {
                return null;
            }

            return parameter.StorageType switch
            {
                StorageType.String => parameter.AsString(),
                StorageType.Integer => parameter.AsInteger().ToString(),
                StorageType.Double => parameter.AsValueString() ?? parameter.AsDouble().ToString("F3"),
                StorageType.ElementId => parameter.AsValueString(),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? SafeName(Element? element)
    {
        try
        {
            return element?.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static T? SafeGet<T>(Func<T?> get)
    {
        try
        {
            return get();
        }
        catch (Exception)
        {
            return default;
        }
    }

    /// <summary>Mọi toạ độ/chiều dài trả ra ngoài đều là mm — agent không phải biết Revit dùng feet.</summary>
    private static double Round(double feet) => Math.Round(RevitCompat.FtToMm(feet), 1);
}
