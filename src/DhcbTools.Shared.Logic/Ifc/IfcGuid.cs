using System;
using System.Globalization;

namespace DhcbTools.Shared.Logic.Ifc
{
    /// <summary>
    /// GlobalId IFC: GUID 128 bit nén thành 22 ký tự base64 theo bảng chữ riêng của IFC
    /// (<c>0-9A-Za-z_$</c>, ký tự đầu chỉ 0–3). Bộ xuất IFC của Revit sinh GlobalId từ <c>UniqueId</c>
    /// theo đúng thuật toán ở <see cref="FromRevitUniqueId"/>, nên đường kiểm IDS trên mô hình Revit trả
    /// được ĐÚNG chuỗi 22 ký tự mà file IFC sẽ có — thay vì UniqueId 45 ký tự làm mọi facet
    /// <c>GlobalId</c> (pattern/length) trượt 100 % trên Revit mà đạt trên IFC.
    /// </summary>
    public static class IfcGuid
    {
        /// <summary>Bảng 64 ký tự của IFC (không phải base64 chuẩn).</summary>
        public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>Nén một GUID thành 22 ký tự.</summary>
        public static string Compress(Guid guid)
        {
            // Thứ tự byte theo chuẩn IFC (Data1 big-endian, Data2, Data3, rồi 8 byte Data4).
            var b = guid.ToByteArray();
            var num = new uint[4];
            num[0] = (uint)((b[3] << 24) | (b[2] << 16) | (b[1] << 8) | b[0]);
            num[1] = (uint)((b[5] << 8) | b[4]) << 16 | (uint)((b[7] << 8) | b[6]);
            num[2] = (uint)(b[8] << 24 | b[9] << 16 | b[10] << 8 | b[11]);
            num[3] = (uint)(b[12] << 24 | b[13] << 16 | b[14] << 8 | b[15]);

            // Cách chia của buildingSMART (getString64FromGuid): 6 khối 8/24/24/24/24/24 bit → 2+4+4+4+4+4 = 22 ký tự.
            var chars = new char[22];
            var pos = 0;
            Encode(num[0] >> 24, 2, chars, ref pos);                       // 8 bit → 2 ký tự (bit cao nhất ≤ 3)
            Encode(num[0] & 0xFFFFFF, 4, chars, ref pos);                  // 24 bit → 4 ký tự
            Encode(num[1] >> 8, 4, chars, ref pos);                        // 24 bit
            Encode(((num[1] & 0xFF) << 16) | (num[2] >> 16), 4, chars, ref pos);
            Encode(((num[2] & 0xFFFF) << 8) | (num[3] >> 24), 4, chars, ref pos);
            Encode(num[3] & 0xFFFFFF, 4, chars, ref pos);
            return new string(chars);
        }

        /// <summary>Giải nén 22 ký tự về GUID. Ném <see cref="FormatException"/> nếu chuỗi không hợp lệ.</summary>
        public static Guid Expand(string compressed)
        {
            if (compressed == null || compressed.Length != 22)
            {
                throw new FormatException("GlobalId IFC phải dài đúng 22 ký tự.");
            }

            var blocks = new uint[6];
            var pos = 0;
            blocks[0] = Decode(compressed, 2, ref pos);
            for (var i = 1; i < 6; i++)
            {
                blocks[i] = Decode(compressed, 4, ref pos);
            }

            if (blocks[0] > 0xFF)
            {
                throw new FormatException("GlobalId IFC: ký tự đầu phải trong 0–3.");
            }

            var num = new uint[4];
            num[0] = (blocks[0] << 24) | blocks[1];
            num[1] = (blocks[2] << 8) | (blocks[3] >> 16);
            num[2] = ((blocks[3] & 0xFFFF) << 16) | (blocks[4] >> 8);
            num[3] = ((blocks[4] & 0xFF) << 24) | blocks[5];

            var b = new byte[16];
            b[0] = (byte)num[0]; b[1] = (byte)(num[0] >> 8); b[2] = (byte)(num[0] >> 16); b[3] = (byte)(num[0] >> 24);
            b[4] = (byte)(num[1] >> 16); b[5] = (byte)(num[1] >> 24);
            b[6] = (byte)num[1]; b[7] = (byte)(num[1] >> 8);
            b[8] = (byte)(num[2] >> 24); b[9] = (byte)(num[2] >> 16); b[10] = (byte)(num[2] >> 8); b[11] = (byte)num[2];
            b[12] = (byte)(num[3] >> 24); b[13] = (byte)(num[3] >> 16); b[14] = (byte)(num[3] >> 8); b[15] = (byte)num[3];
            return new Guid(b);
        }

        /// <summary>Chuỗi có đúng dạng GlobalId IFC không (22 ký tự trong bảng, ký tự đầu 0–3).</summary>
        public static bool IsValid(string? text)
        {
            if (text == null || text.Length != 22 || text[0] < '0' || text[0] > '3')
            {
                return false;
            }

            foreach (var c in text)
            {
                if (Alphabet.IndexOf(c) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// GlobalId mà bộ xuất IFC của Revit sinh cho một phần tử từ <c>Element.UniqueId</c>
        /// (<c>{episode-guid}-{elementId hex 8}</c>): 8 hex cuối của GUID XOR với ElementId, rồi nén.
        /// Trả null nếu chuỗi không đúng dạng UniqueId.
        /// </summary>
        public static string? FromRevitUniqueId(string? uniqueId)
        {
            if (uniqueId == null || uniqueId.Length != 45 || uniqueId[36] != '-')
            {
                return null;
            }

            var guidText = uniqueId.Substring(0, 36);
            var idHex = uniqueId.Substring(37);
            if (!Guid.TryParseExact(guidText, "D", out _)
                || !uint.TryParse(idHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var elementId)
                || !uint.TryParse(guidText.Substring(28), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var tail))
            {
                return null;
            }

            var mixed = guidText.Substring(0, 28) + (tail ^ elementId).ToString("x8", CultureInfo.InvariantCulture);
            return Compress(Guid.ParseExact(mixed, "D"));
        }

        private static void Encode(uint value, int count, char[] target, ref int pos)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                target[pos + i] = Alphabet[(int)(value & 0x3F)];
                value >>= 6;
            }

            pos += count;
        }

        private static uint Decode(string text, int count, ref int pos)
        {
            uint value = 0;
            for (var i = 0; i < count; i++)
            {
                var index = Alphabet.IndexOf(text[pos + i]);
                if (index < 0)
                {
                    throw new FormatException("GlobalId IFC chứa ký tự ngoài bảng: '" + text[pos + i] + "'.");
                }

                value = (value << 6) | (uint)index;
            }

            pos += count;
            return value;
        }
    }
}
