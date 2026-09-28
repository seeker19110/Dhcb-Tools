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
    public sealed partial class IfcIdsModel
    {
        private readonly IfcModel _model;
        private readonly Dictionary<int, int> _typeOf = new Dictionary<int, int>();
        private readonly Dictionary<int, List<string>> _materials = new Dictionary<int, List<string>>();
        private readonly Dictionary<int, List<(string? Relation, string Entity)>> _partOf = new Dictionary<int, List<(string?, string)>>();

        /// <summary>Như <see cref="_partOf"/> nhưng giữ số hiệu tổ tiên — để đọc PredefinedType của nó.</summary>
        private readonly Dictionary<int, List<(string? Relation, int Parent)>> _partOfIds = new Dictionary<int, List<(string?, int)>>();

        /// <summary>Phân loại gán TRỰC TIẾP: IfcClassificationReference hoặc cả một IfcClassification.</summary>
        private readonly Dictionary<int, List<int>> _classificationLinks = new Dictionary<int, List<int>>();

        private IfcIdsModel(IfcModel model)
        {
            _model = model;
            Schema = IfcSchema.For(model.Schema);
            BuildTypes();
            BuildMaterials();
            BuildClassifications();
            BuildPartOf();
            BuildPropertyDefinitions();
            BuildUnits();
        }

        /// <summary>Đọc file IFC (nội dung văn bản) và dựng sẵn các bảng tra.</summary>
        public static IfcIdsModel Parse(string text) => new IfcIdsModel(IfcModel.Parse(text));

        /// <summary>Dựng bảng tra trên một mô hình đã đọc (dùng chung với <see cref="IfcChecker"/>, không đọc lại file).</summary>
        public static IfcIdsModel From(IfcModel model) => new IfcIdsModel(model ?? throw new ArgumentNullException(nameof(model)));

        /// <summary>Mô hình IFC bên dưới.</summary>
        public IfcModel Model => _model;

        /// <summary>Lược đồ dùng để tra thuộc tính theo tên.</summary>
        internal IfcSchema Schema { get; }

        /// <summary>
        /// Mọi thực thể trong phần DATA. IDS nói được tới cả thực thể không có GlobalId (<c>IfcMaterial</c>,
        /// <c>IfcTaskTime</c>, <c>IfcSurfaceStyleRefraction</c>…) — bản trước chỉ lấy thực thể mang GlobalId nên
        /// specification nhắm tới chúng luôn "không có phần tử nào". Tập ứng viên của từng specification do facet
        /// applicability đầu tiên quyết (xem <see cref="IIdsTypedElement.InScopeOf"/>), như IfcTester.
        /// </summary>
        public IReadOnlyList<IIdsElement> Elements()
        {
            var list = new List<IIdsElement>();
            foreach (var entity in _model.File.Data)
            {
                if (entity.Id != 0 && ReferenceEquals(_model.ById(entity.Id), entity))
                {
                    list.Add(new IfcIdsElement(this, entity));
                }
            }

            return list;
        }

        internal IfcEntity? TypeOf(int id) => _typeOf.TryGetValue(id, out var typeId) ? _model.ById(typeId) : null;

        internal IReadOnlyList<string> MaterialsOf(int id) =>
            _materials.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        internal IReadOnlyList<(string? Relation, string Entity)> PartOfOf(int id) =>
            _partOf.TryGetValue(id, out var list) ? list : (IReadOnlyList<(string?, string)>)Array.Empty<(string?, string)>();

        internal IEnumerable<(string? Relation, string Entity, string PredefinedType)> PartOfWithPredefinedTypeOf(int id)
        {
            if (!_partOfIds.TryGetValue(id, out var list))
            {
                yield break;
            }

            foreach (var (relation, parent) in list)
            {
                var entity = _model.ById(parent)!;
                yield return (relation, entity.Type, new IfcIdsElement(this, entity).PredefinedType);
            }
        }

        /// <summary>Tham số theo TÊN thuộc tính (tra lược đồ), hoặc <see cref="IfcValue.Empty"/> khi lớp không có thuộc tính đó.</summary>
        internal IfcValue Value(IfcEntity entity, string attribute)
        {
            var index = Schema.IndexOf(entity.Type, attribute);
            return index < 0 ? IfcValue.Empty : entity.At(index);
        }

        internal string? Text(IfcEntity entity, string attribute) => Value(entity, attribute).AsText();

        internal bool IsA(IfcEntity entity, string ancestor) => Schema.IsA(entity.Type, ancestor);

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
                    // IDS: facet material khớp Name hoặc Category của vật liệu, và Name/LayerSetName của bộ
                    // lớp/thành phần/profile. Bản cũ bỏ sót Category của IfcMaterial và tên các bộ — 6 ca "pass"
                    // của buildingSMART trượt (a_material_category_may_pass…, a_layer_set_name_will_pass…).
                    case "IFCMATERIAL": // (Name, Description, Category) — IFC2X3 chỉ có Name
                        Add(entity.At(0).AsText());
                        Add(entity.At(2).AsText());
                        break;
                    case "IFCMATERIALLAYERSETUSAGE": // (ForLayerSet, …)
                    case "IFCMATERIALPROFILESETUSAGE": // (ForProfileSet, …)
                        foreach (var r in References(entity.At(0))) { AddAll(r); }
                        break;
                    case "IFCMATERIALLAYERSET": // (MaterialLayers, LayerSetName, Description)
                        foreach (var r in References(entity.At(0))) { AddAll(r); }
                        Add(entity.At(1).AsText());
                        break;
                    case "IFCMATERIALLAYER": // (Material, LayerThickness, IsVentilated, Name, Description, Category, Priority)
                        foreach (var r in References(entity.At(0))) { AddAll(r); }
                        Add(entity.At(3).AsText());
                        Add(entity.At(5).AsText());
                        break;
                    case "IFCMATERIALCONSTITUENTSET": // (Name, Description, MaterialConstituents)
                    case "IFCMATERIALPROFILESET": // (Name, Description, MaterialProfiles, CompositeProfile)
                        foreach (var r in References(entity.At(2))) { AddAll(r); }
                        Add(entity.At(0).AsText());
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
        /// Phân loại gán trực tiếp: <c>IfcRelAssociatesClassification</c> (thực thể có GlobalId) và
        /// <c>IfcExternalReferenceRelationship</c> (tài nguyên không có GlobalId như <c>IfcMaterial</c>, IFC4+).
        /// </summary>
        private void BuildClassifications()
        {
            void Link(int target, int reference)
            {
                if (!_classificationLinks.TryGetValue(target, out var list))
                {
                    list = new List<int>();
                    _classificationLinks[target] = list;
                }

                if (!list.Contains(reference))
                {
                    list.Add(reference);
                }
            }

            // IfcRelAssociatesClassification: (…, RelatedObjects=4, RelatingClassification=5)
            foreach (var rel in _model.OfType("IFCRELASSOCIATESCLASSIFICATION"))
            {
                var reference = rel.At(5).AsReference();
                foreach (var target in reference == null ? Enumerable.Empty<int>() : References(rel.At(4)))
                {
                    Link(target, reference!.Value);
                }
            }

            // IfcExternalReferenceRelationship: (Name, Description, RelatingReference=2, RelatedResourceObjects=3)
            foreach (var rel in _model.OfType("IFCEXTERNALREFERENCERELATIONSHIP"))
            {
                var reference = rel.At(2).AsReference();
                foreach (var target in reference == null ? Enumerable.Empty<int>() : References(rel.At(3)))
                {
                    Link(target, reference!.Value);
                }
            }
        }

        /// <summary>
        /// Các cặp (mã, hệ) của phần tử, đúng cách IfcTester/ifcopenshell gom: tham chiếu gán trực tiếp, cộng tham
        /// chiếu của KIỂU cho những hệ mà phần tử không tự khai ("occurrences override the type classification per
        /// system"), cộng mọi tham chiếu cha trong chuỗi <c>ReferencedSource</c>. Hệ = <c>Name</c> của
        /// <c>IfcClassification</c> ở gốc chuỗi; gán thẳng cả một <c>IfcClassification</c> cho mã <c>null</c>.
        /// Tài nguyên không có GlobalId chỉ đọc tham chiếu ngoài của chính nó.
        /// </summary>
        internal List<(string? Code, string? System)> ClassificationPairs(IfcEntity entity)
        {
            var links = Links(entity.Id);
            var type = IsA(entity, "IFCOBJECT") ? TypeOf(entity.Id) : null;
            if (type != null && Links(type.Id).Count > 0)
            {
                var own = new HashSet<int>(links.Select(RootOf));
                links = links.Concat(Links(type.Id).Where(r => !own.Contains(RootOf(r)))).ToList();
            }

            var pairs = new List<(string?, string?)>();
            foreach (var link in links)
            {
                var current = _model.ById(link);
                var system = SystemOf(link);
                if (current != null && current.Type.Equals("IFCCLASSIFICATION", StringComparison.OrdinalIgnoreCase))
                {
                    pairs.Add((null, system));
                }

                // IfcClassificationReference: (Location, Identification|ItemReference=1, Name, ReferencedSource=3, …)
                for (var guard = 0; current != null && current.Type.Equals("IFCCLASSIFICATIONREFERENCE", StringComparison.OrdinalIgnoreCase) && guard < 32; guard++)
                {
                    pairs.Add((current.At(1).AsText(), system));
                    var next = current.At(3).AsReference();
                    current = next == null ? null : _model.ById(next.Value);
                }
            }

            return pairs;
        }

        private IReadOnlyList<int> Links(int id) =>
            _classificationLinks.TryGetValue(id, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();

        /// <summary>Số hiệu IfcClassification ở gốc chuỗi tham chiếu (-1 khi chuỗi không tới gốc nào).</summary>
        private int RootOf(int reference)
        {
            var current = _model.ById(reference);
            for (var guard = 0; current != null && guard < 32; guard++)
            {
                if (current.Type.Equals("IFCCLASSIFICATION", StringComparison.OrdinalIgnoreCase))
                {
                    return current.Id;
                }

                var next = current.At(3).AsReference();
                current = next == null ? null : _model.ById(next.Value);
            }

            return -1;
        }

        /// <summary>IfcClassification: (Source, Edition, EditionDate, Name=3, …) — cùng vị trí ở IFC2X3 và IFC4.</summary>
        private string? SystemOf(int reference)
        {
            var root = RootOf(reference);
            return root < 0 ? null : _model.ById(root)!.At(3).AsText();
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
            List<int> WalkSingle(IReadOnlyDictionary<int, int> parentOf, int start)
            {
                var list = new List<int>();
                var visited = new HashSet<int> { start };
                var current = start;
                while (parentOf.TryGetValue(current, out var next) && visited.Add(next))
                {
                    if (_model.ById(next) != null)
                    {
                        list.Add(next);
                    }

                    current = next;
                }

                return list;
            }

            // Chuỗi THUẦN của quan hệ có thể rẽ nhánh (nhóm/hệ): BFS qua đúng một bảng, có thể nhiều cha.
            List<int> WalkMulti(IReadOnlyDictionary<int, List<int>> parentsOf, int start)
            {
                var list = new List<int>();
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

                        if (_model.ById(parent) != null)
                        {
                            list.Add(parent);
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

            void AddMixedAncestors(List<int> list, int start)
            {
                var guard = 0;
                var current = start;
                while (guard++ < 64)
                {
                    if (_model.ById(current) != null && !list.Contains(current))
                    {
                        list.Add(current);
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

            var mixed = new Dictionary<int, List<int>>();
            foreach (var id in contained.Keys.Concat(singleParent.Keys).Distinct())
            {
                var list = new List<int>();
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
                    list = new List<int>();
                    mixed[pair.Key] = list;
                }

                foreach (var groupId in pair.Value)
                {
                    if (_model.ById(groupId) != null && !list.Contains(groupId))
                    {
                        list.Add(groupId);
                    }
                }
            }

            var everyChild = aggregates.Keys.Concat(nests.Keys).Concat(contained.Keys)
                .Concat(groups.Keys).Concat(voidsAndFills.Keys).Distinct();

            foreach (var id in everyChild)
            {
                var entries = new List<(string?, int)>();
                if (mixed.TryGetValue(id, out var mixedList))
                {
                    entries.AddRange(mixedList.Select(parent => ((string?)null, parent)));
                }

                entries.AddRange(WalkSingle(aggregates, id).Select(parent => ((string?)IdsRelations.Aggregates, parent)));
                entries.AddRange(WalkSingle(nests, id).Select(parent => ((string?)IdsRelations.Nests, parent)));
                entries.AddRange(WalkSingle(contained, id).Select(parent => ((string?)IdsRelations.ContainedInSpatialStructure, parent)));
                entries.AddRange(WalkMulti(groups, id).Select(parent => ((string?)IdsRelations.AssignsToGroup, parent)));
                entries.AddRange(WalkSingle(voidsAndFills, id).Select(parent => ((string?)IdsRelations.VoidsAndFills, parent)));
                _partOfIds[id] = entries;
                // Danh sách theo tên lớp: mỗi (quan hệ, lớp) một lần, giữ thứ tự gặp đầu — y như bản trước.
                _partOf[id] = entries.Select(e => (e.Item1, _model.ById(e.Item2)!.Type)).Distinct().ToList();
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
    public sealed class IfcIdsElement : IIdsElement, IIdsElementDetails, IIdsTypedElement
    {
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
        public string Label => "#" + _entity.Id + " — " + _entity.Type + " \"" + (_model.Text(_entity, "Name") ?? string.Empty) + "\"";

        /// <summary>Tên lớp VIẾT HOA đúng như trong file (<c>IFCWALL</c>).</summary>
        public string IfcEntity => _entity.Type;

        /// <summary>
        /// PredefinedType theo đúng cách IfcTester suy: giá trị ở phần tử; <c>NOTDEFINED</c>/thiếu thì lấy của kiểu;
        /// <c>USERDEFINED</c> thì lấy <c>ObjectType</c> (phần tử) hoặc <c>ElementType</c> (kiểu). Vị trí tra theo
        /// lược đồ — không đoán "enum đầu tiên sau Tag" như bản trước (sai ở IfcDoor, IfcSpace, lớp IFC2X3…).
        /// </summary>
        public string PredefinedType
        {
            get
            {
                var own = Own(_entity);
                if (own == "USERDEFINED")
                {
                    return UserDefined(_entity) ?? string.Empty;
                }

                if (!string.IsNullOrEmpty(own) && own != "NOTDEFINED")
                {
                    return own!;
                }

                var fromType = _type == null ? null : Own(_type);
                if (fromType == "USERDEFINED")
                {
                    return UserDefined(_type!) ?? string.Empty;
                }

                return string.IsNullOrEmpty(fromType) || fromType == "NOTDEFINED" ? string.Empty : fromType!;
            }
        }

        /// <summary>PredefinedType gốc (ở phần tử, hoặc ở kiểu khi phần tử để NOTDEFINED) là <c>USERDEFINED</c>.</summary>
        public bool PredefinedTypeIsUserDefined
        {
            get
            {
                var own = Own(_entity);
                return own == "USERDEFINED"
                       || ((string.IsNullOrEmpty(own) || own == "NOTDEFINED") && _type != null && Own(_type) == "USERDEFINED");
            }
        }

        private string? Own(IfcEntity entity)
        {
            var value = _model.Value(entity, "PredefinedType");
            return value.Kind == IfcValueKind.Enumeration ? value.Raw : null;
        }

        /// <summary>
        /// Nhãn tự khai khi PredefinedType = USERDEFINED: <c>ObjectType</c> ở phần tử; ở kiểu thì <c>ElementType</c>
        /// (sản phẩm), <c>ProcessType</c> (công việc — IfcTaskType) hoặc <c>ResourceType</c> (tài nguyên). Lớp nào chỉ
        /// có một trong bốn tên đó, nên lấy cái có mặt.
        /// </summary>
        private string? UserDefined(IfcEntity entity) =>
            _model.Text(entity, "ObjectType") ?? _model.Text(entity, "ElementType")
            ?? _model.Text(entity, "ProcessType") ?? _model.Text(entity, "ResourceType");

        /// <summary>
        /// Thuộc tính trực tiếp theo tên (không phân biệt hoa thường — tiện tra cứu; bộ kiểm IDS dùng đường có kiểu và
        /// so đúng hoa thường). Boolean trả <c>true</c>/<c>false</c>; <c>.U.</c>, tham chiếu, danh sách trả <c>null</c>.
        /// </summary>
        public string? Attribute(string name)
        {
            var attributes = _model.Schema.AttributesOf(_entity.Type);
            for (var i = 0; i < attributes.Count; i++)
            {
                if (string.Equals(attributes[i], (name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var datum = IdsDatum.FromStep(_entity.At(i));
                    return datum == null || datum.Value.Kind == IdsDatumKind.Object ? null : datum.Value.Text;
                }
            }

            return null;
        }

        /// <summary>Property theo Pset — đã gộp thuộc tính thừa kế từ kiểu (xem <see cref="IfcModel.PropertiesOf"/>).</summary>
        public string? Property(string? propertySet, string name)
        {
            var key = string.IsNullOrWhiteSpace(propertySet) ? name : propertySet + "." + name;
            if (!_model.Model.TryProperty(_entity.Id, key, out var value))
            {
                return null;
            }

            // .T./.F. → true/false (dạng chuẩn XSD); .U. = không có giá trị.
            return value == "T" ? "true" : value == "F" ? "false" : value == "U" ? null : value;
        }

        /// <summary>Mã phân loại theo hệ (rỗng = mọi hệ).</summary>
        public IEnumerable<string> Classifications(string? system) =>
            _model.ClassificationPairs(_entity)
                .Where(p => p.Code != null && (string.IsNullOrWhiteSpace(system) || string.Equals(p.System, system, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Code!);

        /// <summary>Tên vật liệu, tên lớp/thành phần và Category của chúng.</summary>
        public IEnumerable<string> Materials => _model.MaterialsOf(_entity.Id);

        /// <summary>Tên lớp của tầng/toà nhà/tổ hợp/hệ chứa phần tử.</summary>
        public IEnumerable<(string? Relation, string Entity)> PartOf => _model.PartOfOf(_entity.Id);

        /// <summary>Như <see cref="PartOf"/>, kèm PredefinedType của từng tổ tiên (facet partOf khai predefinedType).</summary>
        public IEnumerable<(string? Relation, string Entity, string PredefinedType)> PartOfWithPredefinedType =>
            _model.PartOfWithPredefinedTypeOf(_entity.Id);

        bool IIdsTypedElement.InScopeOf(IdsFacet? first)
        {
            switch (first?.Kind)
            {
                case IdsFacetKind.Property:
                    return _model.IsA(_entity, "IFCOBJECTDEFINITION")
                           || (_model.Schema.Name != "IFC2X3" && (_model.IsA(_entity, "IFCMATERIALDEFINITION") || _model.IsA(_entity, "IFCPROFILEDEF")));
                case IdsFacetKind.Classification:
                case IdsFacetKind.Material:
                case null:
                    return _model.IsA(_entity, "IFCOBJECTDEFINITION");
                default:
                    return true;
            }
        }

        IdsMatch? IIdsTypedElement.Match(IdsFacet facet)
        {
            switch (facet.Kind)
            {
                case IdsFacetKind.Entity:
                    return MatchEntity(facet) ? IdsMatch.Match : IdsMatch.Mismatch;
                case IdsFacetKind.Attribute:
                    return MatchAttribute(facet);
                case IdsFacetKind.Property:
                    return _model.MatchProperty(_entity, _type, facet);
                case IdsFacetKind.Classification:
                    return MatchClassification(facet);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Lớp đúng tên (phân biệt hoa thường, không tính lớp con), rồi predefinedType. IFC2X3 thiếu nhiều lớp
        /// (IfcAirTerminal…) — bảng ánh xạ kiểu của buildingSMART: IFCFLOWTERMINAL có kiểu IFCAIRTERMINALTYPE được
        /// tính là IFCAIRTERMINAL, như IfcTester.
        /// </summary>
        private bool MatchEntity(IdsFacet facet)
        {
            var name = facet.Name.Simple;
            // simpleValue: so thẳng — đường này chạy cho MỌI thực thể của file (cả triệu điểm toạ độ), không qua Accepts.
            var matches = (name != null ? string.Equals(_entity.Type, name, StringComparison.Ordinal) : facet.Name.Accepts(_entity.Type))
                          || (_model.Schema.Name == "IFC2X3" && _type != null && name != null && !_model.Schema.Knows(name)
                              && string.Equals(_type.Type, name + "TYPE", StringComparison.OrdinalIgnoreCase));
            return matches
                   && (facet.Container == null || facet.Container.IsAny || facet.Container.Accepts(PredefinedType)
                       || (PredefinedTypeIsUserDefined && facet.Container.Accepts("USERDEFINED")));
        }

        /// <summary>
        /// Thuộc tính forward theo lược đồ, tên so đúng hoa thường (tên khai bằng restriction thì mọi thuộc tính khớp).
        /// Không có thuộc tính đó (tên sai, thuộc tính inverse) hoặc giá trị <c>$</c>/<c>*</c> → vắng. Chuỗi rỗng,
        /// danh sách rỗng → có mà sai ("an optional attribute fails if empty"). Giá trị so theo kiểu (<see cref="IdsDatum"/>).
        /// </summary>
        private IdsMatch MatchAttribute(IdsFacet facet)
        {
            var attributes = _model.Schema.AttributesOf(_entity.Type);
            var values = new List<IfcValue>();
            for (var i = 0; i < attributes.Count; i++)
            {
                if (facet.Name.Simple != null ? attributes[i] == facet.Name.Simple : facet.Name.Accepts(attributes[i]))
                {
                    values.Add(_entity.At(i));
                }
            }

            var data = values.Select(IdsDatum.FromStep).Where(d => d != null).ToList();
            if (data.Count == 0)
            {
                return IdsMatch.Absent;
            }

            var present = values.Where(v => !(v.Kind == IfcValueKind.List && v.Items.Count == 0))
                .Select(IdsDatum.FromStep).Where(d => d != null && !(d.Value.Kind == IdsDatumKind.Text && d.Value.Text.Length == 0))
                .Select(d => d!.Value).ToList();
            if (present.Count == 0)
            {
                return IdsMatch.Mismatch;
            }

            return present.All(d => facet.Value.Accepts(d)) ? IdsMatch.Match : IdsMatch.Mismatch;
        }

        /// <summary>Có tham chiếu phân loại nào thì "có mặt"; khớp khi MỘT tham chiếu thoả cả mã lẫn hệ.</summary>
        private IdsMatch MatchClassification(IdsFacet facet)
        {
            var pairs = _model.ClassificationPairs(_entity);
            if (pairs.Count == 0)
            {
                return IdsMatch.Absent;
            }

            return pairs.Any(p => (facet.Value.IsAny || (p.Code != null && facet.Value.Accepts(IdsDatum.OfText(p.Code))))
                                  && (facet.Container == null || facet.Container.IsAny || (p.System != null && facet.Container.Accepts(IdsDatum.OfText(p.System)))))
                ? IdsMatch.Match
                : IdsMatch.Mismatch;
        }
    }
}
