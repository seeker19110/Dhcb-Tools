using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

[Collection(EnvironmentCollection.Name)]
public sealed class BridgeReliabilityAuditTests : IDisposable
{
    private readonly string _tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-bridge-audit-" + Guid.NewGuid() + ".txt");
    private readonly HttpBridgeServer _server;
    private readonly HttpClient _client;

    public BridgeReliabilityAuditTests()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        _server = new HttpBridgeServer(port, "audit", "test") { Timeout = TimeSpan.FromMilliseconds(500) };
        _server.Start(_tokenPath);
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _server.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultAfterAwait_ReturnsFailureAndNeverLeavesRunningJob(bool claimed)
    {
        BridgeWorkItem<BridgeRequest, CommandResult>? queued = null;
        _server.ExecuteAsync = async item =>
        {
            queued = item;
            if (claimed) item.TryClaim();
            await Task.Delay(10);
            throw new InvalidOperationException("dispatch fault after await");
        };
        var response = await _client.PostAsync("/execute", Json("{\"command\":\"AutoNumbering\"}"));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("dispatch fault after await", body["summary"]!.ToString());
        if (claimed) Assert.Contains("KHÔNG gửi lại", body["summary"]!.ToString());
        else Assert.False(queued!.TryClaim());
        Assert.Equal(0, _server.Jobs.Count);
    }

    [Fact]
    public async Task FaultAfterCompletion_PreservesCommittedResult()
    {
        _server.ExecuteAsync = async item =>
        {
            item.TryClaim();
            item.Completion.SetResult(CommandResult.Ok("committed", 1));
            await Task.Yield();
            throw new InvalidOperationException("cleanup fault");
        };
        var response = await _client.PostAsync("/execute", Json("{\"command\":\"AutoNumbering\"}"));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("committed", body["summary"]!.ToString());
    }

    [Fact]
    public async Task CancelledDispatch_DoesNotLeaveClaimableQueuedItem()
    {
        BridgeWorkItem<BridgeRequest, CommandResult>? queued = null;
        _server.ExecuteAsync = item => { queued = item; return Task.FromCanceled(new CancellationToken(true)); };
        var response = await _client.PostAsync("/execute", Json("{\"command\":\"AutoNumbering\"}"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(queued!.TryClaim());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AsyncDispatchFaultOrCancel_MarksErrorAndPreventsLateClaim(bool claimed, bool cancelled)
    {
        BridgeWorkItem<BridgeRequest, CommandResult>? queued = null;
        _server.ExecuteAsync = item =>
        {
            queued = item;
            if (claimed) item.TryClaim();
            return cancelled ? Task.FromCanceled(new CancellationToken(true))
                : Task.FromException(new InvalidOperationException("dispatch fault"));
        };
        var response = await _client.PostAsync("/execute", Json("{\"command\":\"AutoNumbering\",\"async\":true}"));
        var id = JObject.Parse(await response.Content.ReadAsStringAsync())["id"]!.ToString();
        var job = _server.Jobs.Find(id)!;
        for (var attempt = 0; attempt < 100 && job.Status == BridgeJobStatus.Running; attempt++) await Task.Delay(10);
        Assert.Equal(BridgeJobStatus.Error, job.Status);
        Assert.False(queued!.TryClaim());
    }

    [Theory]
    [InlineData("execute")]
    [InlineData("query")]
    public async Task Stop_AbandonsSynchronousQueueBeforeHostCanClaim(string endpoint)
    {
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<bool>? claim = null;
        _server.ExecuteAsync = item => { claim = item.TryClaim; received.SetResult(true); return Task.CompletedTask; };
        _server.QueryAsync = item => { claim = item.TryClaim; received.SetResult(true); return Task.CompletedTask; };
        var pending = _client.PostAsync("/" + endpoint, Json(endpoint == "execute"
            ? "{\"command\":\"AutoNumbering\"}" : "{\"query\":\"document_info\"}"));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        _server.Stop();
        Assert.False(claim!());
        try { using var response = await pending; } catch (HttpRequestException) { /* Stop closes HTTP. */ }
    }

    [Theory]
    [InlineData("application/json-patch+json")]
    [InlineData("application/jsonjunk")]
    [InlineData("application/json, text/plain")]
    public void ContentType_MustMatchMediaTypeExactly(string contentType) =>
        Assert.False(HttpBridgeServer.IsJsonContentType(contentType));

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    public void Dispose()
    {
        _client.Dispose(); _server.Dispose(); File.Delete(_tokenPath);
    }
}
