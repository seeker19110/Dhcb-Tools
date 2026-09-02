using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace DhcbTools.Core.Query;

/// <summary>
/// Xử lý tất cả truy vấn đọc (POST /query) cho Revit.
/// Trả về object tự do (serialized sang JSON bởi Bridge).
/// Không mở Transaction — chỉ đọc.
/// </summary>
public static class RevitQueryHandler
{
    public static object Handle(Document doc, QueryRequest req)
    {
        return req.Query.ToUpperInvariant() switch
        {
            "DOCUMENT_INFO" => GetDocumentInfo(doc),
            "ELEMENTS"      => GetElements(doc, req.Params),
            "LEVELS"        => GetLevels(doc),
            "VIEWS"         => GetViews(doc, req.Params),
            "SHEETS"        => GetSheets(doc),
            "ROOMS"         => GetRooms(doc, req.Params),
            "FAMILIES"      => GetFamilies(doc, req.Params),
            "WARNINGS"      => GetWarnings(doc, req.Params),
            "LINKS"         => GetLinks(doc),
            "STATS"         => GetStats(doc),
            _ => new { error = $"Query không xác định: \"{req.Query}\". " +
                 "Hợp lệ: document_info, elements, levels, views, sheets, rooms, families, warnings, links, stats." }
        };
    }

    // ──────────────────────────────────────────────────────────────
    // document_info
    // ──────────────────────────────────────────────────────────────
    private static object GetDocumentInfo(Document doc)
    {
        var info = doc.ProjectInformation;
        var path = doc.PathName;
        var isFamilyDoc = doc.IsFamilyDocument;

        var warnings = doc.GetWarnings();
        var linkCount = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .GetElementCount();

        return new
        {
            title         = doc.Title,
            pathName      = path,
            isFamilyDoc,
            projectNumber = isFamilyDoc ? null : SafeGet(() => info.Number),
            projectName   = isFamilyDoc ? null : SafeGet(() => info.Name),
            projectStatus = isFamilyDoc ? null : SafeGet(() => info.Status),
            address       = isFamilyDoc ? null : SafeGet(() => info.Address),
            clientName    = isFamilyDoc ? null : SafeGet(() => info.ClientName),
            buildingName  = isFamilyDoc ? null : SafeGet(() => info.BuildingName),
            organizationName = isFamilyDoc ? null : SafeGet(() => info.OrganizationName),
            isWorkshared  = doc.IsWorkshared,
            warningCount  = warnings.Count,
            linkCount,
        };
    }

    // ──────────────────────────────────────────────────────────────
    // elements
    // ──────────────────────────────────────────────────────────────
    private static object GetElements(Document doc, QueryParams p)
    {
        var categoryIds = p.Categories.Count > 0
            ? ParameterSync.ParameterExportCommand.ResolveCategoryIds(doc, p.Categories, out _)
            : null;

        var collector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .Where(e => e.Category is not null)
            .Where(e => categoryIds is null || categoryIds.Contains(e.Category!.Id));

        if (p.Level is { Length: > 0 })
        {
            collector = collector.Where(e => BelongsToLevel(doc, e, p.Level));
        }

        var list = collector.ToList();
        if (p.Limit > 0) list = list.Take(p.Limit).ToList();

        var rows = list.Select(e =>
        {
            var location = GetLocation(e);
            var paramValues = new Dictionary<string, string?>();
            foreach (var pName in p.ParameterNames)
            {
                paramValues[pName] = ParameterSync.ParameterExportCommand.ReadParameterAsString(e, pName);
            }

            return new
            {
                id           = RevitCompat.IdValue(e.Id),
                category     = e.Category!.Name,
                name         = e.Name,
                levelId      = GetLevelId(e),
                locationX    = location?.X,
                locationY    = location?.Y,
                locationZ    = location?.Z,
                parameters   = paramValues.Count > 0 ? paramValues : null,
            };
        }).ToList();

        return new { count = rows.Count, elements = rows };
    }

    // ──────────────────────────────────────────────────────────────
    // levels
    // ──────────────────────────────────────────────────────────────
    private static object GetLevels(Document doc)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => new
            {
                id        = RevitCompat.IdValue(l.Id),
                name      = l.Name,
                elevation = l.Elevation,            // internal unit (feet)
                elevationMm = RevitCompat.FtToMm(l.Elevation),  // mm
            })
            .ToList();

        return new { count = levels.Count, levels };
    }

    // ──────────────────────────────────────────────────────────────
    // views
    // ──────────────────────────────────────────────────────────────
    private static object GetViews(Document doc, QueryParams p)
    {
        var placedViewIds = new FilteredElementCollector(doc)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .ToDictionary(vp => vp.ViewId, vp => vp.SheetId);

        var views = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .Where(v => v.ViewType is not (
                Autodesk.Revit.DB.ViewType.DrawingSheet or
                Autodesk.Revit.DB.ViewType.SystemBrowser or
                Autodesk.Revit.DB.ViewType.ProjectBrowser or
                Autodesk.Revit.DB.ViewType.Internal or
                Autodesk.Revit.DB.ViewType.Undefined));

        if (p.ViewType is { Length: > 0 })
        {
            if (System.Enum.TryParse<Autodesk.Revit.DB.ViewType>(p.ViewType, true, out var vt))
                views = views.Where(v => v.ViewType == vt);
        }

        var list = views.Select(v =>
        {
            placedViewIds.TryGetValue(v.Id, out var sheetId);
            return new
            {
                id           = RevitCompat.IdValue(v.Id),
                name         = v.Name,
                viewType     = v.ViewType.ToString(),
                scale        = SafeGet(() => v.Scale),
                templateName = v.ViewTemplateId != ElementId.InvalidElementId
                               ? SafeGet(() => doc.GetElement(v.ViewTemplateId)?.Name)
                               : null,
                onSheet      = sheetId is not null && sheetId != ElementId.InvalidElementId,
                sheetId      = sheetId is null ? (long?)null : RevitCompat.IdValue(sheetId),
            };
        }).ToList();

        if (p.Limit > 0) list = list.Take(p.Limit).ToList();

        return new { count = list.Count, views = list };
    }

    // ──────────────────────────────────────────────────────────────
    // sheets
    // ──────────────────────────────────────────────────────────────
    private static object GetSheets(Document doc)
    {
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(s =>
            {
                var viewIds = s.GetAllPlacedViews();
                return new
                {
                    id          = RevitCompat.IdValue(s.Id),
                    number      = s.SheetNumber,
                    name        = s.Name,
                    viewCount   = viewIds.Count,
                    viewIds     = viewIds.Select(v => RevitCompat.IdValue(v)).ToList(),
                    isPlaceholder = s.IsPlaceholder,
                };
            })
            .OrderBy(s => s.number)
            .ToList();

        return new { count = sheets.Count, sheets };
    }

    // ──────────────────────────────────────────────────────────────
    // rooms
    // ──────────────────────────────────────────────────────────────
    private static object GetRooms(Document doc, QueryParams p)
    {
        var rooms = new FilteredElementCollector(doc)
            .OfClass(typeof(SpatialElement))
            .OfType<Room>()
            .Where(r => r.Area > 0); // chỉ phòng đã đặt (có area)

        if (p.Level is { Length: > 0 })
        {
            rooms = rooms.Where(r => r.Level is not null &&
                string.Equals(r.Level.Name, p.Level, StringComparison.OrdinalIgnoreCase));
        }

        var list = rooms.Select(r => new
        {
            id           = RevitCompat.IdValue(r.Id),
            name         = r.Name,
            number       = r.Number,
            levelName    = r.Level?.Name,
            areaSqm      = Math.Round(RevitCompat.SqFtToSqm(r.Area), 3),  // ft² → m²
            perimeterM   = Math.Round(r.Perimeter * 0.3048, 3),    // ft → m
            department   = SafeGet(() => RevitCompat.Lookup(r, "department")?.AsString()),
            occupancy    = SafeGet(() => RevitCompat.Lookup(r, "occupancy")?.AsString()),
            locationX    = (r.Location as LocationPoint)?.Point.X,
            locationY    = (r.Location as LocationPoint)?.Point.Y,
            locationZ    = (r.Location as LocationPoint)?.Point.Z,
        }).ToList();

        if (p.Limit > 0) list = list.Take(p.Limit).ToList();

        return new { count = list.Count, rooms = list };
    }

    // ──────────────────────────────────────────────────────────────
    // families
    // ──────────────────────────────────────────────────────────────
    private static object GetFamilies(Document doc, QueryParams p)
    {
        var query = new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>();

        if (p.FamilyNameContains is { Length: > 0 })
        {
            query = query.Where(f =>
                f.Name.IndexOf(p.FamilyNameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        var list = query.Select(f =>
        {
            var typeNames = f.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(s => s is not null)
                .Select(s => s!.Name)
                .OrderBy(n => n)
                .ToList();

            return new
            {
                id          = RevitCompat.IdValue(f.Id),
                name        = f.Name,
                category    = f.FamilyCategory?.Name,
                typeCount   = typeNames.Count,
                types       = typeNames,
            };
        })
        .OrderBy(f => f.name)
        .ToList();

        if (p.Limit > 0) list = list.Take(p.Limit).ToList();

        return new { count = list.Count, families = list };
    }

    // ──────────────────────────────────────────────────────────────
    // warnings
    // ──────────────────────────────────────────────────────────────
    private static object GetWarnings(Document doc, QueryParams p)
    {
        var warnings = doc.GetWarnings();
        var list = warnings.Select(w => new
        {
            description  = w.GetDescriptionText(),
            elementIds   = w.GetFailingElements().Select(id => RevitCompat.IdValue(id)).ToList(),
            severity     = w.GetSeverity().ToString(),
        }).ToList();

        if (p.Limit > 0) list = list.Take(p.Limit).ToList();

        return new { count = warnings.Count, warnings = list };
    }

    // ──────────────────────────────────────────────────────────────
    // links
    // ──────────────────────────────────────────────────────────────
    private static object GetLinks(Document doc)
    {
        var links = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .Select(l =>
            {
                var linkType = doc.GetElement(l.GetTypeId()) as RevitLinkType;
                return new
                {
                    id          = RevitCompat.IdValue(l.Id),
                    name        = l.Name,
                    isLoaded    = linkType is not null && RevitLinkType.IsLoaded(doc, linkType.Id),
                    pathName    = linkType?.GetExternalFileReference().GetAbsolutePath(),
                };
            })
            .ToList();

        return new { count = links.Count, links };
    }

    // ──────────────────────────────────────────────────────────────
    // stats
    // ──────────────────────────────────────────────────────────────
    private static object GetStats(Document doc)
    {
        var stats = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .Where(e => e.Category is not null)
            .GroupBy(e => e.Category!.Name)
            .Select(g => new { category = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        return new { categoryCount = stats.Count, totalElements = stats.Sum(s => s.count), stats };
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────
    private static XYZ? GetLocation(Element e)
    {
        return e.Location switch
        {
            LocationPoint pt  => pt.Point,
            LocationCurve lc  => lc.Curve.Evaluate(0.5, true),
            _                 => e.get_BoundingBox(null) is { } box ? (box.Min + box.Max) / 2 : null,
        };
    }

    private static long? GetLevelId(Element e)
    {
        var levelParam = RevitCompat.Lookup(e, "level")
            ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
            ?? e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
        if (levelParam is null || levelParam.StorageType != StorageType.ElementId) return null;
        var id = levelParam.AsElementId();
        return id is null || id == ElementId.InvalidElementId ? null : RevitCompat.IdValue(id);
    }

    private static bool BelongsToLevel(Document doc, Element e, string levelName)
    {
        var id = GetLevelId(e);
        if (id is null) return false;
        var level = doc.GetElement(RevitCompat.MakeId(id.Value)) as Level;
        return level is not null && string.Equals(level.Name, levelName, StringComparison.OrdinalIgnoreCase);
    }

    private static T? SafeGet<T>(Func<T?> fn)
    {
        try { return fn(); }
        catch { return default; }
    }
}
