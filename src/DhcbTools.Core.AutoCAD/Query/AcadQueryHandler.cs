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
        "drawing_info, layers, blocks, inserts, entities, text, xrefs, layouts, stats, entity_geometry, attributes_of, block_contents, snapshot";

    // Resolve known managed types before walking Model Space: reject unrelated native classes
    // without materializing managed Entity wrappers for every line in a large drawing.
    private static readonly Dictionary<string, Type> EntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Line"] = typeof(Line), ["Arc"] = typeof(Arc), ["Circle"] = typeof(Circle),
        ["Polyline"] = typeof(Polyline), ["DBText"] = typeof(DBText), ["MText"] = typeof(MText),
        ["BlockReference"] = typeof(BlockReference), ["Ray"] = typeof(Ray), ["Xline"] = typeof(Xline),
        ["Spline"] = typeof(Spline), ["Ellipse"] = typeof(Ellipse), ["DBPoint"] = typeof(DBPoint),
    };

    public static object Handle(Database db, QueryRequest req)
    {
        if (req == null || req.Params == null || string.IsNullOrWhiteSpace(req.Query))
            return new { error = "query và params phải là object/chuỗi hợp lệ, không được null hoặc rỗng." };
        if (req.Params.Handles?.Count > QueryPage.MaxLimit)
            return new { error = "handles tối đa 10000 phần tử; chia nhỏ danh sách để truy vấn/chọn." };
        if (req.Params.Limit < 0 || req.Params.Limit > QueryPage.MaxLimit || req.Params.Offset < 0)
            return new { error = "limit phải từ 0 đến 10000; offset phải không âm. 0 dùng giới hạn mặc định 2000." };
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
            "BLOCK_CONTENTS"  => GetBlockContents(db, req.Params),
            // Tầng Core của snapshot (accoreconsole): plot PNG trạng thái hiện tại, hoặc ảnh xem trước lưu trong DWG.
            // Vỏ AutoCAD chặn trước để render sống khung nhìn.
            "SNAPSHOT"        => CoreSnapshot(db, req.Params),
            _ => new { error = $"Query không xác định: \"{req.Query}\". Hợp lệ: {ValidQueries}." }
        };
    }

    /// <summary>
    /// Snapshot khi không có GUI: mặc định plot PNG (đúng trạng thái hiện tại, khoanh vùng được), rơi về ảnh xem
    /// trước trong DWG nếu plot hỏng; <c>source="thumbnail"</c> thì lấy thẳng ảnh xem trước.
    /// </summary>
    private static object CoreSnapshot(Database db, AcadQueryParams p)
    {
        if (string.Equals(p.Source, "thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            return AcadSnapshot.Thumbnail(db);
        }

        var region = AcadPlotSnapshot.Region(db, p, out var error, out var note);
        if (error != null)
        {
            return new { error };
        }

        var plotted = AcadPlotSnapshot.Render(db, region, Math.Max(200, Math.Min(p.ImageWidth, 4000)), note);
        if (plotted.GetType().GetProperty("error") is null)
        {
            return plotted;
        }

        return new { fallbackFrom = "plot", reasons = new[] { plotted }, result = AcadSnapshot.Thumbnail(db) };
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
        var page = new QueryPage(p.Limit, p.Offset);
        foreach (ObjectId id in layerTable)
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (p.LayerContains is { Length: > 0 } &&
                l.Name.IndexOf(p.LayerContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!page.IncludeMatch())
            {
                if (page.HasMore) break;
                continue;
            }

            list.Add(new
            {
                name        = l.Name,
                isOff       = l.IsOff,
                isFrozen    = l.IsFrozen,
                isLocked    = l.IsLocked,
                isPlottable = l.IsPlottable,
                colorIndex  = l.Color.IsByAci ? l.Color.ColorIndex : (int?)null,
                colorRgb    = l.Color.IsByAci ? null : "#" + (l.Color.ColorValue.ToArgb() & 0xFFFFFF).ToString("X6", System.Globalization.CultureInfo.InvariantCulture),
                linetype    = GetLinetypeName(tr, db, l.LinetypeObjectId),
                lineweight  = l.LineWeight.ToString(),
                description = l.Description,
            });
        }

        tr.Abort();
        return new { count = list.Count, limit = page.Limit, offset = page.Offset, hasMore = page.HasMore, nextOffset = page.NextOffset, layers = list };
    }

    // ──────────────────────────────────────────────────────────────
    // blocks (definitions)
    // ──────────────────────────────────────────────────────────────
    private static object GetBlocks(Database db, AcadQueryParams p)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        var list = new List<object>();
        var page = new QueryPage(p.Limit, p.Offset);
        foreach (ObjectId id in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (btr.IsAnonymous || btr.IsLayout || btr.IsFromExternalReference) continue;

            if (p.BlockNameContains is { Length: > 0 } &&
                btr.Name.IndexOf(p.BlockNameContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!page.IncludeMatch())
            {
                if (page.HasMore) break;
                continue;
            }

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
        return new { count = list.Count, limit = page.Limit, offset = page.Offset, hasMore = page.HasMore, nextOffset = page.NextOffset, blocks = list };
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
        var page = new QueryPage(p.Limit, p.Offset);
        var insertClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference));
        foreach (ObjectId entId in ms)
        {
            if (!entId.ObjectClass.IsDerivedFrom(insertClass)) continue;
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

            if (!page.IncludeMatch())
            {
                if (page.HasMore) break;
                continue;
            }

            var attrs = new Dictionary<string, string?>();
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                attrs[att.Tag] = att.TextString;
            }

            list.Add(new
            {
                // handle: định danh bền, dùng được cho show_entities/entity_geometry/selection; objectId chỉ sống trong phiên.
                handle    = HandleText.ToText(entId.Handle.Value),
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
        return new { count = list.Count, limit = page.Limit, offset = page.Offset, hasMore = page.HasMore, nextOffset = page.NextOffset, inserts = list };
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
        var page = new QueryPage(p.Limit, p.Offset);
        var entityType = string.Equals(p.EntityType, "Text", StringComparison.OrdinalIgnoreCase) ? "DBText" : p.EntityType;
        EntityTypes.TryGetValue(entityType ?? string.Empty, out var managedType);
        var nativeClass = managedType == null ? null : Autodesk.AutoCAD.Runtime.RXObject.GetClass(managedType);
        foreach (ObjectId entId in ms)
        {
            if (nativeClass != null && !entId.ObjectClass.IsDerivedFrom(nativeClass)) continue;
            var entity = tr.GetObject(entId, OpenMode.ForRead) as Entity;
            if (entity is null) continue;

            var typeName = entity.GetType().Name;
            if (entityType is { Length: > 0 } &&
                !string.Equals(typeName, entityType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (p.LayerContains is { Length: > 0 } &&
                entity.Layer.IndexOf(p.LayerContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!page.IncludeMatch())
            {
                if (page.HasMore) break;
                continue;
            }

            var row = new Dictionary<string, object?>
            {
                ["handle"]   = HandleText.ToText(entId.Handle.Value),
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
                case Dimension dim:
                    AddDimensionDetails(row, dim);
                    break;
            }

            list.Add(row);
        }

        tr.Abort();
        return new { count = list.Count, limit = page.Limit, offset = page.Offset, hasMore = page.HasMore, nextOffset = page.NextOffset, entities = list };
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
        var page = new QueryPage(p.Limit, p.Offset);
        var textClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(DBText));
        var mtextClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(MText));
        var leaderClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(MLeader));
        foreach (ObjectId entId in ms)
        {
            var cls = entId.ObjectClass;
            if (!cls.IsDerivedFrom(textClass) && !cls.IsDerivedFrom(mtextClass) && !cls.IsDerivedFrom(leaderClass)) continue;
            if (tr.GetObject(entId, OpenMode.ForRead) is not Entity entity) continue;
            if ((p.TextLayer is { Length: > 0 } && entity.Layer.IndexOf(p.TextLayer, StringComparison.OrdinalIgnoreCase) < 0)
                || (p.LayerContains is { Length: > 0 } && entity.Layer.IndexOf(p.LayerContains, StringComparison.OrdinalIgnoreCase) < 0))
                continue;
            // Tag ống/thiết bị hay ghi bằng MLeader: chữ nằm trong MText của leader. MLeader.MText trả bản sao
            // ngoài database — đọc xong huỷ ngay trên luồng này (để GC huỷ trên luồng finalizer làm sập AutoCAD).
            LeaderText? leaderText = null;
            if (entity is MLeader leader)
            {
                if (leader.ContentType != ContentType.MTextContent) continue;
                using var copy = leader.MText;
                leaderText = new LeaderText(copy.Contents, copy.Text, copy.Location, copy.Width);
            }
            var display = leaderText?.Display ?? (entity is MText mtDisplay ? mtDisplay.Text : ((DBText)entity).TextString);
            if (p.TextContains is { Length: > 0 } && display.IndexOf(p.TextContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!page.IncludeMatch())
            {
                if (page.HasMore) break;
                continue;
            }
            var handle = HandleText.ToText(entId.Handle.Value);
            switch (entity)
            {
                case DBText t:
                    list.Add(new { handle, type = "DBText", layer = t.Layer, text = t.TextString, displayText = display,
                                   x = t.Position.X, y = t.Position.Y, z = t.Position.Z, height = t.Height });
                    break;
                case MText mt:
                    list.Add(new { handle, type = "MText", layer = mt.Layer, text = mt.Contents, displayText = display,
                                   x = mt.Location.X, y = mt.Location.Y, z = mt.Location.Z, width = mt.Width });
                    break;
                case MLeader when leaderText != null:
                    list.Add(new { handle, type = "MLeader", layer = entity.Layer, text = leaderText.Contents, displayText = display,
                                   x = leaderText.Location.X, y = leaderText.Location.Y, z = leaderText.Location.Z, width = leaderText.Width });
                    break;
            }
        }

        tr.Abort();
        return new { count = list.Count, limit = page.Limit, offset = page.Offset, hasMore = page.HasMore, nextOffset = page.NextOffset, texts = list };
    }

    /// <summary>Chữ đã đọc từ MText của MLeader (giữ giá trị, không giữ đối tượng AutoCAD).</summary>
    private sealed record LeaderText(string Contents, string Display, Point3d Location, double Width);

    // ──────────────────────────────────────────────────────────────
    // block_contents — chữ / line / polyline BÊN TRONG block reference, quy về toạ độ thật
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Nền kiến trúc thường được chèn hoặc bind thành block: tên phòng, tường, trục nằm trong định nghĩa block nên
    /// <c>text</c>/<c>entities</c> (chỉ quét thẳng Model Space) không thấy. Truy vấn này đi đệ quy vào các block
    /// reference (tối đa 8 cấp), nhân ma trận chèn để trả toạ độ thật, lọc theo layer (<c>layerContains</c>) và loại
    /// (<c>entityType</c>: DBText, MText, Attribute, Text = cả ba loại chữ, Line, Polyline). Attribute của mọi block lồng
    /// (kể cả block gốc) được trả kèm <c>tag</c>; định nghĩa attribute (chữ mẫu) bị bỏ qua. Không chỉ định <c>handles</c> thì duyệt mọi
    /// block reference trong Model Space. Block của xref (chưa bind) không đi vào: đó là file của người khác.
    /// </summary>
    private static object GetBlockContents(Database db, AcadQueryParams p)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var page = new QueryPage(p.Limit, p.Offset);
        var list = new List<object>();
        var notFound = new List<string>();
        var roots = new List<BlockReference>();

        if (p.Handles is { Count: > 0 })
        {
            foreach (var text in p.Handles)
            {
                if (!HandleText.TryParse(text, out var raw))
                {
                    notFound.Add(text + " (không phải handle hex)");
                    continue;
                }

                try
                {
                    var id = db.GetObjectId(false, new Handle(raw), 0);
                    if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is BlockReference br) roots.Add(br);
                    else notFound.Add(text + " (không phải block reference)");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    notFound.Add(text + " (không có trong bản vẽ)");
                }
            }
        }
        else
        {
            var refClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference));
            foreach (ObjectId id in ms)
                if (id.ObjectClass.IsDerivedFrom(refClass) && tr.GetObject(id, OpenMode.ForRead) is BlockReference br) roots.Add(br);
        }

        var stop = false;
        bool Wanted(Entity ent, string type)
        {
            if (p.EntityType is { Length: > 0 } && !string.Equals(p.EntityType, type, StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(p.EntityType, "Text", StringComparison.OrdinalIgnoreCase) && (type is "DBText" or "MText" or "MLeader" or "Attribute")))
                return false;
            if (p.LayerContains is { Length: > 0 } && ent.Layer.IndexOf(p.LayerContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (page.IncludeMatch()) return true;
            if (page.HasMore) stop = true;
            return false;
        }

        // parentXf: ma trận đưa toạ độ của KHÔNG GIAN CHỨA br về toạ độ thật. Attribute của br nằm ở chính không gian đó
        // (không nhân BlockTransform của br) — tên phòng của nền kiến trúc là attribute của block tag lồng bên trong.
        void Walk(BlockReference br, Matrix3d parentXf, string root, int depth)
        {
            if (stop || depth > 8) return;
            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (stop) return;
                if (tr.GetObject(attId, OpenMode.ForRead) is not AttributeReference att || att.Invisible) continue;
                if (!Wanted(att, "Attribute")) continue;
                var text = att.IsMTextAttribute ? att.MTextAttribute.Text : att.TextString;
                list.Add(new { root, type = "Attribute", layer = att.Layer, tag = att.Tag, text, position = Pt(att.Position.TransformBy(parentXf)), height = att.Height });
            }

            var xf = parentXf * br.BlockTransform;
            if (tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is not BlockTableRecord btr || btr.IsFromExternalReference) return;
            foreach (ObjectId id in btr)
            {
                if (stop) return;
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                if (ent is BlockReference inner)
                {
                    Walk(inner, xf, root, depth + 1);
                    continue;
                }

                // AttributeDefinition là DBText nhưng chỉ là mẫu ("Ten phong") — giá trị thật nằm ở AttributeReference.
                if (ent is AttributeDefinition) continue;

                var type = ent switch
                {
                    DBText => "DBText", MText => "MText", Line => "Line", Polyline => "Polyline",
                    MLeader { ContentType: ContentType.MTextContent } => "MLeader",
                    _ => null,
                };
                if (type == null || !Wanted(ent, type)) continue;

                switch (ent)
                {
                    case DBText t:
                        list.Add(new { root, type, layer = t.Layer, text = t.TextString, position = Pt(t.Position.TransformBy(xf)), height = t.Height });
                        break;
                    case MText mt:
                        list.Add(new { root, type, layer = mt.Layer, text = mt.Text, position = Pt(mt.Location.TransformBy(xf)), height = mt.TextHeight });
                        break;
                    case MLeader ml:
                        using (var lt = ml.MText)
                            list.Add(new { root, type, layer = ml.Layer, text = lt.Text, position = Pt(lt.Location.TransformBy(xf)), height = lt.TextHeight });
                        break;
                    case Line ln:
                        list.Add(new { root, type, layer = ln.Layer, start = Pt(ln.StartPoint.TransformBy(xf)), end = Pt(ln.EndPoint.TransformBy(xf)) });
                        break;
                    case Polyline pl:
                        var vertices = new List<object>(pl.NumberOfVertices);
                        for (var i = 0; i < pl.NumberOfVertices; i++) vertices.Add(Pt(pl.GetPoint3dAt(i).TransformBy(xf)));
                        list.Add(new { root, type, layer = pl.Layer, closed = pl.Closed, vertices });
                        break;
                }
            }
        }

        foreach (var br in roots)
        {
            if (stop) break;
            Walk(br, Matrix3d.Identity, HandleText.ToText(br.Handle.Value), 1);
        }

        tr.Abort();
        return new { count = list.Count, limit = page.Limit, offset = page.Offset, hasMore = page.HasMore, nextOffset = page.NextOffset, items = list, notFound };
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

            // Đối tượng đã xoá vẫn tra ra ObjectId (còn trong database tới khi lưu và mở lại) và GetObject ném eWasErased:
            // một handle agent nhớ từ lượt trước từng làm sập cả truy vấn thay vì chỉ báo "không có".
            if (id.IsNull || id.IsErased)
            {
                notFound.Add(text + " (không có trong bản vẽ)");
                continue;
            }

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
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
            case Dimension dim:
                AddDimensionDetails(row, dim);
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
    /// Kích thước (DIM): tên dim style (để agent đặt DIM mới cùng style với DIM có sẵn), cỡ chữ riêng, chữ ghi đè và
    /// số đo; với DIM thẳng hàng/xoay thêm hai điểm gốc đường gióng và điểm đặt đường kích thước.
    /// </summary>
    private static void AddDimensionDetails(Dictionary<string, object?> row, Dimension dim)
    {
        row["dimStyle"]     = dim.DimensionStyleName;
        row["dimText"]      = dim.DimensionText;
        row["dimtxt"]       = dim.Dimtxt;
        row["dimscale"]     = dim.Dimscale;
        row["textPosition"] = Pt(dim.TextPosition);
        switch (dim)
        {
            case AlignedDimension ad:
                row["xLine1Point"]   = Pt(ad.XLine1Point);
                row["xLine2Point"]   = Pt(ad.XLine2Point);
                row["dimLinePoint"]  = Pt(ad.DimLinePoint);
                row["measurement"]   = Math.Round(ad.Measurement, 6);
                break;
            case RotatedDimension rd:
                row["xLine1Point"]   = Pt(rd.XLine1Point);
                row["xLine2Point"]   = Pt(rd.XLine2Point);
                row["dimLinePoint"]  = Pt(rd.DimLinePoint);
                row["rotationDeg"]   = Math.Round(rd.Rotation * 180 / Math.PI, 4);
                row["measurement"]   = Math.Round(rd.Measurement, 6);
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

        // Block động: insert đã đổi tham số trỏ tới bản anonymous (*U12) — GetBlockReferenceIds của định nghĩa
        // gốc không thấy chúng, đếm thiếu và mẫu rỗng.
        var referenceIds = new List<ObjectId>();
        foreach (ObjectId refId in btr.GetBlockReferenceIds(true, false)) referenceIds.Add(refId);
        if (btr.IsDynamicBlock)
        {
            foreach (ObjectId anonId in btr.GetAnonymousBlockIds())
            {
                if (tr.GetObject(anonId, OpenMode.ForRead) is BlockTableRecord anon)
                {
                    foreach (ObjectId refId in anon.GetBlockReferenceIds(true, false)) referenceIds.Add(refId);
                }
            }
        }

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
                Handle       = layout.Handle.Value,
                Viewports    = layout.ModelType ? new List<object>() : LayoutViewports(tr, layout),
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
                handle       = Shared.Logic.Cad.HandleText.ToText(l.Handle),
                viewports    = l.Viewports,
            })
            .ToList();

        return new { count = sorted.Count, layouts = sorted };
    }

    /// <summary>
    /// Viewport trong paper space của một layout (kể cả viewport tổng — <c>number</c> 1 khi layout đã kích hoạt, chưa
    /// kích hoạt thì là cái đầu tiên): tâm/kích thước trên giấy, tâm nhìn Model Space, tỉ lệ — để kiểm lại khung do
    /// <c>LayoutViewport</c> tạo mà không cần mở tab.
    /// </summary>
    private static List<object> LayoutViewports(Transaction tr, Layout layout)
    {
        var list = new List<object>();
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Viewport vp) continue;
            list.Add(new
            {
                handle      = AcadHelpers.HandleOf(id),
                number      = vp.Number,
                center      = new[] { vp.CenterPoint.X, vp.CenterPoint.Y },
                width       = vp.Width,
                height      = vp.Height,
                viewTarget  = new[] { vp.ViewTarget.X, vp.ViewTarget.Y, vp.ViewTarget.Z },
                viewCenter  = new[] { vp.ViewCenter.X, vp.ViewCenter.Y },
                viewHeight  = vp.ViewHeight,
                customScale = vp.CustomScale,
                twistAngle  = vp.TwistAngle,
                locked      = vp.Locked,
                on          = vp.On,
            });
        }

        return list;
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
        public long Handle;
        public List<object> Viewports;
    }
}
