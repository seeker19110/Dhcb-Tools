using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Ifc;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Facet property trên file IFC, theo đúng luật IDS 1.0 mà bộ ca của buildingSMART kiểm (và IfcTester làm):
    /// <list type="bullet">
    /// <item>propertySet/baseName khai bằng restriction thì MỌI pset/property khớp đều phải thoả;</item>
    /// <item><c>dataType</c> phải đúng kiểu đo của giá trị (IFCLENGTHMEASURE…), kể cả quantity;</item>
    /// <item>số có đơn vị được đổi về đơn vị chuẩn IDS (m, m², m³, kg, s, rad…) trước khi so;</item>
    /// <item>list/enumerated/bounded/table: một giá trị khớp là đủ;</item>
    /// <item>complex property, reference property: không hỗ trợ → trượt (không đạt oan);</item>
    /// <item>pset của vật liệu/profile (IfcMaterialProperties, IFC2X3 IfcExtendedMaterialProperties) và pset định sẵn
    /// (IfcDoorPanelProperties…) cũng là pset.</item>
    /// </list>
    /// </summary>
    public sealed partial class IfcIdsModel
    {
        /// <summary>Kiểu đo của giá trị (tham số 3) ở từng lớp quantity đơn.</summary>
        private static readonly Dictionary<string, string> QuantityMeasures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IFCQUANTITYLENGTH"] = "IFCLENGTHMEASURE",
            ["IFCQUANTITYAREA"] = "IFCAREAMEASURE",
            ["IFCQUANTITYVOLUME"] = "IFCVOLUMEMEASURE",
            ["IFCQUANTITYCOUNT"] = "IFCCOUNTMEASURE",
            ["IFCQUANTITYWEIGHT"] = "IFCMASSMEASURE",
            ["IFCQUANTITYTIME"] = "IFCTIMEMEASURE",
            ["IFCQUANTITYNUMBER"] = "IFCNUMERICMEASURE",
        };

        private static readonly Dictionary<string, int> SiPrefixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["EXA"] = 18, ["PETA"] = 15, ["TERA"] = 12, ["GIGA"] = 9, ["MEGA"] = 6, ["KILO"] = 3, ["HECTO"] = 2, ["DECA"] = 1,
            ["DECI"] = -1, ["CENTI"] = -2, ["MILLI"] = -3, ["MICRO"] = -6, ["NANO"] = -9, ["PICO"] = -12, ["FEMTO"] = -15, ["ATTO"] = -18,
        };

        /// <summary>Thuộc tính chứa danh sách property của từng loại định nghĩa pset — lớp nào có cái nào thì dùng cái đó.</summary>
        private static readonly string[] PropertyLists = { "HasProperties", "Quantities", "Properties", "ExtendedProperties" };

        /// <summary>Phần tử → định nghĩa pset gán qua IfcRelDefinesByProperties.</summary>
        private readonly Dictionary<int, List<int>> _definedBy = new Dictionary<int, List<int>>();

        /// <summary>Vật liệu/profile → pset của nó (IfcMaterialProperties, IfcProfileProperties, IFC2X3 IfcExtendedMaterialProperties).</summary>
        private readonly Dictionary<int, List<int>> _resourceProperties = new Dictionary<int, List<int>>();

        /// <summary>Pset đã dựng của từng thực thể — một specification có nhiều facet property, một IDS có nhiều specification.</summary>
        private readonly Dictionary<int, Dictionary<string, Dictionary<string, (IfcEntity? Entity, IfcValue Value)>>> _propertySets =
            new Dictionary<int, Dictionary<string, Dictionary<string, (IfcEntity?, IfcValue)>>>();

        /// <summary>Đơn vị mặc định của dự án theo UnitType (LENGTHUNIT…).</summary>
        private readonly Dictionary<string, IfcEntity> _units = new Dictionary<string, IfcEntity>(StringComparer.OrdinalIgnoreCase);

        private void BuildPropertyDefinitions()
        {
            // IfcRelDefinesByProperties: (…, RelatedObjects=4, RelatingPropertyDefinition=5 — IFC4 ADD2 có thể là một tập)
            foreach (var rel in _model.OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                foreach (var definition in References(rel.At(5)))
                {
                    foreach (var target in References(rel.At(4)))
                    {
                        Add(_definedBy, target, definition);
                    }
                }
            }

            // IFC2X3 chỉ IfcExtendedMaterialProperties có tên (như ifcopenshell) và bỏ qua profile.
            var resources = Schema.Name == "IFC2X3"
                ? new[] { ("IFCEXTENDEDMATERIALPROPERTIES", "Material") }
                : new[] { ("IFCMATERIALPROPERTIES", "Material"), ("IFCPROFILEPROPERTIES", "ProfileDefinition") };
            foreach (var (type, owner) in resources)
            {
                foreach (var definition in _model.OfType(type))
                {
                    var target = Value(definition, owner).AsReference();
                    if (target != null)
                    {
                        Add(_resourceProperties, target.Value, definition.Id);
                    }
                }
            }
        }

        private static void Add(Dictionary<int, List<int>> table, int key, int value)
        {
            if (!table.TryGetValue(key, out var list))
            {
                list = new List<int>();
                table[key] = list;
            }

            list.Add(value);
        }

        /// <summary>Đơn vị dự án: IfcProject.UnitsInContext, không có thì IfcUnitAssignment đầu tiên; mỗi UnitType lấy cái đầu.</summary>
        private void BuildUnits()
        {
            var project = _model.OfType("IFCPROJECT").FirstOrDefault();
            var reference = project == null ? null : Value(project, "UnitsInContext").AsReference();
            var assignment = reference == null ? _model.OfType("IFCUNITASSIGNMENT").FirstOrDefault() : _model.ById(reference.Value);
            foreach (var id in assignment == null ? Enumerable.Empty<int>() : References(assignment.At(0)))
            {
                var unit = _model.ById(id);
                var type = unit == null ? IfcValue.Empty : Value(unit, "UnitType");
                if (type.Kind == IfcValueKind.Enumeration && !_units.ContainsKey(type.Raw))
                {
                    _units[type.Raw] = unit!;
                }
            }
        }

        /// <summary>
        /// UnitType của một kiểu đo, như ifcopenshell: <c>IFCPOSITIVELENGTHMEASURE</c> → <c>LENGTHUNIT</c>;
        /// <c>IFCNUMERICMEASURE</c> → <c>USERDEFINED</c>.
        /// </summary>
        internal static string UnitTypeOf(string measure)
        {
            var name = measure.ToUpperInvariant();
            if (name == "IFCNUMERICMEASURE")
            {
                return "USERDEFINED";
            }

            name = name.StartsWith("IFC", StringComparison.Ordinal) ? name.Substring(3) : name;
            name = name.EndsWith("MEASURE", StringComparison.Ordinal) ? name.Substring(0, name.Length - 7) : name;
            foreach (var prefix in new[] { "NON", "POSITIVE", "NEGATIVE" })
            {
                name = name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
            }

            return name + "UNIT";
        }

        /// <summary>Đơn vị của giá trị: khai trên chính property, không có thì đơn vị dự án theo kiểu đo.</summary>
        private IfcEntity? UnitFor(IfcValue declared, string? measure)
        {
            var reference = declared.AsReference();
            if (reference != null)
            {
                return _model.ById(reference.Value);
            }

            return measure != null && _units.TryGetValue(UnitTypeOf(measure), out var unit) ? unit : null;
        }

        /// <summary>
        /// Hệ số đổi một đơn vị về đơn vị chuẩn IDS: SI có tiền tố (MILLI METRE → 0.001; MILLI SQUARE_METRE → 1e-6;
        /// GRAM → 0.001 kg), đơn vị quy đổi (inch, độ…) theo ConversionFactor của chính file. Đơn vị dẫn xuất,
        /// tiền tệ → <c>null</c> (không đổi, như IfcTester).
        /// </summary>
        internal double? Factor(IfcEntity? unit, int depth = 0)
        {
            if (unit == null || depth > 8)
            {
                return null;
            }

            switch (unit.Type.ToUpperInvariant())
            {
                case "IFCSIUNIT": // (Dimensions, UnitType, Prefix, Name)
                    var name = unit.At(3).Raw;
                    var power = name.StartsWith("SQUARE_", StringComparison.Ordinal) ? 2 : name.StartsWith("CUBIC_", StringComparison.Ordinal) ? 3 : 1;
                    var exponent = SiPrefixes.TryGetValue(unit.At(2).Raw, out var e) ? e : 0;
                    return Math.Pow(10, exponent * power) * (name == "GRAM" ? 1e-3 : 1);
                case "IFCCONVERSIONBASEDUNIT":
                case "IFCCONVERSIONBASEDUNITWITHOFFSET": // (Dimensions, UnitType, Name, ConversionFactor, …)
                    var measure = _model.ById(unit.At(3).AsReference() ?? 0);
                    var value = measure == null ? null : IdsDatum.Unwrap(measure.At(0)); // IfcMeasureWithUnit: (ValueComponent, UnitComponent)
                    if (value == null || (value.Value.Kind != IdsDatumKind.Real && value.Value.Kind != IdsDatumKind.Integer))
                    {
                        return null;
                    }

                    return value.Value.Number * (Factor(_model.ById(measure!.At(1).AsReference() ?? 0), depth + 1) ?? 1);
                default:
                    return null;
            }
        }

        private IdsDatum Convert(IdsDatum datum, IfcEntity? unit)
        {
            var factor = datum.Kind == IdsDatumKind.Real || datum.Kind == IdsDatumKind.Integer ? Factor(unit) : null;
            return factor == null ? datum : IdsDatum.OfReal(datum.Number * factor.Value);
        }

        /// <summary>
        /// Mọi pset của thực thể theo tên, mỗi pset là bảng tên property → (thực thể property, hoặc giá trị của pset
        /// định sẵn). Kiểu: HasPropertySets. Vật liệu/profile: pset riêng của chúng. Đối tượng: pset của kiểu trước,
        /// pset gán trực tiếp đè lên theo từng property (như ifcopenshell <c>get_psets</c>).
        /// </summary>
        internal Dictionary<string, Dictionary<string, (IfcEntity? Entity, IfcValue Value)>> PropertySets(IfcEntity entity, IfcEntity? type)
        {
            if (_propertySets.TryGetValue(entity.Id, out var cached))
            {
                return cached;
            }

            var sets = new Dictionary<string, Dictionary<string, (IfcEntity?, IfcValue)>>(StringComparer.Ordinal);
            _propertySets[entity.Id] = sets;

            void AddDefinition(int id)
            {
                var definition = _model.ById(id);
                var name = definition == null ? null : Text(definition, "Name");
                if (definition == null || name == null)
                {
                    return;
                }

                if (!sets.TryGetValue(name, out var properties))
                {
                    properties = new Dictionary<string, (IfcEntity?, IfcValue)>(StringComparer.Ordinal);
                    sets[name] = properties;
                }

                var list = PropertyLists.Select(a => Schema.IndexOf(definition.Type, a)).Where(i => i >= 0).DefaultIfEmpty(-1).First();
                if (list >= 0)
                {
                    foreach (var propertyId in References(definition.At(list)))
                    {
                        var property = _model.ById(propertyId);
                        var propertyName = property?.At(0).AsText();
                        if (propertyName != null)
                        {
                            properties[propertyName] = (property, IfcValue.Empty);
                        }
                    }
                }
                else if (IsA(definition, "IFCPROPERTYSETDEFINITION"))
                {
                    // Pset định sẵn (IfcDoorPanelProperties…): mỗi thuộc tính không phải tham chiếu là một "property".
                    var attributes = Schema.AttributesOf(definition.Type);
                    for (var i = 0; i < attributes.Count; i++)
                    {
                        if (definition.At(i).Kind != IfcValueKind.Reference)
                        {
                            properties[attributes[i]] = (null, definition.At(i));
                        }
                    }
                }
            }

            if (IsA(entity, "IFCTYPEOBJECT"))
            {
                References(Value(entity, "HasPropertySets")).ToList().ForEach(AddDefinition);
            }
            else if ((Schema.Name == "IFC2X3" && entity.Type.Equals("IFCMATERIAL", StringComparison.OrdinalIgnoreCase))
                     || IsA(entity, "IFCMATERIALDEFINITION") || IsA(entity, "IFCPROFILEDEF"))
            {
                (_resourceProperties.TryGetValue(entity.Id, out var own) ? own : new List<int>()).ForEach(AddDefinition);
            }
            else if (IsA(entity, "IFCOBJECTDEFINITION"))
            {
                if (type != null)
                {
                    References(Value(type, "HasPropertySets")).ToList().ForEach(AddDefinition);
                }

                (_definedBy.TryGetValue(entity.Id, out var own) ? own : new List<int>()).ForEach(AddDefinition);
            }

            return sets;
        }

        /// <summary>Soi facet property: không có pset khớp → vắng; pset khớp mà thiếu property → vắng; sai kiểu/giá trị → sai.</summary>
        internal IdsMatch MatchProperty(IfcEntity entity, IfcEntity? type, IdsFacet facet)
        {
            var matched = PropertySets(entity, type)
                .Where(set => facet.Container == null || facet.Container.IsAny || NameMatches(facet.Container, set.Key))
                .ToList();
            if (matched.Count == 0)
            {
                return IdsMatch.Absent;
            }

            var absent = false;
            foreach (var set in matched)
            {
                var present = false;
                foreach (var property in set.Value.Where(p => NameMatches(facet.Name, p.Key)))
                {
                    var values = ValuesOf(property.Value, facet, out var failed);
                    if (failed)
                    {
                        return IdsMatch.Mismatch;
                    }

                    if (values == null)
                    {
                        continue;
                    }

                    present = true;
                    if (!facet.Value.IsAny && !values.Any(v => facet.Value.Accepts(v)))
                    {
                        return IdsMatch.Mismatch;
                    }
                }

                absent |= !present;
            }

            return absent ? IdsMatch.Absent : IdsMatch.Match;
        }

        private static bool NameMatches(IdsValue expected, string name) =>
            expected.Simple != null ? expected.Simple == name : expected.Accepts(IdsDatum.OfText(name));

        /// <summary>
        /// Giá trị của một property, đã đổi đơn vị. <c>null</c> = không có giá trị (<c>$</c>, chuỗi rỗng, logical
        /// unknown) — property coi như vắng. <paramref name="failed"/> = có mà không kiểm được: sai dataType, danh sách
        /// rỗng, lớp property không hỗ trợ.
        /// </summary>
        private List<IdsDatum>? ValuesOf((IfcEntity? Entity, IfcValue Value) source, IdsFacet facet, out bool failed)
        {
            failed = false;
            var property = source.Entity;
            if (property == null)
            {
                // Pset định sẵn: không có dataType/đơn vị để kiểm (IfcTester cũng bỏ qua).
                var datum = Present(source.Value);
                return datum == null ? null : new List<IdsDatum> { datum.Value };
            }

            var type = property.Type.ToUpperInvariant();
            if (QuantityMeasures.TryGetValue(type, out var quantityMeasure))
            {
                // IfcPhysicalSimpleQuantity: (Name, Description, Unit=2, Value=3, Formula)
                return Single(IdsDatum.FromStep(property.At(3)), quantityMeasure, property.At(2), facet, out failed);
            }

            switch (type)
            {
                case "IFCPROPERTYSINGLEVALUE": // (Name, Description, NominalValue, Unit)
                    var nominal = property.At(2);
                    return Single(Present(nominal), nominal.Kind == IfcValueKind.Typed ? nominal.Raw : null, property.At(3), facet, out failed);
                case "IFCPROPERTYENUMERATEDVALUE": // (Name, Description, EnumerationValues, EnumerationReference) — không đổi đơn vị
                    return Many(property.At(2), IfcValue.Empty, facet, out failed, convert: false);
                case "IFCPROPERTYLISTVALUE": // (Name, Description, ListValues, Unit)
                    return Many(property.At(2), property.At(3), facet, out failed, convert: true);
                case "IFCPROPERTYBOUNDEDVALUE": // (Name, Description, UpperBoundValue, LowerBoundValue, Unit, SetPointValue)
                    var bounds = new[] { property.At(2), property.At(3), property.At(5) }.Where(v => Present(v) != null).ToList();
                    if (bounds.Count == 0)
                    {
                        return null;
                    }

                    var boundMeasure = bounds[bounds.Count - 1].Raw;
                    failed = WrongDataType(facet, boundMeasure);
                    var boundUnit = UnitFor(property.At(4), boundMeasure);
                    return bounds.Select(v => Convert(Present(v)!.Value, boundUnit)).ToList();
                case "IFCPROPERTYTABLEVALUE": // (Name, Description, DefiningValues, DefinedValues, Expression, DefiningUnit, DefinedUnit, …)
                    // Chỉ lấy cột có đúng kiểu dataType đã khai; không khai dataType thì không so được cột nào.
                    var values = new List<IdsDatum>();
                    foreach (var (column, unitIndex) in new[] { (2, 5), (3, 6) })
                    {
                        var items = property.At(column).Items;
                        if (items.Count > 0 && !WrongDataType(facet, items[0].Raw) && !string.IsNullOrEmpty(facet.DataType))
                        {
                            var unit = UnitFor(property.At(unitIndex), items[0].Raw);
                            values.AddRange(items.Select(Present).Where(d => d != null).Select(d => Convert(d!.Value, unit)));
                        }
                    }

                    failed = values.Count == 0;
                    return values;
                default:
                    // IfcComplexProperty, IfcPropertyReferenceValue, IfcPhysicalComplexQuantity…: IDS 1.0 không định nghĩa
                    // cách so ("complex properties are not supported") — trượt, không đạt oan.
                    failed = true;
                    return null;
            }
        }

        private List<IdsDatum>? Single(IdsDatum? datum, string? measure, IfcValue unit, IdsFacet facet, out bool failed)
        {
            failed = datum != null && WrongDataType(facet, measure);
            return datum == null ? null : new List<IdsDatum> { Convert(datum.Value, UnitFor(unit, measure)) };
        }

        private List<IdsDatum>? Many(IfcValue list, IfcValue unit, IdsFacet facet, out bool failed, bool convert)
        {
            failed = false;
            if (list.Kind != IfcValueKind.List)
            {
                return null;
            }

            if (list.Items.Count == 0)
            {
                failed = true;
                return null;
            }

            var measure = list.Items[0].Raw;
            failed = WrongDataType(facet, measure);
            var target = convert ? UnitFor(unit, measure) : null;
            return list.Items.Select(Present).Where(d => d != null).Select(d => Convert(d!.Value, target)).ToList();
        }

        /// <summary>Giá trị (đã bóc kiểu bọc) nếu có thật: không phải <c>$</c>, <c>.U.</c>, chuỗi rỗng.</summary>
        private static IdsDatum? Present(IfcValue value)
        {
            var datum = IdsDatum.Unwrap(value);
            return datum == null || (datum.Value.Kind == IdsDatumKind.Text && datum.Value.Text.Length == 0) ? null : datum;
        }

        private static bool WrongDataType(IdsFacet facet, string? measure) =>
            !string.IsNullOrEmpty(facet.DataType) && !string.Equals(measure, facet.DataType, StringComparison.OrdinalIgnoreCase);
    }
}
