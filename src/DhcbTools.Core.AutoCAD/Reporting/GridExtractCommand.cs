using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>
/// Trích các đường trục trên layer trục (mặc định "AXIS") trong Model Space ra CSV cho lệnh
/// GridFromCsv bên Revit.
/// <para>
/// Nhận <see cref="Line"/>, <see cref="Polyline"/> (mỗi đoạn thẳng là một trục; bỏ đoạn cung),
/// <see cref="Xline"/> và <see cref="Ray"/> — hai loại sau vô hạn nên được cắt theo phạm vi các trục hữu hạn
/// khác trên cùng layer (không có thì lấy một đoạn quanh điểm gốc). Trước đây chỉ nhận Line, nên bản vẽ
/// vẽ trục bằng polyline/xline ra CSV rỗng mà lệnh vẫn báo lỗi "không có Line" gây hiểu nhầm là sai layer.
/// </para>
/// <para>
/// Tên trục: lấy từ DBText/MText gần đầu mút nhất TRÊN CÙNG LAYER (bubble trục thường nằm ngay đầu trục);
/// không tìm được thì giữ nguyên quy ước cũ "AXIS-n".
/// </para>
/// </summary>
public sealed class GridExtractCommand : ICoreCommand<GridExtractConfig>
{
    private sealed record Axis(Point3d Start, Point3d End);

    public string CommandName => "GridExtract";

    public CommandResult Execute(Database database, GridExtractConfig config)
    {
        var gridLayer = string.IsNullOrWhiteSpace(config.GridLayer) ? "AXIS" : config.GridLayer;

        var axes = new List<Axis>();
        var infinite = new List<(Point3d Base, Vector3d Direction)>();
        var labels = new List<(string Text, Point3d Position)>();

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

            foreach (ObjectId entityId in modelSpace)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead) is not Entity entity
                    || !string.Equals(entity.Layer, gridLayer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                switch (entity)
                {
                    case Line line:
                        AddIfNotDegenerate(axes, line.StartPoint, line.EndPoint);
                        break;

                    case Polyline polyline:
                        for (var i = 0; i < polyline.NumberOfVertices - 1; i++)
                        {
                            // Đoạn cung (bulge ≠ 0) không phải trục thẳng — bỏ.
                            if (Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                            {
                                continue;
                            }

                            AddIfNotDegenerate(axes, polyline.GetPoint3dAt(i), polyline.GetPoint3dAt(i + 1));
                        }

                        if (polyline.Closed && polyline.NumberOfVertices > 1 && Math.Abs(polyline.GetBulgeAt(polyline.NumberOfVertices - 1)) <= 1e-9)
                        {
                            AddIfNotDegenerate(axes, polyline.GetPoint3dAt(polyline.NumberOfVertices - 1), polyline.GetPoint3dAt(0));
                        }

                        break;

                    case Xline xline:
                        infinite.Add((xline.BasePoint, xline.UnitDir));
                        break;

                    case Ray ray:
                        infinite.Add((ray.BasePoint, ray.UnitDir));
                        break;

                    case DBText text:
                        labels.Add((text.TextString, text.Position));
                        break;

                    case MText mtext:
                        labels.Add((mtext.Text, mtext.Location));
                        break;
                }
            }

            transaction.Commit();
        }

        // Xline/Ray vô hạn: cắt theo phạm vi các trục hữu hạn đã có (hoặc ±10 000 quanh điểm gốc).
        if (infinite.Count > 0)
        {
            var half = axes.Count > 0 ? SpanOf(axes) : 10000.0;
            foreach (var (basePoint, direction) in infinite)
            {
                if (direction.Length < 1e-9)
                {
                    continue;
                }

                var unit = direction.GetNormal();
                axes.Add(new Axis(basePoint - unit * half, basePoint + unit * half));
            }
        }

        if (axes.Count == 0)
        {
            return CommandResult.Fail(
                $"Không tìm thấy trục nào (Line, Polyline, Xline, Ray) trên layer \"{gridLayer}\".");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Name,StartX,StartY,EndX,EndY");

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var axis in axes)
        {
            count++;
            var name = NameFor(axis, labels, used) ?? "AXIS-" + count;
            used.Add(name);

            sb.Append(CsvText.JoinLine(new[]
            {
                name,
                NumericText.Format(axis.Start.X),
                NumericText.Format(axis.Start.Y),
                NumericText.Format(axis.End.X),
                NumericText.Format(axis.End.Y),
            })).Append('\n');
        }

        AcadHelpers.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        return CommandResult.Ok(
            $"Đã trích {count} trục từ layer \"{gridLayer}\" ra \"{config.OutputPath}\".",
            count);
    }

    private static void AddIfNotDegenerate(List<Axis> axes, Point3d start, Point3d end)
    {
        if (start.DistanceTo(end) > 1e-9)
        {
            axes.Add(new Axis(start, end));
        }
    }

    private static double SpanOf(List<Axis> axes)
    {
        var minX = axes.Min(a => Math.Min(a.Start.X, a.End.X));
        var maxX = axes.Max(a => Math.Max(a.Start.X, a.End.X));
        var minY = axes.Min(a => Math.Min(a.Start.Y, a.End.Y));
        var maxY = axes.Max(a => Math.Max(a.Start.Y, a.End.Y));
        return Math.Max(1000.0, Math.Max(maxX - minX, maxY - minY));
    }

    /// <summary>
    /// Tên trục từ text gần nhất một trong hai đầu mút, trong bán kính 10 % chiều dài trục (tối thiểu 1000).
    /// Text đã dùng cho trục khác thì không dùng lại — bubble hai đầu cùng tên là bình thường nhưng hai trục
    /// cùng tên thì Revit sẽ từ chối.
    /// </summary>
    private static string? NameFor(Axis axis, List<(string Text, Point3d Position)> labels, HashSet<string> used)
    {
        if (labels.Count == 0)
        {
            return null;
        }

        var length = axis.Start.DistanceTo(axis.End);
        var radius = Math.Max(1000.0, length * 0.1);

        string? best = null;
        var bestDistance = double.MaxValue;

        foreach (var (text, position) in labels)
        {
            var name = (text ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 16 || used.Contains(name))
            {
                continue;
            }

            var distance = Math.Min(position.DistanceTo(axis.Start), position.DistanceTo(axis.End));
            if (distance <= radius && distance < bestDistance)
            {
                bestDistance = distance;
                best = name;
            }
        }

        return best;
    }
}
