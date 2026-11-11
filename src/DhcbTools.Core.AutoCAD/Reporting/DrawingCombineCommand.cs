using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// Gộp nhiều file DWG vào bản vẽ hiện tại.
/// <list type="bullet">
/// <item>Model Space mỗi file thành MỘT block mang tên file (như lệnh INSERT): xoá/di chuyển/EXPLODE cả bản dễ dàng,
/// không trộn entity của các file vào nhau. Layer/kiểu chữ… trùng tên dùng định nghĩa của bản vẽ hiện tại (giống INSERT).</item>
/// <item><c>Row</c> xếp các block thành hàng ngang bên phải nội dung đang có, cách nhau <c>gapMm</c>; <c>Original</c> giữ toạ độ gốc.</item>
/// <item>Layout của từng file (tuỳ chọn) được chép thành tab mới "&lt;file&gt; - &lt;layout&gt;": entity paper space, viewport
/// (dời theo đúng vị trí block để vẫn nhìn đúng vùng), thiết lập in nếu chép được.</item>
/// </list>
/// Xem trước (mặc định) chỉ đọc các file nguồn và báo kế hoạch; không ghi gì.
/// </summary>
public sealed class DrawingCombineCommand : ICoreCommand<DrawingCombineConfig>
{
    /// <summary>Trần số file mỗi lần — gộp vài trăm DWG vào một file là dấu hiệu nhầm thư mục.</summary>
    public const int MaxInputs = 200;

    private sealed record SourceInfo(string Path, string BlockName, CombineExtents? Extents, List<string> Layouts, int SharedLayers, double Scale);

    public string CommandName => "DrawingCombine";

    public CommandResult Execute(Database database, DrawingCombineConfig config)
    {
        var arrangement = DrawingCombinePlanner.NormalizeArrangement(config.Arrangement);
        if (arrangement is null)
        {
            return CommandResult.Fail($"arrangement \"{config.Arrangement}\" không hợp lệ. Dùng: {string.Join(", ", DrawingCombinePlanner.Arrangements)}.");
        }

        if (config.GapMm < 0 || double.IsNaN(config.GapMm) || double.IsInfinity(config.GapMm))
        {
            return CommandResult.Fail($"gapMm phải là số không âm: {config.GapMm}.");
        }

        var folder = config.InputFolder?.Trim();
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            return CommandResult.Fail($"Không tìm thấy thư mục inputFolder: \"{folder}\".");
        }

        var skipped = new List<string>();
        var folderFiles = string.IsNullOrEmpty(folder) ? Array.Empty<string>() : Directory.GetFiles(folder, "*.dwg", SearchOption.TopDirectoryOnly);
        var inputs = DrawingCombinePlanner.ResolveInputs(config.InputPaths, folder, folderFiles, database.Filename, skipped);
        inputs = inputs.Where(p =>
        {
            if (File.Exists(p)) return true;
            skipped.Add($"Bỏ qua \"{p}\": không tìm thấy file.");
            return false;
        }).ToList();

        if (inputs.Count == 0)
        {
            var fail = CommandResult.Fail("Không có file DWG nào để gộp — điền inputPaths hoặc inputFolder.");
            fail.Messages.AddRange(skipped);
            return fail;
        }

        if (inputs.Count > MaxInputs)
        {
            return CommandResult.Fail($"{inputs.Count} file vượt giới hạn {MaxInputs} file mỗi lần — chia thành nhiều lượt.");
        }

        // ── Lập kế hoạch: đọc từng file nguồn (chỉ đọc), đặt tên block/layout không trùng ──
        var targetUnits = (int)database.Insunits;
        var blockNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var layoutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CombineExtents? targetExtents;
        using (var tr = database.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead))
            {
                blockNames.Add(((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
            }

            foreach (DBDictionaryEntry entry in (DBDictionary)tr.GetObject(database.LayoutDictionaryId, OpenMode.ForRead))
            {
                layoutNames.Add(entry.Key);
            }

            foreach (ObjectId id in (LayerTable)tr.GetObject(database.LayerTableId, OpenMode.ForRead))
            {
                targetLayers.Add(((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
            }

            targetExtents = ModelExtents(database, tr, Point3d.Origin);
            tr.Commit();
        }

        var sources = new List<SourceInfo>();
        foreach (var path in inputs)
        {
            using var source = TryRead(path, out var error);
            if (source is null)
            {
                skipped.Add($"Bỏ qua \"{path}\": {error}");
                continue;
            }

            // Quy đổi đơn vị như INSERT: bản vẽ mm gộp vào bản vẽ inch phải nhỏ lại 25,4 lần, không thì to gấp 25,4.
            var sourceUnits = (int)source.Insunits;
            var scale = DrawingCombinePlanner.UnitScale(sourceUnits, targetUnits);
            if (sourceUnits != targetUnits && (sourceUnits == 0 || targetUnits == 0))
            {
                skipped.Add($"Lưu ý \"{Path.GetFileName(path)}\": INSUNITS={sourceUnits}, bản vẽ hiện tại {targetUnits} — một bên không khai đơn vị nên chèn tỉ lệ 1:1.");
            }

            using var tr = source.TransactionManager.StartTransaction();
            var extents = ModelExtents(source, tr, source.Insbase)?.Scaled(scale);
            var layouts = LayoutNames(source, tr);
            var shared = 0;
            foreach (ObjectId id in (LayerTable)tr.GetObject(source.LayerTableId, OpenMode.ForRead))
            {
                var name = ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
                if (name != "0" && !name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase) && targetLayers.Contains(name)) shared++;
            }

            tr.Commit();
            var blockName = DrawingCombinePlanner.UniqueName(DrawingCombinePlanner.BlockNameFor(path), blockNames);
            sources.Add(new SourceInfo(path, blockName, extents, layouts, shared, scale));
        }

        if (sources.Count == 0)
        {
            var fail = CommandResult.Fail("Không đọc được file DWG nào trong danh sách.");
            fail.Messages.AddRange(skipped);
            return fail;
        }

        var gap = DrawingUnits.FromMillimeters(config.GapMm, targetUnits);
        var startX = targetExtents is null ? 0 : targetExtents.MaxX + gap;
        var baseY = targetExtents?.MinY ?? 0;
        var placements = DrawingCombinePlanner.Place(sources.Select(s => (s.BlockName, s.Extents)).ToList(), arrangement, startX, baseY, gap);

        var plannedLayouts = new List<List<(string Source, string Target)>>();
        var messages = new List<string>();
        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            var p = placements[i];
            var layouts = config.IncludeLayouts
                ? s.Layouts.Select(l => (l, DrawingCombinePlanner.UniqueName(DrawingCombinePlanner.LayoutNameFor(s.BlockName, l), layoutNames, DrawingCombinePlanner.MaxLayoutLength))).ToList()
                : new List<(string, string)>();
            plannedLayouts.Add(layouts);
            var layoutText = layouts.Count == 0 ? "" : $"; {layouts.Count} layout: {string.Join(", ", layouts.Select(l => "\"" + l.Item2 + "\""))}";
            var emptyText = s.Extents is null ? " (Model Space rỗng)" : "";
            var sharedText = s.SharedLayers == 0 ? "" : $"; {s.SharedLayers} layer trùng tên dùng thuộc tính bản vẽ hiện tại";
            var scaleText = s.Scale == 1.0 ? "" : $", tỉ lệ ×{NumericText.Format(s.Scale, 6)} (quy đổi đơn vị)";
            messages.Add($"[{i + 1}] {Path.GetFileName(s.Path)} → block \"{s.BlockName}\" tại ({NumericText.Format(p.X, 1)}, {NumericText.Format(p.Y, 1)}){scaleText}{emptyText}{layoutText}{sharedText}");
        }

        var totalLayouts = plannedLayouts.Sum(l => l.Count);
        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"Xem trước: gộp {sources.Count} bản vẽ thành {sources.Count} block ({arrangement}){(config.IncludeLayouts ? $" và {totalLayouts} layout" : "")}. Chưa ghi gì.",
                sources.Count + totalLayouts);
            preview.Messages.AddRange(messages);
            preview.Messages.AddRange(skipped);
            return preview;
        }

        // ── Ghi: từng file một, đọc lại để không giữ hàng trăm Database trong bộ nhớ ──
        var done = 0;
        var layoutDone = 0;
        var warnings = new List<string>();
        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            using var source = TryRead(s.Path, out var error);
            if (source is null)
            {
                warnings.Add($"\"{Path.GetFileName(s.Path)}\" không đọc lại được khi ghi: {error}");
                continue;
            }

            var blockId = database.Insert(s.BlockName, source, true);
            var position = new Point3d(placements[i].X, placements[i].Y, 0);
            Point3d origin;
            using (var tr = database.TransactionManager.StartTransaction())
            {
                origin = ((BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead)).Origin;
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
                var reference = new BlockReference(position, blockId);
                reference.SetDatabaseDefaults(database);
                reference.Layer = "0";
                reference.ScaleFactors = new Scale3d(s.Scale);
                model.AppendEntity(reference);
                tr.AddNewlyCreatedDBObject(reference, true);
                tr.Commit();
            }

            done++;
            foreach (var (sourceLayout, targetLayout) in plannedLayouts[i])
            {
                try
                {
                    CopyLayout(database, source, sourceLayout, targetLayout, origin, position, s.Scale, warnings);
                    layoutDone++;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    warnings.Add($"Không chép được layout \"{sourceLayout}\" của \"{Path.GetFileName(s.Path)}\": {ex.ErrorStatus}.");
                }
            }
        }

        var result = CommandResult.Ok(
            $"Đã gộp {done}/{sources.Count} bản vẽ thành block ({arrangement}){(config.IncludeLayouts ? $", chép {layoutDone}/{totalLayouts} layout" : "")}.",
            done + layoutDone);
        result.Messages.AddRange(messages);
        result.Messages.AddRange(skipped);
        result.Messages.AddRange(warnings);
        return result;
    }

    private static Database? TryRead(string path, out string error)
    {
        var db = new Database(false, true);
        try
        {
            db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
            db.CloseInput(true);
            error = string.Empty;
            return db;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            db.Dispose();
            error = $"không đọc được ({ex.ErrorStatus}) — file đang khoá, hỏng hoặc thuộc phiên bản AutoCAD mới hơn.";
            return null;
        }
    }

    /// <summary>Hộp bao Model Space (bỏ entity không có extents), dời theo <paramref name="origin"/>; null khi rỗng.</summary>
    private static CombineExtents? ModelExtents(Database db, Transaction tr, Point3d origin)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in model)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity || !entity.Visible)
            {
                continue;
            }

            try
            {
                var e = entity.GeometricExtents;
                minX = Math.Min(minX, e.MinPoint.X);
                minY = Math.Min(minY, e.MinPoint.Y);
                maxX = Math.Max(maxX, e.MaxPoint.X);
                maxY = Math.Max(maxY, e.MaxPoint.Y);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Entity không có extents (ray/xline rỗng, proxy…) — không tính vào hộp bao.
            }
        }

        return minX > maxX ? null : new CombineExtents(minX - origin.X, minY - origin.Y, maxX - origin.X, maxY - origin.Y);
    }

    /// <summary>Layout paper space theo thứ tự tab (bỏ Model).</summary>
    private static List<string> LayoutNames(Database db, Transaction tr)
    {
        var layouts = new List<Layout>();
        foreach (DBDictionaryEntry entry in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
        {
            layouts.Add((Layout)tr.GetObject(entry.Value, OpenMode.ForRead));
        }

        return layouts.Where(l => !l.ModelType).OrderBy(l => l.TabOrder).Select(l => l.LayoutName).ToList();
    }

    /// <summary>
    /// Tạo layout <paramref name="targetName"/> và chép entity paper space của layout nguồn vào. Viewport tổng (số 1) không
    /// chép — layout mới tự có. Viewport được biến đổi giống block (điểm p của bản nguồn → <paramref name="position"/> +
    /// <paramref name="scale"/>·(p − <paramref name="origin"/>)) để vẫn nhìn đúng vùng hình với cùng tỉ lệ in.
    /// </summary>
    private static void CopyLayout(Database target, Database source, string sourceName, string targetName, Point3d origin, Point3d position, double scale, List<string> warnings)
    {
        ObjectId sourceLayoutId;
        ObjectId sourceBtrId;
        var ids = new ObjectIdCollection();
        using (var tr = source.TransactionManager.StartTransaction())
        {
            var dictionary = (DBDictionary)tr.GetObject(source.LayoutDictionaryId, OpenMode.ForRead);
            sourceLayoutId = dictionary.GetAt(sourceName);
            var layout = (Layout)tr.GetObject(sourceLayoutId, OpenMode.ForRead);
            sourceBtrId = layout.BlockTableRecordId;
            var overall = OverallViewport(layout, tr);
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(sourceBtrId, OpenMode.ForRead))
            {
                if (id != overall)
                {
                    ids.Add(id);
                }
            }

            tr.Commit();
        }

        // LayoutManager làm việc trên WorkingDatabase — đặt tạm cho đúng bản vẽ đích (batch/Bridge có thể khác).
        var previous = HostApplicationServices.WorkingDatabase;
        ObjectId targetLayoutId;
        try
        {
            HostApplicationServices.WorkingDatabase = target;
            targetLayoutId = LayoutManager.Current.CreateLayout(targetName);
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = previous;
        }

        ObjectId targetBtrId;
        using (var tr = target.TransactionManager.StartTransaction())
        {
            var layout = (Layout)tr.GetObject(targetLayoutId, OpenMode.ForWrite);
            targetBtrId = layout.BlockTableRecordId;
            using (var sourceTr = source.TransactionManager.StartTransaction())
            {
                try
                {
                    layout.CopyFrom((Layout)sourceTr.GetObject(sourceLayoutId, OpenMode.ForRead));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    warnings.Add($"Layout \"{targetName}\": không chép được thiết lập in ({ex.ErrorStatus}) — dùng khổ giấy mặc định.");
                }

                sourceTr.Commit();
            }

            tr.Commit();
        }

        if (ids.Count == 0)
        {
            return;
        }

        var mapping = new IdMapping();
        source.WblockCloneObjects(ids, targetBtrId, mapping, DuplicateRecordCloning.Ignore, false);

        using (var tr = target.TransactionManager.StartTransaction())
        {
            foreach (IdPair pair in mapping)
            {
                if (pair.IsCloned && pair.IsPrimary && tr.GetObject(pair.Value, OpenMode.ForRead) is Viewport viewport)
                {
                    viewport.UpgradeOpen();
                    viewport.ViewTarget = position + (viewport.ViewTarget - origin) * scale;
                    if (scale != 1.0)
                    {
                        // Tâm nhìn (DCS, tính từ target) và chiều cao vùng nhìn phóng cùng hệ số: tỉ lệ in của viewport không đổi.
                        viewport.ViewCenter = new Point2d(viewport.ViewCenter.X * scale, viewport.ViewCenter.Y * scale);
                        viewport.ViewHeight *= scale;
                    }
                }
            }

            tr.Commit();
        }
    }

    /// <summary>Viewport tổng của layout = viewport đầu tiên trong danh sách của layout (số 1 khi đã kích hoạt).</summary>
    private static ObjectId OverallViewport(Layout layout, Transaction tr)
    {
        var viewports = layout.GetViewports();
        if (viewports.Count > 0)
        {
            return viewports[0];
        }

        // Layout chưa từng mở: danh sách rỗng, lấy viewport đầu tiên gặp trong paper space.
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Viewport)
            {
                return id;
            }
        }

        return ObjectId.Null;
    }
}
