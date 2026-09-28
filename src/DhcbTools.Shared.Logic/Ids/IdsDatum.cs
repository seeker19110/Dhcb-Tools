using System.Globalization;
using DhcbTools.Shared.Logic.Ifc;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>Kiểu của một giá trị IFC khi đem so với IDS.</summary>
    internal enum IdsDatumKind
    {
        /// <summary>Chuỗi (label, text, identifier, ngày giờ, enum không phải boolean).</summary>
        Text,

        /// <summary>Số nguyên — IDS không nhận <c>42.0</c> cho số nguyên.</summary>
        Integer,

        /// <summary>Số thực — so theo dung sai IDS, nhận <c>42</c>, <c>42.</c>, <c>4.2e1</c>.</summary>
        Real,

        /// <summary>Boolean — IDS chỉ viết <c>true</c>/<c>false</c> (hoặc <c>1</c>/<c>0</c>).</summary>
        Boolean,

        /// <summary>Tham chiếu thực thể, danh sách, giá trị select bọc kiểu: "có mặt" được, so giá trị thì luôn trượt.</summary>
        Object,
    }

    /// <summary>
    /// Một giá trị đọc từ file IFC còn giữ <b>kiểu</b>. Đường chuỗi chung (<see cref="IdsValue.Accepts(string)"/>) không
    /// phân biệt được <c>IFCINTEGER(42)</c> với <c>IFCLABEL('42')</c>, hay <c>.T.</c> với chữ "T" — mà IDS 1.0 so theo
    /// kiểu: số nguyên không nhận <c>42.0</c>, chuỗi <c>'42'</c> không bằng số 42, pattern không áp lên số.
    /// </summary>
    internal readonly struct IdsDatum
    {
        private IdsDatum(IdsDatumKind kind, string text, double number)
        {
            Kind = kind;
            Text = text;
            Number = number;
        }

        /// <summary>Kiểu giá trị.</summary>
        public IdsDatumKind Kind { get; }

        /// <summary>Chuỗi; với số là dạng viết trong file; boolean là <c>true</c>/<c>false</c>.</summary>
        public string Text { get; }

        /// <summary>Giá trị số (boolean: 1/0).</summary>
        public double Number { get; }

        /// <summary>Tham chiếu/danh sách/select bọc kiểu.</summary>
        public static readonly IdsDatum Object = new IdsDatum(IdsDatumKind.Object, string.Empty, 0);

        public static IdsDatum OfText(string text) => new IdsDatum(IdsDatumKind.Text, text, 0);

        public static IdsDatum OfBoolean(bool value) => new IdsDatum(IdsDatumKind.Boolean, value ? "true" : "false", value ? 1 : 0);

        public static IdsDatum OfReal(double value) => new IdsDatum(IdsDatumKind.Real, value.ToString("R", CultureInfo.InvariantCulture), value);

        /// <summary>
        /// Đọc một tham số STEP. <c>null</c> = không có giá trị: <c>$</c>, <c>*</c> (dẫn xuất) và <c>.U.</c> (logical
        /// unknown — IDS: "a logical unknown is considered false and will not pass").
        /// </summary>
        public static IdsDatum? FromStep(IfcValue value)
        {
            switch (value.Kind)
            {
                case IfcValueKind.Text:
                    return OfText(value.Raw);
                case IfcValueKind.Enumeration:
                    return value.Raw == "T" ? OfBoolean(true)
                        : value.Raw == "F" ? OfBoolean(false)
                        : value.Raw == "U" ? (IdsDatum?)null
                        : OfText(value.Raw);
                case IfcValueKind.Number:
                    // STEP viết số thực luôn có dấu chấm (42.) — số nguyên thì không.
                    // File hỏng ("-" trơ trọi) không làm sập cả lần kiểm: coi như chuỗi, mọi so số đều trượt.
                    if (!double.TryParse(value.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        return OfText(value.Raw);
                    }

                    var real = value.Raw.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0;
                    return new IdsDatum(real ? IdsDatumKind.Real : IdsDatumKind.Integer, value.Raw, number);
                case IfcValueKind.Null:
                case IfcValueKind.Derived:
                    return null;
                default:
                    return Object;
            }
        }

        /// <summary>Giá trị bọc kiểu của property (<c>IFCLABEL('x')</c>, <c>IFCBOOLEAN(.T.)</c>): lấy giá trị bên trong.</summary>
        public static IdsDatum? Unwrap(IfcValue value) =>
            value.Kind == IfcValueKind.Typed && value.Items.Count == 1 ? FromStep(value.Items[0]) : FromStep(value);
    }
}
