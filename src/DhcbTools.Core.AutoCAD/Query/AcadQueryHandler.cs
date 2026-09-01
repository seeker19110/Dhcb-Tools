using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DhcbTools.Core.AutoCAD.Query;

/// <summary>
/// Xử lý tất cả truy vấn đọc (POST /query) cho AutoCAD.
/// Không cần ExternalEvent — toàn bộ chạy trong ExecuteInCommandContextAsync (main thread).
/// Không ghi — chỉ đọc; mở transaction ForRead rồi Abort.
/// </summary>
public static class AcadQueryHandler
{
    public static object Handle(Database db, QueryRequest req)
    {
        return req.Query.ToUpperInvariant() switch
        {
            "DRAWING_INFO" => GetDrawingInfo(db),
            "LAYERS"       => GetLayers(db, req.Params),
            "BLOCKS"       => GetBlocks(db, req.Params),
            "INSERTS"      => GetInserts(db, req.Params),
            "ENTITIES"     => GetEntities(db, req.Params),
            "TEXT"         => GetText(db, req.Params),
            "XREFS"        => GetXrefs(db),
            "LAYOUTS"      => GetLayouts(db),
            "STATS"        => GetStats(db),
            _ => new { error = $"Query không xác định: \"{req.Query}\". " +
                 "Hợp lệ: drawing_info, layers, blocks, inserts, entities, text, xrefs, layouts, stats." }
        };
    }

    // ──────────────────────────────────────────────────────────────
    // drawing_info
    // ──────────────────────────────────────────────────────────────
    private static object GetDrawingInfo(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();

        var summaryInfo = db.SummaryInfo;
        // AutoCAD API: db.Extmin / db.Extmax (không có db.Extents)
        var extMin = db.Extmin;
        var extMax = db.Extmax;

        string? currentLayer = null;
        if (db.Clayer != ObjectId.Null)
            currentLayer = GetLayerName(tr, db.Clayer);

        var result = new
        {
            filename         = db.Filename,
            originalFileName = db.OriginalFileName,
            dwgVersion       = db.LastSavedAsVersion.ToString(),
            unitsValue       = (int)db.Insunits,
            unitsName        = db.Insunits.ToString(),
            limitsMin        = new { x = db.Limmin.X, y = db.Limmin.Y },
            limitsMax        = new { x = db.Limmax.X, y = db.Limmax.Y },
            extentsMin       = new { x = extMin.X, y = extMin.Y, z = extMin.Z },
            extentsMax       = new { x = extMax.X, y = extMax.Y, z = extMax.Z },
            currentLayer,
            title            = SafeGet(() => summaryInfo.Title),
            subject          = SafeGet(() => summaryInfo.Subject),
            author           = SafeGet(() => summaryInfo.Author),
            comments         = SafeGet(() => summaryInfo.Comments),
        };

        tr.Abort();
        return result;
    }

    // ──────────────────────────────────────────────────────────────
    // layers
    // ──────────────────────────────────────────────────────────────
    private static object GetLayers(Database db, AcadQueryParams p)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

        var list = new List<object>();
        foreach (ObjectId id in layerTable)
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (p.LayerContains is { Length: > 0 } &&
                l.Name.IndexOf(p.LayerContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            list.Add(new
            {
                name        = l.Name,
                isOff       = l.IsOff,
                isFrozen    = l.IsFrozen,
                isLocked    = l.IsLocked,
                isPlottable = l.IsPlottable,
                colorIndex  = l.Color.IsByAci ? l.Color.ColorIndex : (int?)null,
                colorRgb    = l.Color.IsByAci ? null : l.Color.ColorValue.ToString(),
                linetype    = GetLinetypeName(tr, db, l.LinetypeObjectId),
                lineweight  = l.LineWeight.ToString(),
                description = l.Description,
            });
        }

        tr.Abort();
        if (p.Limit > 0) list = list.Take(p.Limit).ToList();
        return new { count = list.Count, layers = list };
    }

    // ──────────────────────────────────────────────────────────────
    // blocks (definitions)
    // ──────────────────────────────────────────────────────────────
    private static object GetBlocks(Database db, AcadQueryParams p)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        var list = new List<object>();
        foreach (ObjectId id in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (btr.IsAnonymous || btr.IsLayout || btr.IsFromExternalReference) continue;

            if (p.BlockNameContains is { Length: > 0 } &&
                btr.Name.IndexOf(p.BlockNameContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            // Đếm số entity trong block definition (net48: enumerate ObjectIds)
            var entityCount = 0;
            foreach (ObjectId _ in btr) entityCount++;

            list.Add(new
            {
                name          = btr.Name,
                entityCount,
                hasAttributes = btr.HasAttributeDefinitions,
                isDynamic     = btr.IsDynamicBlock,
                origin        = new { x = btr.Origin.X, y = btr.Origin.Y, z = btr.Origin.Z },
            });
        }

        tr.Abort();
        if (p.Limit > 0) list = list.Take(p.Limit).ToList();
        return new { count = list.Count, blocks = list };
    }

    // ──────────────────────────────────────────────────────────────
    // inserts (BlockReference instances)
    // ──────────────────────────────────────────────────────────────
    private static object GetInserts(Database db, AcadQueryParams p)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        var list = new List<object>();
        foreach (ObjectId entId in ms)
        {
            if (tr.GetObject(entId, OpenMode.ForRead) is not BlockReference br) continue;

            var blockName = br.IsDynamicBlock
                ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                : br.Name;

            if (p.BlockName is { Length: > 0 } &&
                !string.Equals(blockName, p.BlockName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (p.LayerContains is { Length: > 0 } &&
                br.Layer.IndexOf(p.LayerContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var attrs = new Dictionary<string, string?>();
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                attrs[att.Tag] = att.TextString;
            }

            list.Add(new
            {
                objectId  = entId.ToString(),
                blockName,
                layer     = br.Layer,
                x         = br.Position.X,
                y         = br.Position.Y,
                z         = br.Position.Z,
                rotation  = Math.Round(br.Rotation * 180.0 / Math.PI, 4),
                scaleX    = br.ScaleFactors.X,
                scaleY    = br.ScaleFactors.Y,
                attributes = attrs.Count > 0 ? attrs : null,
            });
        }

        tr.Abort();
        if (p.Limit > 0) list = list.Take(p.Limit).ToList();
        return new { count = list.Count, inserts = list };
    }

    // ──────────────────────────────────────────────────────────────
    // entities
    // ──────────────────────────────────────────────────────────────
    private static object GetEntities(Database db, AcadQueryParams p)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        var list = new List<object>();
        foreach (ObjectId entId in ms)
        {
            var entity = tr.GetObject(entId, OpenMode.ForRead) as Entity;
            if (entity is null) continue;

            var typeName = entity.GetType().Name;
            if (p.EntityType is { Length: > 0 } &&
                !string.Equals(typeName, p.EntityType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (p.LayerContains is { Length: > 0 } &&
                entity.Layer.IndexOf(p.LayerContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var row = new Dictionary<string, object?>
            {
                ["objectId"] = entId.ToString(),
                ["type"]     = typeName,
                ["layer"]    = entity.Layer,
                ["color"]    = entity.Color.IsByAci
                               ? entity.Color.ColorIndex.ToString(CultureInfo.InvariantCulture)
                               : entity.Color.ColorValue.ToString(),
            };

            switch (entity)
            {
                case Line ln:
                    row["start"]  = Pt(ln.StartPoint);
                    row["end"]    = Pt(ln.EndPoint);
                    row["length"] = Math.Round(ln.Length, 6);
                    break;
                case Arc arc:
                    row["center"]        = Pt(arc.Center);
                    row["radius"]        = arc.Radius;
                    row["startAngleDeg"] = Math.Round(arc.StartAngle * 180 / Math.PI, 4);
                    row["endAngleDeg"]   = Math.Round(arc.EndAngle   * 180 / Math.PI, 4);
                    break;
                case Circle c:
                    row["center"] = Pt(c.Center);
                    row["radius"] = c.Radius;
                    break;
                case Polyline pl:
                    row["vertexCount"] = pl.NumberOfVertices;
                    row["isClosed"]    = pl.Closed;
                    row["length"]      = Math.Round(pl.Length, 6);
                    break;
                case DBText t:
                    row["text"]     = t.TextString;
                    row["position"] = Pt(t.Position);
                    row["height"]   = t.Height;
                    break;
                case MText mt:
                    row["text"]     = mt.Contents;
                    row["location"] = Pt(mt.Location);
                    row["width"]    = mt.Width;
                    break;
            }

            list.Add(row);
        }

        tr.Abort();
        if (p.Limit > 0) list = list.Take(p.Limit).ToList();
        return new { count = list.Count, entities = list };
    }

    // ──────────────────────────────────────────────────────────────
    // text
    // ──────────────────────────────────────────────────────────────
    private static object GetText(Database db, AcadQueryParams p)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        var list = new List<object>();
        foreach (ObjectId entId in ms)
        {
            var entity = tr.GetObject(entId, OpenMode.ForRead);

            if (p.TextLayer is { Length: > 0 } && entity is Entity e &&
                e.Layer.IndexOf(p.TextLayer, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            switch (entity)
            {
                case DBText t:
                    list.Add(new { type = "DBText", layer = t.Layer, text = t.TextString,
                                   x = t.Position.X, y = t.Position.Y, height = t.Height });
                    break;
                case MText mt:
                    list.Add(new { type = "MText", layer = mt.Layer, text = mt.Contents,
                                   x = mt.Location.X, y = mt.Location.Y, width = mt.Width });
                    break;
            }
        }

        tr.Abort();
        if (p.Limit > 0) list = list.Take(p.Limit).ToList();
        return new { count = list.Count, texts = list };
    }

    // ──────────────────────────────────────────────────────────────
    // xrefs
    // ──────────────────────────────────────────────────────────────
    private static object GetXrefs(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        var xrefs = new List<object>();
        foreach (ObjectId id in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!btr.IsFromExternalReference) continue;

            // XrefStatus: Resolved / Unresolved / FileNotFound / Unloaded / NotAnXref / v.v.
            var xrefStatus = btr.XrefStatus;
            xrefs.Add(new
            {
                name         = btr.Name,
                pathName     = btr.PathName,
                xrefStatus   = xrefStatus.ToString(),
                isResolved   = xrefStatus == XrefStatus.Resolved,
            });
        }

        tr.Abort();
        return new { count = xrefs.Count, xrefs };
    }

    // ──────────────────────────────────────────────────────────────
    // layouts
    // ──────────────────────────────────────────────────────────────
    private static object GetLayouts(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var dbDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        // Dùng typed struct để tránh dynamic (CS0656 trên net48 thiếu Microsoft.CSharp)
        var layouts = new List<LayoutInfo>();
        foreach (DBDictionaryEntry entry in dbDict)
        {
            if (tr.GetObject(entry.Value, OpenMode.ForRead) is not Layout layout) continue;
            layouts.Add(new LayoutInfo
            {
                Name         = layout.LayoutName,
                TabOrder     = layout.TabOrder,
                IsModelSpace = layout.ModelType,
                PaperSizeX   = layout.PlotPaperSize.X,
                PaperSizeY   = layout.PlotPaperSize.Y,
            });
        }

        tr.Abort();

        var sorted = layouts.OrderBy(l => l.TabOrder)
            .Select(l => new
            {
                name         = l.Name,
                tabOrder     = l.TabOrder,
                isModelSpace = l.IsModelSpace,
                plotPaperSize = new { x = l.PaperSizeX, y = l.PaperSizeY },
            })
            .ToList();

        return new { count = sorted.Count, layouts = sorted };
    }

    // ──────────────────────────────────────────────────────────────
    // stats
    // ──────────────────────────────────────────────────────────────
    private static object GetStats(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        var byType  = new Dictionary<string, int>();
        var byLayer = new Dictionary<string, int>();
        var total   = 0;

        foreach (ObjectId entId in ms)
        {
            if (tr.GetObject(entId, OpenMode.ForRead) is not Entity entity) continue;
            total++;

            var typeName = entity.GetType().Name;
            // net48: Dictionary<K,V>.GetValueOrDefault() не існує — використовуємо TryGetValue
            byType[typeName] = (byType.TryGetValue(typeName, out var tc) ? tc : 0) + 1;
            byLayer[entity.Layer] = (byLayer.TryGetValue(entity.Layer, out var lc) ? lc : 0) + 1;
        }

        tr.Abort();

        return new
        {
            totalEntities = total,
            byType  = byType .OrderByDescending(kv => kv.Value)
                              .Select(kv => new { type  = kv.Key, count = kv.Value }).ToList(),
            byLayer = byLayer.OrderByDescending(kv => kv.Value)
                              .Select(kv => new { layer = kv.Key, count = kv.Value }).ToList(),
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────
    private static object Pt(Point3d p) => new { x = p.X, y = p.Y, z = p.Z };

    private static string GetLayerName(Transaction tr, ObjectId layerId)
    {
        try { return ((LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead)).Name; }
        catch { return "?"; }
    }

    private static string GetLinetypeName(Transaction tr, Database db, ObjectId ltId)
    {
        if (ltId == db.ContinuousLinetype) return "Continuous";
        try { return ((LinetypeTableRecord)tr.GetObject(ltId, OpenMode.ForRead)).Name; }
        catch { return "Continuous"; }
    }

    private static T? SafeGet<T>(Func<T?> fn)
    {
        try { return fn(); }
        catch { return default; }
    }

    // Typed helper để tránh dynamic (net48 cần ref Microsoft.CSharp cho dynamic)
    private struct LayoutInfo
    {
        public string Name;
        public int TabOrder;
        public bool IsModelSpace;
        public double PaperSizeX;
        public double PaperSizeY;
    }
}
