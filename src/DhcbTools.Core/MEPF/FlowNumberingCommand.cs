using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>Mục 3.5 — đánh số theo thứ tự dòng chảy từ thiết bị nguồn.</summary>
public sealed class FlowNumberingConfig
{
    /// <summary>ElementId thiết bị nguồn (AHU, tủ điện, bơm).</summary>
    public required long SourceElementId { get; init; }

    public string ParameterName { get; init; } = "Mark";

    public string Prefix { get; init; } = string.Empty;

    public int PadWidth { get; init; } = 0;

    /// <summary>true: đi hết nhánh (DFS); false: theo lớp (BFS).</summary>
    public bool DepthFirst { get; init; } = true;

    /// <summary>Chỉ đánh số các category này (rỗng = mọi phần tử nối được, trừ fitting nếu <see cref="SkipFittings"/>).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    public bool SkipFittings { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

/// <summary>Dựng đồ thị connector (cạnh = hai phần tử nối nhau), giao cho <see cref="FlowNumbering"/> gán nhãn, ghi tham số.</summary>
public sealed class FlowNumberingCommand : ICoreCommand<FlowNumberingConfig>
{
    public string CommandName => "FlowNumbering";

    public CommandResult Execute(Document document, FlowNumberingConfig config)
    {
        var sourceId = RevitCompat.MakeId(config.SourceElementId);
        var source = document.GetElement(sourceId);
        if (source == null)
        {
            return CommandResult.Fail($"Không tìm thấy phần tử nguồn {config.SourceElementId}.");
        }

        // BFS trên connector từ nguồn để chỉ lấy thành phần liên thông chứa nguồn.
        var edges = new List<Tuple<long, long>>();
        var visited = new HashSet<ElementId> { sourceId };
        var queue = new Queue<Element>();
        queue.Enqueue(source);
        var elements = new Dictionary<long, Element> { [config.SourceElementId] = source };

        while (queue.Count > 0)
        {
            var el = queue.Dequeue();
            var cm = el is MEPCurve mc ? mc.ConnectorManager : (el as FamilyInstance)?.MEPModel?.ConnectorManager;
            if (cm == null)
            {
                continue;
            }

            foreach (Connector c in cm.Connectors)
            {
                if (!c.IsConnected)
                {
                    continue;
                }

                foreach (Connector other in c.AllRefs)
                {
                    var owner = other.Owner;
                    if (owner == null || owner.Id == el.Id || owner is MEPSystem)
                    {
                        continue;
                    }

                    edges.Add(Tuple.Create(RevitCompat.IdValue(el.Id), RevitCompat.IdValue(owner.Id)));
                    if (visited.Add(owner.Id))
                    {
                        elements[RevitCompat.IdValue(owner.Id)] = owner;
                        queue.Enqueue(owner);
                    }
                }
            }
        }

        if (edges.Count == 0)
        {
            return CommandResult.Fail("Nguồn không nối với phần tử nào qua connector.");
        }

        // Ổn định thứ tự nhánh theo toạ độ (X rồi Y) để chạy lại cho cùng kết quả.
        var comparer = Comparer<long>.Create((a, b) =>
        {
            var pa = Centre(elements[a]);
            var pb = Centre(elements[b]);
            var c = pa.X.CompareTo(pb.X);
            return c != 0 ? c : pa.Y.CompareTo(pb.Y);
        });

        var labels = FlowNumbering.Assign(edges, config.SourceElementId, config.Prefix, config.PadWidth, config.DepthFirst, comparer);

        var allowed = config.Categories.Count == 0 ? null : ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out _);
        var plan = new List<(Element Element, string Label)>();
        foreach (var l in labels)
        {
            var el = elements[l.Key];
            if (config.SkipFittings && IsFitting(el))
            {
                continue;
            }

            if (allowed != null && (el.Category == null || !allowed.Contains(el.Category.Id)))
            {
                continue;
            }

            plan.Add((el, l.Label));
        }

        var result = CommandResult.Ok(string.Empty);
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ ghi {plan.Count} nhãn vào \"{config.ParameterName}\" theo dòng chảy từ {config.SourceElementId} (đồ thị {elements.Count} phần tử).";
            result.Messages.AddRange(plan.Select(p => $"{p.Element.Id} ({p.Element.Category?.Name}): {p.Label}"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var written = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Đánh số theo dòng chảy");
        foreach (var (el, label) in plan)
        {
            var err = RevitCompat.TrySetString(el, config.ParameterName, label);
            if (err != null)
            {
                result.Messages.Add($"Bỏ qua {el.Id}: {err}.");
                continue;
            }
            written++;
        }

        tx.Commit();
        result.Summary = $"Đã ghi {written}/{plan.Count} nhãn theo dòng chảy.";
        result.AffectedCount = written;
        return result;
    }

    private static bool IsFitting(Element el)
    {
        if (el.Category == null) return false;
        var bic = (BuiltInCategory)RevitCompat.IdValue(el.Category.Id);
        return bic is BuiltInCategory.OST_DuctFitting or BuiltInCategory.OST_PipeFitting or BuiltInCategory.OST_CableTrayFitting or BuiltInCategory.OST_ConduitFitting;
    }

    private static XYZ Centre(Element el)
    {
        if (el.Location is LocationPoint lp) return lp.Point;
        if (el.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
        var bb = el.get_BoundingBox(null);
        return bb == null ? XYZ.Zero : (bb.Min + bb.Max) / 2;
    }
}
