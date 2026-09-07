using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class BridgeDocumentContextTests
{
    [Fact]
    public void Reopened_document_has_different_identity()
    {
        var doc = new object();
        Assert.Equal(BridgeDocumentContext.IdFor(doc), BridgeDocumentContext.IdFor(doc));
        Assert.NotEqual(BridgeDocumentContext.IdFor(doc), BridgeDocumentContext.IdFor(new object()));
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{dryRun:false}", false)]
    [InlineData("{dryRun:'true'}", false)]
    [InlineData("{dryRun:true}", true)]
    public void Missing_target_only_permits_real_boolean_preview(string json, bool allowed)
    {
        var request = new BridgeRequest { Command = "AutoNumbering", Config = JObject.Parse(json) };
        Assert.Equal(allowed, BridgeDocumentContext.Validate("revit", request, "A") == null);
    }

    [Fact]
    public void Missing_config_and_unknown_command_fail_closed()
    {
        Assert.Contains("REQUIRED", BridgeDocumentContext.Validate("revit", new BridgeRequest { Command = "Unknown" }, "A"));
        Assert.Null(BridgeDocumentContext.Validate("revit", new BridgeRequest { Command = "HealthReport" }, "A"));
    }

    [Fact]
    public void Switched_target_is_rejected_even_for_preview()
    {
        var request = new BridgeRequest { Command = "AutoNumbering", DocumentId = "A", Config = JObject.Parse("{dryRun:true}") };
        Assert.Null(BridgeDocumentContext.Validate("revit", request, "A"));
        Assert.Contains("CHANGED", BridgeDocumentContext.Validate("revit", request, "B"));
    }
}
