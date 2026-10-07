using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DhcbTools.Shared.Logic.Ifc;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Đường IFC đọc giá trị CÓ KIỂU cho IDS: thuộc tính theo lược đồ (<see cref="IfcSchemaTable"/>), property set
    /// kèm kiểu dữ liệu và đơn vị. Tách khỏi <c>IfcIdsElement.cs</c> cho dễ đọc — cùng hai lớp, khai <c>partial</c>.
    /// </summary>
    public sealed partial class IfcIdsModel
    {
        private Dictionary<int, Dictionary<string, Dictionary<string, IdsPropertyValue>>>? _propertySets;
        private Dictionary<string, double>? _unitScales;

        /// <summary>
        /// Phần tử IDS kèm cả thực thể KHÔNG có GlobalId (<c>IfcMaterial</c>, <c>IfcTaskTime</c>,
        /// <c>IfcSurfaceStyleRefraction</c>…) mà applicability của bộ <paramref name="specifications"/> nêu đích danh
        /// bằng facet <c>entity</c>. IDS nói được về mọi thực thể, nhưng liệt kê hết (hàng triệu điểm hình học) chỉ
        /// để mọi specification lọc bỏ là phí — nên chỉ thêm lớp được hỏi tới.
        /// </summary>
        public IReadOnlyList<IIdsElement> Elements(IEnumerable<IdsSpecification> specifications)
        {
            var list = Elements().ToList();
            var named = (specifications ?? Enumerable.Empty<IdsSpecification>())
                .SelectMany(s => s.Applicability).Where(f => f.Kind == IdsFacetKind.Entity).Select(f => f.Name).ToList();
            if (named.Count == 0)
            {
                return list;
            }

            var included = new HashSet<int>(list.Cast<IfcIdsElement>().Select(e => e.Id));
            foreach (var entity in _model.File.Data)
            {
                if (entity.Id != 0 && !included.Contains(entity.Id) && named.Any(n => n.Accepts(entity.Type.ToUpperInvariant())))
                {
                    list.Add(new IfcIdsElement(this, entity));
                }
            }

            return list;
        }

        internal IReadOnlyDictionary<string, Dictionary<string, IdsPropertyValue>> PropertySetsOf(int id)
        {
            _propertySets ??= BuildPropertySets();
            return _propertySets.TryGetValue(id, out var sets)
                ? sets
                : (IReadOnlyDictionary<string, Dictionary<string, IdsPropertyValue>>)new Dictionary<string, Dictionary<string, IdsPropertyValue>>();
        }

        /// <summary>
        /// Property set của từng phần tử, đúng như IfcOpenShell <c>get_psets</c>: pset của KIỂU trước, rồi pset của
        /// chính phần tử đè lên theo từng property (cùng tên pset). Gồm IfcPropertySet, IfcElementQuantity, pset định
        /// nghĩa sẵn (IfcDoorPanelProperties…) và property của vật liệu (IfcMaterialProperties / IFC2X3
        /// IfcExtendedMaterialProperties — gắn cho chính IfcMaterial).
        /// </summary>
        private Dictionary<int, Dictionary<string, Dictionary<string, IdsPropertyValue>>> BuildPropertySets()
        {
            var result = new Dictionary<int, Dictionary<string, Dictionary<string, IdsPropertyValue>>>();
            var cache = new Dictionary<int, KeyValuePair<string, List<IdsPropertyValue>>?>();

            KeyValuePair<string, List<IdsPropertyValue>>? Set(int id)
            {
                if (!cache.TryGetValue(id, out var set))
                {
                    set = ReadSet(_model.ById(id));
                    cache[id] = set;
                }

                return set;
            }

            void Add(int target, IEnumerable<int> setIds)
            {
                foreach (var setId in setIds)
                {
                    var set = Set(setId);
                    if (set == null)
                    {
                        continue;
                    }

                    if (!result.TryGetValue(target, out var sets))
                    {
                        sets = new Dictionary<string, Dictionary<string, IdsPropertyValue>>(StringComparer.Ordinal);
                        result[target] = sets;
                    }

                    if (!sets.TryGetValue(set.Value.Key, out var properties))
                    {
                        properties = new Dictionary<string, IdsPropertyValue>(StringComparer.Ordinal);
                        sets[set.Value.Key] = properties;
                    }

                    foreach (var property in set.Value.Value)
                    {
                        properties[property.Name] = property;
                    }
                }
            }

            // Kiểu trước: IfcTypeObject.HasPropertySets (vị trí 5) — cho chính kiểu (kể cả kiểu chưa phần tử nào dùng)
            // và cho mọi phần tử của nó.
            foreach (var entity in _model.File.Data)
            {
                if (entity.Id != 0 && IfcSchemaTable.Attribute(_model.Schema, entity.Type, "HasPropertySets")?.Index == 5)
                {
                    Add(entity.Id, References(entity.At(5)));
                }
            }

            foreach (var pair in _typeOf)
            {
                Add(pair.Key, References(_model.ById(pair.Value)!.At(5)));
            }

            // Rồi của chính phần tử: IfcRelDefinesByProperties (RelatedObjects 4, RelatingPropertyDefinition 5 — IFC4 ADD2 có thể là tập).
            foreach (var rel in _model.OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                var sets = References(rel.At(5)).ToList();
                foreach (var target in References(rel.At(4)))
                {
                    Add(target, sets);
                }
            }

            // Property của vật liệu: IFC4 IfcMaterialProperties (Name, Description, Properties, Material);
            // IFC2X3 IfcExtendedMaterialProperties (Material, ExtendedProperties, Description, Name).
            foreach (var entity in _model.OfType("IFCMATERIALPROPERTIES").Concat(_model.OfType("IFCEXTENDEDMATERIALPROPERTIES")))
            {
                var material = entity.Type == "IFCMATERIALPROPERTIES" ? entity.At(3).AsReference() : entity.At(0).AsReference();
                if (material != null)
                {
                    Add(material.Value, new[] { entity.Id });
                }
            }

            return result;
        }

        private KeyValuePair<string, List<IdsPropertyValue>>? ReadSet(IfcEntity? set)
        {
            if (set == null)
            {
                return null;
            }

            string? name;
            IEnumerable<int> members;
            switch (set.Type)
            {
                case "IFCPROPERTYSET": // (GlobalId, OwnerHistory, Name, Description, HasProperties)
                    name = set.At(2).AsText();
                    members = References(set.At(4));
                    break;
                case "IFCELEMENTQUANTITY": // (…, Description, MethodOfMeasurement, Quantities)
                    name = set.At(2).AsText();
                    members = References(set.At(5));
                    break;
                case "IFCMATERIALPROPERTIES": // (Name, Description, Properties, Material)
                    name = set.At(0).AsText();
                    members = References(set.At(2));
                    break;
                case "IFCEXTENDEDMATERIALPROPERTIES": // (Material, ExtendedProperties, Description, Name)
                    name = set.At(3).AsText();
                    members = References(set.At(1));
                    break;
                default:
                    return ReadPredefinedSet(set);
            }

            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var properties = new List<IdsPropertyValue>();
            foreach (var member in members)
            {
                var property = ReadProperty(name!, _model.ById(member));
                if (property != null)
                {
                    properties.Add(property);
                }
            }

            return new KeyValuePair<string, List<IdsPropertyValue>>(name!, properties);
        }

        /// <summary>
        /// Pset định nghĩa sẵn (IfcPreDefinedPropertySet: IfcDoorPanelProperties…): mỗi thuộc tính lược đồ sau
        /// GlobalId/OwnerHistory/Name/Description là một "property". IfcTester không kiểm dataType ở đây, nên
        /// kiểu dữ liệu của giá trị để <c>null</c> (không ràng). Thuộc tính trỏ thực thể bị bỏ như IfcTester.
        /// </summary>
        private KeyValuePair<string, List<IdsPropertyValue>>? ReadPredefinedSet(IfcEntity set)
        {
            var attributes = IfcSchemaTable.Attributes(_model.Schema, set.Type);
            var name = set.At(2).AsText();
            if (attributes == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var properties = new List<IdsPropertyValue>();
            foreach (var attribute in attributes.Skip(4))
            {
                var value = IfcIdsElement.Typed(set.At(attribute.Index));
                if (value != null && value.Kind != IdsTypedKind.Reference && value.Kind != IdsTypedKind.Aggregate)
                {
                    properties.Add(new IdsPropertyValue(name!, attribute.Name, new[] { new KeyValuePair<string?, IdsTypedValue>(null, value) }));
                }
            }

            return new KeyValuePair<string, List<IdsPropertyValue>>(name!, properties);
        }

        /// <summary>Kiểu dữ liệu của số đo trong quantity (IfcQuantityLength → IFCLENGTHMEASURE…).</summary>
        private static readonly Dictionary<string, string> QuantityMeasures = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IFCQUANTITYLENGTH"] = "IFCLENGTHMEASURE",
            ["IFCQUANTITYAREA"] = "IFCAREAMEASURE",
            ["IFCQUANTITYVOLUME"] = "IFCVOLUMEMEASURE",
            ["IFCQUANTITYCOUNT"] = "IFCCOUNTMEASURE",
            ["IFCQUANTITYWEIGHT"] = "IFCMASSMEASURE",
            ["IFCQUANTITYTIME"] = "IFCTIMEMEASURE",
            ["IFCQUANTITYNUMBER"] = "IFCNUMERICMEASURE",
        };

        /// <summary>
        /// Một property/quantity → tên, các giá trị khác rỗng kèm kiểu dữ liệu, đã đổi về SI theo đơn vị riêng của
        /// property hoặc đơn vị mặc định của dự án. <c>null</c>: không có giá trị nào (IDS coi như không có property).
        /// Loại IDS không kiểm được (complex, reference) trả về với <see cref="IdsPropertyValue.Supported"/> = false.
        /// </summary>
        private IdsPropertyValue? ReadProperty(string set, IfcEntity? property)
        {
            var name = property?.At(0).AsText();
            if (property == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var values = new List<KeyValuePair<string?, IdsTypedValue>>();
            void AddTyped(IfcValue raw, IfcValue unit)
            {
                var value = IfcIdsElement.Typed(raw);
                if (value != null)
                {
                    var dataType = raw.Kind == IfcValueKind.Typed ? raw.Raw.ToUpperInvariant() : null;
                    values.Add(new KeyValuePair<string?, IdsTypedValue>(dataType, ToSi(value, dataType, unit)));
                }
            }

            switch (property.Type)
            {
                case "IFCPROPERTYSINGLEVALUE": // (Name, Description, NominalValue, Unit)
                    AddTyped(property.At(2), property.At(3));
                    break;
                case "IFCPROPERTYENUMERATEDVALUE": // (Name, Description, EnumerationValues, EnumerationReference)
                case "IFCPROPERTYLISTVALUE": // (Name, Description, ListValues, Unit)
                    foreach (var item in property.At(2).Items)
                    {
                        AddTyped(item, property.Type == "IFCPROPERTYLISTVALUE" ? property.At(3) : IfcValue.Empty);
                    }

                    break;
                case "IFCPROPERTYBOUNDEDVALUE": // (Name, Description, UpperBoundValue, LowerBoundValue, Unit, SetPointValue)
                    AddTyped(property.At(2), property.At(4));
                    AddTyped(property.At(3), property.At(4));
                    AddTyped(property.At(5), property.At(4));
                    break;
                case "IFCPROPERTYTABLEVALUE": // (Name, Description, DefiningValues, DefinedValues, Expression, DefiningUnit, DefinedUnit, …)
                    foreach (var item in property.At(2).Items)
                    {
                        AddTyped(item, property.At(5));
                    }

                    foreach (var item in property.At(3).Items)
                    {
                        AddTyped(item, property.At(6));
                    }

                    break;
                default:
                    if (!QuantityMeasures.TryGetValue(property.Type, out var measure))
                    {
                        return new IdsPropertyValue(set, name!, Array.Empty<KeyValuePair<string?, IdsTypedValue>>(), supported: false);
                    }

                    // IfcPhysicalSimpleQuantity: (Name, Description, Unit, Value, …)
                    var raw = property.At(3);
                    var number = IfcIdsElement.Typed(raw);
                    if (number != null)
                    {
                        values.Add(new KeyValuePair<string?, IdsTypedValue>(measure, ToSi(number, measure, property.At(2))));
                    }

                    break;
            }

            return values.Count == 0 ? null : new IdsPropertyValue(set, name!, values);
        }

        /// <summary>
        /// Đổi số đo về đơn vị SI mà IDS dùng (m, m², m³, kg, s, rad…): đơn vị riêng của property nếu có, không thì
        /// đơn vị mặc định của dự án cho loại số đo đó (<c>IFCLENGTHMEASURE</c> → <c>LENGTHUNIT</c>). File mm ghi
        /// 2000 thì IDS viết 2 — trước đây so thẳng 2000 với 2.
        /// </summary>
        private IdsTypedValue ToSi(IdsTypedValue value, string? dataType, IfcValue unit)
        {
            var unitType = UnitTypeOf(dataType);
            double? scale = null;
            if (unit.Kind == IfcValueKind.Reference)
            {
                scale = UnitScale(_model.ById(unit.Reference), 0);
            }
            else if (unitType != null)
            {
                _unitScales ??= ProjectUnitScales();
                if (_unitScales.TryGetValue(unitType, out var projectScale))
                {
                    scale = projectScale;
                }
            }

            return scale == null || scale.Value == 1 ? value : value.Scale(scale.Value, 0);
        }

        /// <summary><c>IFCPOSITIVELENGTHMEASURE</c> → <c>LENGTHUNIT</c> (như IfcOpenShell <c>get_measure_unit_type</c>); không phải số đo → <c>null</c>.</summary>
        internal static string? UnitTypeOf(string? dataType)
        {
            if (dataType == null || !dataType.StartsWith("IFC", StringComparison.Ordinal) || !dataType.EndsWith("MEASURE", StringComparison.Ordinal))
            {
                return null;
            }

            var core = dataType.Substring(3, dataType.Length - 10);
            foreach (var prefix in new[] { "NON", "POSITIVE", "NEGATIVE" })
            {
                core = core.Replace(prefix, string.Empty);
            }

            return core + "UNIT";
        }

        private Dictionary<string, double> ProjectUnitScales()
        {
            var scales = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var assignment in _model.OfType("IFCUNITASSIGNMENT"))
            {
                foreach (var id in References(assignment.At(0)))
                {
                    var unit = _model.ById(id);
                    var type = unit?.At(1).Kind == IfcValueKind.Enumeration ? unit.At(1).Raw : null;
                    var scale = UnitScale(unit, 0);
                    if (type != null && scale != null && !scales.ContainsKey(type))
                    {
                        scales[type] = scale.Value;
                    }
                }
            }

            return scales;
        }

        private static readonly Dictionary<string, double> Prefixes = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["EXA"] = 1e18, ["PETA"] = 1e15, ["TERA"] = 1e12, ["GIGA"] = 1e9, ["MEGA"] = 1e6, ["KILO"] = 1e3, ["HECTO"] = 1e2,
            ["DECA"] = 1e1, ["DECI"] = 1e-1, ["CENTI"] = 1e-2, ["MILLI"] = 1e-3, ["MICRO"] = 1e-6, ["NANO"] = 1e-9,
            ["PICO"] = 1e-12, ["FEMTO"] = 1e-15, ["ATTO"] = 1e-18,
        };

        /// <summary>
        /// Hệ số nhân để đưa một giá trị ghi theo <paramref name="unit"/> về SI. IfcSIUnit: tiền tố (bình phương với
        /// SQUARE_, lập phương với CUBIC_), gam → kg. IfcConversionBasedUnit (foot, inch…): giá trị của ConversionFactor
        /// nhân hệ số của đơn vị trong đó. Độ C và đơn vị dẫn xuất: không đổi (<c>null</c>) — như IfcTester.
        /// </summary>
        private double? UnitScale(IfcEntity? unit, int depth)
        {
            if (unit == null || depth > 8)
            {
                return null;
            }

            switch (unit.Type)
            {
                case "IFCSIUNIT": // (Dimensions, UnitType, Prefix, Name)
                    var name = unit.At(3).Raw;
                    if (name == "DEGREE_CELSIUS")
                    {
                        return null;
                    }

                    var prefix = Prefixes.TryGetValue(unit.At(2).Raw, out var p) ? p : 1;
                    var power = name.StartsWith("SQUARE_", StringComparison.Ordinal) ? 2 : name.StartsWith("CUBIC_", StringComparison.Ordinal) ? 3 : 1;
                    return Math.Pow(prefix, power) / (name == "GRAM" ? 1000 : 1);
                case "IFCCONVERSIONBASEDUNIT": // (Dimensions, UnitType, Name, ConversionFactor)
                    var factor = _model.ById(unit.At(3).AsReference() ?? 0); // IfcMeasureWithUnit (ValueComponent, UnitComponent)
                    var magnitude = factor == null ? null : IfcIdsElement.Typed(factor.At(0));
                    var inner = factor == null ? null : UnitScale(_model.ById(factor.At(1).AsReference() ?? 0), depth + 1);
                    return magnitude == null || inner == null ? (double?)null : magnitude.Number * inner.Value;
                default:
                    return null;
            }
        }
    }

    public sealed partial class IfcIdsElement
    {
        /// <summary>Lược đồ của file chứa phần tử.</summary>
        private string Schema => _model.Model.Schema;

        /// <summary>
        /// IFC2X3 không có lớp riêng cho thiết bị MEP: miệng gió là <c>IfcFlowTerminal</c> mang kiểu
        /// <c>IfcAirTerminalType</c>. IDS viết tên lớp IFC4 (<c>IFCAIRTERMINAL</c>) và buildingSMART quy định đọc qua
        /// bảng ánh xạ kiểu: kiểu <c>IFC…TYPE</c> mà lớp bỏ đuôi "TYPE" không có trong IFC2X3 nhưng có trong IFC4 thì
        /// phần tử được coi là lớp đó.
        /// </summary>
        public string? EntityAlias
        {
            get
            {
                if (_type == null || IfcSchemaTable.Normalize(Schema) != "IFC2X3" || !_type.Type.EndsWith("TYPE", StringComparison.Ordinal))
                {
                    return null;
                }

                var occurrence = _type.Type.Substring(0, _type.Type.Length - 4);
                return IfcSchemaTable.Attributes("IFC2X3", occurrence) == null && IfcSchemaTable.Attributes("IFC4", occurrence) != null ? occurrence : null;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IdsAttributeValue> Attributes(IdsValue name)
        {
            var attributes = IfcSchemaTable.Attributes(Schema, _entity.Type);
            if (attributes == null)
            {
                return Array.Empty<IdsAttributeValue>();
            }

            var result = new List<IdsAttributeValue>();
            foreach (var attribute in attributes)
            {
                if (!name.Accepts(attribute.Name))
                {
                    continue;
                }

                var raw = _entity.At(attribute.Index);
                var isNull = attribute.Derived || raw.Kind == IfcValueKind.Null || raw.Kind == IfcValueKind.Derived;
                result.Add(new IdsAttributeValue(attribute.Name, isNull ? null : Typed(raw), isNull));
            }

            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<KeyValuePair<string, IReadOnlyList<IdsPropertyValue>>> Properties(IdsValue? propertySet, IdsValue name)
        {
            var result = new List<KeyValuePair<string, IReadOnlyList<IdsPropertyValue>>>();
            foreach (var set in _model.PropertySetsOf(_entity.Id))
            {
                if (propertySet == null || propertySet.Accepts(set.Key))
                {
                    result.Add(new KeyValuePair<string, IReadOnlyList<IdsPropertyValue>>(
                        set.Key, set.Value.Values.Where(p => name.Accepts(p.Name)).ToList()));
                }
            }

            return result;
        }

        /// <summary>
        /// Giá trị STEP → giá trị có kiểu; <c>null</c> khi rỗng (<c>$</c>, <c>*</c>, <c>''</c>, tập hợp rỗng, logical
        /// <c>.U.</c>). Kiểu đọc theo chính cách ghi STEP — số có dấu chấm/mũ là số thực, <c>.T.</c>/<c>.F.</c> là
        /// boolean, enum khác là chuỗi, giá trị bọc (<c>IFCLABEL('x')</c>, select) lấy giá trị bên trong.
        /// </summary>
        internal static IdsTypedValue? Typed(IfcValue raw)
        {
            switch (raw.Kind)
            {
                case IfcValueKind.Text:
                    return raw.Raw.Length == 0 ? null : IdsTypedValue.String(raw.Raw);
                case IfcValueKind.Number:
                    if (raw.Raw.IndexOfAny(new[] { '.', 'E', 'e' }) < 0
                        && long.TryParse(raw.Raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                    {
                        return IdsTypedValue.Integer(integer);
                    }

                    return IdsValue.TryNumber(raw.Raw, out var real) ? IdsTypedValue.Real(real) : null;
                case IfcValueKind.Enumeration:
                    return raw.Raw == "T" ? IdsTypedValue.Boolean(true)
                        : raw.Raw == "F" ? IdsTypedValue.Boolean(false)
                        : raw.Raw == "U" ? null
                        : IdsTypedValue.String(raw.Raw);
                case IfcValueKind.Reference:
                    return IdsTypedValue.Reference(raw.Reference);
                case IfcValueKind.List:
                    return raw.Items.Count == 0 ? null : IdsTypedValue.Aggregate(raw.Items.Count);
                case IfcValueKind.Typed:
                    return raw.Items.Count == 1 ? Typed(raw.Items[0]) : null;
                default:
                    return null;
            }
        }
    }
}
