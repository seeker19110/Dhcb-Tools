using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public sealed class BridgePathAuditTests
{
    [Theory]
    [InlineData(@"\\.\pipe\export.csv")]
    [InlineData(@"\\?\UNC\server\share\export.csv")]
    [InlineData("//./pipe/export.csv")]
    [InlineData("//?/UNC/server/share/export.csv")]
    public void DeviceNamespacesCannotBypassAllowedExtension(string path)
    {
        Assert.NotNull(BridgePathPolicy.Problem(path));
        Assert.NotNull(BridgePathPolicy.FirstUnsafe(CommandCatalog.Find("revit", "ParameterExport")!,
            new JObject { ["outputPath"] = path }));
    }
}
