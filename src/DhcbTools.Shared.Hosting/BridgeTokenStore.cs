using System;
using System.IO;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Đọc/sinh token cho HTTP Bridge (mục 0.1). Token nằm ở <c>%APPDATA%\DHCB\bridge-token.txt</c>,
    /// sinh mới lần đầu bằng <see cref="BridgeAuth.GenerateToken"/>. Client (<c>scripts/dhcb_agent.py</c>)
    /// đọc cùng file, hoặc ghi đè bằng biến môi trường <c>DHCB_BRIDGE_TOKEN</c>.
    /// </summary>
    public static class BridgeTokenStore
    {
        public const string EnvironmentVariable = "DHCB_BRIDGE_TOKEN";

        public static string DefaultDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB");

        public static string DefaultPath => Path.Combine(DefaultDirectory, "bridge-token.txt");

        /// <summary>
        /// Lấy token đang hiệu lực: biến môi trường → file → sinh mới rồi ghi file.
        /// Ném <see cref="IOException"/> nếu không ghi được file (Bridge phải từ chối khởi động thay vì chạy không token).
        /// </summary>
        public static string LoadOrCreate(string? path = null)
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv.Trim();
            }

            var file = path ?? DefaultPath;
            if (File.Exists(file))
            {
                var existing = File.ReadAllText(file).Trim();
                if (existing.Length >= 32)
                {
                    return existing;
                }
            }

            var token = BridgeAuth.GenerateToken();
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(file, token);
            TryRestrictToOwner(file);
            return token;
        }

        /// <summary>
        /// Thu quyền file về chủ sở hữu (Windows: xoá kế thừa ACL, chỉ giữ user hiện tại). Bỏ qua im lặng trên
        /// nền tảng không hỗ trợ — thư mục %APPDATA% vốn đã là của riêng user.
        /// </summary>
        private static void TryRestrictToOwner(string file)
        {
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    return;
                }

                // Dùng icacls để không phải tham chiếu System.Security.AccessControl (không có trong netstandard2.0 đầy đủ).
                var user = Environment.UserDomainName + "\\" + Environment.UserName;
                var psi = new System.Diagnostics.ProcessStartInfo("icacls",
                    "\"" + file + "\" /inheritance:r /grant:r \"" + user + ":F\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    p?.WaitForExit(5000);
                }
            }
            catch
            {
                // không chặn khởi động vì lý do ACL
            }
        }
    }
}
