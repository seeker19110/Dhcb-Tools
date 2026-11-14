using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic.Cad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Tạo khung bản vẽ (layout giấy) từ layout mẫu: <c>LayoutManager.CopyLayout</c> giữ thiết lập in, khung tên, viewport
/// tổng; rồi canh viewport nổi đã chọn về mặt bằng (nhìn từ trên, không xoay) tại tâm Model Space + tỉ lệ 1:N, khoá
/// viewport, thay chữ MText tiêu đề (giữ mã định dạng) và thêm dòng "TỶ LỆ 1:N" bên dưới nếu cần.
/// <para>
/// Không đi đường COM <c>Layouts.Add</c> + <c>CopyObjects</c>: cách đó không tạo viewport tổng nên <c>MSpace</c> sau
/// đó hỏng. Không đổi tab hiện hành — trừ khi phải xoá chính layout đang hiện để tạo lại (khi đó về Model).
/// </para>
/// <para>
/// Xem trước chỉ đọc: kiểm layout mẫu, viewport sẽ canh, MText tiêu đề — không tạo gì.
/// </para>
/// </summary>
public sealed class LayoutViewportCommand : ICoreCommand<LayoutViewportConfig>
{
    public string CommandName => "LayoutViewport";

    private static readonly JsonSerializerSettings Strict = new() { MissingMemberHandling = MissingMemberHandling.Error };

    /// <summary>Những gì đọc được từ layout mẫu cho một khung: vị trí viewport và MText tiêu đề trong layout.</summary>
    private sealed class Inspection
    {
        public required LayoutSheet Sheet { get; init; }

        public required string Name { get; init; }

        public required string Template { get; init; }

        public int ViewportIndex { get; init; }

        public long ViewportHandle { get; init; }

        public double ViewportWidth { get; init; }

        public double ViewportHeight { get; init; }

        public int TitleIndex { get; init; } = -1;

        public long TitleHandle { get; init; }

        public string TitleText { get; init; } = string.Empty;
    }

    public CommandResult Execute(Database database, LayoutViewportConfig config)
    {
        var sheets = new List<LayoutSheet>();
        if (!string.IsNullOrWhiteSpace(config.InputPath))
        {
            if (!File.Exists(config.InputPath))
            {
                return CommandResult.Fail($"Không tìm thấy file: \"{config.InputPath}\".");
            }

            try
            {
                var token = JToken.Parse(File.ReadAllText(config.InputPath));
                var array = token is JObject obj && obj.TryGetValue("sheets", StringComparison.OrdinalIgnoreCase, out var inner) ? inner : token;
                sheets.AddRange(array.ToObject<List<LayoutSheet>>(JsonSerializer.Create(Strict)) ?? new List<LayoutSheet>());
            }
            catch (JsonException ex)
            {
                return CommandResult.Fail($"File \"{config.InputPath}\" không phải mảng khung JSON hợp lệ: {ex.Message}");
            }
        }

        sheets.AddRange(config.Sheets ?? new List<LayoutSheet>());

        var layouts = LayoutNames(database);
        var plan = LayoutViewportPlanner.Plan(sheets, layouts.Keys, config.ReplaceExisting);
        if (!plan.IsValid)
        {
            return CommandResult.Fail($"Danh sách khung không hợp lệ ({plan.Errors.Count} lỗi) — chưa tạo layout nào.", plan.Errors);
        }

        // ── Đọc layout mẫu (chỉ đọc) ──
        var inspections = new List<Inspection>();
        var errors = new List<string>();
        using (var tr = database.TransactionManager.StartTransaction())
        {
            foreach (var sheet in sheets)
            {
                var inspection = Inspect(tr, layouts[sheet.Template!.Trim()], sheet, out var error);
                if (error != null) errors.Add($"\"{sheet.Name!.Trim()}\": {error}");
                else inspections.Add(inspection!);
            }

            tr.Abort();
        }

        if (errors.Count > 0)
        {
            return CommandResult.Fail($"Không dựng được {errors.Count} khung từ layout mẫu — chưa tạo layout nào.", errors);
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok($"[Xem trước] Sẽ tạo {inspections.Count} layout từ layout mẫu. Chưa ghi vào drawing.", inspections.Count);
            foreach (var replaced in plan.Replaced)
            {
                preview.Messages.Add($"Layout \"{replaced}\" đã có — sẽ xoá rồi tạo lại.");
            }

            preview.Messages.AddRange(inspections.Select(Describe));
            return preview;
        }

        return Write(database, inspections, plan.Replaced);
    }

    private static string Describe(Inspection i)
    {
        var s = i.Sheet;
        var line = $"\"{i.Name}\" ← \"{i.Template}\": viewport {HandleText.ToText(i.ViewportHandle)} ({LayoutViewportPlanner.Number(i.ViewportWidth)}×{LayoutViewportPlanner.Number(i.ViewportHeight)}) "
                   + $"tâm {EntityEditPlanner.Format(s.ViewCenter!.Take(2).ToArray())} tỉ lệ 1:{LayoutViewportPlanner.Number(s.Scale!.Value)}{(s.Lock ?? true ? ", khoá" : string.Empty)}";
        if (s.Title != null) line += $"; tiêu đề MText {HandleText.ToText(i.TitleHandle)} \"{i.TitleText}\" → \"{s.Title}\"";
        if (s.ScaleText != null) line += $"; thêm dòng \"{s.ScaleText}\"";
        return line;
    }

    /// <summary>Tên layout giấy (không kể Model) → ObjectId của Layout.</summary>
    private static Dictionary<string, ObjectId> LayoutNames(Database database)
    {
        var names = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        using var tr = database.TransactionManager.StartTransaction();
        var dictionary = (DBDictionary)tr.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (tr.GetObject(entry.Value, OpenMode.ForRead) is Layout { ModelType: false } layout)
            {
                names[layout.LayoutName] = entry.Value;
            }
        }

        tr.Abort();
        return names;
    }

    /// <summary>Chọn viewport và MText tiêu đề trong layout mẫu; vị trí ghi theo thứ tự entity trong paper space.</summary>
    private static Inspection? Inspect(Transaction tr, ObjectId layoutId, LayoutSheet sheet, out string? error)
    {
        var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
        var entities = Entities(tr, layout.BlockTableRecordId);
        var overall = OverallViewport(tr, layout, entities);
        var candidates = new List<ViewportCandidate>();
        for (var i = 0; i < entities.Count; i++)
        {
            if (tr.GetObject(entities[i], OpenMode.ForRead) is Viewport vp)
            {
                candidates.Add(new ViewportCandidate { Index = i, Handle = vp.Handle.Value, Width = vp.Width, Height = vp.Height, IsOverall = entities[i] == overall });
            }
        }

        var chosen = LayoutViewportPlanner.ChooseViewport(candidates, sheet.ViewportHandle, layout.LayoutName, out error);
        if (chosen == null)
        {
            return null;
        }

        var titleIndex = -1;
        long titleHandle = 0;
        var titleText = string.Empty;
        if (!string.IsNullOrWhiteSpace(sheet.TitleMatch))
        {
            var match = sheet.TitleMatch!.Trim();
            for (var i = 0; i < entities.Count && titleIndex < 0; i++)
            {
                if (tr.GetObject(entities[i], OpenMode.ForRead) is MText mtext && mtext.Text.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    titleIndex = i;
                    titleHandle = mtext.Handle.Value;
                    titleText = mtext.Text.Replace("\r\n", " ").Replace('\n', ' ');
                }
            }

            if (titleIndex < 0)
            {
                error = $"layout mẫu \"{layout.LayoutName}\" không có MText chứa \"{match}\" (titleMatch) trong paper space.";
                return null;
            }
        }

        return new Inspection
        {
            Sheet = sheet,
            Name = sheet.Name!.Trim(),
            Template = layout.LayoutName,
            ViewportIndex = chosen.Index,
            ViewportHandle = chosen.Handle,
            ViewportWidth = chosen.Width,
            ViewportHeight = chosen.Height,
            TitleIndex = titleIndex,
            TitleHandle = titleHandle,
            TitleText = titleText,
        };
    }

    private static List<ObjectId> Entities(Transaction tr, ObjectId blockId)
    {
        var list = new List<ObjectId>();
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead))
        {
            list.Add(id);
        }

        return list;
    }

    /// <summary>Viewport tổng = viewport đầu trong danh sách của layout (số 1 khi đã kích hoạt); layout chưa mở thì viewport đầu tiên trong paper space.</summary>
    private static ObjectId OverallViewport(Transaction tr, Layout layout, List<ObjectId> entities)
    {
        var viewports = layout.GetViewports();
        if (viewports != null && viewports.Count > 0)
        {
            return viewports[0];
        }

        return entities.FirstOrDefault(id => tr.GetObject(id, OpenMode.ForRead) is Viewport);
    }

    private static CommandResult Write(Database database, List<Inspection> inspections, List<string> replaced)
    {
        var result = CommandResult.Ok(string.Empty);
        var previous = HostApplicationServices.WorkingDatabase;
        try
        {
            // LayoutManager làm việc trên WorkingDatabase — đặt tạm cho đúng bản vẽ đích (batch/Bridge có thể khác).
            HostApplicationServices.WorkingDatabase = database;
            var manager = LayoutManager.Current;
            using var tr = database.TransactionManager.StartTransaction();
            try
            {
                foreach (var name in replaced)
                {
                    if (string.Equals(manager.CurrentLayout, name, StringComparison.OrdinalIgnoreCase))
                    {
                        manager.CurrentLayout = "Model";
                        result.Messages.Add($"Layout \"{name}\" đang hiện — chuyển về Model để xoá.");
                    }

                    manager.DeleteLayout(name);
                    result.Messages.Add($"Đã xoá layout cũ \"{name}\".");
                }

                foreach (var inspection in inspections)
                {
                    result.Messages.Add(Create(tr, database, manager, inspection, result));
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                tr.Abort();
                return CommandResult.Fail($"AutoCAD từ chối khi tạo khung ({ex.ErrorStatus}): {ex.Message} — đã huỷ, bản vẽ không đổi.");
            }
            catch (InvalidOperationException ex)
            {
                tr.Abort();
                return CommandResult.Fail($"{ex.Message} — đã huỷ, bản vẽ không đổi.");
            }

            tr.Commit();
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = previous;
        }

        result.Summary = $"Đã tạo {inspections.Count} layout: {string.Join(", ", inspections.Select(i => "\"" + i.Name + "\""))}.";
        result.AffectedCount = inspections.Count;
        return result;
    }

    /// <summary>Chép layout mẫu thành layout mới, canh viewport, sửa tiêu đề. Trả dòng báo cáo của khung.</summary>
    private static string Create(Transaction tr, Database database, LayoutManager manager, Inspection inspection, CommandResult result)
    {
        var sheet = inspection.Sheet;
        manager.CopyLayout(inspection.Template, inspection.Name);
        var layout = (Layout)tr.GetObject(manager.GetLayoutId(inspection.Name), OpenMode.ForWrite);

        // CopyLayout đặt tab mới cùng số thứ tự với mẫu (đo trong accoreconsole 2026: B2, bản chép đều TabOrder 1) —
        // đẩy về cuối để thứ tự tab xác định.
        layout.TabOrder = MaxTabOrder(tr, database, layout.ObjectId) + 1;
        var entities = Entities(tr, layout.BlockTableRecordId);

        // CopyLayout chép paper space theo đúng thứ tự — viewport ở cùng vị trí; kiểm kích thước cho chắc, lệch thì tìm theo hình.
        var viewport = FindCopy<Viewport>(tr, entities, inspection.ViewportIndex,
                           v => Math.Abs(v.Width - inspection.ViewportWidth) < 1e-6 && Math.Abs(v.Height - inspection.ViewportHeight) < 1e-6)
                       ?? throw new InvalidOperationException($"\"{inspection.Name}\": không tìm thấy bản chép của viewport {HandleText.ToText(inspection.ViewportHandle)}.");
        viewport.UpgradeOpen();
        var center = sheet.ViewCenter!;
        var scale = sheet.Scale!.Value;
        viewport.ViewDirection = Vector3d.ZAxis;
        viewport.TwistAngle = 0;
        viewport.ViewTarget = new Point3d(center[0], center[1], 0);
        viewport.ViewCenter = Point2d.Origin;
        viewport.CustomScale = LayoutViewportPlanner.CustomScale(scale);
        viewport.Locked = sheet.Lock ?? true;
        // Layout chưa kích hoạt thì On đọc ra false cho mọi viewport (kể cả viewport đang bật) và gán On ném
        // eNotInPaperspace (đo trong accoreconsole 2026 với layout mới chép) — khi đó viewport giữ cờ bật/tắt của mẫu.
        var onKeptFromTemplate = false;
        try
        {
            viewport.On = true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.NotInPaperspace)
        {
            onKeptFromTemplate = true;
        }

        result.WithChanged(layout.Handle.Value).WithChanged(viewport.Handle.Value);
        var line = $"\"{inspection.Name}\" ← \"{inspection.Template}\": layout {HandleText.ToText(layout.Handle.Value)}, viewport {HandleText.ToText(viewport.Handle.Value)} "
                   + $"tâm {EntityEditPlanner.Format(new[] { viewport.ViewTarget.X, viewport.ViewTarget.Y })} tỉ lệ 1:{LayoutViewportPlanner.Number(scale)} "
                   + $"(CustomScale {viewport.CustomScale.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}){(viewport.Locked ? ", khoá" : string.Empty)}"
                   + (onKeptFromTemplate ? ", bật/tắt theo mẫu (layout chưa kích hoạt)" : string.Empty);

        if (inspection.TitleIndex >= 0)
        {
            var match = sheet.TitleMatch!.Trim();
            var title = FindCopy<MText>(tr, entities, inspection.TitleIndex, m => m.Text.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? throw new InvalidOperationException($"\"{inspection.Name}\": không tìm thấy bản chép của MText tiêu đề {HandleText.ToText(inspection.TitleHandle)}.");
            var originalContents = title.Contents;
            if (sheet.Title != null)
            {
                title.UpgradeOpen();
                title.Contents = LayoutViewportPlanner.ReplacePlain(originalContents, sheet.Title.Trim());
                result.WithChanged(title.Handle.Value);
                line += $"; tiêu đề MText {HandleText.ToText(title.Handle.Value)}";
            }
            else
            {
                line += $"; MText tiêu đề {HandleText.ToText(title.Handle.Value)} giữ nguyên";
            }

            if (sheet.ScaleText != null)
            {
                var added = AddScaleText(tr, database, layout.BlockTableRecordId, title, originalContents, sheet.ScaleText.Trim());
                result.WithChanged(added.Value);
                line += $"; dòng tỉ lệ MText {HandleText.ToText(added.Value)}";
            }
        }

        return "Đã tạo " + line + ".";
    }

    private static int MaxTabOrder(Transaction tr, Database database, ObjectId except)
    {
        var max = 0;
        foreach (DBDictionaryEntry entry in (DBDictionary)tr.GetObject(database.LayoutDictionaryId, OpenMode.ForRead))
        {
            if (entry.Value != except && tr.GetObject(entry.Value, OpenMode.ForRead) is Layout other)
            {
                max = Math.Max(max, other.TabOrder);
            }
        }

        return max;
    }

    /// <summary>Đối tượng loại <typeparamref name="T"/> ở vị trí <paramref name="index"/> nếu đúng điều kiện, không thì cái đầu tiên đúng điều kiện.</summary>
    private static T? FindCopy<T>(Transaction tr, List<ObjectId> entities, int index, Func<T, bool> same)
        where T : Entity
    {
        if (index < entities.Count && tr.GetObject(entities[index], OpenMode.ForRead) is T exact && same(exact))
        {
            return exact;
        }

        return entities.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<T>().FirstOrDefault(same);
    }

    /// <summary>MText dòng tỉ lệ dưới tiêu đề: cùng style/layer/màu/căn lề/bề rộng/góc, cao ×0,8.</summary>
    private static Handle AddScaleText(Transaction tr, Database database, ObjectId paperSpaceId, MText title, string titleContents, string text)
    {
        var height = title.TextHeight * LayoutViewportPlanner.ScaleTextHeightFactor;
        var row = title.Attachment switch
        {
            AttachmentPoint.TopLeft or AttachmentPoint.TopCenter or AttachmentPoint.TopRight => 0,
            AttachmentPoint.MiddleLeft or AttachmentPoint.MiddleCenter or AttachmentPoint.MiddleRight => 1,
            _ => 2,
        };
        var drop = LayoutViewportPlanner.ScaleTextDrop(row, title.ActualHeight, height);
        var down = new Vector3d(Math.Sin(title.Rotation), -Math.Cos(title.Rotation), 0) * drop;
        var mtext = new MText();
        mtext.SetDatabaseDefaults(database);
        mtext.TextStyleId = title.TextStyleId;
        mtext.LayerId = title.LayerId;
        mtext.Color = title.Color;
        mtext.Attachment = title.Attachment;
        mtext.Rotation = title.Rotation;
        mtext.Width = title.Width;
        mtext.TextHeight = height;
        mtext.Location = title.Location + down;
        mtext.Contents = LayoutViewportPlanner.ScaleTextContents(titleContents, text);
        var space = (BlockTableRecord)tr.GetObject(paperSpaceId, OpenMode.ForWrite);
        space.AppendEntity(mtext);
        tr.AddNewlyCreatedDBObject(mtext, true);
        return mtext.Handle;
    }
}
