using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public sealed class LocalAiAuditTests
{
    [Fact]
    public void OversizedIntentDoesNotRunRegexOrProduceExecutableProposal()
    {
        var text = "AutoNumbering " + new string('a', 100_000);
        var intent = CommandIntentParser.Parse(text, "revit");
        Assert.Null(intent.Command);
        Assert.Contains("4096", intent.Explanation);
        Assert.Empty(CommandIntentParser.ExtractLengthsMm(text));
        Assert.NotEmpty(CommandIntentParser.Candidates(text, "revit"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void IntentNumbersMustBeFinite(string text) => Assert.False(CommandIntentParser.TryParseNumber(text, out _));

    [Theory]
    [InlineData("file:///C:/private/")]
    [InlineData("ftp://127.0.0.1:11434")]
    public void LocalEndpointRequiresHttpProtocol(string endpoint)
    {
        var settings = new LocalAiSettings { Enabled = true, Endpoint = endpoint };
        var calls = 0;
        var client = new OllamaClient(settings, (_, _, _) => { calls++; return "{\"response\":\"unsafe\"}"; });
        Assert.False(settings.IsLoopback());
        Assert.False(client.IsUsable);
        Assert.Null(client.Generate("model content"));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(-1, 5000)]
    [InlineData(30, 30000)]
    [InlineData(int.MaxValue, 600000)]
    public void TimeoutIsBoundedAndDoesNotOverflow(int configured, int expected)
    {
        var request = OllamaClient.CreateRequest("http://127.0.0.1:11434/api/generate", configured);
        Assert.Equal(expected, request.Timeout);
        Assert.Equal(expected, request.ReadWriteTimeout);
    }
}
