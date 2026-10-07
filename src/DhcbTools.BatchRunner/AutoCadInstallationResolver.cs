using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace DhcbTools.BatchRunner;

/// <summary>Chooses a supported host together with a compatible installed or portable plugin.</summary>
public static class AutoCadInstallationResolver
{
    private static readonly int[] SupportedYears = { 2026, 2025, 2024 };
    private static readonly string[] PluginNames = { "DhcbTools.AutoCAD.Core.dll", "DhcbTools.AutoCAD.dll" };

    public sealed record Resolution(string? ConsolePath, string? PluginPath, string? Error, string? Warning = null)
    {
        public bool Success => Error is null && ConsolePath is not null && PluginPath is not null;
    }

    public static Resolution Resolve(string? consoleOverride, string? pluginOverride,
        string programFiles, string applicationData, string runnerDirectory,
        Func<string, bool>? fileExists = null, Func<string, string>? readText = null,
        Func<string, string?>? readPluginFramework = null)
    {
        fileExists ??= File.Exists;
        readText ??= File.ReadAllText;
        readPluginFramework ??= ReadPluginFramework;
        try
        {
            if (consoleOverride is not null && !fileExists(Path.GetFullPath(consoleOverride)))
                return Fail("Không tìm thấy host --accoreconsole: " + consoleOverride);
            if (pluginOverride is not null && !fileExists(Path.GetFullPath(pluginOverride)))
                return Fail("Không tìm thấy DLL --plugin-dll: " + pluginOverride);

            var consoles = consoleOverride is not null
                ? new[] { Path.GetFullPath(consoleOverride) }
                : SupportedYears.Select(year => Path.Combine(programFiles, "Autodesk", "AutoCAD " + year, "accoreconsole.exe"));
            var reasons = new List<string>();
            var foundHost = false;
            foreach (var console in consoles)
            {
                if (!fileExists(console)) continue;
                foundHost = true;
                var directory = Path.GetDirectoryName(console)!;
                var name = Path.GetFileName(directory);
                int? year = name.StartsWith("AutoCAD ", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(name.Substring(8), out var parsed) ? parsed : null;
                var knownYear = year.HasValue;
                if (year.HasValue && !SupportedYears.Contains(year.Value))
                {
                    reasons.Add("Host " + name + " chưa được hỗ trợ; chọn AutoCAD 2024, 2025 hoặc 2026.");
                    continue;
                }

                string? runtime = null;
                var runtimePath = Path.Combine(directory, "acdbmgd.runtimeconfig.json");
                if (fileExists(runtimePath))
                {
                    try
                    {
                        using var json = JsonDocument.Parse(readText(runtimePath));
                        if (json.RootElement.ValueKind == JsonValueKind.Object
                            && json.RootElement.TryGetProperty("runtimeOptions", out var options)
                            && options.ValueKind == JsonValueKind.Object
                            && options.TryGetProperty("tfm", out var target) && target.ValueKind == JsonValueKind.String)
                            runtime = target.GetString();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        reasons.Add("Không đọc được runtime " + runtimePath + ": " + ex.Message);
                        continue;
                    }
                }
                if (year == 2026 && runtime != "net10.0")
                {
                    reasons.Add("AutoCAD 2026 cần Update 1.2 trở lên dùng .NET 10; kiểm " + runtimePath + ".");
                    continue;
                }
                if ((year == 2025 && runtime is not null && runtime != "net8.0")
                    || (year == 2024 && runtime is not null && runtime != "net48"))
                {
                    reasons.Add("Runtime AutoCAD " + year + " không khớp phiên bản tại " + runtimePath + ".");
                    continue;
                }
                if (runtime is not null && runtime is not ("net8.0" or "net10.0" or "net48"))
                {
                    reasons.Add("Runtime " + runtime + " chưa được hỗ trợ tại " + runtimePath + ".");
                    continue;
                }
                year ??= runtime switch { "net10.0" => 2026, "net8.0" => 2025, _ => null };
                var plugins = pluginOverride is not null
                    ? new[] { Path.GetFullPath(pluginOverride) }
                    : PluginNames.Select(n => Path.Combine(runnerDirectory, n)).Concat(knownYear
                        ? PluginNames.Select(n => Path.Combine(applicationData, "Autodesk", "ApplicationPlugins",
                            "DhcbTools.bundle", "Contents", year.GetValueOrDefault().ToString(), n))
                        : Array.Empty<string>());
                foreach (var plugin in plugins)
                {
                    if (!fileExists(plugin)) continue;
                    string? framework;
                    try { framework = readPluginFramework(plugin); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
                    {
                        reasons.Add("Không đọc được DLL " + plugin + ": " + ex.Message);
                        continue;
                    }
                    if (framework is null)
                    {
                        reasons.Add("Không đọc được TargetFramework của DLL: " + plugin + ".");
                        continue;
                    }
                    var expected = year switch
                    {
                        2024 => ".NETFramework,Version=v4.8",
                        2025 => ".NETCoreApp,Version=v8.0",
                        2026 => ".NETCoreApp,Version=v10.0",
                        _ => runtime == "net48" ? ".NETFramework,Version=v4.8" : null,
                    };
                    if (expected is not null && framework != expected)
                    {
                        reasons.Add("DLL " + plugin + " dùng " + framework + ", host AutoCAD " + year + " cần " + expected + ".");
                        continue;
                    }
                    return new Resolution(Path.GetFullPath(console), Path.GetFullPath(plugin), null,
                        !knownYear ? "Không xác định được năm host tùy chọn từ thư mục; runtime chỉ kiểm .NET, cần xác nhận DLL phù hợp với phiên bản AutoCAD trước khi chạy job." : null);
                }
                reasons.Add("Không có DLL phù hợp cho " + console + "; tìm cạnh runner và trong "
                    + Path.Combine(applicationData, "Autodesk", "ApplicationPlugins", "DhcbTools.bundle", "Contents", year?.ToString() ?? "<năm>") + ".");
            }
            return Fail((foundHost ? string.Join(" ", reasons.Distinct())
                    : "Không tìm thấy AutoCAD 2024/2025/2026 trong " + Path.Combine(programFiles, "Autodesk") + ".")
                + " Cài component AutoCAD tương ứng của DHCB hoặc chỉ định --accoreconsole và --plugin-dll đúng phiên bản.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or JsonException or BadImageFormatException)
        {
            return Fail("Không đọc được host/plugin AutoCAD: " + ex.Message + " Kiểm --accoreconsole, --plugin-dll và quyền đọc file.");
        }
    }

    private static Resolution Fail(string message) => new(null, null, message);

    /// <summary>Reads assembly metadata without loading Autodesk or executing plugin code.</summary>
    public static string? ReadPluginFramework(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return null;
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly) return null;
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (metadata.GetString(type.Name) != "TargetFrameworkAttribute"
                || metadata.GetString(type.Namespace) != "System.Runtime.Versioning") continue;
            var value = metadata.GetBlobReader(attribute.Value);
            return value.ReadUInt16() == 1 ? value.ReadSerializedString() : null;
        }
        return null;
    }
}
