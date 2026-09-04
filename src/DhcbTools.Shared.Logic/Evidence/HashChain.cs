using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DhcbTools.Shared.Logic.Evidence
{
    /// <summary>Kết luận của một lượt kiểm chuỗi băm.</summary>
    public enum ChainStatus
    {
        /// <summary>Mọi dòng đều mang dấu vết và các mắt xích nối liền nhau.</summary>
        Intact,

        /// <summary>Có dòng chưa mang chuỗi băm — log ghi trước khi bật, hoặc dấu vết đã bị gỡ.</summary>
        NotSealed,

        /// <summary>Băm không khớp nội dung của chính dòng đó — dòng đã bị sửa.</summary>
        ContentChanged,

        /// <summary><c>prevHash</c> không khớp băm của dòng trước — có dòng bị chèn, xoá hoặc đảo chỗ.</summary>
        ChainBroken,

        /// <summary>Dòng không đọc lại được để lấy <c>prevHash</c>.</summary>
        Malformed,
    }

    /// <summary>Kết quả kiểm một file log: đạt hay không, và nếu không thì hỏng ở **đúng dòng nào**.</summary>
    public sealed class ChainVerification
    {
        public ChainVerification(ChainStatus status, int checkedLines, int? problemLine, string message)
        {
            Status = status;
            CheckedLines = checkedLines;
            ProblemLine = problemLine;
            Message = message;
        }

        public ChainStatus Status { get; }

        /// <summary>Số dòng không rỗng đã kiểm được trước khi dừng.</summary>
        public int CheckedLines { get; }

        /// <summary>Số thứ tự dòng hỏng, đếm từ 1. Null khi chuỗi nguyên vẹn.</summary>
        public int? ProblemLine { get; }

        /// <summary>Câu tiếng Việt để in thẳng ra cho người vận hành.</summary>
        public string Message { get; }

        public bool Ok => Status == ChainStatus.Intact;
    }

    /// <summary>
    /// Chuỗi băm nối tiếp cho nhật ký dòng-JSON (mục 11.5 của <c>roadmap.md</c>). Mỗi dòng mang
    /// <c>prevHash</c> (băm của dòng ngay trước) và <c>hash</c> = SHA-256 của **chính phần nội dung dòng
    /// đó tính đến trước trường <c>hash</c>**. Sửa một dòng cũ làm gãy chuỗi từ dòng đó trở đi, và
    /// <see cref="Verify"/> chỉ ra đúng dòng bị sửa.
    /// <para>
    /// Băm tính trên **đúng chuỗi ký tự đã ghi ra file**, không tính trên object đọc lại rồi serialize
    /// lần nữa: vòng JSON → object → JSON không bảo đảm ra byte y hệt (DateTime, thứ tự trường, culture),
    /// mà kiểm toàn vẹn thì chỉ cần lệch một byte là báo sai. Nhờ vậy <see cref="Verify"/> không phụ
    /// thuộc thư viện JSON nào.
    /// </para>
    /// <para>
    /// <b>Giới hạn phải nói thật:</b> chuỗi băm chứng minh **tính toàn vẹn nội bộ** của log — ai sửa một
    /// dòng mà không tính lại toàn bộ chuỗi thì bị phát hiện. Nó **không** chứng minh log do ai ghi và
    /// ghi lúc nào: người có quyền ghi file vẫn dựng lại được cả chuỗi. Muốn đủ giá trị pháp lý theo
    /// NĐ 207/2026 còn cần chữ ký số của các bên (điều kiện ②) và bản sao lưu độc lập (điều kiện ③);
    /// lớp này phủ điều kiện ① và tạo điều kiện cho hai cái còn lại.
    /// </para>
    /// </summary>
    public static class HashChain
    {
        /// <summary><c>prevHash</c> của dòng đầu tiên: 64 số 0, để dòng đầu cũng có mắt xích kiểm được.</summary>
        public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

        /// <summary>Độ dài chuỗi băm SHA-256 viết dạng hex thường.</summary>
        public const int HashLength = 64;

        private const string HashField = ",\"hash\":\"";
        private const string Tail = "\"}";

        /// <summary>SHA-256 của <paramref name="payload"/> (UTF-8, không BOM), viết hex thường.</summary>
        public static string ComputeHash(string payload)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(new UTF8Encoding(false).GetBytes(payload));
                var text = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// Gắn <c>hash</c> vào cuối một object JSON một dòng. Đặt ở cuối là cố ý: nhờ vậy tách ra lại
        /// được bằng cắt chuỗi thuần, không cần parse.
        /// </summary>
        public static string Seal(string payload, string hash)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (hash is null)
            {
                throw new ArgumentNullException(nameof(hash));
            }

            if (payload.Length < 2 || payload[payload.Length - 1] != '}')
            {
                throw new ArgumentException("payload phải là một object JSON một dòng kết thúc bằng '}'.", nameof(payload));
            }

            return payload.Substring(0, payload.Length - 1) + HashField + hash + Tail;
        }

        /// <summary>Tách một dòng đã gắn dấu vết thành phần nội dung và phần băm. False khi dòng chưa mang dấu vết.</summary>
        public static bool TrySplit(string? line, out string payload, out string hash)
        {
            payload = string.Empty;
            hash = string.Empty;
            if (line is null)
            {
                return false;
            }

            var text = line.TrimEnd();
            if (!text.EndsWith(Tail, StringComparison.Ordinal))
            {
                return false;
            }

            // LastIndexOf: nếu chính nội dung log có chuỗi giống trường hash thì cái thật vẫn là cái cuối.
            var at = text.LastIndexOf(HashField, StringComparison.Ordinal);
            if (at < 1)
            {
                return false;
            }

            var start = at + HashField.Length;
            if (text.Length - Tail.Length - start != HashLength)
            {
                return false;
            }

            var candidate = text.Substring(start, HashLength);
            if (!IsHex(candidate))
            {
                return false;
            }

            payload = text.Substring(0, at) + "}";
            hash = candidate;
            return true;
        }

        /// <summary>
        /// Kiểm cả file. Dừng ở dòng hỏng đầu tiên và chỉ đúng số thứ tự dòng đó — biết dòng nào bị sửa
        /// quan trọng hơn biết có bao nhiêu dòng bị sửa.
        /// </summary>
        /// <param name="lines">Các dòng của file, theo đúng thứ tự đã ghi. Dòng rỗng được bỏ qua.</param>
        /// <param name="prevHashOf">
        /// Cách lấy <c>prevHash</c> ra khỏi một dòng — truyền từ ngoài vào để lớp này không phải biết
        /// tới định dạng bản ghi hay thư viện JSON nào. Trả null nghĩa là dòng không đọc được.
        /// </param>
        public static ChainVerification Verify(IReadOnlyList<string> lines, Func<string, string?> prevHashOf)
        {
            if (lines is null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            if (prevHashOf is null)
            {
                throw new ArgumentNullException(nameof(prevHashOf));
            }

            var expectedPrev = Genesis;
            var done = 0;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (StringGuard.IsBlank(line))
                {
                    continue;
                }

                var no = i + 1;

                if (!TrySplit(line, out var payload, out var hash))
                {
                    return new ChainVerification(
                        ChainStatus.NotSealed,
                        done,
                        no,
                        $"Dòng {no} chưa mang chuỗi băm — log ghi trước khi bật EvidenceLog, hoặc dấu vết đã bị gỡ.");
                }

                if (!string.Equals(ComputeHash(payload), hash, StringComparison.OrdinalIgnoreCase))
                {
                    return new ChainVerification(
                        ChainStatus.ContentChanged,
                        done,
                        no,
                        $"Dòng {no} đã bị sửa: băm ghi trong dòng không khớp nội dung của chính dòng đó.");
                }

                var prev = prevHashOf(line);
                if (prev is null)
                {
                    return new ChainVerification(
                        ChainStatus.Malformed,
                        done,
                        no,
                        $"Dòng {no} không đọc lại được nên không lấy được prevHash để nối chuỗi.");
                }

                if (!string.Equals(prev, expectedPrev, StringComparison.OrdinalIgnoreCase))
                {
                    return new ChainVerification(
                        ChainStatus.ChainBroken,
                        done,
                        no,
                        $"Chuỗi đứt tại dòng {no}: prevHash không khớp băm của dòng trước — có dòng bị chèn, xoá hoặc đảo chỗ.");
                }

                expectedPrev = hash;
                done++;
            }

            return new ChainVerification(
                ChainStatus.Intact,
                done,
                null,
                done == 0
                    ? "Log rỗng: không có dòng nào để kiểm."
                    : $"Chuỗi băm nguyên vẹn: {done} dòng, không dòng nào bị sửa hay mất.");
        }

        private static bool IsHex(string text)
        {
            foreach (var c in text)
            {
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
