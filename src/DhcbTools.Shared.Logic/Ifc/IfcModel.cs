using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Ifc
{
    /// <summary>
    /// Lớp tra cứu trên kết quả đọc thô: đánh chỉ mục theo số hiệu và theo tên kiểu, rồi dựng sẵn hai
    /// quan hệ mà mọi quy tắc kiểm đều cần — bộ thuộc tính (property set) và phân loại (classification).
    /// <para>
    /// Hai quan hệ này trong IFC là quan hệ ngược: không đi từ bức tường ra thuộc tính, mà từ một thực
    /// thể <c>IFCRELDEFINESBYPROPERTIES</c> trỏ ngược về danh sách bức tường. Duyệt một lần dựng bảng
    /// tra là O(n); tra từng phần tử bằng cách quét lại toàn bộ quan hệ là O(n·m) — đúng cái bẫy đã sập
    /// một lần ở SleeveCommand.
    /// </para>
    /// </summary>
    public sealed class IfcModel
    {
        private readonly Dictionary<int, IfcEntity> _byId;
        private readonly Dictionary<string, List<IfcEntity>> _byType;
        private readonly Dictionary<int, Dictionary<string, string?>> _properties;
        private readonly Dictionary<int, List<string>> _classifications;

        private IfcModel(IfcStepFile file)
        {
            File = file;
            _byId = new Dictionary<int, IfcEntity>();
            var duplicates = new List<int>();
            foreach (var e in file.Data)
            {
                if (e.Id == 0)
                {
                    continue;
                }

                if (_byId.ContainsKey(e.Id))
                {
                    duplicates.Add(e.Id);
                    continue;
                }

                _byId[e.Id] = e;
            }

            DuplicateIds = duplicates;

            _byType = new Dictionary<string, List<IfcEntity>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _byId.Values)
            {
                if (!_byType.TryGetValue(e.Type, out var list))
                {
                    list = new List<IfcEntity>();
                    _byType[e.Type] = list;
                }

                list.Add(e);
            }

            foreach (var list in _byType.Values)
            {
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
            }

            Schema = ReadSchema(file);
            _properties = new Dictionary<int, Dictionary<string, string?>>();
            _classifications = new Dictionary<int, List<string>>();
            BuildProperties();
            BuildClassifications();
        }

        /// <summary>Đọc một file IFC dạng văn bản thành mô hình tra cứu được.</summary>
        public static IfcModel Parse(string text) => new IfcModel(IfcStepParser.Parse(text));

        /// <summary>Kết quả đọc thô, khi cần đi xuống tận tham số.</summary>
        public IfcStepFile File { get; }

        /// <summary>Tên lược đồ khai trong <c>FILE_SCHEMA</c> (<c>IFC4</c>, <c>IFC2X3</c>…), rỗng nếu không khai.</summary>
        public string Schema { get; }

        /// <summary>Các số hiệu bị khai hai lần — file hỏng, bản khai sau bị bỏ.</summary>
        public IReadOnlyList<int> DuplicateIds { get; }

        /// <summary>Tổng số thực thể có số hiệu.</summary>
        public int Count => _byId.Count;

        /// <summary>Thực thể theo số hiệu, hoặc <c>null</c>.</summary>
        public IfcEntity? ById(int id) => _byId.TryGetValue(id, out var e) ? e : null;

        /// <summary>Mọi thực thể mang đúng tên kiểu này (không phân biệt hoa thường, KHÔNG gồm lớp con).</summary>
        public IReadOnlyList<IfcEntity> OfType(string type) =>
            _byType.TryGetValue(type ?? string.Empty, out var list) ? list : (IReadOnlyList<IfcEntity>)Array.Empty<IfcEntity>();

        /// <summary>Mọi tên kiểu có mặt trong file, kèm số lượng — dùng để in bảng đối chiếu.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> TypeCounts() =>
            _byType.Select(p => new KeyValuePair<string, int>(p.Key, p.Value.Count))
                   .OrderByDescending(p => p.Value)
                   .ThenBy(p => p.Key, StringComparer.Ordinal)
                   .ToList();

        /// <summary>
        /// Chuỗi ở tham số 0 — vị trí của mã định danh toàn cục ở mọi lớp con <c>IfcRoot</c> — hoặc
        /// <c>null</c> khi tham số đó không phải chuỗi.
        /// <para>
        /// <b>Không phải cứ có chuỗi ở đây là mã định danh:</b> <c>IFCPROPERTYSINGLEVALUE('IsExternal',…)</c>
        /// cũng mở đầu bằng một chuỗi. Coi mọi chuỗi ở vị trí 0 là mã định danh thì hai thuộc tính cùng
        /// tên ở hai Pset khác nhau bị báo là "trùng mã" — báo sai kiểu này làm kỹ sư tắt bộ kiểm đi.
        /// Dùng kèm <see cref="LooksLikeGlobalId"/>.
        /// </para>
        /// </summary>
        public static string? GlobalIdOf(IfcEntity entity) =>
            entity.At(0).Kind == IfcValueKind.Text ? entity.At(0).Raw : null;

        /// <summary>
        /// Đúng dạng mã định danh IFC: 22 ký tự trong bảng base64 riêng của IFC (<c>0-9 A-Z a-z _ $</c>),
        /// và ký tự ĐẦU chỉ là <c>0</c>–<c>3</c> — dạng nén 128 bit của một GUID.
        /// <para>
        /// Ràng buộc ký tự đầu không phải chi tiết vụn: 22 ký tự × 6 bit = 132 bit, nên ký tự đầu chỉ
        /// chở được 2 bit. Thiếu nó thì một tên thuộc tính dài đúng 22 ký tự cũng lọt — file IFC thật
        /// do Revit xuất (925.815 thực thể) có <c>TreadLengthAtInnerSide</c>, đúng 22 chữ cái, và bộ
        /// kiểm báo nhầm 106 "mã định danh trùng nhau" trước khi thêm ràng buộc này.
        /// </para>
        /// </summary>
        public static bool LooksLikeGlobalId(string? value)
        {
            if (value is null || value.Length != 22)
            {
                return false;
            }

            if (value[0] < '0' || value[0] > '3')
            {
                return false;
            }

            foreach (var c in value)
            {
                var ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == '$';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Tên (tham số 2 của <c>IfcRoot</c>), hoặc <c>null</c>.</summary>
        public static string? NameOf(IfcEntity entity) =>
            entity.At(2).Kind == IfcValueKind.Text ? entity.At(2).Raw : null;

        /// <summary>
        /// Bảng thuộc tính của một phần tử: khoá <c>Pset.Thuoctinh</c>, giá trị đã đưa về chuỗi
        /// (<c>null</c> khi thuộc tính có mặt nhưng bỏ trống). Đã gộp cả thuộc tính gán trực tiếp lẫn
        /// thuộc tính thừa kế từ kiểu qua <c>IFCRELDEFINESBYTYPE</c>; thuộc tính gán trực tiếp thắng.
        /// </summary>
        public IReadOnlyDictionary<string, string?> PropertiesOf(int id) =>
            _properties.TryGetValue(id, out var map)
                ? map
                : (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>();

        /// <summary>Các mã phân loại gán cho phần tử (<c>Identification</c> của <c>IfcClassificationReference</c>).</summary>
        public IReadOnlyList<string> ClassificationsOf(int id) =>
            _classifications.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>
        /// Tìm giá trị thuộc tính theo khoá <c>Pset.Ten</c> hoặc chỉ <c>Ten</c> (khi không nói Pset thì
        /// khớp thuộc tính cùng tên ở bất kỳ Pset nào). Trả về <c>false</c> nếu thuộc tính không có mặt.
        /// </summary>
        public bool TryProperty(int id, string key, out string? value)
        {
            value = null;
            if (!_properties.TryGetValue(id, out var map))
            {
                return false;
            }

            if (map.TryGetValue(key, out value))
            {
                return true;
            }

            if (key.IndexOf('.') >= 0)
            {
                return false;
            }

            foreach (var pair in map)
            {
                var dot = pair.Key.LastIndexOf('.');
                if (dot >= 0 && string.Equals(pair.Key.Substring(dot + 1), key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static string ReadSchema(IfcStepFile file)
        {
            foreach (var h in file.Header)
            {
                if (!h.Type.Equals("FILE_SCHEMA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var list = h.At(0);
                if (list.Kind == IfcValueKind.List && list.Items.Count > 0)
                {
                    return list.Items[0].AsText() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Dựng bảng thuộc tính. Hai chặng: gán trực tiếp cho phần tử, rồi thừa kế từ kiểu —
        /// chặng sau chỉ điền khoá còn thiếu, vì giá trị đặt trên phần tử luôn thắng giá trị của kiểu.
        /// </summary>
        private void BuildProperties()
        {
            var byDefinition = new Dictionary<int, Dictionary<string, string?>>();

            Dictionary<string, string?> PropertiesOfSet(int setId)
            {
                if (byDefinition.TryGetValue(setId, out var cached))
                {
                    return cached;
                }

                var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                byDefinition[setId] = map; // đặt trước để quan hệ vòng không gây đệ quy vô hạn
                var set = ById(setId);
                if (set is null)
                {
                    return map;
                }

                if (set.Type.Equals("IFCPROPERTYSET", StringComparison.OrdinalIgnoreCase))
                {
                    var setName = NameOf(set) ?? string.Empty;
                    foreach (var pid in References(set.At(4)))
                    {
                        var prop = ById(pid);
                        if (prop is null)
                        {
                            continue;
                        }

                        var propName = prop.At(0).Kind == IfcValueKind.Text ? prop.At(0).Raw : null;
                        if (string.IsNullOrEmpty(propName))
                        {
                            continue;
                        }

                        // IfcPropertySingleValue: (Name, Description, NominalValue, Unit)
                        map[setName + "." + propName] = prop.At(2).AsText();
                    }
                }
                else if (set.Type.Equals("IFCELEMENTQUANTITY", StringComparison.OrdinalIgnoreCase))
                {
                    var setName = NameOf(set) ?? string.Empty;
                    foreach (var qid in References(set.At(5)))
                    {
                        var q = ById(qid);
                        if (q is null)
                        {
                            continue;
                        }

                        var qName = q.At(0).Kind == IfcValueKind.Text ? q.At(0).Raw : null;
                        if (string.IsNullOrEmpty(qName))
                        {
                            continue;
                        }

                        // IfcQuantityLength/Area/Volume/Count: (Name, Description, Unit, Value, Formula)
                        map[setName + "." + qName] = q.At(3).AsText();
                    }
                }

                return map;
            }

            void Merge(int elementId, Dictionary<string, string?> from, bool overwrite)
            {
                if (from.Count == 0)
                {
                    return;
                }

                if (!_properties.TryGetValue(elementId, out var target))
                {
                    target = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    _properties[elementId] = target;
                }

                foreach (var pair in from)
                {
                    if (overwrite || !target.ContainsKey(pair.Key))
                    {
                        target[pair.Key] = pair.Value;
                    }
                }
            }

            // IfcRelDefinesByProperties: (GlobalId, Owner, Name, Desc, RelatedObjects, RelatingPropertyDefinition)
            foreach (var rel in OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                var defId = rel.At(5).AsReference();
                if (defId is null)
                {
                    continue;
                }

                var map = PropertiesOfSet(defId.Value);
                foreach (var target in References(rel.At(4)))
                {
                    Merge(target, map, overwrite: true);
                }
            }

            // IfcRelDefinesByType: (GlobalId, Owner, Name, Desc, RelatedObjects, RelatingType)
            foreach (var rel in OfType("IFCRELDEFINESBYTYPE"))
            {
                var typeId = rel.At(5).AsReference();
                if (typeId is null)
                {
                    continue;
                }

                var typeEntity = ById(typeId.Value);
                if (typeEntity is null)
                {
                    continue;
                }

                // IfcTypeObject: (GlobalId, Owner, Name, Desc, ApplicableOccurrence, HasPropertySets)
                var inherited = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var setId in References(typeEntity.At(5)))
                {
                    foreach (var pair in PropertiesOfSet(setId))
                    {
                        inherited[pair.Key] = pair.Value;
                    }
                }

                foreach (var target in References(rel.At(4)))
                {
                    Merge(target, inherited, overwrite: false);
                }
            }
        }

        private void BuildClassifications()
        {
            // IfcRelAssociatesClassification: (GlobalId, Owner, Name, Desc, RelatedObjects, RelatingClassification)
            foreach (var rel in OfType("IFCRELASSOCIATESCLASSIFICATION"))
            {
                var refId = rel.At(5).AsReference();
                if (refId is null)
                {
                    continue;
                }

                var reference = ById(refId.Value);
                if (reference is null)
                {
                    continue;
                }

                // IfcClassificationReference: (Location, Identification, Name, ReferencedSource, …).
                // IFC2X3 gọi tham số 1 là ItemReference; cùng vị trí nên đọc chung được.
                var code = reference.At(1).AsText() ?? reference.At(2).AsText();
                if (string.IsNullOrEmpty(code))
                {
                    continue;
                }

                foreach (var target in References(rel.At(4)))
                {
                    if (!_classifications.TryGetValue(target, out var list))
                    {
                        list = new List<string>();
                        _classifications[target] = list;
                    }

                    if (!list.Contains(code!))
                    {
                        list.Add(code!);
                    }
                }
            }
        }

        /// <summary>Số hiệu của mọi tham chiếu trong một giá trị, dù giá trị là một tham chiếu đơn hay một danh sách.</summary>
        private static IEnumerable<int> References(IfcValue value)
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

        /// <summary>
        /// Mọi tham chiếu trỏ tới số hiệu KHÔNG có trong file — dấu hiệu file bị cắt ngang hoặc bộ xuất
        /// ghi thiếu. Trả về cặp (thực thể chứa tham chiếu, số hiệu bị thiếu), đã bỏ trùng.
        /// </summary>
        public IReadOnlyList<KeyValuePair<IfcEntity, int>> DanglingReferences()
        {
            var found = new List<KeyValuePair<IfcEntity, int>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in File.Data)
            {
                foreach (var missing in MissingIn(entity.Attributes))
                {
                    if (seen.Add(entity.Id + ":" + missing))
                    {
                        found.Add(new KeyValuePair<IfcEntity, int>(entity, missing));
                    }
                }
            }

            return found;
        }

        private IEnumerable<int> MissingIn(IReadOnlyList<IfcValue> values)
        {
            foreach (var v in values)
            {
                if (v.Kind == IfcValueKind.Reference && !_byId.ContainsKey(v.Reference))
                {
                    yield return v.Reference;
                }
                else if (v.Kind == IfcValueKind.List || v.Kind == IfcValueKind.Typed)
                {
                    foreach (var inner in MissingIn(v.Items))
                    {
                        yield return inner;
                    }
                }
            }
        }
    }
}
