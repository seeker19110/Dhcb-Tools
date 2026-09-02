using System;
using System.Security.Cryptography;
using System.Text;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Xác thực cho HTTP Bridge (lỗi #8 trong docs/progress.md: cổng 8765/8766 hiện mở không xác thực,
    /// bất kỳ tiến trình nào trên máy cũng gửi được lệnh sửa mô hình với dryRun:false).
    /// Token sinh ngẫu nhiên lúc khởi động, lưu ở %APPDATA%\DHCB\bridge-token.txt, client gửi kèm
    /// header <c>Authorization: Bearer &lt;token&gt;</c>.
    /// </summary>
    public static class BridgeAuth
    {
        /// <summary>Tiền tố bắt buộc của header Authorization.</summary>
        public const string BearerPrefix = "Bearer ";

        /// <summary>Sinh token ngẫu nhiên 256 bit, mã base64url (không có ký tự cần escape trong URL/header).</summary>
        public static string GenerateToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>Lấy phần token từ giá trị header Authorization; trả về null nếu header sai định dạng.</summary>
        public static string? ExtractBearerToken(string? authorizationHeader)
        {
            if (StringGuard.IsBlank(authorizationHeader))
            {
                return null;
            }

            var trimmed = authorizationHeader.Trim();
            if (!trimmed.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var token = trimmed.Substring(BearerPrefix.Length).Trim();
            return token.Length == 0 ? null : token;
        }

        /// <summary>
        /// So sánh token theo thời gian hằng số. Dùng cách này thay vì <c>==</c> để không rò rỉ
        /// độ dài tiền tố trùng khớp qua thời gian phản hồi.
        /// </summary>
        public static bool TokensMatch(string? expected, string? actual)
        {
            if (expected == null || actual == null)
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(actual);

            // Token rỗng không bao giờ hợp lệ — và cũng không có byte nào để so, nên chặn trước
            // vòng lặp thay vì lấy dư chỉ số trên mảng rỗng.
            if (expectedBytes.Length == 0 || actualBytes.Length == 0)
            {
                return false;
            }

            var diff = expectedBytes.Length ^ actualBytes.Length;
            for (var i = 0; i < expectedBytes.Length; i++)
            {
                // Lấy dư chỉ số để số vòng lặp chỉ phụ thuộc độ dài token đúng, không phụ thuộc
                // token client gửi lên — giữ thời gian so sánh không rò rỉ độ dài.
                diff |= expectedBytes[i] ^ actualBytes[i % actualBytes.Length];
            }

            return diff == 0;
        }

        /// <summary>
        /// Kiểm tra một request có được phép chạy không: đúng token VÀ Content-Type là JSON.
        /// Ràng buộc Content-Type chặn dạng tấn công CSRF đơn giản từ trình duyệt (form post
        /// không đặt được Content-Type application/json nếu không qua CORS preflight).
        /// </summary>
        public static bool IsAuthorized(string? expectedToken, string? authorizationHeader, string? contentTypeHeader)
        {
            var token = ExtractBearerToken(authorizationHeader);
            if (!TokensMatch(expectedToken, token))
            {
                return false;
            }

            if (StringGuard.IsBlank(contentTypeHeader))
            {
                return false;
            }

            return contentTypeHeader.TrimStart().StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
