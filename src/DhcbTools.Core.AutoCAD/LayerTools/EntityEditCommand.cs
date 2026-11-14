using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic.Cad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Vẽ và sửa hình học bằng một lô thao tác JSON — "cây bút" cho agent: line, polyline, rectangle, circle, arc,
/// ellipse, text, mtext, hatch, dimension (kích thước thẳng hàng), point (tạo trong Model Space) và move, copy, array, rotate, scale, mirror, offset,
/// erase, explode, set, trim, replacePoints (sửa đối tượng có sẵn theo handle, hoặc đối tượng op trước vừa tạo qua
/// <c>"@id"</c>).
/// <para>
/// <c>explode</c> (phá block/đối tượng phức, attribute thành chữ như BURST, ánh xạ layer cho mảnh), <c>set</c> (layer,
/// màu, bề rộng đều polyline) và <c>trim</c>/<c>replacePoints</c> (sửa đỉnh LWPOLYLINE tại chỗ, giữ handle) thêm sau
/// phiên bóc khối lượng 2026-11-13 — trước đó phải vòng qua COM hoặc xoá rồi vẽ lại.
/// </para>
/// <para>
/// Cả lô là <b>một giao dịch</b>: kiểm hết bằng <see cref="EntityEditPlanner"/> trước, rồi chạy; op nào hỏng giữa
/// chừng (handle không có, layer khoá, offset không ra đường) thì huỷ toàn bộ và báo đúng op đó — bản vẽ không bao
/// giờ ở trạng thái nửa vời. Xem trước cũng chạy thật trong giao dịch rồi huỷ, nên lỗi của lần ghi thật đã lộ ra
/// từ lần xem trước. Kết quả liệt kê handle của mọi đối tượng mới để agent gọi <c>query entity_geometry</c> /
/// <c>snapshot</c> kiểm lại.
/// </para>
/// </summary>
public sealed class EntityEditCommand : ICoreCommand<EntityEditConfig>
{
    public string CommandName => "EntityEdit";

    private static readonly JsonSerializerSettings Strict = new() { MissingMemberHandling = MissingMemberHandling.Error };

    public CommandResult Execute(Database database, EntityEditConfig config)
    {
        var operations = new List<EntityOperation>();
        if (!string.IsNullOrWhiteSpace(config.InputPath))
        {
            if (!File.Exists(config.InputPath))
            {
                return CommandResult.Fail($"Không tìm thấy file: \"{config.InputPath}\".");
            }

            try
            {
                var token = JToken.Parse(File.ReadAllText(config.InputPath));
                var array = token is JObject obj && obj.TryGetValue("operations", StringComparison.OrdinalIgnoreCase, out var inner) ? inner : token;
                operations.AddRange(array.ToObject<List<EntityOperation>>(JsonSerializer.Create(Strict)) ?? new List<EntityOperation>());
            }
            catch (JsonException ex)
            {
                return CommandResult.Fail($"File \"{config.InputPath}\" không phải mảng thao tác JSON hợp lệ: {ex.Message}");
            }
        }

        operations.AddRange(config.Operations ?? new List<EntityOperation>());
        var plan = EntityEditPlanner.Plan(operations);
        if (!plan.IsValid)
        {
            return CommandResult.Fail($"Lô thao tác không hợp lệ ({plan.Errors.Count} lỗi) — chưa đụng vào bản vẽ.", plan.Errors);
        }

        var run = new Runner(database, config.DryRun);
        using var transaction = database.TransactionManager.StartTransaction();
        try
        {
            foreach (var step in plan.Steps)
            {
                run.Execute(transaction, step);
            }
        }
        catch (StepException ex)
        {
            transaction.Abort();
            var failed = CommandResult.Fail($"{ex.Step.Label}: {ex.Message} — đã huỷ cả lô, bản vẽ không đổi.");
            failed.Messages.AddRange(run.Report);
            return failed;
        }

        var created = run.Created.Count;
        var summary = $"{plan.Steps.Count} thao tác: tạo {created} đối tượng, sửa {run.Modified.Count}, xoá {run.Erased}.";
        CommandResult result;
        if (config.DryRun)
        {
            transaction.Abort();
            result = CommandResult.Ok("[Xem trước] " + summary + " Chưa ghi vào drawing.", created + run.Modified.Count + run.Erased);
        }
        else
        {
            transaction.Commit();
            result = CommandResult.Ok("Đã chạy " + summary, created + run.Modified.Count + run.Erased);
            result.WithChanged(run.Created.Concat(run.Modified).Select(id => id.Handle.Value));
        }

        result.Messages.AddRange(run.Report);
        foreach (var label in run.Labels)
        {
            result.Messages.Add(config.DryRun
                ? $"@{label.Key} → {label.Value.Count} đối tượng (handle có sau khi ghi thật)"
                : $"@{label.Key} → {string.Join(", ", label.Value.Select(AcadHelpers.HandleOf))}");
        }

        return result;
    }

    private sealed class StepException : Exception
    {
        public StepException(EntityEditStep step, string message) : base(message) => Step = step;

        public EntityEditStep Step { get; }
    }

    /// <summary>Trạng thái chạy một lô: nhãn → đối tượng, danh sách tạo/sửa, cache layer đã kiểm.</summary>
    private sealed class Runner
    {
        private readonly Database _db;
        private readonly bool _dryRun;
        private readonly Dictionary<string, ObjectId> _layers = new(StringComparer.OrdinalIgnoreCase);

        public Runner(Database db, bool dryRun)
        {
            _db = db;
            _dryRun = dryRun;
        }

        public List<string> Report { get; } = new();

        public List<ObjectId> Created { get; } = new();

        public HashSet<ObjectId> Modified { get; } = new();

        public int Erased { get; private set; }

        public SortedDictionary<string, List<ObjectId>> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Execute(Transaction tr, EntityEditStep step)
        {
            var op = step.Source;
            var produced = new List<ObjectId>();
            if (_dryRun)
            {
                produced.AddRange(Preview(tr, step));
            }
            else if (step.Creates)
            {
                var entity = Build(tr, step);
                entity.SetDatabaseDefaults(_db);
                entity.LayerId = Layer(tr, step, op.Layer);
                if (op.Color is short aci)
                {
                    entity.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
                }

                var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                produced.Add(space.AppendEntity(entity));
                tr.AddNewlyCreatedDBObject(entity, true);
                if (entity is Hatch hatch)
                {
                    FinishHatch(step, hatch);
                }

                Report.Add($"{step.Label} → {AcadHelpers.HandleOf(produced[0])} trên layer \"{entity.Layer}\".");
            }
            else
            {
                var targets = Resolve(tr, step, out _);
                produced.AddRange(Modify(tr, step, targets));
            }

            Created.AddRange(produced);
            if (!string.IsNullOrWhiteSpace(op.Id))
            {
                Labels[op.Id!.Trim()] = produced;
            }
        }

        /// <summary>
        /// Xem trước <b>chỉ đọc</b>: không tạo, không sửa gì trong database (xem trước của mọi lệnh DHCB không được phát
        /// sự kiện ghi tạm). Vẫn kiểm thật những gì kiểm được mà không ghi: handle có tồn tại, layer có khoá, đối tượng
        /// offset có phải đường cong. Đối tượng "sẽ tạo" là <see cref="ObjectId.Null"/> — chỉ để đếm và để nhãn
        /// <c>@id</c> của op sau trỏ vào.
        /// </summary>
        private List<ObjectId> Preview(Transaction tr, EntityEditStep step)
        {
            var op = step.Source;
            if (step.Creates)
            {
                if (step.Kind is EntityOpKind.Text or EntityOpKind.MText)
                {
                    TextStyle(tr, step); // kiểm tên style ngay từ xem trước
                }
                else if (step.Kind == EntityOpKind.Dimension)
                {
                    DimStyle(tr, step);
                }

                Report.Add($"{step.Label} → (mới) trên layer \"{PreviewLayer(tr, step, op.Layer)}\".");
                return new List<ObjectId> { ObjectId.Null };
            }

            var real = Resolve(tr, step, out var pending);
            var n = real.Count + pending;
            int made;
            switch (step.Kind)
            {
                case EntityOpKind.Copy:
                    made = n * (op.Count ?? 1);
                    break;
                case EntityOpKind.Array:
                    made = n * (((op.Rows ?? 1) * (op.Columns ?? 1)) - 1);
                    break;
                case EntityOpKind.Mirror when op.KeepOriginal != false:
                    made = n;
                    break;
                case EntityOpKind.Offset:
                    var notCurve = real.FirstOrDefault(e => e is not Curve);
                    if (notCurve != null)
                    {
                        throw new StepException(step, $"{AcadHelpers.HandleOf(notCurve.ObjectId)} là {notCurve.GetRXClass().DxfName}, không offset được (chỉ đường cong).");
                    }

                    made = n;
                    break;
                case EntityOpKind.Erase:
                    foreach (var entity in real) Modified.Remove(entity.ObjectId);
                    for (var i = 0; i < pending; i++) Created.Remove(ObjectId.Null);
                    Erased += n;
                    Report.Add($"{step.Label}: xoá {n} đối tượng.");
                    return new List<ObjectId>();
                case EntityOpKind.Explode:
                {
                    var pieces = 0;
                    foreach (var entity in real)
                    {
                        foreach (var piece in ExplodePieces(tr, step, entity))
                        {
                            var target = ExplodeLayer(op, piece.Layer);
                            if (target != null) PreviewLayer(tr, step, target);
                            piece.Dispose();
                            pieces++;
                        }

                        Modified.Remove(entity.ObjectId);
                    }

                    for (var i = 0; i < pending; i++) Created.Remove(ObjectId.Null);
                    Erased += n;
                    Report.Add($"{step.Label}: phá {n} đối tượng → {pieces} mảnh{(pending > 0 ? " (chưa kể mảnh của đối tượng op trước sẽ tạo — đếm khi ghi thật)" : string.Empty)}.");
                    return Enumerable.Repeat(ObjectId.Null, pieces).ToList();
                }
                case EntityOpKind.Set:
                    if (op.Layer != null) PreviewLayer(tr, step, op.Layer);
                    CheckWidthTargets(step, real);
                    goto default;
                case EntityOpKind.Trim:
                case EntityOpKind.ReplacePoints:
                    CheckSingleTarget(step, real.Count + pending);
                    foreach (var entity in real) EditVertices(step, entity, write: false);
                    goto default;
                default:
                    foreach (var entity in real) Modified.Add(entity.ObjectId);
                    Report.Add($"{step.Label}: sửa {n} đối tượng.");
                    return new List<ObjectId>();
            }

            Report.Add($"{step.Label}: tạo {made} đối tượng.");
            return Enumerable.Repeat(ObjectId.Null, made).ToList();
        }

        /// <summary>Layer cho xem trước: kiểm tên/khoá, chưa có thì chỉ báo "sẽ tạo" — không ghi bảng layer.</summary>
        private string PreviewLayer(Transaction tr, EntityEditStep step, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ((LayerTableRecord)tr.GetObject(_db.Clayer, OpenMode.ForRead)).Name;
            }

            name = name!.Trim();
            if (_layers.ContainsKey(name)) return name;
            if (!AcadHelpers.IsValidSymbolName(name))
            {
                throw new StepException(step, $"tên layer \"{name}\" không hợp lệ với AutoCAD.");
            }

            var table = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (table.Has(name))
            {
                if (((LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead)).IsLocked)
                {
                    throw new StepException(step, $"layer \"{name}\" đang khoá.");
                }
            }
            else
            {
                Report.Add($"[Xem trước] Sẽ tạo layer \"{name}\".");
            }

            _layers[name] = ObjectId.Null;
            return name;
        }

        private Entity Build(Transaction tr, EntityEditStep step)
        {
            var op = step.Source;
            switch (step.Kind)
            {
                case EntityOpKind.Line:
                    return new Line(P(op.From!), P(op.To!));
                case EntityOpKind.Polyline:
                    return Pline(op.Points!.Select(p => (p[0], p[1])).ToList(), op.Closed == true, op.Width ?? 0, Elevation(op.Points![0]));
                case EntityOpKind.Rectangle:
                {
                    double x1 = op.From![0], y1 = op.From[1], x2 = op.To![0], y2 = op.To[1];
                    return Pline(new List<(double, double)> { (x1, y1), (x2, y1), (x2, y2), (x1, y2) }, true, op.Width ?? 0, Elevation(op.From));
                }
                case EntityOpKind.Circle:
                    return new Circle(P(op.Center!), Vector3d.ZAxis, op.Radius!.Value);
                case EntityOpKind.Arc:
                    return new Arc(P(op.Center!), op.Radius!.Value, EntityEditPlanner.Radians(op.StartAngle!.Value), EntityEditPlanner.Radians(op.EndAngle!.Value));
                case EntityOpKind.Ellipse:
                    return new Ellipse(P(op.Center!), Vector3d.ZAxis, new Vector3d(op.MajorAxis![0], op.MajorAxis[1], 0), op.Ratio!.Value, 0, Math.PI * 2);
                case EntityOpKind.Text:
                {
                    var text = new DBText { TextString = op.Text, Position = P(op.Position!), Rotation = EntityEditPlanner.Radians(op.Rotation ?? 0) };
                    text.Height = op.Height ?? _db.Textsize;
                    text.TextStyleId = TextStyle(tr, step);
                    return text;
                }
                case EntityOpKind.MText:
                {
                    var mtext = new MText { Contents = op.Text, Location = P(op.Position!), Rotation = EntityEditPlanner.Radians(op.Rotation ?? 0) };
                    mtext.TextHeight = op.Height ?? _db.Textsize;
                    mtext.TextStyleId = TextStyle(tr, step);
                    if (op.Width is double width) mtext.Width = width;
                    return mtext;
                }
                case EntityOpKind.Hatch:
                    return new Hatch { Normal = Vector3d.ZAxis, Elevation = Elevation(op.Points![0]) };
                case EntityOpKind.Dimension:
                {
                    var from = P(op.From!);
                    var to = P(op.To!);
                    var dimension = new AlignedDimension(from, to, DimLinePoint(op, from, to), op.Text ?? string.Empty, DimStyle(tr, step));
                    if (op.Height is double textHeight) dimension.Dimtxt = textHeight;
                    return dimension;
                }
                case EntityOpKind.Point:
                    return new DBPoint(P(op.Position!));
                default:
                    throw new StepException(step, "op không phải op tạo.");
            }
        }

        /// <summary>Text style theo tên (rỗng = hiện hành); không có thì dừng lô và liệt kê style có sẵn.</summary>
        private ObjectId TextStyle(Transaction tr, EntityEditStep step)
        {
            var name = step.Source.Style?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return _db.Textstyle;
            }

            var table = (TextStyleTable)tr.GetObject(_db.TextStyleTableId, OpenMode.ForRead);
            if (table.Has(name))
            {
                return table[name];
            }

            var known = new List<string>();
            foreach (ObjectId id in table)
            {
                known.Add(((TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
            }

            throw new StepException(step, $"text style \"{name}\" không có trong bản vẽ (có: {string.Join(", ", known.Where(n => n.Length > 0).Take(30))}).");
        }

        /// <summary>
        /// Điểm đặt đường kích thước của op <c>dimension</c>: <c>position</c> nếu có; không thì lệch <c>distance</c>
        /// vuông góc với from→to (dấu quyết định phía, quay trái là dương); cả hai trống = trung điểm — đường kích
        /// thước chạy đúng tim đoạn, kiểu "DIM giữa ống".
        /// </summary>
        private static Point3d DimLinePoint(EntityOperation op, Point3d from, Point3d to)
        {
            if (op.Position != null)
            {
                return P(op.Position);
            }

            var mid = new Point3d((from.X + to.X) / 2, (from.Y + to.Y) / 2, (from.Z + to.Z) / 2);
            if (op.Distance is not double offset || offset == 0)
            {
                return mid;
            }

            var along = (to - from).GetNormal();
            var normal = new Vector3d(-along.Y, along.X, 0);
            return mid + (normal * offset);
        }

        /// <summary>Dim style theo tên (rỗng = hiện hành); không có thì dừng lô và liệt kê style có sẵn.</summary>
        private ObjectId DimStyle(Transaction tr, EntityEditStep step)
        {
            var name = step.Source.Style?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return _db.Dimstyle;
            }

            var table = (DimStyleTable)tr.GetObject(_db.DimStyleTableId, OpenMode.ForRead);
            if (table.Has(name))
            {
                return table[name];
            }

            var known = new List<string>();
            foreach (ObjectId id in table)
            {
                known.Add(((DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
            }

            throw new StepException(step, $"dim style \"{name}\" không có trong bản vẽ (có: {string.Join(", ", known.Where(n => n.Length > 0).Take(30))}).");
        }

        /// <summary>Hatch phải nằm trong database rồi mới nạp mẫu và biên — thứ tự bắt buộc của API AutoCAD.</summary>
        private static void FinishHatch(EntityEditStep step, Hatch hatch)
        {
            var op = step.Source;
            var pattern = string.IsNullOrWhiteSpace(op.Pattern) ? "SOLID" : op.Pattern!.Trim();
            try
            {
                if (!pattern.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    hatch.PatternScale = op.PatternScale ?? 1;
                }

                hatch.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                var vertices = new Point2dCollection();
                var bulges = new DoubleCollection();
                foreach (var p in op.Points!)
                {
                    vertices.Add(new Point2d(p[0], p[1]));
                    bulges.Add(0);
                }

                // Biên polyline phải khép: đỉnh cuối trùng đỉnh đầu.
                if (vertices[0] != vertices[vertices.Count - 1])
                {
                    vertices.Add(vertices[0]);
                    bulges.Add(0);
                }

                hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, vertices, bulges);
                hatch.EvaluateHatch(true);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new StepException(step, $"không dựng được hatch mẫu \"{pattern}\" ({ex.ErrorStatus}) — kiểm tên mẫu và biên kín không tự cắt.");
            }
        }

        private List<ObjectId> Modify(Transaction tr, EntityEditStep step, List<Entity> targets)
        {
            var op = step.Source;
            var produced = new List<ObjectId>();
            switch (step.Kind)
            {
                case EntityOpKind.Move:
                    Transform(targets, Matrix3d.Displacement(V(op.Delta!)));
                    break;
                case EntityOpKind.Rotate:
                    Transform(targets, Matrix3d.Rotation(EntityEditPlanner.Radians(op.Angle!.Value), Vector3d.ZAxis, P(op.BasePoint!)));
                    break;
                case EntityOpKind.Scale:
                    Transform(targets, Matrix3d.Scaling(op.Factor!.Value, P(op.BasePoint!)));
                    break;
                case EntityOpKind.Mirror:
                {
                    var mirror = Matrix3d.Mirroring(new Line3d(P(op.From!), P(op.To!)));
                    if (op.KeepOriginal == false)
                    {
                        Transform(targets, mirror);
                    }
                    else
                    {
                        produced.AddRange(targets.Select(e => CopyOf(tr, step, e, mirror)));
                    }

                    break;
                }
                case EntityOpKind.Copy:
                {
                    var delta = V(op.Delta!);
                    for (var i = 1; i <= (op.Count ?? 1); i++)
                    {
                        var matrix = Matrix3d.Displacement(delta * i);
                        produced.AddRange(targets.Select(e => CopyOf(tr, step, e, matrix)));
                    }

                    break;
                }
                case EntityOpKind.Array:
                    for (var r = 0; r < (op.Rows ?? 1); r++)
                    {
                        for (var c = 0; c < (op.Columns ?? 1); c++)
                        {
                            if (r == 0 && c == 0) continue;
                            var matrix = Matrix3d.Displacement(new Vector3d(c * (op.ColumnSpacing ?? 0), r * (op.RowSpacing ?? 0), 0));
                            produced.AddRange(targets.Select(e => CopyOf(tr, step, e, matrix)));
                        }
                    }

                    break;
                case EntityOpKind.Offset:
                    foreach (var entity in targets)
                    {
                        if (entity is not Curve curve)
                        {
                            throw new StepException(step, $"{AcadHelpers.HandleOf(entity.ObjectId)} là {entity.GetRXClass().DxfName}, không offset được (chỉ đường cong).");
                        }

                        DBObjectCollection offsets;
                        try
                        {
                            offsets = curve.GetOffsetCurves(op.Distance!.Value);
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            throw new StepException(step, $"offset {AcadHelpers.HandleOf(entity.ObjectId)} khoảng {op.Distance} không ra đường ({ex.ErrorStatus}).");
                        }

                        var owner = (BlockTableRecord)tr.GetObject(entity.OwnerId, OpenMode.ForWrite);
                        foreach (Entity offset in offsets)
                        {
                            produced.Add(owner.AppendEntity(offset));
                            tr.AddNewlyCreatedDBObject(offset, true);
                        }
                    }

                    break;
                case EntityOpKind.Erase:
                    foreach (var entity in targets)
                    {
                        entity.UpgradeOpen();
                        entity.Erase();
                        Modified.Remove(entity.ObjectId);
                        Created.Remove(entity.ObjectId);
                        Erased++;
                    }

                    break;
                case EntityOpKind.Explode:
                    foreach (var entity in targets)
                    {
                        var owner = (BlockTableRecord)tr.GetObject(entity.OwnerId, OpenMode.ForWrite);
                        foreach (var piece in ExplodePieces(tr, step, entity))
                        {
                            var target = ExplodeLayer(op, piece.Layer);
                            if (target != null) piece.LayerId = Layer(tr, step, target);
                            produced.Add(owner.AppendEntity(piece));
                            tr.AddNewlyCreatedDBObject(piece, true);
                        }

                        entity.UpgradeOpen();
                        entity.Erase();
                        Modified.Remove(entity.ObjectId);
                        Created.Remove(entity.ObjectId);
                        Erased++;
                    }

                    Report.Add($"{step.Label}: phá {targets.Count} đối tượng → {produced.Count} mảnh ({string.Join(", ", produced.Take(10).Select(AcadHelpers.HandleOf))}{(produced.Count > 10 ? ", …" : "")}).");
                    return produced;
                case EntityOpKind.Set:
                {
                    CheckWidthTargets(step, targets);
                    var layerId = op.Layer != null ? Layer(tr, step, op.Layer) : ObjectId.Null;
                    foreach (var entity in targets)
                    {
                        entity.UpgradeOpen();
                        if (!layerId.IsNull) entity.LayerId = layerId;
                        if (op.Color is short aci)
                        {
                            entity.Color = aci switch
                            {
                                0 => Color.FromColorIndex(ColorMethod.ByBlock, 0),
                                256 => Color.FromColorIndex(ColorMethod.ByLayer, 256),
                                _ => Color.FromColorIndex(ColorMethod.ByAci, aci),
                            };
                        }

                        if (op.Width is double width)
                        {
                            if (entity is Polyline pline) pline.ConstantWidth = width;
                            else ((Polyline2d)entity).ConstantWidth = width;
                        }

                        MarkModified(entity);
                    }

                    break;
                }
                case EntityOpKind.Trim:
                case EntityOpKind.ReplacePoints:
                    CheckSingleTarget(step, targets.Count);
                    foreach (var entity in targets)
                    {
                        EditVertices(step, entity, write: true);
                        MarkModified(entity);
                    }

                    break;
            }

            var verb = step.Kind switch
            {
                EntityOpKind.Erase => "xoá",
                EntityOpKind.Copy or EntityOpKind.Array or EntityOpKind.Offset => "tạo",
                EntityOpKind.Mirror when op.KeepOriginal != false => "tạo",
                _ => "sửa",
            };
            var count = verb == "tạo" ? produced.Count : targets.Count;
            var sample = verb == "tạo" ? produced : targets.Select(e => e.ObjectId).ToList();
            Report.Add($"{step.Label}: {verb} {count} đối tượng ({string.Join(", ", sample.Take(10).Select(AcadHelpers.HandleOf))}{(sample.Count > 10 ? ", …" : "")}).");
            return produced;
        }

        private void Transform(List<Entity> targets, Matrix3d matrix)
        {
            foreach (var entity in targets)
            {
                entity.UpgradeOpen();
                entity.TransformBy(matrix);
                MarkModified(entity);
            }
        }

        private void MarkModified(Entity entity)
        {
            if (!Created.Contains(entity.ObjectId)) Modified.Add(entity.ObjectId);
        }

        /// <summary>
        /// Mảnh của một đối tượng khi phá (chưa vào database — người gọi thêm vào hoặc Dispose). Block: một cấp như
        /// EXPLODE; attribute hiện thấy thành TEXT/MTEXT mang giá trị (như BURST) thay vì hiện tên tag; xref và MINSERT
        /// không phá được.
        /// </summary>
        private List<Entity> ExplodePieces(Transaction tr, EntityEditStep step, Entity entity)
        {
            var handle = AcadHelpers.HandleOf(entity.ObjectId);
            if (entity is MInsertBlock)
            {
                throw new StepException(step, $"{handle} là MINSERT — AutoCAD không phá được.");
            }

            if (entity is BlockReference reference
                && tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord definitionRecord
                && (definitionRecord.IsFromExternalReference || definitionRecord.IsFromOverlayReference))
            {
                throw new StepException(step, $"{handle} là xref — không phá được; bind xref trước nếu thật sự cần.");
            }

            var collection = new DBObjectCollection();
            try
            {
                entity.Explode(collection);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                foreach (DBObject leftover in collection) leftover.Dispose();
                throw new StepException(step, $"{handle} ({entity.GetRXClass().DxfName}) không phá được ({ex.ErrorStatus}).");
            }

            var pieces = new List<Entity>();
            foreach (DBObject obj in collection)
            {
                switch (obj)
                {
                    // Định nghĩa attribute thường hiện TÊN TAG sau EXPLODE — bỏ; giá trị thật lấy từ AttributeReference bên dưới.
                    // Attribute hằng (Constant) không có AttributeReference nên đổi thẳng thành chữ.
                    case AttributeDefinition definition:
                        if (definition.Constant && !definition.Invisible && !string.IsNullOrEmpty(definition.TextString))
                        {
                            pieces.Add(TextOf(definition));
                        }

                        definition.Dispose();
                        break;
                    case Entity piece:
                        pieces.Add(piece);
                        break;
                    default:
                        obj.Dispose();
                        break;
                }
            }

            if (entity is BlockReference block)
            {
                var hidden = new List<string>();
                foreach (ObjectId attId in block.AttributeCollection)
                {
                    if (attId.IsErased || tr.GetObject(attId, OpenMode.ForRead) is not AttributeReference attribute
                        || string.IsNullOrEmpty(attribute.TextString))
                    {
                        continue;
                    }

                    if (attribute.Invisible)
                    {
                        hidden.Add($"{attribute.Tag}={attribute.TextString}");
                    }
                    else
                    {
                        pieces.Add(TextOf(attribute));
                    }
                }

                // Attribute ẩn mang dữ liệu (mã, kích thước…) nhưng không có hình — phá xong là mất. Nói ra để người gọi
                // xuất trước bằng AttributeExport nếu cần.
                if (hidden.Count > 0)
                {
                    Report.Add($"{step.Label}: {handle} bỏ {hidden.Count} attribute ẩn ({string.Join("; ", hidden.Take(5))}{(hidden.Count > 5 ? "; …" : "")}) — xuất bằng AttributeExport trước nếu cần giữ.");
                }
            }

            return pieces;
        }

        /// <summary>Attribute → TEXT/MTEXT đứng y chỗ, cùng style/cỡ/canh lề/layer/màu.</summary>
        private static Entity TextOf(DBText source)
        {
            MText? mtext = source switch
            {
                AttributeReference { IsMTextAttribute: true } reference => reference.MTextAttribute,
                AttributeDefinition { IsMTextAttributeDefinition: true } definition => definition.MTextAttributeDefinition,
                _ => null,
            };
            if (mtext != null)
            {
                mtext.SetPropertiesFrom(source);
                return mtext;
            }

            var text = new DBText();
            text.SetPropertiesFrom(source);
            text.Normal = source.Normal;
            text.Thickness = source.Thickness;
            text.TextStyleId = source.TextStyleId;
            text.Height = source.Height;
            text.WidthFactor = source.WidthFactor;
            text.Oblique = source.Oblique;
            text.Rotation = source.Rotation;
            text.IsMirroredInX = source.IsMirroredInX;
            text.IsMirroredInY = source.IsMirroredInY;
            text.TextString = source.TextString;
            text.HorizontalMode = source.HorizontalMode;
            text.VerticalMode = source.VerticalMode;
            text.Position = source.Position;
            if (source.HorizontalMode != TextHorizontalMode.TextLeft || source.VerticalMode != TextVerticalMode.TextBase)
            {
                text.AlignmentPoint = source.AlignmentPoint;
            }

            return text;
        }

        /// <summary>Layer đích của một mảnh phá ra: theo layerMap, không khớp thì theo layer chung, không có thì giữ (null).</summary>
        private static string? ExplodeLayer(EntityOperation op, string pieceLayer)
        {
            if (op.LayerMap != null)
            {
                foreach (var pair in op.LayerMap)
                {
                    if (string.Equals(pair.Key.Trim(), pieceLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        return pair.Value.Trim();
                    }
                }
            }

            return string.IsNullOrWhiteSpace(op.Layer) ? null : op.Layer!.Trim();
        }

        /// <summary><c>set width</c> chỉ có nghĩa với polyline (LWPOLYLINE hoặc POLYLINE 2D).</summary>
        private static void CheckWidthTargets(EntityEditStep step, List<Entity> targets)
        {
            if (step.Source.Width == null) return;
            var wrong = targets.FirstOrDefault(e => e is not Polyline and not Polyline2d);
            if (wrong != null)
            {
                throw new StepException(step, $"{AcadHelpers.HandleOf(wrong.ObjectId)} là {wrong.GetRXClass().DxfName} — width chỉ đặt cho polyline.");
            }
        }

        /// <summary>replacePoints sửa đúng một polyline — @nhãn trỏ tới nhiều đối tượng thì dừng, tránh ghi đè hàng loạt.</summary>
        private static void CheckSingleTarget(EntityEditStep step, int count)
        {
            if (step.Kind == EntityOpKind.ReplacePoints && count != 1)
            {
                throw new StepException(step, $"replacePoints cần đúng một polyline, nhận {count} đối tượng.");
            }
        }

        /// <summary>
        /// trim/replacePoints trên LWPOLYLINE (và trim trên LINE): tính đỉnh mới bằng <see cref="PolylineVertexEdit"/>,
        /// ghi tại chỗ khi <paramref name="write"/> — giữ handle, layer, xdata. Xem trước gọi với write=false để lỗi lộ sớm.
        /// </summary>
        private void EditVertices(EntityEditStep step, Entity entity, bool write)
        {
            var op = step.Source;
            var handle = AcadHelpers.HandleOf(entity.ObjectId);
            if (entity is Line line && step.Kind == EntityOpKind.Trim)
            {
                var ends = new List<PlineVertex> { new(line.StartPoint.X, line.StartPoint.Y), new(line.EndPoint.X, line.EndPoint.Y) };
                var cut = PolylineVertexEdit.Trim(ends, false, op.From, op.To);
                if (!cut.Ok) throw new StepException(step, $"{handle}: {cut.Error}");
                Report.Add($"{step.Label}: {handle} {TrimNote(cut)}");
                if (!write) return;
                var dz = line.EndPoint.Z - line.StartPoint.Z;
                var z0 = line.StartPoint.Z + (dz * cut.Start!.Value.Fraction);
                var z1 = line.StartPoint.Z + (dz * cut.End!.Value.Fraction);
                line.UpgradeOpen();
                line.StartPoint = new Point3d(cut.Vertices[0].X, cut.Vertices[0].Y, z0);
                line.EndPoint = new Point3d(cut.Vertices[1].X, cut.Vertices[1].Y, z1);
                return;
            }

            if (entity is not Polyline pline)
            {
                var hint = entity is Polyline2d or Polyline3d ? " — đổi sang LWPOLYLINE (CONVERTPOLY) trước" : string.Empty;
                var accepts = step.Kind == EntityOpKind.Trim ? "LWPOLYLINE và LINE" : "LWPOLYLINE";
                throw new StepException(step, $"{handle} là {entity.GetRXClass().DxfName} — {step.Kind.ToString().ToLowerInvariant()} chỉ nhận {accepts}{hint}.");
            }

            if (!pline.Normal.IsParallelTo(Vector3d.ZAxis))
            {
                throw new StepException(step, $"{handle} không nằm trong mặt phẳng XY — không sửa đỉnh theo toạ độ [x, y].");
            }

            var current = new List<PlineVertex>(pline.NumberOfVertices);
            for (var i = 0; i < pline.NumberOfVertices; i++)
            {
                var p = pline.GetPoint2dAt(i);
                current.Add(new PlineVertex(p.X, p.Y, pline.GetBulgeAt(i), pline.GetStartWidthAt(i), pline.GetEndWidthAt(i)));
            }

            List<PlineVertex> next;
            var closed = pline.Closed;
            if (step.Kind == EntityOpKind.Trim)
            {
                var cut = PolylineVertexEdit.Trim(current, pline.Closed, op.From, op.To);
                if (!cut.Ok) throw new StepException(step, $"{handle}: {cut.Error}");
                Report.Add($"{step.Label}: {handle} {TrimNote(cut)}");
                next = cut.Vertices;
            }
            else
            {
                var uniform = PolylineVertexEdit.UniformWidth(current, pline.Closed);
                next = PolylineVertexEdit.FromPoints(op.Points!, op.Width ?? uniform ?? 0);
                closed = (op.Closed ?? pline.Closed) && next.Count > 2;
                var widthNote = op.Width == null && uniform == null ? " (bề rộng cũ không đều — đặt về 0; truyền width nếu cần)" : string.Empty;
                Report.Add($"{step.Label}: {handle} {current.Count} → {next.Count} đỉnh{widthNote}.");
            }

            if (!write) return;

            // Thêm đỉnh mới vào cuối rồi mới bỏ đỉnh cũ: polyline trong database không bao giờ còn dưới 2 đỉnh.
            pline.UpgradeOpen();
            var old = pline.NumberOfVertices;
            foreach (var v in next)
            {
                pline.AddVertexAt(pline.NumberOfVertices, new Point2d(v.X, v.Y), v.Bulge, v.StartWidth, v.EndWidth);
            }

            for (var i = 0; i < old; i++)
            {
                pline.RemoveVertexAt(0);
            }

            pline.Closed = closed;
        }

        private static string TrimNote(PlineTrimResult cut)
        {
            var s = cut.Start!.Value;
            var e = cut.End!.Value;
            var drift = Math.Max(s.Distance, e.Distance);
            var driftNote = drift > 1e-6
                ? $" (from/to cách đường {drift.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} — đã chiếu lên đường)"
                : string.Empty;
            return $"giữ đoạn {EntityEditPlanner.Format(new[] { s.X, s.Y })} → {EntityEditPlanner.Format(new[] { e.X, e.Y })}, còn {cut.Vertices.Count} đỉnh{driftNote}.";
        }

        private static ObjectId CopyOf(Transaction tr, EntityEditStep step, Entity source, Matrix3d matrix)
        {
            Entity copy;
            try
            {
                copy = source.GetTransformedCopy(matrix);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new StepException(step, $"không sao chép được {AcadHelpers.HandleOf(source.ObjectId)} ({ex.ErrorStatus}).");
            }

            var owner = (BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForWrite);
            var id = owner.AppendEntity(copy);
            tr.AddNewlyCreatedDBObject(copy, true);
            return id;
        }

        /// <summary>Handle hex hoặc <c>@nhãn</c> → entity mở ForRead; đối tượng phải nằm thẳng trong layout và không trên layer khoá.</summary>
        private List<Entity> Resolve(Transaction tr, EntityEditStep step, out int pending)
        {
            pending = 0;
            var result = new List<Entity>();
            var seen = new HashSet<ObjectId>();
            foreach (var raw in step.Source.Handles!)
            {
                var text = raw.Trim();
                IEnumerable<ObjectId> ids;
                if (text.StartsWith("@", StringComparison.Ordinal))
                {
                    ids = Labels.TryGetValue(text.Substring(1), out var labelled) ? labelled : Enumerable.Empty<ObjectId>();
                }
                else
                {
                    HandleText.TryParse(text, out var value);
                    ObjectId id;
                    try
                    {
                        id = _db.GetObjectId(false, new Handle(value), 0);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        throw new StepException(step, $"handle {text} không có trong bản vẽ.");
                    }

                    ids = new[] { id };
                }

                foreach (var id in ids)
                {
                    // Đối tượng "sẽ tạo" của xem trước (chỉ có khi dryRun) — đếm, không mở.
                    if (id.IsNull)
                    {
                        pending++;
                        continue;
                    }

                    if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
                    {
                        throw new StepException(step, $"{text} không phải đối tượng còn trong bản vẽ (đã xoá, hoặc không phải entity).");
                    }

                    if (tr.GetObject(entity.OwnerId, OpenMode.ForRead) is not BlockTableRecord { IsLayout: true })
                    {
                        throw new StepException(step, $"{text} nằm trong định nghĩa block — sửa block bằng BEDIT, không sửa từng đối tượng bên trong.");
                    }

                    var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                    if (layer.IsLocked)
                    {
                        throw new StepException(step, $"{text} nằm trên layer khoá \"{layer.Name}\".");
                    }

                    if (seen.Add(id)) result.Add(entity);
                }
            }

            return result;
        }

        /// <summary>Layer cho đối tượng mới: rỗng = layer hiện hành; chưa có thì tạo; khoá thì dừng lô.</summary>
        private ObjectId Layer(Transaction tr, EntityEditStep step, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return _db.Clayer;
            }

            name = name!.Trim();
            if (_layers.TryGetValue(name, out var cached))
            {
                return cached;
            }

            if (!AcadHelpers.IsValidSymbolName(name))
            {
                throw new StepException(step, $"tên layer \"{name}\" không hợp lệ với AutoCAD.");
            }

            var table = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            ObjectId id;
            if (table.Has(name))
            {
                id = table[name];
                var record = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (record.IsLocked)
                {
                    throw new StepException(step, $"layer \"{name}\" đang khoá.");
                }
            }
            else
            {
                table.UpgradeOpen();
                var record = new LayerTableRecord { Name = name };
                id = table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
                Report.Add($"Đã tạo layer \"{name}\".");
            }

            _layers[name] = id;
            return id;
        }

        private static Polyline Pline(List<(double X, double Y)> points, bool closed, double width, double elevation)
        {
            var pline = new Polyline(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                pline.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, width, width);
            }

            pline.Closed = closed && points.Count > 2;
            pline.Elevation = elevation;
            return pline;
        }

        private static double Elevation(double[] p) => EntityEditPlanner.Z(p);

        private static Point3d P(double[] p) => new(p[0], p[1], EntityEditPlanner.Z(p));

        private static Vector3d V(double[] p) => new(p[0], p[1], EntityEditPlanner.Z(p));
    }
}
