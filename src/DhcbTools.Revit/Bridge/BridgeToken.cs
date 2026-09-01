using System.Security.Cryptography;

namespace DhcbTools.Revit.Bridge;

/// <summary>
/// Token dùng chung cho HTTP Bridge (lỗi #8). Sinh ngẫu nhiên lần đầu, lưu ở
/// <c>%APPDATA%\DhcbTools\bridge-token.txt</c> để client (ví dụ <c>scripts/dhcb_agent.py</c>)
/// đọc lại. Mọi request tới <c>/execute</c> và <c>/query</c> phải kèm header
/// <c>Authorization: Bearer &lt;token&gt;</c>; riêng <c>GET /health</c> không cần.
/// </summary>
internal static class BridgeToken
{
    public static string TokenFilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DhcbTools",
        "bridge-token.txt");

    /// <summary>Đọc token đã lưu, sinh mới nếu chưa có hoặc file rỗng/hỏng.</summary>
    public static string LoadOrCreate()
    {
        var path = TokenFilePath;
        try
        {
            if (System.IO.File.Exists(path))
            {
                var existing = System.IO.File.ReadAllText(path).Trim();
                if (existing.Length >= 32)
                {
                    return existing;
                }
            }
        }
        catch (System.IO.IOException)
        {
            // Không đọc được thì sinh mới bên dưới.
        }

        var token = Generate();
        var directory = System.IO.Path.GetDirectoryName(path)!;
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(path, token);
        return token;
    }

    private static string Generate()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    /// <summary>So sánh token theo thời gian cố định để không lộ thông tin qua thời gian phản hồi.</summary>
    public static bool Matches(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided) || provided!.Length != expected.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            diff |= expected[i] ^ provided[i];
        }
        return diff == 0;
    }
}
