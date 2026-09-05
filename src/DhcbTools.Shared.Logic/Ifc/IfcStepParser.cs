using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DhcbTools.Shared.Logic.Ifc
{
    /// <summary>Loại của một giá trị trong file STEP.</summary>
    public enum IfcValueKind
    {
        /// <summary><c>$</c> — không có giá trị.</summary>
        Null,

        /// <summary><c>*</c> — giá trị dẫn xuất, khai ở lớp con.</summary>
        Derived,

        /// <summary><c>#123</c> — trỏ tới một thực thể khác.</summary>
        Reference,

        /// <summary>Chuỗi trong dấu nháy đơn.</summary>
        Text,

        /// <summary>Số nguyên hoặc số thực.</summary>
        Number,

        /// <summary><c>.T.</c>, <c>.FALSE.</c>, <c>.NOTDEFINED.</c>…</summary>
        Enumeration,

        /// <summary><c>(a,b,c)</c>.</summary>
        List,

        /// <summary><c>IFCLABEL('x')</c> — giá trị bọc trong tên kiểu.</summary>
        Typed,
    }

    /// <summary>Một giá trị trong danh sách tham số của thực thể STEP.</summary>
    public sealed class IfcValue
    {
        internal IfcValue(IfcValueKind kind)
        {
            Kind = kind;
            Items = Array.Empty<IfcValue>();
        }

        /// <summary>Loại giá trị.</summary>
        public IfcValueKind Kind { get; }

        /// <summary>Nội dung chuỗi đã giải mã, tên enum không có dấu chấm, chữ số nguyên bản của số,
        /// hoặc tên kiểu của giá trị bọc.</summary>
        public string Raw { get; internal set; } = string.Empty;

        /// <summary>Số hiệu thực thể được trỏ tới, khi <see cref="Kind"/> là <see cref="IfcValueKind.Reference"/>.</summary>
        public int Reference { get; internal set; }

        /// <summary>Phần tử con của danh sách, hoặc tham số của giá trị bọc kiểu.</summary>
        public IReadOnlyList<IfcValue> Items { get; internal set; }

        /// <summary>Giá trị rỗng dùng chung cho tham số thiếu.</summary>
        public static readonly IfcValue Empty = new IfcValue(IfcValueKind.Null);

        /// <summary>
        /// Chuỗi để so sánh trong quy tắc kiểm: chuỗi và enum trả nguyên văn, số trả dạng bất biến,
        /// giá trị bọc kiểu (<c>IFCBOOLEAN(.T.)</c>) trả giá trị bên trong. Null/Derived trả <c>null</c>.
        /// </summary>
        public string? AsText()
        {
            switch (Kind)
            {
                case IfcValueKind.Text:
                case IfcValueKind.Enumeration:
                case IfcValueKind.Number:
                    return Raw;
                case IfcValueKind.Typed:
                    return Items.Count == 1 ? Items[0].AsText() : null;
                default:
                    return null;
            }
        }

        /// <summary>Số hiệu thực thể được trỏ tới, hoặc <c>null</c> nếu giá trị không phải tham chiếu.</summary>
        public int? AsReference() => Kind == IfcValueKind.Reference ? Reference : (int?)null;
    }

    /// <summary>Một dòng <c>#id = TYPE(...)</c> trong phần DATA, hoặc một mục trong HEADER (khi <see cref="Id"/> = 0).</summary>
    public sealed class IfcEntity
    {
        internal IfcEntity(int id, string type, IReadOnlyList<IfcValue> attributes, int line)
        {
            Id = id;
            Type = type;
            Attributes = attributes;
            Line = line;
        }

        /// <summary>Số hiệu <c>#id</c>; 0 với mục trong HEADER.</summary>
        public int Id { get; }

        /// <summary>Tên kiểu VIẾT HOA (<c>IFCWALL</c>) — STEP không phân biệt hoa thường ở tên kiểu.</summary>
        public string Type { get; }

        /// <summary>Danh sách tham số theo đúng thứ tự trong file.</summary>
        public IReadOnlyList<IfcValue> Attributes { get; }

        /// <summary>Số dòng trong file (bắt đầu từ 1) để báo lỗi chỉ đúng chỗ.</summary>
        public int Line { get; }

        /// <summary>Tham số thứ <paramref name="index"/>, hoặc <see cref="IfcValue.Empty"/> nếu thiếu.</summary>
        public IfcValue At(int index) => index >= 0 && index < Attributes.Count ? Attributes[index] : IfcValue.Empty;
    }

    /// <summary>Lỗi cú pháp khi đọc file STEP.</summary>
    public sealed class IfcParseException : Exception
    {
        /// <summary>Khởi tạo với thông báo tiếng Việt kèm số dòng.</summary>
        public IfcParseException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Bộ đọc file STEP (ISO 10303-21) — định dạng văn bản của mọi file <c>.ifc</c>.
    /// <para>
    /// Vì sao viết tay thay vì lấy thư viện: tầng này phải thuần để chạy trên CI không có Revit
    /// (nguyên tắc 5), và việc cần làm rất hẹp — đọc lại chính file mình vừa xuất để đếm phần tử và
    /// tra thuộc tính. Cú pháp STEP chỉ có bảy loại giá trị; không cần bảng lược đồ EXPRESS nào.
    /// </para>
    /// <para>
    /// Bộ đọc KHÔNG biết cây kế thừa của lược đồ IFC: <c>IFCWALL</c> và <c>IFCWALLSTANDARDCASE</c> là
    /// hai tên khác nhau. Quy tắc kiểm phải kể tên đầy đủ — đổi lại không phải bảo trì bảng lược đồ cho
    /// từng bản IFC2X3/IFC4/IFC4X3.
    /// </para>
    /// </summary>
    public static class IfcStepParser
    {
        /// <summary>Đọc toàn bộ nội dung một file IFC dạng văn bản.</summary>
        /// <exception cref="IfcParseException">Khi thiếu phần DATA hoặc gặp ký tự không hợp lệ.</exception>
        public static IfcStepFile Parse(string text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var reader = new Cursor(text);
            var header = new List<IfcEntity>();
            var data = new List<IfcEntity>();
            var section = Section.None;
            var sawData = false;

            while (true)
            {
                reader.SkipTrivia();
                if (reader.AtEnd)
                {
                    break;
                }

                var startLine = reader.Line;

                // END-ISO-10303-21 phải thử TRƯỚC ISO-10303-21, không thì phần đuôi khớp nhầm.
                if (reader.TryKeyword("END-ISO-10303-21"))
                {
                    reader.ExpectSemicolon();
                    break;
                }

                if (reader.TryKeyword("ISO-10303-21"))
                {
                    reader.ExpectSemicolon();
                    continue;
                }

                if (reader.TryKeyword("HEADER"))
                {
                    reader.ExpectSemicolon();
                    section = Section.Header;
                    continue;
                }

                if (reader.TryKeyword("DATA"))
                {
                    // DATA có thể mang tham số: DATA('ten');
                    if (reader.Peek() == '(')
                    {
                        reader.ReadArguments();
                    }

                    reader.ExpectSemicolon();
                    section = Section.Data;
                    sawData = true;
                    continue;
                }

                if (reader.TryKeyword("ENDSEC"))
                {
                    reader.ExpectSemicolon();
                    section = Section.None;
                    continue;
                }

                if (reader.Peek() == '#')
                {
                    data.Add(reader.ReadInstance(startLine));
                    continue;
                }

                var headerEntry = reader.ReadHeaderEntry(startLine);
                if (section == Section.Header || !sawData)
                {
                    header.Add(headerEntry);
                }
                else
                {
                    data.Add(headerEntry);
                }
            }

            if (!sawData)
            {
                throw new IfcParseException("File không có phần DATA — đây không phải file STEP/IFC hợp lệ.");
            }

            return new IfcStepFile(header, data);
        }

        private enum Section
        {
            None,
            Header,
            Data,
        }

        /// <summary>Con trỏ đọc ký tự, tách riêng để phần trên chỉ nói về cấu trúc file.</summary>
        private sealed class Cursor
        {
            private readonly string _text;
            private int _pos;

            public Cursor(string text)
            {
                _text = text;
                Line = 1;
            }

            public int Line { get; private set; }

            public bool AtEnd => _pos >= _text.Length;

            public char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

            private char Take()
            {
                var c = _text[_pos++];
                if (c == '\n')
                {
                    Line++;
                }

                return c;
            }

            /// <summary>Bỏ khoảng trắng và chú thích kiểu C.</summary>
            public void SkipTrivia()
            {
                while (!AtEnd)
                {
                    var c = Peek();
                    if (char.IsWhiteSpace(c))
                    {
                        Take();
                        continue;
                    }

                    if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '*')
                    {
                        Take();
                        Take();
                        while (!AtEnd && !(Peek() == '*' && _pos + 1 < _text.Length && _text[_pos + 1] == '/'))
                        {
                            Take();
                        }

                        if (AtEnd)
                        {
                            throw new IfcParseException("Chú thích không được đóng, từ dòng " + Line + ".");
                        }

                        Take();
                        Take();
                        continue;
                    }

                    return;
                }
            }

            public bool TryKeyword(string word)
            {
                if (_pos + word.Length > _text.Length)
                {
                    return false;
                }

                if (string.Compare(_text, _pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }

                // Không khớp một nửa: DATA không được nuốt phần đầu của một tên dài hơn như DATABASE.
                var after = _pos + word.Length < _text.Length ? _text[_pos + word.Length] : '\0';
                if (char.IsLetterOrDigit(after) || after == '_' || after == '-')
                {
                    return false;
                }

                for (var i = 0; i < word.Length; i++)
                {
                    Take();
                }

                return true;
            }

            public void ExpectSemicolon()
            {
                SkipTrivia();
                if (AtEnd || Peek() != ';')
                {
                    throw new IfcParseException("Thiếu dấu chấm phẩy ở dòng " + Line + ".");
                }

                Take();
            }

            /// <summary>Đọc một dòng thực thể có số hiệu.</summary>
            public IfcEntity ReadInstance(int startLine)
            {
                Take(); // #
                var id = ReadInteger();
                SkipTrivia();
                if (AtEnd || Peek() != '=')
                {
                    throw new IfcParseException("Dòng " + Line + ": sau #" + id + " phải là dấu bằng.");
                }

                Take();
                SkipTrivia();
                var type = ReadTypeName();
                SkipTrivia();
                var args = Peek() == '(' ? ReadArguments() : (IReadOnlyList<IfcValue>)Array.Empty<IfcValue>();
                ExpectSemicolon();
                return new IfcEntity(id, type, args, startLine);
            }

            /// <summary>Đọc một mục trong HEADER (không có số hiệu).</summary>
            public IfcEntity ReadHeaderEntry(int startLine)
            {
                var type = ReadTypeName();
                SkipTrivia();
                var args = Peek() == '(' ? ReadArguments() : (IReadOnlyList<IfcValue>)Array.Empty<IfcValue>();
                ExpectSemicolon();
                return new IfcEntity(0, type, args, startLine);
            }

            private int ReadInteger()
            {
                var sb = new StringBuilder();
                while (!AtEnd && char.IsDigit(Peek()))
                {
                    sb.Append(Take());
                }

                if (sb.Length == 0)
                {
                    throw new IfcParseException("Dòng " + Line + ": chờ một số nguyên.");
                }

                return int.Parse(sb.ToString(), CultureInfo.InvariantCulture);
            }

            private string ReadTypeName()
            {
                var sb = new StringBuilder();
                while (!AtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '-'))
                {
                    sb.Append(Take());
                }

                if (sb.Length == 0)
                {
                    throw new IfcParseException("Dòng " + Line + ": chờ tên kiểu, gặp ký tự " + Peek() + ".");
                }

                return sb.ToString().ToUpperInvariant();
            }

            /// <summary>Đọc danh sách trong ngoặc tròn thành danh sách giá trị.</summary>
            public IReadOnlyList<IfcValue> ReadArguments()
            {
                Take(); // (
                var items = new List<IfcValue>();
                SkipTrivia();
                if (Peek() == ')')
                {
                    Take();
                    return items;
                }

                while (true)
                {
                    items.Add(ReadValue());
                    SkipTrivia();
                    if (AtEnd)
                    {
                        throw new IfcParseException("Danh sách tham số không được đóng, từ dòng " + Line + ".");
                    }

                    var c = Take();
                    if (c == ',')
                    {
                        SkipTrivia();
                        continue;
                    }

                    if (c == ')')
                    {
                        return items;
                    }

                    throw new IfcParseException("Dòng " + Line + ": chờ dấu phẩy hoặc ngoặc đóng, gặp " + c + ".");
                }
            }

            private IfcValue ReadValue()
            {
                SkipTrivia();
                if (AtEnd)
                {
                    throw new IfcParseException("File kết thúc giữa chừng ở dòng " + Line + ".");
                }

                var c = Peek();
                if (c == '$')
                {
                    Take();
                    return new IfcValue(IfcValueKind.Null);
                }

                if (c == '*')
                {
                    Take();
                    return new IfcValue(IfcValueKind.Derived);
                }

                if (c == '#')
                {
                    Take();
                    return new IfcValue(IfcValueKind.Reference) { Reference = ReadInteger() };
                }

                if (c == '\'')
                {
                    return new IfcValue(IfcValueKind.Text) { Raw = ReadText() };
                }

                if (c == '(')
                {
                    var list = new IfcValue(IfcValueKind.List);
                    list.Items = ReadArguments();
                    return list;
                }

                if (c == '.')
                {
                    return new IfcValue(IfcValueKind.Enumeration) { Raw = ReadEnumeration() };
                }

                if (c == '"')
                {
                    // Chuỗi nhị phân — giữ nguyên văn, không quy tắc nào kiểm theo nó.
                    Take();
                    var sb = new StringBuilder();
                    while (!AtEnd && Peek() != '"')
                    {
                        sb.Append(Take());
                    }

                    if (AtEnd)
                    {
                        throw new IfcParseException("Chuỗi nhị phân không được đóng, dòng " + Line + ".");
                    }

                    Take();
                    return new IfcValue(IfcValueKind.Text) { Raw = sb.ToString() };
                }

                if (c == '-' || c == '+' || char.IsDigit(c))
                {
                    return new IfcValue(IfcValueKind.Number) { Raw = ReadNumber() };
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var name = ReadTypeName();
                    SkipTrivia();
                    if (Peek() == '(')
                    {
                        var typed = new IfcValue(IfcValueKind.Typed) { Raw = name };
                        typed.Items = ReadArguments();
                        return typed;
                    }

                    return new IfcValue(IfcValueKind.Enumeration) { Raw = name };
                }

                throw new IfcParseException("Dòng " + Line + ": ký tự không hợp lệ " + c + ".");
            }

            private string ReadNumber()
            {
                var sb = new StringBuilder();
                if (Peek() == '-' || Peek() == '+')
                {
                    sb.Append(Take());
                }

                while (!AtEnd && (char.IsDigit(Peek()) || Peek() == '.'))
                {
                    sb.Append(Take());
                }

                if (!AtEnd && (Peek() == 'e' || Peek() == 'E'))
                {
                    sb.Append(Take());
                    if (!AtEnd && (Peek() == '-' || Peek() == '+'))
                    {
                        sb.Append(Take());
                    }

                    while (!AtEnd && char.IsDigit(Peek()))
                    {
                        sb.Append(Take());
                    }
                }

                return sb.ToString();
            }

            private string ReadEnumeration()
            {
                Take(); // dấu chấm mở
                var sb = new StringBuilder();
                while (!AtEnd && Peek() != '.')
                {
                    sb.Append(Take());
                }

                if (AtEnd)
                {
                    throw new IfcParseException("Giá trị liệt kê không được đóng, dòng " + Line + ".");
                }

                Take();
                return sb.ToString().ToUpperInvariant();
            }

            /// <summary>
            /// Đọc chuỗi và giải mã theo ISO 10303-21: hai dấu nháy liền là một dấu nháy, còn các dãy
            /// thoát để ở <see cref="DecodeEscapes"/>. Không giải mã thì tên tiếng Việt trong file Revit
            /// xuất ra đọc thành rác và mọi so khớp tên đều trượt.
            /// </summary>
            private string ReadText()
            {
                Take(); // nháy mở
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw new IfcParseException("Chuỗi không được đóng, dòng " + Line + ".");
                    }

                    var c = Take();
                    if (c == '\'')
                    {
                        if (!AtEnd && Peek() == '\'')
                        {
                            Take();
                            sb.Append('\'');
                            continue;
                        }

                        return DecodeEscapes(sb.ToString());
                    }

                    sb.Append(c);
                }
            }
        }

        /// <summary>Giải mã các dãy thoát Unicode của STEP trong chuỗi.</summary>
        internal static string DecodeEscapes(string raw)
        {
            if (raw.IndexOf('\\') < 0)
            {
                return raw;
            }

            var sb = new StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '\\')
                {
                    sb.Append(raw[i]);
                    continue;
                }

                // Dãy mã UTF-16, mỗi ký tự bốn chữ số hex, đóng bằng X0.
                if (Starts(raw, i, "\\X2\\"))
                {
                    var start = i + 4;
                    var end = raw.IndexOf("\\X0\\", start, StringComparison.OrdinalIgnoreCase);
                    var body = end < 0 ? raw.Substring(start) : raw.Substring(start, end - start);
                    for (var k = 0; k + 4 <= body.Length; k += 4)
                    {
                        if (ushort.TryParse(body.Substring(k, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                        }
                    }

                    i = end < 0 ? raw.Length - 1 : end + 3;
                    continue;
                }

                // Dãy mã UTF-32, mỗi ký tự tám chữ số hex.
                if (Starts(raw, i, "\\X4\\"))
                {
                    var start = i + 4;
                    var end = raw.IndexOf("\\X0\\", start, StringComparison.OrdinalIgnoreCase);
                    var body = end < 0 ? raw.Substring(start) : raw.Substring(start, end - start);
                    for (var k = 0; k + 8 <= body.Length; k += 8)
                    {
                        if (int.TryParse(body.Substring(k, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)
                            && code >= 0 && code <= 0x10FFFF)
                        {
                            sb.Append(char.ConvertFromUtf32(code));
                        }
                    }

                    i = end < 0 ? raw.Length - 1 : end + 3;
                    continue;
                }

                // Một byte đơn.
                if (Starts(raw, i, "\\X\\") && i + 5 <= raw.Length
                    && byte.TryParse(raw.Substring(i + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var one))
                {
                    sb.Append((char)one);
                    i += 4;
                    continue;
                }

                // Ký tự trang mã trên: cộng 128 vào mã ASCII đứng sau.
                if (Starts(raw, i, "\\S\\") && i + 3 < raw.Length)
                {
                    sb.Append((char)(raw[i + 3] + 128));
                    i += 3;
                    continue;
                }

                if (Starts(raw, i, "\\\\"))
                {
                    sb.Append('\\');
                    i++;
                    continue;
                }

                sb.Append(raw[i]);
            }

            return sb.ToString();
        }

        private static bool Starts(string s, int at, string what) =>
            at + what.Length <= s.Length && string.Compare(s, at, what, 0, what.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>Kết quả đọc thô một file STEP: phần HEADER và phần DATA.</summary>
    public sealed class IfcStepFile
    {
        internal IfcStepFile(IReadOnlyList<IfcEntity> header, IReadOnlyList<IfcEntity> data)
        {
            Header = header;
            Data = data;
        }

        /// <summary>Các mục trong HEADER.</summary>
        public IReadOnlyList<IfcEntity> Header { get; }

        /// <summary>Các thực thể có số hiệu trong DATA, theo thứ tự trong file.</summary>
        public IReadOnlyList<IfcEntity> Data { get; }
    }
}
