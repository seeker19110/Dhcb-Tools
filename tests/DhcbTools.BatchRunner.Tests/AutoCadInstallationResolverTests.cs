using DhcbTools.BatchRunner;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

public class AutoCadInstallationResolverTests
{
    private sealed class Files
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "dhcb-discovery-" + Guid.NewGuid().ToString("N"));
        public string ProgramFiles => Path.Combine(_root, "programs");
        public string AppData => Path.Combine(_root, "appdata");
        public string Runner => Path.Combine(_root, "runner");
        public Dictionary<string, string?> Content { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Version?> Api { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Host(int year, string? runtime = null)
        {
            var folder = Path.Combine(ProgramFiles, "Autodesk", "AutoCAD " + year);
            var path = Path.Combine(folder, "accoreconsole.exe");
            Content[path] = "";
            if (runtime is not null || year >= 2025)
                Content[Path.Combine(folder, "acdbmgd.runtimeconfig.json")] =
                    "{\"runtimeOptions\":{\"tfm\":\"" + (runtime ?? (year >= 2026 ? "net10.0" : "net8.0")) + "\"}}";
            return path;
        }
        public string Bundle(int year, bool core = true)
        {
            var path = Path.Combine(AppData, "Autodesk", "ApplicationPlugins", "DhcbTools.bundle", "Contents",
                year.ToString(), core ? "DhcbTools.AutoCAD.Core.dll" : "DhcbTools.AutoCAD.dll");
            Content[path] = Frame(year);
            Api[path] = ApiVersion(year);
            return path;
        }
        public string Portable(int year, bool core = true)
        {
            var path = Path.Combine(Runner, core ? "DhcbTools.AutoCAD.Core.dll" : "DhcbTools.AutoCAD.dll");
            Content[path] = Frame(year);
            Api[path] = ApiVersion(year);
            return path;
        }
        public AutoCadInstallationResolver.Resolution Resolve(string? console = null, string? plugin = null,
            Func<string, string?>? readFrame = null, Func<string, string>? readText = null) =>
            AutoCadInstallationResolver.Resolve(console, plugin, ProgramFiles, AppData, Runner,
                Content.ContainsKey, readText ?? (p => Content[p]!), readFrame ?? (p => Content[p]), p => Api.GetValueOrDefault(p));
        public static string Frame(int year) => year switch
        {
            2022 or 2023 or 2024 => ".NETFramework,Version=v4.8",
            2025 => ".NETCoreApp,Version=v8.0",
            _ => ".NETCoreApp,Version=v10.0",
        };
        public static Version ApiVersion(int year) => year switch
        {
            2022 => new Version(24, 1), 2023 => new Version(24, 2),
            2024 => new Version(24, 3), 2025 => new Version(25, 0),
            2026 => new Version(25, 1), _ => new Version(26, 0),
        };
    }

    [Theory]
    [InlineData(2022)]
    [InlineData(2023)]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void InstalledBundlePairsWithSupportedHost(int year)
    {
        var files = new Files(); var host = files.Host(year); var plugin = files.Bundle(year);
        var found = files.Resolve();
        Assert.True(found.Success, found.Error);
        Assert.Equal(host, found.ConsolePath);
        Assert.Equal(plugin, found.PluginPath);
        Assert.Null(found.Warning);
    }

    [Fact]
    public void UnsupportedOldInstallationIsIgnoredAndNewestCompatiblePairWins()
    {
        var files = new Files(); files.Host(2022); files.Host(2024); files.Bundle(2024);
        files.Host(2025); files.Bundle(2025); var newest = files.Host(2026); files.Bundle(2026);
        Assert.Equal(newest, files.Resolve().ConsolePath);
    }

    [Fact]
    public void NewestHostWithoutMatchingPluginFallsBackToCompleteOlderPair()
    {
        var files = new Files(); files.Host(2026); var older = files.Host(2025); var portable = files.Portable(2025);
        var found = files.Resolve();
        Assert.Equal(older, found.ConsolePath);
        Assert.Equal(portable, found.PluginPath);
    }

    [Fact]
    public void PortableAndCoreOnlyHavePriorityWithinMatchingHost()
    {
        var files = new Files(); files.Host(2026); files.Bundle(2026);
        files.Portable(2026, core: false); var core = files.Portable(2026);
        Assert.Equal(core, files.Resolve().PluginPath);
    }

    [Fact]
    public void WrongPortableRuntimeDoesNotHideCorrectInstalledPlugin()
    {
        var files = new Files(); files.Host(2026); files.Portable(2024); var correct = files.Bundle(2026);
        Assert.Equal(correct, files.Resolve().PluginPath);
    }

    [Fact]
    public void FullWrapperIsFallbackWhenCoreOnlyIsMissing()
    {
        var files = new Files(); files.Host(2024); var full = files.Bundle(2024, core: false);
        Assert.Equal(full, files.Resolve().PluginPath);
    }

    [Fact]
    public void ExplicitHostAndPluginOverrideNewestInstalledPair()
    {
        var files = new Files(); files.Host(2026); files.Bundle(2026);
        var host = files.Host(2024); var plugin = files.Portable(2024);
        var found = files.Resolve(host, plugin);
        Assert.Equal(host, found.ConsolePath);
        Assert.Equal(plugin, found.PluginPath);
    }

    [Fact]
    public void RelativeOverridesAreMadeAbsoluteForCoreConsole()
    {
        var files = new Files(); var host = files.Host(2024); var plugin = files.Portable(2024);
        var found = files.Resolve(Path.GetRelativePath(Environment.CurrentDirectory, host),
            Path.GetRelativePath(Environment.CurrentDirectory, plugin));
        Assert.Equal(host, found.ConsolePath);
        Assert.Equal(plugin, found.PluginPath);
    }

    [Fact]
    public void ExplicitPluginFindsItsMatchingHostAndNeverUsesAnotherDll()
    {
        var files = new Files(); files.Host(2026); files.Bundle(2026);
        var host = files.Host(2025); var plugin = files.Portable(2025);
        var found = files.Resolve(plugin: plugin);
        Assert.Equal(host, found.ConsolePath);
        Assert.Equal(plugin, found.PluginPath);
    }

    [Fact]
    public void ExplicitRuntimeMismatchFailsBeforeLaunchingHost()
    {
        var files = new Files(); var host = files.Host(2026); files.Bundle(2026);
        var plugin = files.Portable(2024);
        var found = files.Resolve(host, plugin);
        Assert.False(found.Success);
        Assert.Contains(".NETFramework,Version=v4.8", found.Error);
        Assert.Contains(".NETCoreApp,Version=v10.0", found.Error);
        Assert.Contains("--plugin-dll", found.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingExplicitPathsAreActionable(bool hostMissing)
    {
        var files = new Files(); var missing = Path.Combine(files.Runner, "missing");
        var found = hostMissing ? files.Resolve(console: missing) : files.Resolve(plugin: missing);
        Assert.False(found.Success);
        Assert.Contains(hostMissing ? "--accoreconsole" : "--plugin-dll", found.Error);
        Assert.Contains(missing, found.Error);
    }

    [Fact]
    public void MissingAutodeskDirectoryReturnsConfigurationErrorWithoutEnumeration()
    {
        var files = new Files(); var found = files.Resolve();
        Assert.False(found.Success);
        Assert.Contains("2022–2027", found.Error);
        Assert.Contains("--accoreconsole", found.Error);
    }

    [Fact]
    public void ExplicitUnsupportedHostIsRejected()
    {
        var files = new Files(); var found = files.Resolve(files.Host(2021), files.Portable(2024));
        Assert.False(found.Success);
        Assert.Contains("chưa được hỗ trợ", found.Error);
    }

    [Fact]
    public void AutoCad2026Net8DoesNotLoadNet10Plugin()
    {
        var files = new Files(); var rtm = files.Host(2026, "net8.0"); files.Bundle(2026);
        var older = files.Host(2025); files.Bundle(2025);
        Assert.Equal(older, files.Resolve().ConsolePath);
        Assert.Contains(".NETCoreApp,Version=v8.0", files.Resolve(rtm).Error);
    }

    [Theory]
    [InlineData(2025, "net8.0")]
    [InlineData(2025, "net10.0")]
    [InlineData(2026, "net8.0")]
    [InlineData(2026, "net10.0")]
    public void UpdateRuntimeSelectsMatchingInstalledDll(int year, string runtime)
    {
        var files = new Files(); var host = files.Host(year, runtime); var plugin = files.Bundle(year);
        files.Content[plugin] = runtime == "net8.0" ? ".NETCoreApp,Version=v8.0" : ".NETCoreApp,Version=v10.0";
        var found = files.Resolve();
        Assert.True(found.Success, found.Error);
        Assert.Equal(host, found.ConsolePath);
        Assert.Equal(plugin, found.PluginPath);
    }

    [Theory]
    [InlineData(2022, 2024)]
    [InlineData(2023, 2024)]
    [InlineData(2025, 2026)]
    [InlineData(2026, 2027)]
    public void SameRuntimeDoesNotMakeNewerAutodeskApiCompatible(int hostYear, int pluginYear)
    {
        var files = new Files(); var host = files.Host(hostYear); var plugin = files.Portable(pluginYear);
        files.Content[plugin] = Files.Frame(hostYear); // prove API check is independent of TFM
        var found = files.Resolve(host, plugin);
        Assert.False(found.Success);
        Assert.Contains("API AutoCAD", found.Error);
    }

    [Fact]
    public void SupportedOlderSdkCanLoadInNewerCompatibleHost()
    {
        var files = new Files(); var host = files.Host(2024); var plugin = files.Portable(2022);
        Assert.True(files.Resolve(host, plugin).Success);
    }

    [Fact]
    public void MissingRuntimeCannotClaimModernHostCompatibility()
    {
        var files = new Files(); var host = files.Host(2027); files.Bundle(2027);
        files.Content.Remove(Path.Combine(Path.GetDirectoryName(host)!, "acdbmgd.runtimeconfig.json"));
        Assert.Contains("Không xác định được runtime", files.Resolve(host).Error);
    }

    [Fact]
    public void CustomHostRuntimeStillRejectsMismatchedFramework()
    {
        var files = new Files(); var directory = Path.Combine(files.Runner, "custom-host");
        var host = Path.Combine(directory, "accoreconsole.exe"); files.Content[host] = "";
        files.Content[Path.Combine(directory, "acdbmgd.runtimeconfig.json")] = "{\"runtimeOptions\":{\"tfm\":\"net8.0\"}}";
        Assert.False(files.Resolve(host, files.Portable(2026)).Success);
    }

    [Fact]
    public void MalformedOrUnreadableRuntimeFallsBackWithoutCrashing()
    {
        var files = new Files(); files.Host(2026); files.Bundle(2026);
        var older = files.Host(2024); files.Bundle(2024);
        Assert.Equal(older, files.Resolve(readText: _ => "[").ConsolePath);
        Assert.Equal(older, files.Resolve(readText: _ => throw new UnauthorizedAccessException("no read")).ConsolePath);
    }

    [Fact]
    public void InvalidCoreDllFallsBackToValidFullWrapper()
    {
        var files = new Files(); files.Host(2026); var core = files.Bundle(2026); var full = files.Bundle(2026, false);
        var found = files.Resolve(readFrame: p => p == core ? throw new BadImageFormatException("junk") : files.Content[p]);
        Assert.Equal(full, found.PluginPath);
    }

    [Fact]
    public void HostWithoutPluginExplainsBundlePathAndOverrides()
    {
        var files = new Files(); files.Host(2026);
        var found = files.Resolve();
        Assert.False(found.Success);
        Assert.Contains(Path.Combine("DhcbTools.bundle", "Contents", "2026"), found.Error);
        Assert.Contains("--plugin-dll", found.Error);
    }

    [Fact]
    public void UnknownCustomRuntimeIsNotAcceptedAsCompatible()
    {
        var files = new Files(); var directory = Path.Combine(files.Runner, "custom-host");
        var custom = Path.Combine(directory, "accoreconsole.exe"); files.Content[custom] = "";
        files.Content[Path.Combine(directory, "acdbmgd.runtimeconfig.json")] = "{\"runtimeOptions\":{\"tfm\":\"net7.0\"}}";
        var found = files.Resolve(custom, files.Portable(2025));
        Assert.False(found.Success);
        Assert.Contains("net7.0", found.Error);
    }

    [Fact]
    public void CustomHostDirectoryCanUseExplicitPortableDll()
    {
        var files = new Files(); var custom = Path.Combine(files.Runner, "custom-host", "accoreconsole.exe");
        files.Content[custom] = "";
        var found = files.Resolve(custom, files.Portable(2024));
        Assert.True(found.Success, found.Error);
        Assert.NotNull(found.Warning);
    }

    [Fact]
    public void RuntimeCannotProveCustomHostYearOrSelectInstalledBundleByItself()
    {
        var files = new Files(); var directory = Path.Combine(files.Runner, "custom-host");
        var custom = Path.Combine(directory, "accoreconsole.exe"); files.Content[custom] = "";
        files.Content[Path.Combine(directory, "acdbmgd.runtimeconfig.json")] = "{\"runtimeOptions\":{\"tfm\":\"net8.0\"}}";
        var installed = files.Bundle(2025);
        Assert.False(files.Resolve(custom).Success);
        var found = files.Resolve(custom, installed);
        Assert.True(found.Success, found.Error);
        Assert.NotNull(found.Warning);
    }

    [Fact]
    public void MetadataReaderReadsFrameworkWithoutLoadingPluginDependencies()
    {
        Assert.Equal(".NETCoreApp,Version=v10.0", AutoCadInstallationResolver.ReadPluginFramework(typeof(Program).Assembly.Location));
    }

    [Fact]
    public void CliMissingHostReportsCode2InsteadOfThrowing()
    {
        using var cli = new Cli();
        var job = cli.Write("job.json", new JObject
        {
            ["name"] = "Discovery", ["app"] = "autocad", ["saveMode"] = "None",
            ["files"] = new JArray(new JObject { ["path"] = cli.Path_("fixture.dwg") }),
            ["steps"] = new JArray(new JObject { ["command"] = "LayerExport" }),
        }.ToString());
        var result = cli.Run("--job", job, "--log-dir", cli.Path_("logs"), "--accoreconsole", cli.Path_("missing.exe"));
        Assert.Equal(2, result.Code);
        Assert.Contains("--accoreconsole", result.Output);
    }
}
