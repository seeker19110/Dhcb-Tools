using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Ids;

namespace DhcbTools.Core.Checks;

/// <summary>
/// Một phần tử Revit nhìn dưới con mắt IDS. Đây là <b>toàn bộ</b> chỗ dịch giữa hai thế giới; luật kiểm
/// nằm ở tầng thuần <see cref="IdsEvaluator"/> nên có test trên CI.
/// </summary>
internal sealed class RevitIdsElement : IIdsElement
{
    private static readonly Dictionary<BuiltInCategory, string> IfcByCategory = new()
    {
        [BuiltInCategory.OST_Walls] = "IfcWall",
        [BuiltInCategory.OST_Floors] = "IfcSlab",
        [BuiltInCategory.OST_Roofs] = "IfcRoof",
        [BuiltInCategory.OST_Doors] = "IfcDoor",
        [BuiltInCategory.OST_Windows] = "IfcWindow",
        [BuiltInCategory.OST_Columns] = "IfcColumn",
        [BuiltInCategory.OST_StructuralColumns] = "IfcColumn",
        [BuiltInCategory.OST_StructuralFraming] = "IfcBeam",
        [BuiltInCategory.OST_StructuralFoundation] = "IfcFooting",
        [BuiltInCategory.OST_Stairs] = "IfcStair",
        [BuiltInCategory.OST_Ceilings] = "IfcCovering",
        [BuiltInCategory.OST_Rooms] = "IfcSpace",
        [BuiltInCategory.OST_CurtainWallPanels] = "IfcPlate",
        [BuiltInCategory.OST_PipeCurves] = "IfcPipeSegment",
        [BuiltInCategory.OST_PipeFitting] = "IfcPipeFitting",
        [BuiltInCategory.OST_DuctCurves] = "IfcDuctSegment",
        [BuiltInCategory.OST_DuctFitting] = "IfcDuctFitting",
        [BuiltInCategory.OST_DuctTerminal] = "IfcAirTerminal",
        [BuiltInCategory.OST_CableTray] = "IfcCableCarrierSegment",
        [BuiltInCategory.OST_Conduit] = "IfcCableCarrierSegment",
        [BuiltInCategory.OST_MechanicalEquipment] = "IfcUnitaryEquipment",
        [BuiltInCategory.OST_PlumbingFixtures] = "IfcSanitaryTerminal",
        [BuiltInCategory.OST_Sprinklers] = "IfcFireSuppressionTerminal",
        [BuiltInCategory.OST_ElectricalEquipment] = "IfcElectricDistributionBoard",
        [BuiltInCategory.OST_ElectricalFixtures] = "IfcElectricAppliance",
        [BuiltInCategory.OST_LightingFixtures] = "IfcLightFixture",
        [BuiltInCategory.OST_GenericModel] = "IfcBuildingElementProxy",
    };

    private readonly Document _document;
    private readonly Element _element;
    private readonly Element? _type;

    internal RevitIdsElement(Document document, Element element)
    {
        _document = document;
        _element = element;
        _type = document.GetElement(element.GetTypeId());
    }

    public string Label => $"{RevitCompat.IdValue(_element.Id)} — {_element.Category?.Name} \"{_element.Name}\"";

    /// <summary>
    /// Lớp IFC của phần tử. Ưu tiên tham số <c>IfcExportAs</c> (instance rồi type) đúng như bộ xuất IFC
    /// của Revit làm — khai đè ở đó là cách kỹ sư chỉnh ánh xạ cho từng đối tượng; không có thì tra bảng
    /// theo category.
    /// </summary>
    public string IfcEntity
    {
        get
        {
            var declared = TextOf(_element, "IfcExportAs") ?? (_type != null ? TextOf(_type, "IfcExportAs") : null);
            if (!string.IsNullOrWhiteSpace(declared))
            {
                // Dạng "IfcWall.SOLIDWALL" hoặc "IfcWallType" — phần trước dấu chấm là lớp.
                var name = declared!.Split('.')[0].Trim();
                if (name.Length > 0 && !name.Equals("DontExport", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            if (_element.Category == null)
            {
                return string.Empty;
            }

            var id = RevitCompat.IdValue(_element.Category.Id);
            return IfcByCategory.TryGetValue((BuiltInCategory)id, out var mapped) ? mapped : string.Empty;
        }
    }

    /// <summary>PredefinedType: phần sau dấu chấm của <c>IfcExportAs</c>, hoặc tham số <c>IfcExportType</c>.</summary>
    public string PredefinedType
    {
        get
        {
            var declared = TextOf(_element, "IfcExportAs") ?? (_type != null ? TextOf(_type, "IfcExportAs") : null);
            if (!string.IsNullOrWhiteSpace(declared) && declared!.Contains('.'))
            {
                return declared.Substring(declared.IndexOf('.') + 1).Trim();
            }

            return TextOf(_element, "IfcExportType") ?? (_type != null ? TextOf(_type, "IfcExportType") ?? string.Empty : string.Empty);
        }
    }

    /// <summary>
    /// Thuộc tính IFC. Ánh xạ những cái bộ xuất Revit thật sự điền: <c>Name</c> = tên type/phần tử,
    /// <c>Tag</c> = Mark, <c>Description</c> = mô tả của type.
    /// </summary>
    public string? Attribute(string name)
    {
        switch ((name ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "name":
                return _element.Name;
            case "tag":
                return TextOf(_element, "Mark");
            case "description":
                return TextOf(_element, "Description") ?? (_type != null ? TextOf(_type, "Description") : null);
            case "objecttype":
                return _type?.Name;
            case "globalid":
                return _element.UniqueId;
            default:
                // Thuộc tính lạ: thử luôn như một tham số cùng tên, rồi mới chịu thua. Trả rỗng khác
                // hẳn trả "" ngầm hiểu là đạt — IdsValue.Accepts coi rỗng là KHÔNG đạt.
                return TextOf(_element, name) ?? (_type != null ? TextOf(_type, name) : null);
        }
    }

    /// <summary>
    /// Property theo property set. Revit không giữ Pset như IFC, nên tra theo <b>tên tham số</b>:
    /// trước hết "Pset_Tên.Prop" (cách khai của bộ xuất qua file mapping), sau đó chính tên property ở
    /// instance rồi ở type — đúng thứ tự bộ xuất IFC lấy giá trị.
    /// </summary>
    public string? Property(string? propertySet, string name)
    {
        if (!string.IsNullOrWhiteSpace(propertySet))
        {
            var qualified = TextOf(_element, propertySet + "." + name) ?? (_type != null ? TextOf(_type, propertySet + "." + name) : null);
            if (qualified != null)
            {
                return qualified;
            }
        }

        return TextOf(_element, name) ?? (_type != null ? TextOf(_type, name) : null);
    }

    /// <summary>Mã phân loại: Assembly Code và Keynote — hai chỗ Revit thật sự chở mã phân loại.</summary>
    public IEnumerable<string> Classifications(string? system)
    {
        foreach (var key in new[] { "Assembly Code", "Keynote", "ClassificationCode" })
        {
            var value = TextOf(_element, key) ?? (_type != null ? TextOf(_type, key) : null);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value!;
            }
        }
    }

    /// <summary>Vật liệu của phần tử (kể cả vật liệu của lớp cấu tạo).</summary>
    public IEnumerable<string> Materials
    {
        get
        {
            ICollection<ElementId> ids;
            try
            {
                ids = _element.GetMaterialIds(false);
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var id in ids)
            {
                if (_document.GetElement(id) is Material material)
                {
                    yield return material.Name;
                }
            }
        }
    }

    /// <summary>Tầng và hệ mà phần tử thuộc về.</summary>
    public IEnumerable<string> PartOf
    {
        get
        {
            if (_document.GetElement(_element.LevelId) is Level level)
            {
                yield return level.Name;
            }

            var system = TextOf(_element, "System Name") ?? TextOf(_element, "System Classification");
            if (!string.IsNullOrWhiteSpace(system))
            {
                yield return system!;
            }
        }
    }

    private static string? TextOf(Element element, string parameterName)
    {
        var parameter = element.LookupParameter(parameterName);
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        var text = parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer => parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
            StorageType.Double => RevitCompat.FtToMm(parameter.AsDouble()).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            StorageType.ElementId => parameter.AsValueString(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
