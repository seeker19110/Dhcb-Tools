using System;

namespace DhcbTools.Shared.Logic.Bcf
{
    /// <summary>
    /// Đổi <see cref="Guid"/> 128 bit sang chuỗi <b>IFC GUID</b> 22 ký tự (ISO 10303-21) và ngược lại.
    /// <para>
    /// BCF định danh phần tử bằng IFC GUID chứ không bằng ElementId, nên không có bước này thì file BCF
    /// mở ra ở Solibri/BIMcollab không chỉ được vào phần tử nào. Guid gốc lấy từ Revit bằng
    /// <c>ExportUtils.GetExportId</c> — đúng guid mà chính bộ xuất IFC của Revit dùng, nên phần tử trong
    /// BCF khớp với phần tử trong file IFC đã nộp.
    /// </para>
    /// <para>Thuần số học nên có test vòng tròn trên CI.</para>
    /// </summary>
    public static class IfcGuid
    {
        /// <summary>Bảng 64 ký tự của IFC — không phải base64 chuẩn: hai ký tự cuối là <c>_</c> và <c>$</c>.</summary>
        private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>Guid → 22 ký tự.</summary>
        public static string From(Guid guid)
        {
            var bytes = guid.ToByteArray();
            var a = BitConverter.ToUInt32(bytes, 0);
            uint b = BitConverter.ToUInt16(bytes, 4);
            uint c = BitConverter.ToUInt16(bytes, 6);
            var d = new byte[8];
            Array.Copy(bytes, 8, d, 0, 8);

            // Sáu nhóm: nhóm đầu 8 bit (2 ký tự), năm nhóm sau 24 bit (4 ký tự) = 128 bit.
            var groups = new uint[6];
            groups[0] = a / 16777216;
            groups[1] = a % 16777216;
            groups[2] = b * 256 + c / 256;
            groups[3] = (c % 256) * 65536 + (uint)d[0] * 256 + d[1];
            groups[4] = (uint)d[2] * 65536 + (uint)d[3] * 256 + d[4];
            groups[5] = (uint)d[5] * 65536 + (uint)d[6] * 256 + d[7];

            var text = new char[22];
            var offset = 0;
            for (var i = 0; i < 6; i++)
            {
                var length = i == 0 ? 2 : 4;
                var value = groups[i];
                for (var j = length - 1; j >= 0; j--)
                {
                    text[offset + j] = Chars[(int)(value % 64)];
                    value /= 64;
                }

                offset += length;
            }

            return new string(text);
        }

        /// <summary>22 ký tự → Guid. Chuỗi sai độ dài hoặc có ký tự lạ thì trả <c>false</c>.</summary>
        public static bool TryParse(string? text, out Guid guid)
        {
            guid = Guid.Empty;
            if (text == null || text.Length != 22)
            {
                return false;
            }

            var groups = new uint[6];
            var offset = 0;
            for (var i = 0; i < 6; i++)
            {
                var length = i == 0 ? 2 : 4;
                uint value = 0;
                for (var j = 0; j < length; j++)
                {
                    var index = Chars.IndexOf(text[offset + j]);
                    if (index < 0)
                    {
                        return false;
                    }

                    value = value * 64 + (uint)index;
                }

                groups[i] = value;
                offset += length;
            }

            var a = groups[0] * 16777216 + groups[1];
            var b = (ushort)(groups[2] / 256);
            var c = (ushort)((groups[2] % 256) * 256 + groups[3] / 65536);
            var d = new byte[8];
            d[0] = (byte)((groups[3] % 65536) / 256);
            d[1] = (byte)(groups[3] % 256);
            d[2] = (byte)(groups[4] / 65536);
            d[3] = (byte)((groups[4] % 65536) / 256);
            d[4] = (byte)(groups[4] % 256);
            d[5] = (byte)(groups[5] / 65536);
            d[6] = (byte)((groups[5] % 65536) / 256);
            d[7] = (byte)(groups[5] % 256);

            var bytes = new byte[16];
            Array.Copy(BitConverter.GetBytes(a), 0, bytes, 0, 4);
            Array.Copy(BitConverter.GetBytes(b), 0, bytes, 4, 2);
            Array.Copy(BitConverter.GetBytes(c), 0, bytes, 6, 2);
            Array.Copy(d, 0, bytes, 8, 8);
            guid = new Guid(bytes);
            return true;
        }
    }
}
