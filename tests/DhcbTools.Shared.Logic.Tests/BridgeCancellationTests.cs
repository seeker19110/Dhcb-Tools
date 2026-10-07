using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

[Collection(EnvironmentCollection.Name)]
public class BridgeCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownGiuaNhanHttpVaDangKyJob_KhongChoJobChayMuon(bool renewedSession)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var tokenPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        using var server = new HttpBridgeServer(port, "test", "test");
        BridgeWorkItem<BridgeRequest, CommandResult>? item = null;
        server.ExecuteAsync = work => { item = work; return Task.CompletedTask; };
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
            await http.GetStringAsync("/health");
            // Tiêm đúng thời điểm Stop đã hủy token nhưng chưa Stop listener; request đã được nhận vẫn trả lời.
            var ctsField = typeof(HttpBridgeServer)
                .GetField("_cts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var cts = (CancellationTokenSource)ctsField.GetValue(server)!;
            cts.Cancel();
            // Phiên mới không được hồi sinh request đã nhận ở phiên cũ.
            if (renewedSession)
            {
                ctsField.SetValue(server, new CancellationTokenSource());
                cts.Dispose(); // server.Dispose sẽ dọn nguồn token thay thế.
            }
            var response = await http.PostAsync("/execute", Json("{\"command\":\"AutoNumbering\",\"async\":true}"));
            var body = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("abandoned", body["status"]!.ToString());
            Assert.False(item!.TryClaim());
        }
        finally { File.Delete(tokenPath); }
    }

    [Fact]
    public void StopNgaySauDangKy_TruocOnClaimed_KhongThucThiHoacThongBaoClaim()
    {
        using var server = new HttpBridgeServer(12345, "test", "test");
        var item = new BridgeWorkItem<BridgeRequest, CommandResult>(new BridgeRequest { Command = "AutoNumbering" });
        var job = server.Jobs.TryAdd("AutoNumbering", DateTime.UtcNow, TimeSpan.FromMinutes(1),
            tryAbandonWork: item.MarkAbandoned)!;
        // Đúng cửa sổ trước đây: TryAdd đã công bố job, handler chưa gắn OnClaimed/Completion.
        // Hook hủy phải tồn tại ngay lúc công bố; nếu không, Stop chỉ hủy job còn item vẫn chạy.
        server.Stop();
        var notifications = 0;
        item.OnClaimed = () => { notifications++; job.MarkStarted(); };
        var writes = 0;
        if (item.TryClaim()) writes++;
        job.Complete("kết quả muộn", DateTime.UtcNow);
        Assert.Equal(BridgeJobStatus.Abandoned, job.Status);
        Assert.True(item.Abandoned);
        Assert.False(job.Started);
        Assert.Equal(0, notifications);
        Assert.Equal(0, writes);
        Assert.Null(job.Result);
    }

    [Theory]
    [InlineData("queued", 200, "abandoned", true)]
    [InlineData("started", 409, "running", false)]
    [InlineData("done", 200, "done", false)]
    [InlineData("error", 200, "error", false)]
    public async Task Cancel_ChiHuyKhiChuaChay(string initial, int code, string final, bool cancelled)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var tokenPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        using var server = new HttpBridgeServer(port, "test", "test");
        BridgeWorkItem<BridgeRequest, CommandResult>? item = null;
        server.ExecuteAsync = work => { item = work; return Task.CompletedTask; };
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
            var created = await http.PostAsync("/execute", Json("{\"command\":\"AutoNumbering\",\"async\":true}"));
            var id = JObject.Parse(await created.Content.ReadAsStringAsync())["id"]!.ToString();
            var job = server.Jobs.Find(id)!;
            if (initial != "queued") Assert.True(item!.TryClaim());
            if (initial == "done") job.Complete("kết quả", DateTime.UtcNow);
            if (initial == "error") job.Fail("lỗi", DateTime.UtcNow);

            var response = await http.PostAsync("/cancel/" + id, Json("{}"));
            Assert.Equal(code, (int)response.StatusCode);
            var body = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(final, body["status"]!.ToString());
            Assert.Equal(cancelled, body["cancelled"]!.Value<bool>());
            if (cancelled)
            {
                Assert.False(item!.TryClaim());
                var again = await http.PostAsync("/cancel/" + id, Json("{}"));
                Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            }
            if (initial == "done") Assert.Equal("kết quả", job.Result);
            var missing = await http.PostAsync("/cancel/khong-co", Json("{}"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sai");
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/cancel/" + id, Json("{}"))).StatusCode);
        }
        finally { File.Delete(tokenPath); }
    }

    [Fact]
    public void Stop_HuyHangDoi_GiuLenhDangChayVaKetQua()
    {
        using var server = new HttpBridgeServer(12345, "test", "test");
        var item = new BridgeWorkItem<string, string>("chờ");
        var queued = server.Jobs.Add("a", DateTime.UtcNow);
        queued.TryAbandonWork = item.MarkAbandoned;
        var running = server.Jobs.Add("b", DateTime.UtcNow); running.MarkStarted();
        var done = server.Jobs.Add("c", DateTime.UtcNow); done.Complete("xong", DateTime.UtcNow);
        server.Stop();
        Assert.Equal(BridgeJobStatus.Abandoned, queued.Status);
        Assert.False(item.TryClaim());
        Assert.Equal(BridgeJobStatus.Running, running.Status);
        Assert.Equal("xong", done.Result);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
