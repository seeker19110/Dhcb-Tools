using System;
using System.Collections.Generic;
using System.Globalization;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>Loại của một giá trị đọc từ mô hình, theo lược đồ IFC.</summary>
    public enum IdsTypedKind
    {
        /// <summary>Chuỗi — gồm cả enum, ngày giờ, khoảng thời gian (IDS so như chuỗi).</summary>
        String,

        /// <summary>Số thực — so với dung sai IDS.</summary>
        Real,

        /// <summary>Số nguyên — IDS phải viết số nguyên (<c>42.0</c> không khớp).</summary>
        Integer,

        /// <summary>Boolean — IDS viết <c>true</c>/<c>false</c> chữ thường.</summary>
        Boolean,

        /// <summary>Trỏ tới một thực thể: "có giá trị", nhưng không so được với chuỗi nào.</summary>
        Reference,

        /// <summary>Tập hợp: "có giá trị", nhưng không so được với chuỗi nào.</summary>
        Aggregate,
    }

    /// <summary>
    /// Một giá trị có kiểu. IDS 1.0 so theo kiểu của giá trị trong mô hình chứ không theo chữ: <c>42</c> khớp
    /// số thực <c>42.</c>, pattern không bao giờ khớp một số, số nguyên không nhận <c>42.0</c>, boolean chỉ nhận
    /// <c>true</c>/<c>false</c>. Trước đây mọi thứ thành chuỗi rồi mới so, nên các luật đó không phân biệt được.
    /// </summary>
    public sealed class IdsTypedValue
    {
        private IdsTypedValue(IdsTypedKind kind, string text, double number)
        {
            Kind = kind;
            Text = text;
            Number = number;
        }

        /// <summary>Loại giá trị.</summary>
        public IdsTypedKind Kind { get; }

        /// <summary>Dạng chữ (để báo cáo và cho ràng buộc độ dài).</summary>
        public string Text { get; }

        /// <summary>Giá trị số (số thực, số nguyên; boolean là 1/0).</summary>
        public double Number { get; }

        /// <summary>Chuỗi.</summary>
        public static IdsTypedValue String(string text) => new IdsTypedValue(IdsTypedKind.String, text ?? string.Empty, 0);

        /// <summary>Số thực.</summary>
        public static IdsTypedValue Real(double number) =>
            new IdsTypedValue(IdsTypedKind.Real, number.ToString("R", CultureInfo.InvariantCulture), number);

        /// <summary>Số nguyên.</summary>
        public static IdsTypedValue Integer(long number) =>
            new IdsTypedValue(IdsTypedKind.Integer, number.ToString(CultureInfo.InvariantCulture), number);

        /// <summary>Boolean.</summary>
        public static IdsTypedValue Boolean(bool value) => new IdsTypedValue(IdsTypedKind.Boolean, value ? "true" : "false", value ? 1 : 0);

        /// <summary>Tham chiếu tới thực thể <c>#id</c>.</summary>
        public static IdsTypedValue Reference(int id) => new IdsTypedValue(IdsTypedKind.Reference, "#" + id.ToString(CultureInfo.InvariantCulture), 0);

        /// <summary>Tập hợp khác rỗng.</summary>
        public static IdsTypedValue Aggregate(int count) =>
            new IdsTypedValue(IdsTypedKind.Aggregate, "(" + count.ToString(CultureInfo.InvariantCulture) + " mục)", 0);

        /// <summary>Đổi đơn vị: giá trị số nhân hệ số (đưa về đơn vị SI mà IDS dùng); loại khác giữ nguyên.</summary>
        public IdsTypedValue Scale(double factor, double offset)
        {
            return Kind == IdsTypedKind.Real || Kind == IdsTypedKind.Integer ? Real(Number * factor + offset) : this;
        }

        /// <inheritdoc />
        public override string ToString() => Text;

        /// <summary>
        /// So với một chuỗi khai trong IDS (<c>simpleValue</c> hoặc một <c>enumeration</c>), ép chuỗi đó về kiểu của
        /// giá trị — đúng cách IfcTester <c>cast_to_value</c> làm, trừ boolean: chỉ <c>true</c>/<c>false</c>/<c>1</c>/<c>0</c>
        /// (IfcTester nhận cả "TRUE" vì <c>bool("TRUE")</c>, trái với luật chữ thường của chính bộ ca).
        /// </summary>
        public bool EqualsText(string expected)
        {
            switch (Kind)
            {
                case IdsTypedKind.String:
                    return string.Equals(Text, expected, StringComparison.Ordinal);
                case IdsTypedKind.Real:
                    return IdsValue.TryNumber(expected, out var real) && IdsValue.ValuesEqual(Number, real);
                case IdsTypedKind.Integer:
                    return long.TryParse(expected, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer) && integer == Number;
                case IdsTypedKind.Boolean:
                    return (Number == 1 ? expected == "true" || expected == "1" : expected == "false" || expected == "0");
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Một thuộc tính khớp tên của facet <c>attribute</c>. <see cref="Value"/> <c>null</c>: thuộc tính để trống —
    /// <see cref="IsNull"/> khi STEP ghi <c>$</c> (hoặc dẫn xuất <c>*</c>), ngược lại là rỗng mà vẫn "có ghi"
    /// (<c>''</c>, tập hợp rỗng, logical <c>.U.</c>). Phân biệt vì IDS: thuộc tính tuỳ chọn để <c>$</c> thì đạt,
    /// ghi <c>''</c> thì trượt.
    /// </summary>
    public sealed class IdsAttributeValue
    {
        /// <summary>Khởi tạo.</summary>
        public IdsAttributeValue(string name, IdsTypedValue? value, bool isNull)
        {
            Name = name;
            Value = value;
            IsNull = value == null && isNull;
        }

        /// <summary>Tên thuộc tính.</summary>
        public string Name { get; }

        /// <summary>Giá trị; <c>null</c> khi trống.</summary>
        public IdsTypedValue? Value { get; }

        /// <summary>Trống kiểu <c>$</c> — coi như không khai.</summary>
        public bool IsNull { get; }
    }

    /// <summary>
    /// Một property khớp facet <c>property</c>: mọi giá trị khác rỗng của nó (property danh sách/liệt kê/khoảng/bảng
    /// có nhiều giá trị — chỉ cần MỘT cái khớp), mỗi giá trị kèm kiểu dữ liệu IFC (<c>IFCLABEL</c>,
    /// <c>IFCLENGTHMEASURE</c>…; <c>null</c> = không xác định, không ràng), đã đổi về đơn vị SI.
    /// </summary>
    public sealed class IdsPropertyValue
    {
        /// <summary>Khởi tạo.</summary>
        public IdsPropertyValue(string propertySet, string name, IReadOnlyList<KeyValuePair<string?, IdsTypedValue>> values, bool supported = true)
        {
            PropertySet = propertySet;
            Name = name;
            Values = values;
            Supported = supported;
        }

        /// <summary>Tên property set.</summary>
        public string PropertySet { get; }

        /// <summary>Tên property.</summary>
        public string Name { get; }

        /// <summary>Giá trị khác rỗng, mỗi cái kèm kiểu dữ liệu IFC viết hoa.</summary>
        public IReadOnlyList<KeyValuePair<string?, IdsTypedValue>> Values { get; }

        /// <summary>
        /// <c>false</c>: loại property IDS không kiểm được (complex, reference) — facet trượt, như IfcTester.
        /// </summary>
        public bool Supported { get; }
    }

    /// <summary>
    /// Phần tử biết đọc giá trị CÓ KIỂU (đường file IFC). Evaluator dùng khi phần tử cài interface này: tên thuộc
    /// tính/property/pset khai bằng pattern được hiểu đúng (mọi cái khớp đều phải đạt), giá trị so theo kiểu.
    /// Đường Revit không cài — giữ cách so bằng chuỗi như trước.
    /// </summary>
    public interface IIdsTypedElement
    {
        /// <summary>Mọi thuộc tính trực tiếp có tên thoả <paramref name="name"/>; rỗng khi lớp không có thuộc tính nào như vậy.</summary>
        IReadOnlyList<IdsAttributeValue> Attributes(IdsValue name);

        /// <summary>
        /// Property set có tên thoả <paramref name="propertySet"/> (<c>null</c>/không ràng buộc = mọi pset), mỗi pset kèm
        /// các property có tên thoả <paramref name="name"/> và có giá trị. Pset khớp mà không property nào có giá trị
        /// vẫn có mặt với danh sách rỗng — IDS: "mọi pset khớp đều phải có".
        /// </summary>
        IReadOnlyList<KeyValuePair<string, IReadOnlyList<IdsPropertyValue>>> Properties(IdsValue? propertySet, IdsValue name);

        /// <summary>
        /// Mọi tham chiếu phân loại (hệ, mã) — đã gồm tham chiếu cha và phân loại thừa kế từ kiểu. Mã <c>null</c>:
        /// phần tử gắn thẳng hệ phân loại, không có mã.
        /// </summary>
        IReadOnlyList<KeyValuePair<string, string?>> ClassificationReferences();

        /// <summary>
        /// Tên lớp IFC4 tương đương của phần tử IFC2X3 theo bảng ánh xạ kiểu (<c>IfcFlowTerminal</c> có kiểu
        /// <c>IfcAirTerminalType</c> = <c>IFCAIRTERMINAL</c>); <c>null</c> khi không có.
        /// </summary>
        string? EntityAlias { get; }
    }
}
