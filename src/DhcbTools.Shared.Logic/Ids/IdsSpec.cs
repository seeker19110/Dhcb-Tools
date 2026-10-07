using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Giá trị mà một facet IDS chấp nhận: một chuỗi cố định (<c>simpleValue</c>), hoặc một
    /// <c>xs:restriction</c> gồm nhiều ràng buộc ĐỒNG THỜI (<c>xs:enumeration</c>, <c>xs:pattern</c>,
    /// khoảng số, độ dài chuỗi) — XSD hiểu các ràng buộc trong cùng một restriction là HỘI, không phải
    /// "cái nào có trước thì dùng". Không ràng buộc gì = "có giá trị là được".
    /// </summary>
    public sealed class IdsValue
    {
        private Regex? _pattern;

        /// <summary>Chuỗi phải khớp đúng (phân biệt hoa thường; hai bên đều là số thì so số có dung sai).</summary>
        public string? Simple { get; set; }

        /// <summary>Danh sách giá trị cho phép.</summary>
        public List<string> Enumeration { get; } = new List<string>();

        /// <summary>Biểu thức chính quy theo XSD — neo hai đầu khi so.</summary>
        public string? Pattern { get; set; }

        /// <summary>Chặn dưới, lấy cả biên.</summary>
        public double? MinInclusive { get; set; }

        /// <summary>Chặn trên, lấy cả biên.</summary>
        public double? MaxInclusive { get; set; }

        /// <summary>Chặn dưới, không lấy biên.</summary>
        public double? MinExclusive { get; set; }

        /// <summary>Chặn trên, không lấy biên.</summary>
        public double? MaxExclusive { get; set; }

        /// <summary><c>xs:length</c>: độ dài chuỗi phải đúng bằng.</summary>
        public int? Length { get; set; }

        /// <summary><c>xs:minLength</c>.</summary>
        public int? MinLength { get; set; }

        /// <summary><c>xs:maxLength</c>.</summary>
        public int? MaxLength { get; set; }

        /// <summary>
        /// Dung sai khi so bằng hai số, đúng IDS 1.0: thực tế nằm trong
        /// <c>kỳ vọng ± (|kỳ vọng| × 1e-6 + 1e-6)</c>. Chỉ áp cho so bằng (<c>simpleValue</c>/<c>enumeration</c>);
        /// biên <c>min/maxInclusive/Exclusive</c> so chặt. Bộ ca <c>tolerance/</c> của buildingSMART đặt giá trị
        /// ngay trên biên này — dung sai cũ 1e-9 trượt cả 14 ca "pass".
        /// </summary>
        public const double NumericTolerance = 1e-6;

        /// <summary>Thời gian tối đa cho một lần khớp pattern — regex ác ý/vô tình (<c>(a+)+$</c>) không được treo Revit.</summary>
        public static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

        /// <summary>Không ràng buộc gì: chỉ cần thuộc tính/property tồn tại và khác rỗng.</summary>
        public bool IsAny =>
            string.IsNullOrEmpty(Simple) && Enumeration.Count == 0 && string.IsNullOrEmpty(Pattern)
            && MinInclusive == null && MaxInclusive == null && MinExclusive == null && MaxExclusive == null
            && Length == null && MinLength == null && MaxLength == null;

        /// <summary>
        /// Biên dịch <see cref="Pattern"/> ngay (thay vì lười lúc so): pattern XSD dùng cú pháp .NET không nhận
        /// (<c>\i</c>, <c>\c</c>, trừ lớp ký tự) phải lộ ra lúc ĐỌC file thành <see cref="IdsParseException"/>,
        /// không phải ném <c>ArgumentException</c> giữa vòng kiểm.
        /// </summary>
        public void CompilePattern()
        {
            if (string.IsNullOrEmpty(Pattern))
            {
                return;
            }

            try
            {
                // XSD pattern khớp TOÀN BỘ chuỗi, Regex .NET thì khớp một đoạn. Không neo hai đầu thì
                // "AB-01-rác" cũng đạt quy tắc "AB-\d\d" — quy tắc đặt tên mất hiệu lực mà vẫn xanh.
                _pattern = new Regex("^(?:" + TranslateXsd(Pattern!) + ")$", RegexOptions.CultureInvariant, PatternTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new IdsParseException("xs:pattern \"" + Pattern + "\" không phải biểu thức chính quy .NET đọc được: " + ex.Message);
            }
        }

        /// <summary>Ký tự đầu của tên XML (<c>\i</c> trong XSD): chữ cái, <c>_</c>, <c>:</c>.</summary>
        private const string XsdInitialNameChars = @"\p{L}_:";

        /// <summary>Ký tự tên XML (<c>\c</c> trong XSD): thêm chữ số, dấu kết hợp, <c>.</c> và <c>-</c>.</summary>
        private const string XsdNameChars = @"\p{L}\p{Mn}\p{Mc}\p{Nd}_:.\-";

        /// <summary>
        /// Dịch bốn lớp ký tự XSD mà .NET không có — <c>\i</c>, <c>\c</c> và hai phủ định <c>\I</c>, <c>\C</c>.
        /// Trước đây file IDS dùng chúng bị từ chối lúc đọc ("không phải biểu thức chính quy .NET"), dù là
        /// pattern XSD hợp lệ mà IfcTester chạy được. <c>\I</c>/<c>\C</c> nằm trong <c>[…]</c> không có dạng
        /// .NET tương đương → vẫn báo lỗi đọc file thay vì kiểm sai.
        /// </summary>
        internal static string TranslateXsd(string pattern)
        {
            var sb = new System.Text.StringBuilder(pattern.Length + 16);
            var inClass = false;
            for (var i = 0; i < pattern.Length; i++)
            {
                var ch = pattern[i];
                if (ch == '\\' && i + 1 < pattern.Length)
                {
                    var next = pattern[++i];
                    var set = next == 'i' || next == 'I' ? XsdInitialNameChars : next == 'c' || next == 'C' ? XsdNameChars : null;
                    if (set == null)
                    {
                        sb.Append(ch).Append(next);
                    }
                    else if (char.IsLower(next))
                    {
                        sb.Append(inClass ? set : "[" + set + "]");
                    }
                    else if (!inClass)
                    {
                        sb.Append("[^" + set + "]");
                    }
                    else
                    {
                        throw new ArgumentException("\\" + next + " bên trong [...] chưa hỗ trợ");
                    }

                    continue;
                }

                inClass = ch == '[' || (inClass && ch != ']');
                sb.Append(ch);
            }

            return sb.ToString();
        }

        /// <summary>Giá trị đọc được từ mô hình có thoả không — MỌI ràng buộc đã khai đều phải đúng.</summary>
        public bool Accepts(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            return value.Length > 0 && (IsAny || AcceptsText(value));
        }

        private bool AcceptsText(string value)
        {
            if (!string.IsNullOrEmpty(Simple) && !ValuesEqual(value, Simple!))
            {
                return false;
            }

            if (Enumeration.Count > 0 && !Enumeration.Any(e => ValuesEqual(value, e)))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(Pattern))
            {
                if (_pattern == null)
                {
                    CompilePattern();
                }

                try
                {
                    if (!_pattern!.IsMatch(value))
                    {
                        return false;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            if (!LengthOk(value))
            {
                return false;
            }

            if (MinInclusive != null || MaxInclusive != null || MinExclusive != null || MaxExclusive != null)
            {
                return TryNumber(value, out var number) && BoundsOk(number);
            }

            return true;
        }

        private bool LengthOk(string value) =>
            (Length == null || value.Length == Length.Value)
            && (MinLength == null || value.Length >= MinLength.Value)
            && (MaxLength == null || value.Length <= MaxLength.Value);

        private bool BoundsOk(double number) =>
            (MinInclusive == null || number >= MinInclusive)
            && (MaxInclusive == null || number <= MaxInclusive)
            && (MinExclusive == null || number > MinExclusive)
            && (MaxExclusive == null || number < MaxExclusive);

        /// <summary>
        /// Hai giá trị bằng nhau: cả hai là số thì so số theo <see cref="NumericTolerance"/> (<c>0.3</c> bằng
        /// <c>IFCREAL(0.29999999999999999)</c>, <c>3.</c> bằng <c>3.0</c>); còn lại so chuỗi <b>phân biệt hoa
        /// thường</b> như IDS 1.0 quy định (bộ ca <c>…_case_sensitively</c> của buildingSMART). Boolean không có
        /// luật riêng: phía mô hình ghi <c>true</c>/<c>false</c> chữ thường (dạng chuẩn XSD) nên IDS viết
        /// <c>TRUE</c> không khớp — đúng chuẩn: "booleans must be specified as lowercase strings".
        /// </summary>
        internal static bool ValuesEqual(string actual, string expected)
        {
            if (TryNumber(actual, out var a) && TryNumber(expected, out var b))
            {
                return ValuesEqual(a, b);
            }

            return string.Equals(actual, expected, StringComparison.Ordinal);
        }

        /// <summary>Hai số bằng nhau theo dung sai IDS 1.0 (<see cref="NumericTolerance"/>).</summary>
        internal static bool ValuesEqual(double actual, double expected)
        {
            // × (1 + 1e-9): bộ ca đặt giá trị ĐÚNG trên biên, mà trong double |1.000002 − 1| = 2.0000000003e-6
            // > 2e-6 — thiếu phần đệm này thì 8/14 ca "pass" của buildingSMART trượt vì sai số làm tròn.
            return Math.Abs(actual - expected) <= (Math.Abs(expected) * NumericTolerance + NumericTolerance) * (1 + 1e-9);
        }

        /// <summary>
        /// Giá trị CÓ KIỂU có thoả không — như <see cref="Accepts(string)"/> nhưng theo kiểu của giá trị: so bằng/liệt kê
        /// ép chuỗi IDS về kiểu đó (<see cref="IdsTypedValue.EqualsText"/>), pattern chỉ áp cho chuỗi (IDS: "patterns always
        /// fail on any number"), biên số chỉ áp cho số hoặc chuỗi đọc được thành số. Tham chiếu và tập hợp chỉ đạt khi không
        /// ràng buộc giá trị — IDS không so được chúng với chuỗi nào.
        /// </summary>
        public bool AcceptsTyped(IdsTypedValue value)
        {
            if (IsAny)
            {
                return true;
            }

            if (value.Kind == IdsTypedKind.String)
            {
                // Chuỗi: giữ nguyên luật cũ (đã khớp bộ ca), trừ việc không cắt khoảng trắng hai đầu.
                return value.Text.Length > 0 && AcceptsText(value.Text);
            }

            if (value.Kind == IdsTypedKind.Reference || value.Kind == IdsTypedKind.Aggregate || !string.IsNullOrEmpty(Pattern))
            {
                return false;
            }

            if ((!string.IsNullOrEmpty(Simple) && !value.EqualsText(Simple!))
                || (Enumeration.Count > 0 && !Enumeration.Any(value.EqualsText))
                || !LengthOk(value.Text))
            {
                return false;
            }

            return value.Kind == IdsTypedKind.Boolean
                ? MinInclusive == null && MaxInclusive == null && MinExclusive == null && MaxExclusive == null
                : BoundsOk(value.Number);
        }

        internal static bool TryNumber(string text, out double number)
        {
            // "3." (STEP real) hợp lệ với NumberStyles.Float; NaN/∞ không phải số đo.
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !double.IsNaN(number) && !double.IsInfinity(number))
            {
                return true;
            }

            number = 0;
            return false;
        }

        /// <summary>Câu mô tả ràng buộc, để báo cáo nói được "cần gì" chứ không chỉ "không đạt".</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Simple))
            {
                parts.Add("= \"" + Simple + "\"");
            }

            if (Enumeration.Count > 0)
            {
                parts.Add("thuộc {" + string.Join(", ", Enumeration) + "}");
            }

            if (!string.IsNullOrEmpty(Pattern))
            {
                parts.Add("khớp mẫu \"" + Pattern + "\"");
            }

            if (MinInclusive != null) { parts.Add("≥ " + Text(MinInclusive.Value)); }
            if (MinExclusive != null) { parts.Add("> " + Text(MinExclusive.Value)); }
            if (MaxInclusive != null) { parts.Add("≤ " + Text(MaxInclusive.Value)); }
            if (MaxExclusive != null) { parts.Add("< " + Text(MaxExclusive.Value)); }
            if (Length != null) { parts.Add("dài đúng " + Length.Value.ToString(CultureInfo.InvariantCulture) + " ký tự"); }
            if (MinLength != null) { parts.Add("dài ≥ " + MinLength.Value.ToString(CultureInfo.InvariantCulture)); }
            if (MaxLength != null) { parts.Add("dài ≤ " + MaxLength.Value.ToString(CultureInfo.InvariantCulture)); }
            return parts.Count > 0 ? string.Join(" và ", parts) : "có giá trị (khác rỗng)";
        }

        private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Loại facet IDS.</summary>
    public enum IdsFacetKind
    {
        /// <summary>Lớp IFC (IfcWall, IfcDuctSegment…), kèm predefinedType nếu khai.</summary>
        Entity,

        /// <summary>Thuộc tính của thực thể IFC (Name, Description, Tag…).</summary>
        Attribute,

        /// <summary>Property trong một property set.</summary>
        Property,

        /// <summary>Mã phân loại (Uniclass, mã hiệu công tác…).</summary>
        Classification,

        /// <summary>Vật liệu.</summary>
        Material,

        /// <summary>Thuộc về một thực thể khác (nhóm, hệ, tầng).</summary>
        PartOf,
    }

    /// <summary>
    /// Năm giá trị hợp lệ của thuộc tính <c>relation</c> trên facet <c>partOf</c>, chép nguyên văn từ
    /// <c>simpleType "relations"</c> trong <c>Schema/ids.xsd</c> 1.0 (buildingSMART/IDS) — kể cả
    /// <see cref="VoidsAndFills"/> có khoảng trắng bên trong đúng như enum của lược đồ gốc. Dùng chung ở
    /// ba chỗ: đọc file (kiểm giá trị hợp lệ), <c>IfcIdsElement</c> (đường IFC), <c>RevitIdsElement</c>
    /// (đường Revit) — một nguồn duy nhất, đổi tên quan hệ không phải sửa ba nơi.
    /// </summary>
    public static class IdsRelations
    {
        /// <summary>Tổ hợp lớn hơn gồm nhiều phần tử nhỏ hơn (nhiều tầng làm nên một toà nhà).</summary>
        public const string Aggregates = "IFCRELAGGREGATES";

        /// <summary>Nhóm/hệ tuỳ mục đích (thiết bị vào một hệ phân phối).</summary>
        public const string AssignsToGroup = "IFCRELASSIGNSTOGROUP";

        /// <summary>Vị trí chứa chính của phần tử (tầng, site).</summary>
        public const string ContainedInSpatialStructure = "IFCRELCONTAINEDINSPATIALSTRUCTURE";

        /// <summary>Gắn vật lý vào vật chủ lớn hơn (phụ kiện lồng trong khối lớn).</summary>
        public const string Nests = "IFCRELNESTS";

        /// <summary>Cặp quan hệ khoét lỗ/lấp lỗ — IDS gộp thành một loại (cửa/cửa sổ lấp opening của tường).</summary>
        public const string VoidsAndFills = "IFCRELVOIDSELEMENT IFCRELFILLSELEMENT";

        /// <summary>Tập hợp cả năm giá trị, so không phân biệt hoa/thường.</summary>
        public static readonly HashSet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Aggregates, AssignsToGroup, ContainedInSpatialStructure, Nests, VoidsAndFills,
        };
    }

    /// <summary>Một facet: một điều kiện áp lên phần tử.</summary>
    public sealed class IdsFacet
    {
        /// <summary>Loại facet.</summary>
        public IdsFacetKind Kind { get; set; }

        /// <summary>Entity: tên lớp IFC. Attribute: tên thuộc tính. Property: tên property.</summary>
        public IdsValue Name { get; set; } = new IdsValue();

        /// <summary>Property: tên property set. Entity/PartOf: predefinedType. Classification: hệ phân loại.</summary>
        public IdsValue? Container { get; set; }

        /// <summary>Giá trị phải thoả (không ràng buộc = chỉ cần tồn tại).</summary>
        public IdsValue Value { get; set; } = new IdsValue();

        /// <summary>Property: thuộc tính <c>dataType</c> (IFCLABEL, IFCREAL…) — ghi lại để báo cáo; so sánh số tự nhận theo giá trị.</summary>
        public string? DataType { get; set; }

        /// <summary>
        /// Chỉ có ở <see cref="IdsFacetKind.PartOf"/>: một trong năm giá trị của <see cref="IdsRelations"/>
        /// (thuộc tính <c>relation</c> của <c>partOf</c>), hoặc <c>null</c> khi IDS không khai — nghĩa là
        /// chấp nhận mọi cấu trúc quan hệ IFC hợp lệ, kể cả trộn nhiều loại (xem <c>partof-facet.md</c>
        /// của buildingSMART). Khác <c>null</c> ở mọi facet khác không có ý nghĩa gì.
        /// </summary>
        public string? Relation { get; set; }

        /// <summary>
        /// <c>required</c> (mặc định) | <c>optional</c> | <c>prohibited</c>. Bên phần yêu cầu,
        /// <c>prohibited</c> nghĩa là <b>có mới là sai</b> — đọc sót nó là đọc ngược quy tắc.
        /// </summary>
        public string Cardinality { get; set; } = "required";

        /// <summary>Facet cấm: phần tử thoả facet này là vi phạm.</summary>
        public bool IsProhibited => string.Equals(Cardinality, "prohibited", StringComparison.OrdinalIgnoreCase);

        /// <summary>Facet tuỳ chọn: thiếu cũng không sao.</summary>
        public bool IsOptional => string.Equals(Cardinality, "optional", StringComparison.OrdinalIgnoreCase);

        /// <summary>Câu mô tả facet cho báo cáo.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case IdsFacetKind.Entity:
                    return "lớp IFC " + Name.Describe()
                           + (Container != null ? ", predefinedType " + Container.Describe() : string.Empty);
                case IdsFacetKind.Attribute:
                    return "thuộc tính " + Name.Describe() + " " + Value.Describe();
                case IdsFacetKind.Property:
                    return "property " + (Container != null ? Container.Describe() + "." : string.Empty)
                           + Name.Describe() + " " + Value.Describe();
                case IdsFacetKind.Classification:
                    return "phân loại " + (Container != null ? Container.Describe() + ": " : string.Empty) + Value.Describe();
                case IdsFacetKind.Material:
                    return "vật liệu " + Value.Describe();
                default:
                    return "thuộc về " + (Relation != null ? "(qua " + Relation + ") " : string.Empty) + Value.Describe()
                           + (Container != null ? ", predefinedType " + Container.Describe() : string.Empty);
            }
        }
    }

    /// <summary>Một specification: "phần tử nào" (applicability) phải "thoả gì" (requirements).</summary>
    public sealed class IdsSpecification
    {
        /// <summary>Tên do người khai đặt — hiện nguyên văn trong báo cáo.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Mô tả kèm theo.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Lọc phần tử áp dụng. Rỗng = mọi phần tử.</summary>
        public List<IdsFacet> Applicability { get; } = new List<IdsFacet>();

        /// <summary>Điều kiện phần tử áp dụng phải thoả.</summary>
        public List<IdsFacet> Requirements { get; } = new List<IdsFacet>();

        /// <summary>
        /// Lược đồ IFC mà specification nhắm tới (<c>ifcVersion="IFC4 IFC4X3_ADD2"</c>). Rỗng = không khai
        /// (lint sẽ cảnh báo) → áp cho mọi file.
        /// </summary>
        public List<string> IfcVersions { get; } = new List<string>();

        /// <summary><c>minOccurs</c> ở mức specification (IDS 1.0); mặc định 1.</summary>
        public int MinOccurs { get; set; } = 1;

        /// <summary><c>maxOccurs</c> ở mức specification; <c>null</c> = <c>unbounded</c>.</summary>
        public int? MaxOccurs { get; set; }

        /// <summary>
        /// <c>minOccurs="0" maxOccurs="0"</c>: KHÔNG được có phần tử nào lọt applicability — mỗi phần tử lọt
        /// là một vi phạm (ví dụ "không có ống nước trên mái"). Đọc sót là đảo ngược kết luận.
        /// </summary>
        public bool IsProhibited => MaxOccurs == 0;

        /// <summary><c>minOccurs="0"</c> (không cấm): không có phần tử nào lọt cũng không sao.</summary>
        public bool IsOptional => MinOccurs == 0 && !IsProhibited;

        /// <summary>Specification có áp cho lược đồ này không (rỗng một trong hai bên = áp).</summary>
        public bool AppliesTo(string? modelSchema)
        {
            if (string.IsNullOrWhiteSpace(modelSchema) || IfcVersions.Count == 0)
            {
                return true;
            }

            return IfcVersions.Any(v => string.Equals(v, modelSchema!.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Bộ đọc file <b>IDS 1.0</b> (buildingSMART Information Delivery Specification — chuẩn chính thức từ
    /// 01/6/2024).
    /// <para>
    /// Vì sao đọc IDS thay vì một định dạng JSON tự nghĩ: chủ đầu tư hoặc tư vấn thẩm tra khai yêu cầu
    /// <b>một lần</b>, rồi DHCB, IfcTester hay Solibri kiểm đều phải ra <b>cùng kết luận</b> — đó chính là
    /// điều IDS được lập ra để bảo đảm. Một định dạng riêng thì mỗi phần mềm hiểu một kiểu, và tranh cãi
    /// giữa các bên quay về đúng chỗ cũ.
    /// </para>
    /// <para>
    /// Đọc bằng XLinq và <b>bỏ qua namespace</b> khi so tên thẻ: file IDS ngoài đời khai namespace nhiều
    /// kiểu (có/không prefix, bản nháp cũ), mà từ chối vì namespace thì kỹ sư chỉ thấy "file hỏng".
    /// </para>
    /// </summary>
    public static class IdsSpec
    {
        /// <summary>Đọc nội dung XML. Ném <see cref="IdsParseException"/> khi file không dùng được.</summary>
        public static IReadOnlyList<IdsSpecification> Parse(string xml)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml ?? string.Empty);
            }
            catch (Exception ex)
            {
                throw new IdsParseException("File IDS không phải XML đọc được: " + ex.Message);
            }

            // XDocument.Parse đã ném khi file không có thẻ gốc, nên tới đây Root luôn khác null —
            // không dựng nhánh "root rỗng" mà không ai chạy tới được.
            var root = document.Root!;
            if (!Local(root).Equals("ids", StringComparison.OrdinalIgnoreCase))
            {
                throw new IdsParseException("File IDS phải có thẻ gốc <ids>, đang thấy <" + Local(root) + ">.");
            }

            var specifications = Descendants(root, "specification").ToList();
            if (specifications.Count == 0)
            {
                throw new IdsParseException("File IDS không có <specification> nào — không có gì để kiểm.");
            }

            var result = new List<IdsSpecification>();
            foreach (var element in specifications)
            {
                var spec = new IdsSpecification
                {
                    Name = (string?)element.Attribute("name") ?? string.Empty,
                    Description = (string?)element.Attribute("description") ?? string.Empty,
                };

                foreach (var version in ((string?)element.Attribute("ifcVersion") ?? string.Empty).Split(' '))
                {
                    if (version.Trim().Length > 0)
                    {
                        spec.IfcVersions.Add(version.Trim().ToUpperInvariant());
                    }
                }

                // IDS 1.0 đặt minOccurs/maxOccurs trên <applicability>; bản nháp cũ đặt trên <specification>.
                // Trước đây chỉ đọc chỗ thứ hai: specification CẤM của file IDS 1.0 thật (applicability
                // maxOccurs="0") bị coi là bắt buộc — thiếu requirements là từ chối cả file, có requirements
                // thì kiểm ngược. Đọc applicability trước, không khai mới rơi về specification.
                var applicability = Child(element, "applicability");
                var occurs = applicability != null
                             && (applicability.Attribute("minOccurs") != null || applicability.Attribute("maxOccurs") != null)
                    ? applicability
                    : element;
                spec.MinOccurs = ReadOccurs(occurs, spec.Name, "minOccurs", 1) ?? 0;
                spec.MaxOccurs = ReadOccurs(occurs, spec.Name, "maxOccurs", null);

                foreach (var facet in ReadFacets(applicability))
                {
                    spec.Applicability.Add(facet);
                }

                foreach (var facet in ReadFacets(Child(element, "requirements")))
                {
                    spec.Requirements.Add(facet);
                }

                // Specification không có yêu cầu nào thì luôn đạt. Nhận nó là in ra một dòng "✓" cho một
                // điều kiện chưa ai viết — đúng loại no-op im lặng mà E-PRECOND sinh ra để chặn.
                // Ngoại lệ đúng chuẩn, vì khi đó applicability CHÍNH LÀ điều kiện: specification CẤM (maxOccurs="0"
                // — "không được có X"), và specification BẮT BUỘC có lọc applicability ("mô hình phải có ít nhất một
                // X" — bộ ca buildingSMART "there must be an airterminal…"). Chỉ còn từ chối khi không có gì để kiểm:
                // tuỳ chọn, hoặc applicability rỗng (mọi mô hình có phần tử đều đạt).
                if (spec.Requirements.Count == 0 && !spec.IsProhibited && (spec.MinOccurs == 0 || spec.Applicability.Count == 0))
                {
                    throw new IdsParseException(
                        "Specification \"" + spec.Name + "\" không có <requirements> nào — nó sẽ luôn đạt, tức là không kiểm gì cả.");
                }

                // Cấm mà vẫn khai yêu cầu: IDS 1.0 coi là file sai ("prohibited specifications invalid if
                // requirements are specified") — đọc tiếp thì yêu cầu bị lờ đi mà tác giả không biết.
                if (spec.Requirements.Count > 0 && spec.IsProhibited)
                {
                    throw new IdsParseException(
                        "Specification \"" + spec.Name + "\" là CẤM (maxOccurs=\"0\") nhưng vẫn có <requirements> — bỏ requirements, hoặc bỏ maxOccurs=\"0\".");
                }

                result.Add(spec);
            }

            return result;
        }

        /// <summary><c>minOccurs</c>/<c>maxOccurs</c>: số nguyên ≥ 0 hoặc <c>unbounded</c> (= null). Thiếu = mặc định.</summary>
        private static int? ReadOccurs(XElement element, string specification, string attribute, int? missing)
        {
            var text = ((string?)element.Attribute(attribute))?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return missing;
            }

            if (text!.Equals("unbounded", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                return value;
            }

            throw new IdsParseException("Specification \"" + specification + "\": " + attribute + "=\"" + text + "\" phải là số nguyên ≥ 0 hoặc \"unbounded\".");
        }

        private static IEnumerable<IdsFacet> ReadFacets(XElement? parent)
        {
            if (parent == null)
            {
                yield break;
            }

            foreach (var element in parent.Elements())
            {
                switch (Local(element).ToLowerInvariant())
                {
                    case "entity":
                        var entity = Facet(element, IdsFacetKind.Entity, "name", "predefinedType", null);
                        // Tên lớp IFC không có nghĩa khác theo hoa thường: nhận cả "IfcWall" (file viết tay, lint
                        // cảnh báo lệch chuẩn) bằng cách nâng lên chữ hoa — phía mô hình cũng được nâng lên trước khi so.
                        entity.Name.Simple = entity.Name.Simple?.ToUpperInvariant();
                        for (var i = 0; i < entity.Name.Enumeration.Count; i++)
                        {
                            entity.Name.Enumeration[i] = entity.Name.Enumeration[i].ToUpperInvariant();
                        }

                        yield return entity;
                        break;
                    case "attribute":
                        yield return Facet(element, IdsFacetKind.Attribute, "name", null, "value");
                        break;
                    case "property":
                        var property = Facet(element, IdsFacetKind.Property, "baseName", "propertySet", "value");
                        property.DataType = ((string?)element.Attribute("dataType"))?.Trim();
                        yield return property;
                        break;
                    case "classification":
                        yield return Facet(element, IdsFacetKind.Classification, "value", "system", "value");
                        break;
                    case "material":
                        yield return Facet(element, IdsFacetKind.Material, "value", null, "value");
                        break;
                    case "partof":
                        var partOf = PartOfFacet(element);
                        var relation = ((string?)element.Attribute("relation"))?.Trim();
                        if (!string.IsNullOrEmpty(relation))
                        {
                            // Sai một chữ trong "relation" (viết thiếu, sai chính tả) mà nhận thì facet lặng
                            // lẽ rơi về "chuỗi trộn" — kiểm lỏng hơn điều IDS author thật sự đòi, và báo cáo
                            // không có cách nào biết. Từ chối rõ, giống mọi facet/ràng buộc lạ khác ở đây.
                            if (!IdsRelations.All.Contains(relation!))
                            {
                                throw new IdsParseException(
                                    "Facet \"partOf\" khai relation=\"" + relation + "\" không hợp lệ. Hợp lệ: "
                                    + string.Join(", ", new[] { IdsRelations.Aggregates, IdsRelations.AssignsToGroup, IdsRelations.ContainedInSpatialStructure, IdsRelations.Nests, IdsRelations.VoidsAndFills }) + ".");
                            }

                            partOf.Relation = relation!.ToUpperInvariant();
                        }

                        yield return partOf;
                        break;
                    default:
                        // Facet lạ (bản IDS mới hơn) — bỏ qua im lặng thì bộ kiểm báo "đạt" cho một điều
                        // kiện nó không hề kiểm, và người đọc báo cáo không có cách nào biết.
                        throw new IdsParseException("Facet \"" + Local(element) + "\" chưa hỗ trợ.");
                }
            }
        }

        /// <summary>
        /// <c>&lt;partOf&gt;&lt;entity&gt;&lt;name&gt;…&lt;/name&gt;&lt;predefinedType&gt;…&lt;/predefinedType&gt;&lt;/entity&gt;&lt;/partOf&gt;</c>:
        /// tên lớp của tổ tiên vào <see cref="IdsFacet.Value"/>, predefinedType vào <see cref="IdsFacet.Container"/>.
        /// Bản cũ đọc cả khối <c>&lt;entity&gt;</c> như một chuỗi: có predefinedType thì ra "IFCINVENTORYBUNNY" và
        /// không bao giờ khớp; tên khai bằng restriction thì bị gộp chữ. Khối không có <c>&lt;name&gt;</c>
        /// (bản nháp cũ ghi thẳng giá trị trong <c>&lt;entity&gt;</c>) vẫn đọc như trước.
        /// </summary>
        private static IdsFacet PartOfFacet(XElement element)
        {
            var entity = Child(element, "entity");
            var name = entity != null ? Child(entity, "name") : null;
            var value = name != null ? ReadValue(name) : ReadValue(entity);
            return new IdsFacet
            {
                Kind = IdsFacetKind.PartOf,
                Name = value ?? new IdsValue(),
                Value = value ?? new IdsValue(),
                Container = name != null ? ReadValue(Child(entity!, "predefinedType")) : null,
                Cardinality = (string?)element.Attribute("cardinality") ?? "required",
            };
        }

        private static IdsFacet Facet(XElement element, IdsFacetKind kind, string nameChild, string? containerChild, string? valueChild)
        {
            var facet = new IdsFacet
            {
                Kind = kind,
                Name = ReadValue(Child(element, nameChild)) ?? new IdsValue(),
                Cardinality = (string?)element.Attribute("cardinality") ?? "required",
            };

            if (containerChild != null)
            {
                facet.Container = ReadValue(Child(element, containerChild));
            }

            if (valueChild != null)
            {
                facet.Value = ReadValue(Child(element, valueChild)) ?? new IdsValue();
            }

            return facet;
        }

        private static IdsValue? ReadValue(XElement? element)
        {
            if (element == null)
            {
                return null;
            }

            var value = new IdsValue();
            var simple = Child(element, "simpleValue");
            if (simple != null)
            {
                var text = simple.Value.Trim();
                if (text.Length == 0)
                {
                    // <simpleValue/> rỗng: nếu coi là "không ràng buộc" thì facet "phải bằng X" thành "chỉ cần có
                    // giá trị" — lỏng hơn điều tác giả IDS viết mà không ai biết.
                    throw new IdsParseException("<" + Local(element) + "> có <simpleValue> rỗng — ghi giá trị cần so, hoặc bỏ hẳn thẻ để chỉ đòi \"có giá trị\".");
                }

                value.Simple = text;
                return value;
            }

            var restriction = Child(element, "restriction");
            if (restriction == null)
            {
                var text = element.Value.Trim();
                if (text.Length > 0)
                {
                    value.Simple = text;
                }

                return value;
            }

            foreach (var facet in restriction.Elements())
            {
                var text = ((string?)facet.Attribute("value") ?? facet.Value).Trim();
                switch (Local(facet).ToLowerInvariant())
                {
                    case "enumeration":
                        value.Enumeration.Add(text);
                        break;
                    case "pattern":
                        // Nhiều xs:pattern trong cùng một restriction là phép HOẶC (XSD), không phải "cái sau
                        // đè cái trước" — bản cũ chỉ giữ pattern cuối nên "[A-Z]{2}\d{2} hoặc [a-z]{2}\d{2}"
                        // trượt oan mọi tên viết hoa.
                        value.Pattern = value.Pattern == null ? text : "(?:" + value.Pattern + ")|(?:" + text + ")";
                        break;
                    case "mininclusive":
                        value.MinInclusive = Number(text);
                        break;
                    case "maxinclusive":
                        value.MaxInclusive = Number(text);
                        break;
                    case "minexclusive":
                        value.MinExclusive = Number(text);
                        break;
                    case "maxexclusive":
                        value.MaxExclusive = Number(text);
                        break;
                    case "length":
                        value.Length = Count(facet, text);
                        break;
                    case "minlength":
                        value.MinLength = Count(facet, text);
                        break;
                    case "maxlength":
                        value.MaxLength = Count(facet, text);
                        break;
                    default:
                        // Cùng lý do với facet lạ: nhận file rồi lờ một ràng buộc đi là nói dối về kết quả.
                        throw new IdsParseException("Ràng buộc \"" + Local(facet) + "\" chưa hỗ trợ.");
                }
            }

            value.CompilePattern();
            return value;
        }

        private static double? Number(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (double?)null;

        private static int Count(XElement facet, string text)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                return value;
            }

            throw new IdsParseException("xs:" + Local(facet) + " value=\"" + text + "\" phải là số nguyên ≥ 0.");
        }

        private static string Local(XElement element) => element.Name.LocalName;

        private static XElement? Child(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(e => Local(e).Equals(name, StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<XElement> Descendants(XElement root, string name) =>
            root.Descendants().Where(e => Local(e).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File IDS không dùng được — thông báo nói rõ chỗ hỏng.</summary>
    public sealed class IdsParseException : Exception
    {
        /// <summary>Khởi tạo với câu nói rõ chỗ hỏng.</summary>
        public IdsParseException(string message) : base(message)
        {
        }
    }
}
