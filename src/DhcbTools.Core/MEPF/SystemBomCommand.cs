using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>P2 — BOM theo hệ/spool (Victaulic Procurement, Naviate spool BOM): ống/duct/tray theo chiều dài, fitting/phụ kiện theo số lượng.</summary>
public sealed class SystemBomConfig
{
    public required string OutputPath { get; init; }

    /// <summary>Lọc System Name chứa chuỗi (rỗng = tất cả).</summary>
    public string? SystemContains { get; init; }

    /// <summary>Tham số dùng làm mã spool/khu vực (ví dụ "DHCB_Spool" hoặc "Comments"); rỗng = không chia spool.</summary>
    public string? SpoolParameter { get; init; }

    /// <summary>Categories (rỗng = Pipes, Pipe Fittings, Pipe Accessories, Ducts, Duct Fittings, Duct Accessories, Cable Trays, Conduits, Mechanical Equipment, Plumbing Fixtures, Sprinklers).</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Chiều dài cây ống (mm) để tính số cây đặt hàng.</summary>
    public double StockLengthMm { get; init; } = 6000;

    public double WastePercent { get; init; } = 5;
}

public sealed class SystemBomCommand : ICoreCommand<SystemBomConfig>
{
    public string CommandName => "SystemBom";

    private static readonly BuiltInCategory[] DefaultCats =
    {
        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers,
    };

    public CommandResult Execute(Document document, SystemBomConfig config)
    {
        ICollection<ElementId> catIds;
        if (config.Categories.Count > 0)
        {
            catIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
            if (catIds.Count == 0) return CommandResult.Fail("Category không có: " + string.Join(", ", unknown));
        }
        else
        {
            catIds = DefaultCats.Select(c => new ElementId(c)).ToList();
        }

        var elements = new FilteredElementCollector(document).WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(catIds.ToList())).ToElements();

        var items = new List<BomItem>();
        var skipped = 0;
        foreach (var e in elements)
        {
            var system = RevitCompat.ReadString(e, "System Name");
            if (string.IsNullOrEmpty(system)) system = RevitCompat.ReadString(e, "System Type");
            if (!string.IsNullOrEmpty(config.SystemContains) && system.IndexOf(config.SystemContains!, StringComparison.OrdinalIgnoreCase) < 0)
            {
                skipped++;
                continue;
            }

            var typeName = document.GetElement(e.GetTypeId()) is ElementType t ? (t.FamilyName + (string.IsNullOrEmpty(t.FamilyName) ? string.Empty : ": ") + t.Name) : e.Name;
            var size = RevitCompat.ReadString(e, "Size");
            if (string.IsNullOrEmpty(size)) size = RevitCompat.ReadString(e, "Diameter");
            double? lengthMm = null;
            if (e is MEPCurve)
            {
                var lp = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (lp != null && lp.HasValue) lengthMm = RevitCompat.FtToMm(lp.AsDouble());
            }
            var spool = string.IsNullOrEmpty(config.SpoolParameter) ? string.Empty : RevitCompat.ReadString(e, config.SpoolParameter!);
            items.Add(new BomItem(system, e.Category?.Name ?? string.Empty, typeName, size, lengthMm, RevitCompat.IdValue(e.Id).ToString(), spool));
        }

        if (items.Count == 0)
        {
            return CommandResult.Fail("Không có phần tử MEP nào khớp bộ lọc.");
        }

        var rows = BomAggregator.Aggregate(items);
        var dir = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(config.OutputPath, BomAggregator.ToCsv(rows, config.StockLengthMm, config.WastePercent), CsvText.Utf8WithBom);

        var result = CommandResult.Ok($"BOM {items.Count} phần tử → {rows.Count} dòng → \"{config.OutputPath}\"" + (skipped > 0 ? $" ({skipped} phần tử ngoài bộ lọc hệ)." : "."), items.Count);
        foreach (var kv in BomAggregator.TotalsBySystem(rows).OrderByDescending(k => k.Value.LengthMm).Take(40))
        {
            result.Messages.Add($"{(kv.Key.Length == 0 ? "(không hệ)" : kv.Key)}: {kv.Value.Count} phần tử, {kv.Value.LengthMm / 1000:F1} m");
        }
        return result;
    }
}
