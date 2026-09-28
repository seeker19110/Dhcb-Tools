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
    /// <para>
    /// Chặn được tới đâu: tiến trình chạy dưới TÀI KHOẢN KHÁC không đọc được file token
    /// (<see cref="T:DhcbTools.Shared.Hosting.BridgeTokenStore"/> thu ACL về chủ sở hữu), và cổng chỉ
    /// bind 127.0.0.1 nên không có đường từ máy khác. KHÔNG chặn được: mã chạy dưới CÙNG tài khoản
    /// người dùng — nó đọc được file token như chính add-in. Đó là giới hạn cố hữu của công cụ
    /// desktop giữ bí mật trên đĩa; muốn đóng nốt phải đổi sang cơ chế khác (ví dụ cấp handle qua
    /// IPC có kiểm tiến trình gọi), chưa làm. Đừng đọc dòng "lỗi #8" ở trên thành đã đóng hoàn toàn.
    /// </para>
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
        /// Request do trình duyệt gửi từ một trang web? Client hợp lệ của Bridge (script Python, MCP server,
        /// panel gateway, curl) không bao giờ gửi <c>Origin</c> hay <c>Sec-Fetch-Site</c>; trình duyệt gửi
        /// <c>Origin</c> với mọi POST và <c>Sec-Fetch-Site</c> với mọi request do trang web phát ra.
        /// <para>
        /// Vì sao phải chặn TRƯỚC bước kiểm token: trang web bất kỳ kỹ sư đang mở có thể bắn
        /// <c>fetch("http://127.0.0.1:8765/execute", {mode: "no-cors"})</c> 5 lần — không đọc được phản hồi,
        /// nhưng mỗi lần là một "sai token" → <see cref="T:DhcbTools.Shared.Hosting.AuthLockout"/> khoá Bridge
        /// 5 phút, lặp lại mãi chừng nào tab còn mở. Chặn ở đây thì không tính vào bộ đếm khoá.
        /// </para>
        /// <para>
        /// <c>Sec-Fetch-Site: none</c> là kỹ sư tự gõ URL vào thanh địa chỉ (ví dụ mở <c>/health</c> để xem
        /// Bridge còn sống) — không phải trang web nào khởi xướng, nên vẫn cho qua.
        /// </para>
        /// </summary>
        public static bool IsBrowserRequest(string? originHeader, string? secFetchSiteHeader)
        {
            if (!StringGuard.IsBlank(originHeader))
            {
                return true;
            }

            return !StringGuard.IsBlank(secFetchSiteHeader)
                   && !secFetchSiteHeader.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);
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
