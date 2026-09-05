using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Ifc;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Mục 11.4 — nhìn <b>chính file IFC</b> dưới con mắt IDS, để cùng một bộ luật <see cref="IdsEvaluator"/>
    /// chạy được trên file đã nộp chứ không chỉ trên mô hình Revit.
    /// <para>
    /// Vì sao cần cả hai đường: kiểm trên Revit cho kỹ sư sửa tại chỗ; kiểm trên IFC là thứ bên thẩm tra
    /// thật sự làm (IfcTester, Solibri đều đọc IFC). §39 cho thấy hai đường có thể lệch nhau (42 lỗi giả do
    /// ánh xạ tường kính) và chỉ lộ khi có bộ tham chiếu độc lập. Đường IFC ở đây là bộ tham chiếu đó,
    /// chạy được trên CI không cần Revit.
    /// </para>
    /// <para>
    /// Quy ước khớp theo IfcTester (buildingSMART): tên lớp so <b>đúng lớp</b>, không tính lớp con
    /// (<c>IFCWALL</c> không gồm <c>IFCWALLSTANDARDCASE</c>); property/vật liệu/phân loại của <b>kiểu</b>
    /// (qua <c>IfcRelDefinesByType</c>) được thừa kế xuống phần tử.
    /// </para>
    /// </summary>
    public sealed class IfcIdsModel
    {
        private readonly IfcModel _model;
        private readonly Dictionary<int, int> _typeOf = new Dictionary<int, int>();
        private readonly Dictionary<int, List<string>> _materials = new Dictionary<int, List<string>>();
        private readonly Dictionary<int, List<KeyValuePair<string, string>>> _classifications = new Dictionary<int, List<KeyValuePair<string, string>>>();
        private readonly Dictionary<int, List<string>> _partOf = new Dictionary<int, List<string>>();

        private IfcIdsModel(IfcModel model)
        {
            _model = model;
            BuildTypes();
            BuildMaterials();
            BuildClassifications();
            BuildPartOf();
        }

        /// <summary>Đọc file IFC (nội dung văn bản) và dựng sẵn các bảng tra.</summary>
        public static IfcIdsModel Parse(string text) => new IfcIdsModel(IfcModel.Parse(text));

        /// <summary>Mô hình IFC bên dưới.</summary>
        public IfcModel Model => _model;

        /// <summary>
        /// Mọi phần tử IDS có thể nói tới: thực thể mang GlobalId, trừ quan hệ (<c>IfcRel*</c>) và định nghĩa
        /// thuộc tính (<c>IfcPropertySet</c>, <c>IfcElementQuantity</c>…) — chúng có GlobalId nhưng không phải
        /// "đối tượng" mà một specification nhắm tới. Kiểu (<c>IfcWallType</c>…) được giữ: IDS cho phép
        /// specification áp lên kiểu.
        /// </summary>
        public IReadOnlyList<IIdsElement> Elements()
        {
            var list = new List<IIdsElement>();
            foreach (var entity in _model.File.Data)
            {
                if (entity.Id == 0 || !IfcModel.LooksLikeGlobalId(IfcModel.GlobalIdOf(entity)))
                {
                    continue;
                }

                var type = entity.Type;
                if (type.StartsWith("IFCREL", StringComparison.OrdinalIgnoreCase)
                    || type.StartsWith("IFCPROPERTY", StringComparison.OrdinalIgnoreCase)
                    || type.Equals("IFCELEMENTQUANTITY", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list.Add(new IfcIdsElement(this, entity));
            }

            return list;
        }

        internal IfcEntity? TypeOf(int id) => _typeOf.TryGetValue(id, out var typeId) ? _model.ById(typeId) : null;

        internal IReadOnlyList<string> MaterialsOf(int id) =>
            _materials.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        internal IReadOnlyList<KeyValuePair<string, string>> ClassificationsOf(int id) =>
            _classifications.TryGetValue(id, out var list) ? list : (IReadOnlyList<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>();

        internal IReadOnlyList<string> PartOfOf(int id) =>
            _partOf.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        private void BuildTypes()
        {
            // IfcRelDefinesByType: (GlobalId, OwnerHistory, Name, Description, RelatedObjects, RelatingType)
            foreach (var rel in _model.OfType("IFCRELDEFINESBYTYPE"))
            {
                var typeId = rel.At(5).AsReference();
                if (typeId == null)
                {
                    continue;
                }

                foreach (var target in References(rel.At(4)))
                {
                    _typeOf[target] = typeId.Value;
                }
            }
        }

        /// <summary>
        /// Vật liệu: <c>IfcRelAssociatesMaterial</c> trỏ tới một trong nhiều dạng — <c>IfcMaterial</c>,
        /// LayerSetUsage → LayerSet → Layer → Material, ConstituentSet → Constituent → Material, ProfileSet…
        /// Gom tên vật liệu <b>và</b> tên/Category của lớp, đúng như IfcTester so cả hai. Phần tử không có
        /// thì thừa kế từ kiểu.
        /// </summary>
        private void BuildMaterials()
        {
            var byDefinition = new Dictionary<int, List<string>>();

            List<string> Names(int id)
            {
                if (byDefinition.TryGetValue(id, out var cached))
                {
                    return cached;
                }

                var names = new List<string>();
                byDefinition[id] = names;
                var entity = _model.ById(id);
                if (entity == null)
                {
                    return names;
                }

                void Add(string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value!))
                    {
                        names.Add(value!);
                    }
                }

                void AddAll(int other)
                {
                    foreach (var n in Names(other))
                    {
                        Add(n);
                    }
                }

                switch (entity.Type.ToUpperInvariant())
                {
                    case "IFCMATERIAL": // (Name, Description, Category)
                        Add(entity.At(0).AsText());
                        break;
                    case "IFCMATERIALLAYERSETUSAGE": // (ForLayerSet, …)
                    case "IFCMATERIALPROFILESETUSAGE": // (ForProfileSet, …)
                        foreach (var r in References(entity.At(0))) { AddAll(r); }
                        break;
                    case "IFCMATERIALLAYERSET": // (MaterialLayers, LayerSetName, Description)
                        foreach (var r in References(entity.At(0))) { AddAll(r); }
                        break;
                    case "IFCMATERIALLAYER": // (Material, LayerThickness, IsVentilated, Name, Description, Category, Priority)
                        foreach (var r in References(entity.At(0))) { AddAll(r); }
                        Add(entity.At(3).AsText());
                        Add(entity.At(5).AsText());
                        break;
                    case "IFCMATERIALCONSTITUENTSET": // (Name, Description, MaterialConstituents)
                    case "IFCMATERIALPROFILESET": // (Name, Description, MaterialProfiles, CompositeProfile)
                        foreach (var r in References(entity.At(2))) { AddAll(r); }
                        break;
                    case "IFCMATERIALCONSTITUENT": // (Name, Description, Material, Fraction, Category)
                        Add(entity.At(0).AsText());
                        foreach (var r in References(entity.At(2))) { AddAll(r); }
                        Add(entity.At(4).AsText());
                        break;
                    case "IFCMATERIALPROFILE": // (Name, Description, Material, Profile, Priority, Category)
                        Add(entity.At(0).AsText());
                        foreach (var r in References(entity.At(2))) { AddAll(r); }
                        Add(entity.At(5).AsText());
                        break;
                    case "IFCMATERIALLIST": // (Materials)
                        foreach (var r in References(entity.At(0))) { AddAll(r); }
                        break;
                }

                return names;
            }

            // IfcRelAssociatesMaterial: (GlobalId, OwnerHistory, Name, Description, RelatedObjects, RelatingMaterial)
            foreach (var rel in _model.OfType("IFCRELASSOCIATESMATERIAL"))
            {
                var materialId = rel.At(5).AsReference();
                if (materialId == null)
                {
                    continue;
                }

                var names = Names(materialId.Value);
                if (names.Count == 0)
                {
                    continue;
                }

                foreach (var target in References(rel.At(4)))
                {
                    if (!_materials.TryGetValue(target, out var list))
                    {
                        list = new List<string>();
                        _materials[target] = list;
                    }

                    foreach (var n in names)
                    {
                        if (!list.Contains(n))
                        {
                            list.Add(n);
                        }
                    }
                }
            }

            InheritFromType(_materials);
        }

        /// <summary>
        /// Phân loại: mỗi tham chiếu cho (hệ, mã). Hệ = <c>Name</c> của <c>IfcClassification</c> ở gốc chuỗi
        /// <c>ReferencedSource</c>; mã = <c>Identification</c> (IFC4) / <c>ItemReference</c> (IFC2X3) — cùng vị trí 1.
        /// Tham chiếu cha trong chuỗi cũng tính (IfcTester gộp "inherited references").
        /// </summary>
        private void BuildClassifications()
        {
            // IfcRelAssociatesClassification: (…, RelatedObjects=4, RelatingClassification=5)
            foreach (var rel in _model.OfType("IFCRELASSOCIATESCLASSIFICATION"))
            {
                var refId = rel.At(5).AsReference();
                if (refId == null)
                {
                    continue;
                }

                var pairs = new List<KeyValuePair<string, string>>();
                var system = string.Empty;
                var chain = new List<string>();
                var current = _model.ById(refId.Value);
                var guard = 0;
                while (current != null && guard++ < 32)
                {
                    if (current.Type.Equals("IFCCLASSIFICATION", StringComparison.OrdinalIgnoreCase))
                    {
                        // IfcClassification: (Source, Edition, EditionDate, Name, …)
                        system = current.At(3).AsText() ?? string.Empty;
                        break;
                    }

                    // IfcClassificationReference: (Location, Identification, Name, ReferencedSource, …)
                    var code = current.At(1).AsText();
                    if (!string.IsNullOrEmpty(code))
                    {
                        chain.Add(code!);
                    }

                    var next = current.At(3).AsReference();
                    current = next == null ? null : _model.ById(next.Value);
                }

                foreach (var code in chain)
                {
                    pairs.Add(new KeyValuePair<string, string>(system, code));
                }

                if (pairs.Count == 0)
                {
                    continue;
                }

                foreach (var target in References(rel.At(4)))
                {
                    if (!_classifications.TryGetValue(target, out var list))
                    {
                        list = new List<KeyValuePair<string, string>>();
                        _classifications[target] = list;
                    }

                    list.AddRange(pairs);
                }
            }

            InheritFromType(_classifications);
        }

        /// <summary>
        /// "Thuộc về": tên lớp của cấu trúc không gian chứa phần tử và tổ tiên của nó
        /// (<c>IfcRelContainedInSpatialStructure</c> + <c>IfcRelAggregates</c>), của tổ hợp chứa phần tử,
        /// và của nhóm/hệ (<c>IfcRelAssignsToGroup</c>). IDS khai <c>partOf</c> bằng <b>tên lớp</b>
        /// (<c>IFCBUILDINGSTOREY</c>, <c>IFCSYSTEM</c>…), nên ở đây trả tên lớp chứ không trả tên tầng.
        /// </summary>
        private void BuildPartOf()
        {
            var parentOf = new Dictionary<int, int>();
            // IfcRelAggregates: (…, RelatingObject=4, RelatedObjects=5)
            foreach (var rel in _model.OfType("IFCRELAGGREGATES"))
            {
                var parent = rel.At(4).AsReference();
                if (parent == null)
                {
                    continue;
                }

                foreach (var child in References(rel.At(5)))
                {
                    parentOf[child] = parent.Value;
                }
            }

            // IfcRelNests: cùng bố cục với IfcRelAggregates
            foreach (var rel in _model.OfType("IFCRELNESTS"))
            {
                var parent = rel.At(4).AsReference();
                if (parent == null)
                {
                    continue;
                }

                foreach (var child in References(rel.At(5)))
                {
                    if (!parentOf.ContainsKey(child))
                    {
                        parentOf[child] = parent.Value;
                    }
                }
            }

            var containerOf = new Dictionary<int, int>();
            // IfcRelContainedInSpatialStructure: (…, RelatedElements=4, RelatingStructure=5)
            foreach (var rel in _model.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
            {
                var container = rel.At(5).AsReference();
                if (container == null)
                {
                    continue;
                }

                foreach (var element in References(rel.At(4)))
                {
                    containerOf[element] = container.Value;
                }
            }

            void AddAncestors(List<string> list, int start)
            {
                var guard = 0;
                var current = start;
                while (guard++ < 64)
                {
                    var entity = _model.ById(current);
                    if (entity != null && !list.Contains(entity.Type))
                    {
                        list.Add(entity.Type);
                    }

                    if (!parentOf.TryGetValue(current, out var next))
                    {
                        if (containerOf.TryGetValue(current, out var container))
                        {
                            next = container;
                        }
                        else
                        {
                            return;
                        }
                    }

                    current = next;
                }
            }

            foreach (var id in containerOf.Keys.Concat(parentOf.Keys).Distinct())
            {
                var list = new List<string>();
                if (parentOf.TryGetValue(id, out var parent))
                {
                    AddAncestors(list, parent);
                }

                if (containerOf.TryGetValue(id, out var container))
                {
                    AddAncestors(list, container);
                }

                _partOf[id] = list;
            }

            // IfcRelAssignsToGroup: (…, RelatedObjects=4, RelatedObjectsType=5, RelatingGroup=6)
            foreach (var rel in _model.OfType("IFCRELASSIGNSTOGROUP"))
            {
                var groupId = rel.At(6).AsReference();
                var group = groupId == null ? null : _model.ById(groupId.Value);
                if (group == null)
                {
                    continue;
                }

                foreach (var member in References(rel.At(4)))
                {
                    if (!_partOf.TryGetValue(member, out var list))
                    {
                        list = new List<string>();
                        _partOf[member] = list;
                    }

                    if (!list.Contains(group.Type))
                    {
                        list.Add(group.Type);
                    }
                }
            }
        }

        private void InheritFromType<T>(Dictionary<int, List<T>> table)
        {
            foreach (var pair in _typeOf)
            {
                if (table.ContainsKey(pair.Key) || !table.TryGetValue(pair.Value, out var fromType))
                {
                    continue;
                }

                table[pair.Key] = new List<T>(fromType);
            }
        }

        internal static IEnumerable<int> References(IfcValue value)
        {
            if (value.Kind == IfcValueKind.Reference)
            {
                yield return value.Reference;
                yield break;
            }

            if (value.Kind != IfcValueKind.List)
            {
                yield break;
            }

            foreach (var item in value.Items)
            {
                if (item.Kind == IfcValueKind.Reference)
                {
                    yield return item.Reference;
                }
            }
        }
    }

    /// <summary>Một thực thể IFC nhìn dưới con mắt IDS. Toàn bộ chỗ dịch IFC → IDS nằm ở đây.</summary>
    public sealed class IfcIdsElement : IIdsElement
    {
        // Vị trí tham số theo lược đồ IFC — giống nhau ở mọi lớp con của IfcObject/IfcTypeObject:
        // IfcRoot: GlobalId 0, OwnerHistory 1, Name 2, Description 3. IfcObject: ObjectType 4.
        // IfcElement (IfcProduct + Tag): ObjectPlacement 5, Representation 6, Tag 7.
        // IfcTypeProduct: ApplicableOccurrence 4, HasPropertySets 5, RepresentationMaps 6, Tag 7, ElementType 8.
        private const int NameIndex = 2;
        private const int DescriptionIndex = 3;
        private const int ObjectTypeIndex = 4;
        private const int TagIndex = 7;
        private const int ElementTypeIndex = 8;

        /// <summary>
        /// Thuộc tính riêng của một số lớp hay bị IDS hỏi, theo vị trí trong lược đồ IFC4 (IFC2X3 giống ở
        /// những lớp này). Không có bảng lược đồ đầy đủ — tên khác các tên này thì trả <c>null</c> (facet trượt,
        /// không âm thầm đạt); báo cáo đối chiếu §41 nói rõ giới hạn.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, int>> ClassAttributes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["IFCDOOR"] = Table(("OverallHeight", 8), ("OverallWidth", 9), ("OperationType", 11), ("UserDefinedOperationType", 12)),
            ["IFCWINDOW"] = Table(("OverallHeight", 8), ("OverallWidth", 9), ("PartitioningType", 11), ("UserDefinedPartitioningType", 12)),
            ["IFCSPACE"] = Table(("LongName", 7), ("CompositionType", 8), ("ElevationWithFlooring", 10)),
            ["IFCBUILDINGSTOREY"] = Table(("LongName", 7), ("CompositionType", 8), ("Elevation", 9)),
            ["IFCBUILDING"] = Table(("LongName", 7), ("CompositionType", 8), ("ElevationOfRefHeight", 9), ("ElevationOfTerrain", 10)),
            ["IFCSITE"] = Table(("LongName", 7), ("CompositionType", 8), ("RefLatitude", 9), ("RefLongitude", 10), ("RefElevation", 11), ("LandTitleNumber", 12)),
            ["IFCPROJECT"] = Table(("LongName", 5), ("Phase", 6)),
        };

        private static Dictionary<string, int> Table(params (string Name, int Index)[] entries)
        {
            var table = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                table[entry.Name] = entry.Index;
            }

            return table;
        }

        private static readonly Dictionary<string, int> PredefinedTypeIndexOverride = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // IfcSpatialStructureElement có CompositionType (enum) ở vị trí 8 trước PredefinedType.
            ["IFCSPACE"] = 9,
            ["IFCBUILDINGSTOREY"] = -1,
            ["IFCBUILDING"] = -1,
            ["IFCSITE"] = -1,
        };

        private readonly IfcIdsModel _model;
        private readonly IfcEntity _entity;
        private readonly IfcEntity? _type;

        internal IfcIdsElement(IfcIdsModel model, IfcEntity entity)
        {
            _model = model;
            _entity = entity;
            _type = model.TypeOf(entity.Id);
        }

        /// <summary>Số hiệu <c>#id</c> trong file — để người đọc báo cáo tìm lại dòng.</summary>
        public int Id => _entity.Id;

        /// <summary>Nhãn trong báo cáo: <c>#25604 — IFCWALL "Basic Wall:…"</c>.</summary>
        public string Label => "#" + _entity.Id + " — " + _entity.Type + " \"" + (IfcModel.NameOf(_entity) ?? string.Empty) + "\"";

        /// <summary>Tên lớp VIẾT HOA đúng như trong file (<c>IFCWALL</c>); IDS so không phân biệt hoa thường.</summary>
        public string IfcEntity => _entity.Type;

        /// <summary>
        /// PredefinedType theo đúng cách IfcTester suy: giá trị ở phần tử; <c>NOTDEFINED</c>/thiếu thì lấy
        /// của kiểu; <c>USERDEFINED</c> thì lấy <c>ObjectType</c> (phần tử) hoặc <c>ElementType</c> (kiểu).
        /// Vị trí của PredefinedType khác nhau theo lớp, nhưng ở mọi IfcElement nó là <b>enum đầu tiên sau
        /// Tag</b> (IfcWall: 8; IfcDoor IFC4: 10 sau OverallHeight/OverallWidth) — không cần bảng lược đồ.
        /// </summary>
        public string PredefinedType
        {
            get
            {
                var own = EnumAfter(_entity, IsType(_entity) ? ElementTypeIndex : TagIndex);
                if (own == "USERDEFINED")
                {
                    return (IsType(_entity) ? _entity.At(ElementTypeIndex).AsText() : _entity.At(ObjectTypeIndex).AsText()) ?? string.Empty;
                }

                if (!string.IsNullOrEmpty(own) && own != "NOTDEFINED")
                {
                    return own!;
                }

                if (_type == null)
                {
                    return string.Empty;
                }

                var fromType = EnumAfter(_type, ElementTypeIndex);
                if (fromType == "USERDEFINED")
                {
                    return _type.At(ElementTypeIndex).AsText() ?? string.Empty;
                }

                return string.IsNullOrEmpty(fromType) || fromType == "NOTDEFINED" ? string.Empty : fromType!;
            }
        }

        /// <summary>Thuộc tính trực tiếp của thực thể: GlobalId, Name, Description, ObjectType, Tag, PredefinedType, ElementType.</summary>
        public string? Attribute(string name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "globalid":
                    return IfcModel.GlobalIdOf(_entity);
                case "name":
                    return _entity.At(NameIndex).AsText();
                case "description":
                    return _entity.At(DescriptionIndex).AsText();
                case "objecttype":
                    return IsType(_entity) ? null : _entity.At(ObjectTypeIndex).AsText();
                case "elementtype":
                    return IsType(_entity) ? _entity.At(ElementTypeIndex).AsText() : null;
                case "tag":
                    return _entity.At(TagIndex).Kind == IfcValueKind.Text ? _entity.At(TagIndex).Raw : null;
                case "predefinedtype":
                    var value = EnumAfter(_entity, IsType(_entity) ? ElementTypeIndex : TagIndex);
                    return string.IsNullOrEmpty(value) ? null : value;
                default:
                    if (ClassAttributes.TryGetValue(_entity.Type, out var byName)
                        && byName.TryGetValue((name ?? string.Empty).Trim(), out var index))
                    {
                        return NormalizeText(_entity.At(index).AsText());
                    }

                    return null;
            }
        }

        /// <summary>
        /// Boolean/logical trong STEP là <c>.T.</c>/<c>.F.</c>/<c>.U.</c>; IDS (và IfcTester) so với
        /// <c>TRUE</c>/<c>FALSE</c>. Không đổi thì "IsExternal = FALSE" trượt cả 1078 tường trong khi IfcTester
        /// cho 590 đạt (§41). <c>UNKNOWN</c> giữ chữ — IfcTester coi nó là rỗng, tức không đạt.
        /// </summary>
        private static string? NormalizeText(string? text)
        {
            switch (text)
            {
                case "T": return "TRUE";
                case "F": return "FALSE";
                case "U": return "UNKNOWN";
                default: return text;
            }
        }

        /// <summary>Property theo Pset — đã gộp thuộc tính thừa kế từ kiểu (xem <see cref="IfcModel.PropertiesOf"/>).</summary>
        public string? Property(string? propertySet, string name)
        {
            var key = string.IsNullOrWhiteSpace(propertySet) ? name : propertySet + "." + name;
            return _model.Model.TryProperty(_entity.Id, key, out var value) ? NormalizeText(value) : null;
        }

        /// <summary>Mã phân loại theo hệ (rỗng = mọi hệ).</summary>
        public IEnumerable<string> Classifications(string? system)
        {
            foreach (var pair in _model.ClassificationsOf(_entity.Id))
            {
                if (string.IsNullOrWhiteSpace(system) || string.Equals(pair.Key, system, StringComparison.OrdinalIgnoreCase))
                {
                    yield return pair.Value;
                }
            }
        }

        /// <summary>Tên vật liệu, tên lớp/thành phần và Category của chúng.</summary>
        public IEnumerable<string> Materials => _model.MaterialsOf(_entity.Id);

        /// <summary>Tên lớp của tầng/toà nhà/tổ hợp/hệ chứa phần tử.</summary>
        public IEnumerable<string> PartOf => _model.PartOfOf(_entity.Id);

        private static bool IsType(IfcEntity entity) => entity.Type.EndsWith("TYPE", StringComparison.OrdinalIgnoreCase);

        private static string? EnumAfter(IfcEntity entity, int after)
        {
            var start = after + 1;
            if (PredefinedTypeIndexOverride.TryGetValue(entity.Type, out var index))
            {
                if (index < 0)
                {
                    return null;
                }

                start = index;
            }

            for (var i = start; i < entity.Attributes.Count; i++)
            {
                var value = entity.Attributes[i];
                if (value.Kind == IfcValueKind.Enumeration)
                {
                    // .T./.F./.U. là IfcBoolean/IfcLogical, không phải PredefinedType.
                    if (value.Raw == "T" || value.Raw == "F" || value.Raw == "U")
                    {
                        continue;
                    }

                    return value.Raw;
                }
            }

            return null;
        }
    }
}
