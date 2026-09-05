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
        /// <para>
        /// Ghi qua file tạm rồi đổi tên vào chỗ: file token không bao giờ tồn tại ở trạng thái ghi dở, và
        /// ACL được thu về chủ sở hữu TRƯỚC khi file mang tên thật — không có khoảnh khắc nào file token
        /// nằm đúng chỗ với quyền kế thừa rộng. Thu ACL hỏng chỉ ghi cảnh báo (<paramref name="log"/>),
        /// không chặn khởi động: %APPDATA% vốn đã là thư mục riêng của user.
        /// </para>
        /// </summary>
        /// <param name="restrictToOwner">
        /// Bước thu ACL, tiêm được để test đường cảnh báo mà không cần một máy Windows có ACL hỏng thật;
        /// null = <see cref="TryRestrictToOwner"/>.
        /// </param>
        public static string LoadOrCreate(string? path = null, Action<string>? log = null, Func<string, bool>? restrictToOwner = null)
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

            var temp = file + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                File.WriteAllText(temp, token);
                if (!(restrictToOwner ?? TryRestrictToOwner)(temp))
                {
                    log?.Invoke("[DHCB Bridge] CẢNH BÁO: không thu được quyền file token về chủ sở hữu (" + file
                                + ") — file vẫn dùng được, quyền theo thư mục cha.");
                }

                if (File.Exists(file))
                {
                    File.Delete(file);
                }

                File.Move(temp, file);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* dọn file tạm, không quan trọng */ }
            }

            return token;
        }

        /// <summary>
        /// Thu quyền file về chủ sở hữu (Windows: xoá kế thừa ACL, chỉ giữ user hiện tại). Trả <c>true</c>
        /// khi đã thu xong hoặc nền tảng không cần (không phải Windows); <c>false</c> khi icacls lỗi.
        /// </summary>
        /// <remarks>
        /// Chỉ làm việc thật trên Windows (icacls), nên CI Linux không chạy qua được — loại khỏi phép đo
        /// phủ thay vì để một nhánh không đo được kéo cả cổng coverage xuống. Đường cảnh báo "thu ACL hỏng"
        /// vẫn có test, qua tham số <c>restrictToOwner</c> của <see cref="LoadOrCreate"/>.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        private static bool TryRestrictToOwner(string file)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return true;
            }

            try
            {
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
                    if (p == null)
                    {
                        return false;
                    }

                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { /* đang thoát */ }
                        return false;
                    }

                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
