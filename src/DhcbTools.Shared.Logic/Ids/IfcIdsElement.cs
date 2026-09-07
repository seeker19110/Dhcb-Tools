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
        private readonly Dictionary<int, List<(string? Relation, string Entity)>> _partOf = new Dictionary<int, List<(string?, string)>>();

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

        internal IReadOnlyList<(string? Relation, string Entity)> PartOfOf(int id) =>
            _partOf.TryGetValue(id, out var list) ? list : (IReadOnlyList<(string?, string)>)Array.Empty<(string?, string)>();

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
        /// "Thuộc về": mỗi tổ tiên gắn đúng loại quan hệ IFC đã dùng để tới đó, theo
        /// <c>Documentation/UserManual/partof-facet.md</c> của buildingSMART/IDS — <c>partOf</c> khai
        /// <c>relation</c> thì chỉ chuỗi <b>thuần một loại quan hệ đó</b> mới hợp lệ (không được rơi
        /// xuống loại khác giữa chừng); không khai <c>relation</c> thì mọi cấu trúc quan hệ hợp lệ, kể cả
        /// <b>trộn nhiều loại</b>, đều tính — hai trường hợp này không phải cùng một tập hợp (chuỗi trộn
        /// A→B bằng <c>IfcRelNests</c> rồi B→C bằng <c>IfcRelAggregates</c> làm C "thuộc về" A khi không
        /// khai <c>relation</c>, nhưng KHÔNG khi khai <c>relation="IFCRELAGGREGATES"</c>), nên tính riêng:
        /// năm chuỗi thuần (<c>Relation</c> khác <c>null</c>) và một chuỗi trộn
        /// (<c>Relation</c> <c>null</c>, đi qua hợp của cả năm loại quan hệ).
        /// IDS khai <c>partOf</c> bằng <b>tên lớp</b> (<c>IFCBUILDINGSTOREY</c>, <c>IFCSYSTEM</c>…), nên ở
        /// đây trả tên lớp chứ không trả tên tầng.
        /// </summary>
        private void BuildPartOf()
        {
            var aggregates = new Dictionary<int, int>();
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
                    aggregates[child] = parent.Value;
                }
            }

            var nests = new Dictionary<int, int>();
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
                    nests[child] = parent.Value;
                }
            }

            var contained = new Dictionary<int, int>();
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
                    contained[element] = container.Value;
                }
            }

            var groups = new Dictionary<int, List<int>>();
            // IfcRelAssignsToGroup: (…, RelatedObjects=4, RelatedObjectsType=5, RelatingGroup=6). Một phần
            // tử có thể vào nhiều nhóm (member của cả hệ điện lẫn hệ điều khiển), nên gom danh sách chứ
            // không ghi đè.
            foreach (var rel in _model.OfType("IFCRELASSIGNSTOGROUP"))
            {
                var group = rel.At(6).AsReference();
                if (group == null)
                {
                    continue;
                }

                foreach (var member in References(rel.At(4)))
                {
                    if (!groups.TryGetValue(member, out var list))
                    {
                        list = new List<int>();
                        groups[member] = list;
                    }

                    list.Add(group.Value);
                }
            }

            var voidsAndFills = new Dictionary<int, int>();
            {
                // IfcRelVoidsElement: (…, RelatingBuildingElement=4, RelatedOpeningElement=5) — tường/dầm
                // "khoét" một opening. IfcRelFillsElement: (…, RelatingOpeningElement=4,
                // RelatedBuildingElement=5) — cửa/cửa sổ "lấp" opening đó. IDS gộp cặp này thành MỘT loại
                // quan hệ (giá trị enum có khoảng trắng ở giữa trong chính ids.xsd) nối thẳng cửa → phần tử
                // chủ nhà, bỏ qua opening trung gian — opening tự nó không phải là điều IDS author cần nói.
                var openingHost = new Dictionary<int, int>();
                foreach (var rel in _model.OfType("IFCRELVOIDSELEMENT"))
                {
                    var host = rel.At(4).AsReference();
                    var opening = rel.At(5).AsReference();
                    if (host != null && opening != null)
                    {
                        openingHost[opening.Value] = host.Value;
                    }
                }

                foreach (var rel in _model.OfType("IFCRELFILLSELEMENT"))
                {
                    var opening = rel.At(4).AsReference();
                    var filled = rel.At(5).AsReference();
                    if (opening != null && filled != null && openingHost.TryGetValue(opening.Value, out var host))
                    {
                        voidsAndFills[filled.Value] = host;
                    }
                }
            }

            // Phần tử không có IfcRelContainedInSpatialStructure của RIÊNG nó nhưng nằm trong một tổ hợp
            // (cửa của một curtain wall, cấu kiện của một assembly) vẫn thuộc về đúng tầng của tổ hợp: IFC
            // không cho phép xếp phần và tổng vào hai vị trí không gian khác nhau, nên vị trí của tổng LÀ
            // vị trí của phần. IfcOpenShell/IfcTester kết luận đúng như vậy (`get_container` rơi về cha
            // phân rã khi không có quan hệ trực tiếp). Bản trước chỉ đọc quan hệ trực tiếp nên báo 7 cửa
            // curtain wall của Snowdon "không thuộc tầng nào" trong khi IfcTester nói 142/142 — báo nhầm,
            // xem bang-chung-test.md §71. Cha phân rã lấy theo đúng thứ tự của IfcOpenShell `get_parent`:
            // aggregates → nests → cặp voids/fills.
            {
                var decompositionParent = new Dictionary<int, int>(aggregates);
                foreach (var table in new[] { nests, voidsAndFills })
                {
                    foreach (var pair in table)
                    {
                        if (!decompositionParent.ContainsKey(pair.Key))
                        {
                            decompositionParent[pair.Key] = pair.Value;
                        }
                    }
                }

                foreach (var child in decompositionParent.Keys.ToList())
                {
                    if (contained.ContainsKey(child))
                    {
                        continue;
                    }

                    var chain = new List<int>();
                    var visited = new HashSet<int> { child };
                    var current = child;
                    int? inherited = null;
                    while (decompositionParent.TryGetValue(current, out var parent) && visited.Add(parent))
                    {
                        chain.Add(current);
                        if (contained.TryGetValue(parent, out var container))
                        {
                            inherited = container;
                            break;
                        }

                        current = parent;
                    }

                    if (inherited != null)
                    {
                        foreach (var id in chain)
                        {
                            contained[id] = inherited.Value;
                        }
                    }
                }
            }

            // Chuỗi THUẦN một loại quan hệ: đi tới hết theo đúng một bảng cha-con, dừng khi hết cạnh hoặc
            // gặp lại (chắn vòng lặp — mô hình lỗi có thể tự tham chiếu).
            List<string> WalkSingle(IReadOnlyDictionary<int, int> parentOf, int start)
            {
                var list = new List<string>();
                var visited = new HashSet<int> { start };
                var current = start;
                while (parentOf.TryGetValue(current, out var next) && visited.Add(next))
                {
                    var entity = _model.ById(next);
                    if (entity != null && !list.Contains(entity.Type))
                    {
                        list.Add(entity.Type);
                    }

                    current = next;
                }

                return list;
            }

            // Chuỗi THUẦN của quan hệ có thể rẽ nhánh (nhóm/hệ): BFS qua đúng một bảng, có thể nhiều cha.
            List<string> WalkMulti(IReadOnlyDictionary<int, List<int>> parentsOf, int start)
            {
                var list = new List<string>();
                var visited = new HashSet<int> { start };
                var queue = new Queue<int>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    if (!parentsOf.TryGetValue(queue.Dequeue(), out var parents))
                    {
                        continue;
                    }

                    foreach (var parent in parents)
                    {
                        if (!visited.Add(parent))
                        {
                            continue;
                        }

                        var entity = _model.ById(parent);
                        if (entity != null && !list.Contains(entity.Type))
                        {
                            list.Add(entity.Type);
                        }

                        queue.Enqueue(parent);
                    }
                }

                return list;
            }

            // Chuỗi TRỘN cho trường hợp không khai relation: y NGUYÊN thuật toán trước bản sửa này (một
            // "cha kế tiếp" mỗi bước — ưu tiên aggregates rồi mới container, KHÔNG rẽ nhánh qua nhóm/hệ),
            // để không âm thầm đổi hành vi đã có test giữ từ trước. Rẽ nhánh đầy đủ qua mọi quan hệ (kể cả
            // nhóm/hệ) từng làm cho phụ kiện lồng trong cửa "thuộc về" luôn cả hệ của chính cửa — hợp lý
            // theo nghĩa đồ thị, nhưng KHÁC kết luận cũ mà chưa ai xác nhận lại là đúng hơn (không có
            // IfcTester ở đây để đối chiếu). Chỉ phần "khai relation cụ thể" là mục 11.4 phải sửa, không
            // phải phần này.
            var singleParent = new Dictionary<int, int>(aggregates);
            foreach (var pair in nests)
            {
                if (!singleParent.ContainsKey(pair.Key))
                {
                    singleParent[pair.Key] = pair.Value;
                }
            }

            void AddMixedAncestors(List<string> list, int start)
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

                    if (!singleParent.TryGetValue(current, out var next))
                    {
                        if (contained.TryGetValue(current, out var container))
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

            var mixed = new Dictionary<int, List<string>>();
            foreach (var id in contained.Keys.Concat(singleParent.Keys).Distinct())
            {
                var list = new List<string>();
                if (singleParent.TryGetValue(id, out var parent))
                {
                    AddMixedAncestors(list, parent);
                }

                if (contained.TryGetValue(id, out var container))
                {
                    AddMixedAncestors(list, container);
                }

                mixed[id] = list;
            }

            // Nhóm/hệ: một bước phẳng, KHÔNG đệ quy — chỉ gắn cho đúng phần tử là RelatedObjects của
            // IfcRelAssignsToGroup, không lan lên/xuống theo aggregates/nests/contained. Y nguyên bản cũ.
            foreach (var pair in groups)
            {
                if (!mixed.TryGetValue(pair.Key, out var list))
                {
                    list = new List<string>();
                    mixed[pair.Key] = list;
                }

                foreach (var groupId in pair.Value)
                {
                    var group = _model.ById(groupId);
                    if (group != null && !list.Contains(group.Type))
                    {
                        list.Add(group.Type);
                    }
                }
            }

            var everyChild = aggregates.Keys.Concat(nests.Keys).Concat(contained.Keys)
                .Concat(groups.Keys).Concat(voidsAndFills.Keys).Distinct();

            foreach (var id in everyChild)
            {
                var entries = new List<(string?, string)>();
                if (mixed.TryGetValue(id, out var mixedList))
                {
                    entries.AddRange(mixedList.Select(type => ((string?)null, type)));
                }

                entries.AddRange(WalkSingle(aggregates, id).Select(type => ((string?)IdsRelations.Aggregates, type)));
                entries.AddRange(WalkSingle(nests, id).Select(type => ((string?)IdsRelations.Nests, type)));
                entries.AddRange(WalkSingle(contained, id).Select(type => ((string?)IdsRelations.ContainedInSpatialStructure, type)));
                entries.AddRange(WalkMulti(groups, id).Select(type => ((string?)IdsRelations.AssignsToGroup, type)));
                entries.AddRange(WalkSingle(voidsAndFills, id).Select(type => ((string?)IdsRelations.VoidsAndFills, type)));
                _partOf[id] = entries;
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
        public IEnumerable<(string? Relation, string Entity)> PartOf => _model.PartOfOf(_entity.Id);

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
