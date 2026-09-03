using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.Query;

/// <summary>
/// Xử lý tất cả truy vấn đọc (POST /query) cho AutoCAD.
/// Không cần ExternalEvent — toàn bộ chạy trong ExecuteInCommandContextAsync (main thread).
/// Không ghi — chỉ đọc; mở transaction ForRead rồi Abort.
/// </summary>
public static class AcadQueryHandler
{
    /// <summary>
    /// Danh sách truy vấn hợp lệ, để **một chỗ duy nhất**: agent gõ sai tên thì đọc đúng danh sách này.
    /// Trước đây câu báo lỗi là chuỗi rời, thêm truy vấn mới mà quên sửa nó thì agent không có cách nào
    /// biết truy vấn đó tồn tại (<c>QueryCatalogTests</c> chốt cho khỏi lệch).
    /// </summary>
    public const string ValidQueries =
        "drawing_info, layers, blocks, inserts, entities, text, xrefs, layouts, stats, entity_geometry, attributes_of";

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
            // Giai đoạn 10.1 — đủ để agent nhìn và kiểm được đúng đối tượng vừa đụng tới.
            "ENTITY_GEOMETRY" => GetEntityGeometry(db, req.Params),
            "ATTRIBUTES_OF"   => GetAttributesOf(db, req.Params),
            _ => new { error = $"Query không xác định: \"{req.Query}\". Hợp lệ: {ValidQueries}." }
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
    // entity_geometry (giai đoạn 10.1) — hộp bao + chi tiết theo loại, tra bằng handle
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Hình học của những entity được chỉ đích danh. Đối xứng với <c>element_geometry</c> bên Revit:
    /// agent chạy lệnh xong, cầm handle trả về rồi hỏi lại đúng những cái đó thay vì quét cả bản vẽ.
    /// <para>
    /// Handle sai định dạng hoặc không có trong bản vẽ đều được NÓI RA trong <c>notFound</c>, không im
    /// lặng trả rỗng — im lặng ở đây nghĩa là agent tưởng lệnh không đổi gì.
    /// </para>
    /// </summary>
    private static object GetEntityGeometry(Database db, AcadQueryParams p)
    {
        if (p.Handles is null || p.Handles.Count == 0)
        {
            return new { error = "entity_geometry cần \"handles\" — danh sách handle (hex) của entity cần xem." };
        }

        using var tr = db.TransactionManager.StartTransaction();
        var found = new List<object>();
        var notFound = new List<string>();

        foreach (var text in p.Handles)
        {
            if (!HandleText.TryParse(text, out var raw))
            {
                notFound.Add(text + " (không phải handle hex)");
                continue;
            }

            ObjectId id;
            try
            {
                id = db.GetObjectId(false, new Handle(raw), 0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                notFound.Add(text + " (không có trong bản vẽ)");
                continue;
            }

            if (id.IsNull || tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
            {
                notFound.Add(text + " (không phải entity)");
                continue;
            }

            var row = new Dictionary<string, object?>
            {
                ["handle"]   = HandleText.ToText(raw),
                ["objectId"] = id.ToString(),
                ["type"]     = entity.GetType().Name,
                ["layer"]    = entity.Layer,
                ["linetype"] = entity.Linetype,
            };

            // Entity suy biến (text rỗng, đường dài 0) không có extents — hỏi là ném.
            try
            {
                var ext = entity.GeometricExtents;
                row["extentsMin"] = Pt(ext.MinPoint);
                row["extentsMax"] = Pt(ext.MaxPoint);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                row["extentsMin"] = null;
                row["extentsMax"] = null;
            }

            AddTypeDetails(row, entity, tr);
            found.Add(row);
        }

        tr.Abort();
        return new { count = found.Count, entities = found, notFound };
    }

    /// <summary>Chi tiết theo loại entity — dùng chung cho <c>entities</c> và <c>entity_geometry</c>.</summary>
    private static void AddTypeDetails(Dictionary<string, object?> row, Entity entity, Transaction tr)
    {
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
            case BlockReference br:
                row["blockName"]   = BlockNameOf(br, tr);
                row["position"]    = Pt(br.Position);
                row["rotationDeg"] = Math.Round(br.Rotation * 180 / Math.PI, 4);
                row["scale"]       = new { x = br.ScaleFactors.X, y = br.ScaleFactors.Y, z = br.ScaleFactors.Z };
                row["attributes"]  = AttributeValues(br, tr);
                break;
        }
    }

    /// <summary>
    /// Tên block người dùng nhìn thấy. Block động: <c>Name</c> là tên bản sao vô danh (<c>*U12</c>),
    /// tên thật nằm ở <c>DynamicBlockTableRecordId</c> — lấy nhầm là agent lọc theo tên không ra gì.
    /// </summary>
    private static string BlockNameOf(BlockReference br, Transaction tr)
    {
        var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
        return tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr ? btr.Name : br.Name;
    }

    private static Dictionary<string, string> AttributeValues(BlockReference br, Transaction tr)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId attId in br.AttributeCollection)
        {
            if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference att)
            {
                values[att.Tag] = att.TextString;
            }
        }
        return values;
    }

    // ──────────────────────────────────────────────────────────────
    // attributes_of (giai đoạn 10.1) — thuộc tính của một block, kèm giá trị mẫu
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Định nghĩa thuộc tính của một block + giá trị mẫu từ vài insert có thật. Đối xứng với
    /// <c>parameters_of</c> bên Revit, và sinh ra vì cùng một lý do: agent phải biết TAG CÓ THẬT LÀ GÌ
    /// trước khi ghi, thay vì đoán tên rồi chạy một lệnh không đổi được gì mà vẫn báo thành công.
    /// </summary>
    private static object GetAttributesOf(Database db, AcadQueryParams p)
    {
        if (string.IsNullOrWhiteSpace(p.BlockName))
        {
            return new { error = "attributes_of cần \"blockName\"." };
        }

        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        if (!blockTable.Has(p.BlockName!))
        {
            var names = new List<string>();
            foreach (ObjectId anyId in blockTable)
            {
                if (tr.GetObject(anyId, OpenMode.ForRead) is BlockTableRecord b && !b.IsAnonymous && !b.IsLayout)
                {
                    names.Add(b.Name);
                }
            }
            tr.Abort();
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return new { error = "Không có block \"" + p.BlockName + "\".", availableBlocks = names };
        }

        var btr = (BlockTableRecord)tr.GetObject(blockTable[p.BlockName!], OpenMode.ForRead);

        var definitions = new List<object>();
        foreach (ObjectId id in btr)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition def) continue;
            definitions.Add(new
            {
                tag          = def.Tag,
                prompt       = def.Prompt,
                defaultValue = def.TextString,
                constant     = def.Constant,
                preset       = def.Preset,
                invisible    = def.Invisible,
                // Thuộc tính hằng KHÔNG ghi được qua AttributeReference — nói trước, còn hơn để lệnh
                // chạy xong báo thành công mà giá trị không đổi.
                writable     = !def.Constant,
            });
        }

        var referenceIds = btr.GetBlockReferenceIds(true, false);
        var samples = new List<object>();
        foreach (ObjectId refId in referenceIds)
        {
            if (samples.Count >= 3) break;
            if (tr.GetObject(refId, OpenMode.ForRead) is not BlockReference br) continue;
            samples.Add(new
            {
                handle = br.Handle.ToString(),
                layer  = br.Layer,
                values = AttributeValues(br, tr),
            });
        }

        var insertCount = referenceIds.Count;
        tr.Abort();
        return new
        {
            blockName      = p.BlockName,
            insertCount,
            attributeCount = definitions.Count,
            attributes     = definitions,
            samples,
        };
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
