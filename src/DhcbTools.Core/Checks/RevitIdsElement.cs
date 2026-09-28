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

            // Tường kính: bộ xuất IFC của Revit ghi IfcCurtainWall, và trong IFC4 lớp đó KHÔNG phải con của
            // IfcWall. Gộp chung theo category Walls thì specification "IfcWall phải có vật liệu" áp cả lên
            // tường kính (không có lớp cấu tạo) → 42 lỗi giả trên Snowdon, trong khi IfcTester trên chính file
            // IFC xuất ra báo 0. Đối chiếu 2026-09-05, §39.
            if (_element is Wall wall && wall.WallType?.Kind == WallKind.Curtain)
            {
                return "IfcCurtainWall";
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
            var declared = TextOf(_element, "IfcExportAs") ?? (_type != null ? TextOf(_type, "IfcExportAs") : null) ?? string.Empty;
            var dot = declared.IndexOf('.');
            if (dot >= 0)
            {
                return declared.Substring(dot + 1).Trim();
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
        var key = (name ?? string.Empty).Trim();
        switch (key.ToLowerInvariant())
        {
            case "name":
                return _element.Name;
            case "tag":
                // Bộ xuất IFC của Revit ghi Tag = Mark, và khi Mark rỗng thì = ElementId — nên trên file IFC mọi
                // phần tử đều có Tag. Trước đây đường Revit trả rỗng khi thiếu Mark: dự án A báo 12 cửa "thiếu
                // Tag" trong khi IfcTester/đường IFC trên chính file xuất ra báo 0 (§43). Muốn bắt "thiếu Mark"
                // thì khai property/pattern, không phải attribute Tag.
                // Tra bằng BuiltInParameter chứ không bằng tên "Mark": trên Revit giao diện tiếng Việt/khác,
                // LookupParameter("Mark") trả null và mọi phần tử bị coi là thiếu Mark.
                var mark = _element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                return !string.IsNullOrWhiteSpace(mark) ? mark : RevitCompat.IdValue(_element.Id).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "description":
                return TextOf(_element, "Description") ?? (_type != null ? TextOf(_type, "Description") : null);
            case "objecttype":
                return _type?.Name;
            case "globalid":
                // Bộ xuất IFC sinh GlobalId 22 ký tự từ UniqueId (XOR 8 hex cuối với ElementId rồi nén) — trả đúng
                // chuỗi đó để facet GlobalId (pattern/length) cho cùng kết luận với file IFC.
                return Shared.Logic.Ifc.IfcGuid.FromRevitUniqueId(_element.UniqueId) ?? _element.UniqueId;
            default:
                // Thuộc tính lạ: thử luôn như một tham số cùng tên, rồi mới chịu thua. Trả rỗng khác
                // hẳn trả "" ngầm hiểu là đạt — IdsValue.Accepts coi rỗng là KHÔNG đạt.
                return key.Length == 0 ? null : TextOf(_element, key) ?? (_type != null ? TextOf(_type, key) : null);
        }
    }

    /// <summary>
    /// Property theo property set. Revit không giữ Pset như IFC, nên tra theo <b>tên tham số</b>:
    /// trước hết "Pset_Tên.Prop" (cách khai của bộ xuất qua file mapping), sau đó chính tên property ở
    /// instance rồi ở type — đúng thứ tự bộ xuất IFC lấy giá trị. Số có đơn vị theo <b>đơn vị chuẩn IDS</b>
    /// (m, m², m³, rad) — đường IFC đổi property về đúng các đơn vị này, nên cùng một IDS cho cùng kết luận.
    /// </summary>
    public string? Property(string? propertySet, string name)
    {
        if (!string.IsNullOrWhiteSpace(propertySet))
        {
            var qualified = TextOf(_element, propertySet + "." + name, true) ?? (_type != null ? TextOf(_type, propertySet + "." + name, true) : null);
            if (qualified != null)
            {
                return qualified;
            }
        }

        return TextOf(_element, name, true) ?? (_type != null ? TextOf(_type, name, true) : null);
    }

    /// <summary>
    /// Mã phân loại theo hệ. Revit chở mã phân loại ở vài tham số cố định; hệ IDS hỏi được ánh xạ về đúng
    /// tham số (OmniClass → "OmniClass Number", Uniformat/Uniclass → "Assembly Code", Keynote → "Keynote",
    /// tên khác → "ClassificationCode" hoặc tham số cùng tên hệ). Hệ không biết → không trả gì: trước đây
    /// mọi hệ đều nhận Assembly Code nên đường Revit "đạt" trong khi file IFC trượt (§39).
    /// </summary>
    public IEnumerable<string> Classifications(string? system)
    {
        IEnumerable<string> keys;
        if (string.IsNullOrWhiteSpace(system))
        {
            keys = new[] { "Assembly Code", "Keynote", "ClassificationCode", "OmniClass Number" };
        }
        else
        {
            var s = system!.Trim();
            var lower = s.ToLowerInvariant();
            keys = lower.Contains("omniclass") ? new[] { "OmniClass Number" }
                : lower.Contains("uniformat") || lower.Contains("uniclass") || lower.Contains("assembly") ? new[] { "Assembly Code" }
                : lower.Contains("keynote") ? new[] { "Keynote" }
                : new[] { "ClassificationCode", s, "ClassificationCode(" + s + ")" };
        }

        foreach (var key in keys)
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

    /// <summary>
    /// Tầng và hệ mà phần tử thuộc về — tên thật (<c>"Tầng 1"</c>, <c>"HT-01"</c>), không phải tên lớp IFC
    /// (<c>IFCBUILDINGSTOREY</c>): kỹ sư đọc báo cáo trên Revit cần thấy tên quen thuộc, và đây là chỗ
    /// khác với <see cref="IfcIdsElement"/> đã ghi rõ ở <c>kiem-ids.md</c> ("Giới hạn"), không phải lỗi cần sửa.
    /// <para>
    /// Quan hệ gắn theo mỗi mục là quan hệ IFC <b>gần đúng nhất</b> mà tầng/hệ Revit ánh xạ tới khi bộ xuất
    /// IFC chạy: Level → <see cref="IdsRelations.ContainedInSpatialStructure"/>, hệ (System Name/
    /// Classification) → <see cref="IdsRelations.AssignsToGroup"/>. Nhờ vậy facet <c>partOf</c> khai
    /// <c>relation</c> cụ thể vẫn lọc đúng trên Revit, không chỉ trên đường IFC.
    /// </para>
    /// </summary>
    public IEnumerable<(string? Relation, string Entity)> PartOf
    {
        get
        {
            if (_document.GetElement(_element.LevelId) is Level level)
            {
                yield return (null, level.Name);
                yield return (IdsRelations.ContainedInSpatialStructure, level.Name);
            }

            var system = TextOf(_element, "System Name") ?? TextOf(_element, "System Classification");
            if (!string.IsNullOrWhiteSpace(system))
            {
                yield return (null, system!);
                yield return (IdsRelations.AssignsToGroup, system!);
            }
        }
    }

    /// <summary>
    /// Số thực có đơn vị. Property (<paramref name="idsUnits"/>): đơn vị chuẩn IDS 1.0 — dài → m, diện tích → m²,
    /// thể tích → m³, góc → rad — vì đường IFC đổi property về đúng các đơn vị này (như IfcTester). Attribute: đơn
    /// vị bộ xuất IFC ghi (dài → mm, góc → độ) — đường IFC và IfcTester so attribute theo số ghi trong file, không đổi.
    /// Đại lượng không đơn vị (tỉ số, hệ số) giữ nguyên. Trước đây MỌI double nhân 304,8 nên "U-value 0,3" thành 91.
    /// </summary>
    private static string DoubleText(Parameter parameter, bool idsUnits)
    {
        var raw = parameter.AsDouble();
        double value;
        try
        {
            var spec = parameter.Definition.GetDataType();
            value = spec == SpecTypeId.Length ? UnitUtils.ConvertFromInternalUnits(raw, idsUnits ? UnitTypeId.Meters : UnitTypeId.Millimeters)
                : spec == SpecTypeId.Area ? UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters)
                : spec == SpecTypeId.Volume ? UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.CubicMeters)
                : spec == SpecTypeId.Angle ? UnitUtils.ConvertFromInternalUnits(raw, idsUnits ? UnitTypeId.Radians : UnitTypeId.Degrees)
                : raw;
        }
        catch (Exception)
        {
            value = raw; // tham số không có kiểu dữ liệu (family cũ) — giữ số nội bộ
        }

        // m/rad cần nhiều chữ số hơn mm/độ để không làm tròn mất phần dưới milimét.
        return value.ToString(idsUnits ? "0.#########" : "0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? TextOf(Element element, string parameterName, bool idsUnits = false)
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
            StorageType.Double => DoubleText(parameter, idsUnits),
            StorageType.ElementId => parameter.AsValueString(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
