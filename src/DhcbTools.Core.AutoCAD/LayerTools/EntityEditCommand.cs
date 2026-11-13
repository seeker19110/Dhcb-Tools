using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic.Cad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Vẽ và sửa hình học bằng một lô thao tác JSON — "cây bút" cho agent: line, polyline, rectangle, circle, arc,
/// ellipse, text, mtext, hatch, point (tạo trong Model Space) và move, copy, array, rotate, scale, mirror, offset,
/// erase (sửa đối tượng có sẵn theo handle, hoặc đối tượng op trước vừa tạo qua <c>"@id"</c>).
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
                    text.TextStyleId = _db.Textstyle;
                    return text;
                }
                case EntityOpKind.MText:
                {
                    var mtext = new MText { Contents = op.Text, Location = P(op.Position!), Rotation = EntityEditPlanner.Radians(op.Rotation ?? 0) };
                    mtext.TextHeight = op.Height ?? _db.Textsize;
                    mtext.TextStyleId = _db.Textstyle;
                    if (op.Width is double width) mtext.Width = width;
                    return mtext;
                }
                case EntityOpKind.Hatch:
                    return new Hatch { Normal = Vector3d.ZAxis, Elevation = Elevation(op.Points![0]) };
                case EntityOpKind.Point:
                    return new DBPoint(P(op.Position!));
                default:
                    throw new StepException(step, "op không phải op tạo.");
            }
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
                if (!Created.Contains(entity.ObjectId)) Modified.Add(entity.ObjectId);
            }
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
