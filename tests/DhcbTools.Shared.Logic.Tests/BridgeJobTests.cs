using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giai đoạn 10.5 — lệnh chạy nền và <c>GET /progress/&lt;id&gt;</c>.
/// <para>
/// Vì sao có: Revit một luồng, lệnh nặng chạy hàng chục giây (đo được 26,6 s cho 1120 hanger ở vòng
/// ghi thật). Giữ một kết nối HTTP suốt thời gian đó là mong manh — ngắt giữa chừng là mất kết quả
/// của việc đã chạy xong. Có id để hỏi lại thì kết quả không đi theo kết nối.
/// </para>
/// </summary>
public class BridgeJobTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    // ── Sổ lệnh nền (thuần) ────────────────────────────────────────────────────

    [Fact]
    public void JobMoi_DangChay_VaTraVeIdRieng()
    {
        var store = new BridgeJobStore();
        var a = store.Add("HangerAuto", T0);
        var b = store.Add("HangerAuto", T0);

        Assert.Equal(BridgeJobStatus.Running, a.Status);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Same(a, store.Find(a.Id));
        Assert.Null(store.Find("khong-co-that"));
    }

    [Fact]
    public void JobXong_GiuKetQuaVaThoiGianChay()
    {
        var store = new BridgeJobStore();
        var job = store.Add("HangerAuto", T0);
        job.Complete(new { summary = "Đã đặt 1120 hanger" }, T0.AddSeconds(26));

        Assert.Equal(BridgeJobStatus.Done, job.Status);
        Assert.Equal(26000, job.ElapsedMs(T0.AddMinutes(5)));   // đã xong thì không đếm tiếp
        Assert.NotNull(job.Result);
    }

    [Fact]
    public void JobDangChay_DemThoiGianTiepTheoHienTai()
    {
        var job = new BridgeJobStore().Add("SleeveAuto", T0);
        Assert.Equal(5000, job.ElapsedMs(T0.AddSeconds(5)));
    }

    [Fact]
    public void JobLoi_GiuMoTaLoi()
    {
        var job = new BridgeJobStore().Add("SleeveAuto", T0);
        job.Fail("Lỗi thực thi: không tìm thấy family", T0.AddSeconds(1));

        Assert.Equal(BridgeJobStatus.Error, job.Status);
        Assert.Contains("family", job.Error);
    }

    [Fact]
    public void Prune_BoJobDaXongQuaHan_GiuJobDangChay()
    {
        var store = new BridgeJobStore { MaxAge = TimeSpan.FromMinutes(30) };
        var cu = store.Add("A", T0);
        cu.Complete(new { }, T0);
        var dangChay = store.Add("B", T0);

        store.Prune(T0.AddMinutes(31));

        Assert.Null(store.Find(cu.Id));
        Assert.Same(dangChay, store.Find(dangChay.Id));
    }

    [Fact]
    public void Prune_VuotSoLuong_BoCaiXongLauNhatTruoc()
    {
        var store = new BridgeJobStore { MaxCount = 2, MaxAge = TimeSpan.FromDays(1) };
        var a = store.Add("A", T0);
        a.Complete(new { }, T0.AddSeconds(1));
        var b = store.Add("B", T0);
        b.Complete(new { }, T0.AddSeconds(2));
        var c = store.Add("C", T0);
        c.Complete(new { }, T0.AddSeconds(3));

        store.Prune(T0.AddSeconds(4));

        Assert.Null(store.Find(a.Id));      // xong lâu nhất → bỏ trước
        Assert.NotNull(store.Find(b.Id));
        Assert.NotNull(store.Find(c.Id));
    }

    [Fact]
    public void Prune_KhongBaoGioBoJobDangChay_DuVuotSoLuong()
    {
        var store = new BridgeJobStore { MaxCount = 1, MaxAge = TimeSpan.FromDays(1) };
        var x = store.Add("A", T0);
        var y = store.Add("B", T0);

        store.Prune(T0.AddHours(2));

        Assert.NotNull(store.Find(x.Id));
        Assert.NotNull(store.Find(y.Id));
    }

    // ── Hạn nhận việc, hàng đợi có trần, work item ba trạng thái ──────────────

    [Fact]
    public void JobChuaAiNhan_QuaHan_BiAbandoned_VaKhongBaoGioChay()
    {
        var store = new BridgeJobStore();
        var item = new BridgeWorkItem<BridgeRequest, CommandResult>(new BridgeRequest { Command = "SleeveAuto" });
        var job = store.Add("SleeveAuto", T0, timeout: TimeSpan.FromSeconds(30));
        job.TryAbandonWork = item.MarkAbandoned;
        item.OnClaimed = job.MarkStarted;

        Assert.Equal(0, store.ExpireQueued(T0.AddSeconds(29)));
        Assert.Equal(1, store.ExpireQueued(T0.AddSeconds(30)));

        Assert.Equal(BridgeJobStatus.Abandoned, job.Status);
        Assert.Contains("KHÔNG chạy", job.Error);
        Assert.False(item.TryClaim());          // luồng UI tới muộn → không được chạy
        Assert.True(item.Abandoned);
    }

    [Fact]
    public void JobDaDuocNhan_QuaHanCungKhongHuy()
    {
        var store = new BridgeJobStore();
        var item = new BridgeWorkItem<BridgeRequest, CommandResult>(new BridgeRequest { Command = "SleeveAuto" });
        var job = store.Add("SleeveAuto", T0, timeout: TimeSpan.FromSeconds(30));
        job.TryAbandonWork = item.MarkAbandoned;
        item.OnClaimed = job.MarkStarted;

        Assert.True(item.TryClaim());
        Assert.True(job.Started);
        Assert.Equal(0, store.ExpireQueued(T0.AddMinutes(5)));
        Assert.Equal(BridgeJobStatus.Running, job.Status);
        Assert.False(item.MarkAbandoned());     // đã nhận rồi thì không huỷ được
        Assert.True(item.Claimed);
    }

    [Fact]
    public void WorkItem_MarkAbandonedTruoc_TryClaimSauThatBai()
    {
        var item = new BridgeWorkItem<BridgeQuery, object>(new BridgeQuery { Query = "x" });
        Assert.True(item.MarkAbandoned());
        Assert.False(item.TryClaim());
        Assert.False(item.Claimed);
    }

    [Fact]
    public void HangDoiDay_TryAddTraNull_JobQuaHanKhongChiemCho()
    {
        var store = new BridgeJobStore { MaxQueued = 2 };
        Assert.NotNull(store.TryAdd("A", T0, TimeSpan.FromSeconds(10)));
        Assert.NotNull(store.TryAdd("B", T0, TimeSpan.FromSeconds(10)));
        Assert.Null(store.TryAdd("C", T0, TimeSpan.FromSeconds(10)));
        Assert.Equal(2, store.QueuedCount);

        // 10 giây sau: A, B quá hạn → huỷ → còn chỗ.
        var c = store.TryAdd("C", T0.AddSeconds(10), TimeSpan.FromSeconds(10));
        Assert.NotNull(c);
        Assert.Equal(1, store.QueuedCount);
    }

    [Fact]
    public void JobDangChay_KhongTinhVaoHangDoi()
    {
        var store = new BridgeJobStore { MaxQueued = 1 };
        var a = store.TryAdd("A", T0, TimeSpan.FromSeconds(10))!;
        a.MarkStarted();
        Assert.Equal(0, store.QueuedCount);
        Assert.NotNull(store.TryAdd("B", T0, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Prune_BoCaJobAbandoned_KhiVuotSoLuong()
    {
        var store = new BridgeJobStore { MaxCount = 1, MaxAge = TimeSpan.FromDays(1) };
        var a = store.Add("A", T0, timeout: TimeSpan.FromSeconds(1));
        var b = store.Add("B", T0);
        b.Complete(new { }, T0.AddSeconds(5));
        store.ExpireQueued(T0.AddSeconds(2));   // a → Abandoned lúc giây 2
        Assert.Equal(BridgeJobStatus.Abandoned, a.Status);

        store.Prune(T0.AddSeconds(6));

        Assert.Null(store.Find(a.Id));          // xong (huỷ) sớm hơn → bỏ trước
        Assert.NotNull(store.Find(b.Id));
    }

    // ── Đường HTTP thật ────────────────────────────────────────────────────────

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task ExecuteAsync_TraVe202VaProgressDoiSangDone()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9");

        // Lệnh "nặng": chỉ hoàn thành khi test cho phép — đúng hình dạng của lệnh chạy hàng chục giây.
        var chophep = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ExecuteAsync = async item =>
        {
            await chophep.Task.ConfigureAwait(false);
            item.Completion.SetResult(CommandResult.Ok("Đã đặt 1120 hanger.", 1120));
        };

        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var post = await http.PostAsync("http://127.0.0.1:" + port + "/execute",
                new StringContent("{\"command\":\"HangerAuto\",\"async\":true}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
            var accepted = JObject.Parse(await post.Content.ReadAsStringAsync());
            var id = (string?)accepted["id"];
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.Equal("running", (string?)accepted["status"]);

            var running = JObject.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/progress/" + id));
            Assert.Equal("running", (string?)running["status"]);

            chophep.SetResult(true);

            JObject done;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                done = JObject.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/progress/" + id));
            }
            while ((string?)done["status"] == "running" && DateTime.UtcNow < deadline);

            Assert.Equal("done", (string?)done["status"]);
            Assert.Equal(1120, (int?)done["result"]!["affectedCount"]);
            Assert.Contains("1120 hanger", (string?)done["result"]!["summary"]);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    /// <summary>
    /// Lệnh đồng bộ hết giờ SAU KHI luồng UI đã nhận: 504 phải nói "có thể đã chạy, KHÔNG gửi lại" và kèm
    /// id để hỏi /progress — nơi kết quả thật về sau khi lệnh chạy xong. Bản cũ nói "không chạy" trong
    /// khi lệnh vẫn chạy nốt: client nghe lời gửi lại → chạy hai lần trên model thật.
    /// </summary>
    [Fact]
    public async Task ExecuteDongBo_HetGioSauKhiDaNhan_TraIdDeHoiTiep_VaKhongNoiKhongChay()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9") { Timeout = TimeSpan.FromMilliseconds(300) };

        var chophep = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ExecuteAsync = item =>
        {
            Assert.True(item.TryClaim());       // luồng UI nhận ngay, rồi chạy lâu
            _ = Task.Run(async () =>
            {
                await chophep.Task.ConfigureAwait(false);
                item.Completion.SetResult(CommandResult.Ok("Đã đặt 7 sleeve.", 7));
            });
            return Task.CompletedTask;
        };

        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var post = await http.PostAsync("http://127.0.0.1:" + port + "/execute",
                new StringContent("{\"command\":\"SleeveAuto\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.GatewayTimeout, post.StatusCode);
            var body = JObject.Parse(await post.Content.ReadAsStringAsync());
            var id = (string?)body["id"];
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.Equal("/progress/" + id, (string?)body["progressUrl"]);
            var error = (string?)body["summary"] ?? string.Empty;
            Assert.Contains("KHÔNG gửi lại", error);
            Assert.DoesNotContain("không chạy", error);

            chophep.SetResult(true);
            JObject done;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                done = JObject.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/progress/" + id));
            }
            while ((string?)done["status"] == "running" && DateTime.UtcNow < deadline);

            Assert.Equal("done", (string?)done["status"]);
            Assert.Equal(7, (int?)done["result"]!["affectedCount"]);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task ExecuteDongBo_HetGioTruocKhiAiNhan_NoiKhongChay_KhongCoId()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9") { Timeout = TimeSpan.FromMilliseconds(200) };
        server.ExecuteAsync = _ => Task.CompletedTask;   // không ai nhận việc

        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var post = await http.PostAsync("http://127.0.0.1:" + port + "/execute",
                new StringContent("{\"command\":\"SleeveAuto\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.GatewayTimeout, post.StatusCode);
            var body = JObject.Parse(await post.Content.ReadAsStringAsync());
            Assert.Null(body["id"]);
            Assert.Contains("không chạy", (string?)body["summary"]);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_JobQuaHanChuaAiNhan_ProgressBaoAbandoned()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9") { Timeout = TimeSpan.FromMilliseconds(200) };
        BridgeWorkItem<BridgeRequest, CommandResult>? captured = null;
        server.ExecuteAsync = item => { captured = item; return Task.CompletedTask; };

        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var post = await http.PostAsync("http://127.0.0.1:" + port + "/execute",
                new StringContent("{\"command\":\"HangerAuto\",\"async\":true}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
            var id = (string?)JObject.Parse(await post.Content.ReadAsStringAsync())["id"];

            JObject progress;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                await Task.Delay(50);
                progress = JObject.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/progress/" + id));
            }
            while ((string?)progress["status"] == "running" && DateTime.UtcNow < deadline);

            Assert.Equal("abandoned", (string?)progress["status"]);
            Assert.NotNull(captured);
            Assert.False(captured!.TryClaim());  // luồng UI tới muộn → không chạy
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_HangDoiDay_TraVe429()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9");
        server.Jobs.MaxQueued = 1;
        server.ExecuteAsync = _ => Task.CompletedTask;

        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
            var body = "{\"command\":\"HangerAuto\",\"async\":true,\"timeoutSeconds\":60}";

            var first = await http.PostAsync("http://127.0.0.1:" + port + "/execute", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

            var second = await http.PostAsync("http://127.0.0.1:" + port + "/execute", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal((HttpStatusCode)429, second.StatusCode);
            Assert.Contains("Hàng đợi đầy", await second.Content.ReadAsStringAsync());
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task SaiContentType_TraVe415_VaKhongKhoa()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9");
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            for (var i = 0; i < 6; i++)
            {
                var res = await http.PostAsync("http://127.0.0.1:" + port + "/execute", new StringContent("{}", Encoding.UTF8, "text/plain"));
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
                Assert.Contains("application/json", await res.Content.ReadAsStringAsync());
            }

            // Sau 6 lần sai Content-Type vẫn KHÔNG bị khoá — token đúng vẫn vào được.
            var ok = await http.GetAsync("http://127.0.0.1:" + port + "/tools");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task BodyQuaLon_TraVe413()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9") { MaxBodyBytes = 1024 };
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var big = "{\"command\":\"X\",\"config\":{\"pad\":\"" + new string('a', 2048) + "\"}}";
            var res = await http.PostAsync("http://127.0.0.1:" + port + "/execute", new StringContent(big, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task QuaTai_TraVe503_HealthVanTraLoi()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9", maxInFlight: 1);
        var giu = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.QueryAsync = async item =>
        {
            await giu.Task.ConfigureAwait(false);
            item.Completion.SetResult(new { ok = true });
        };

        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var busy = http.PostAsync("http://127.0.0.1:" + port + "/query", new StringContent("{\"query\":\"x\"}", Encoding.UTF8, "application/json"));
            await Task.Delay(200);
            var overflow = await http.GetAsync("http://127.0.0.1:" + port + "/tools");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, overflow.StatusCode);
            Assert.Contains("quá tải", await overflow.Content.ReadAsStringAsync());

            var health = await http.GetAsync("http://127.0.0.1:" + port + "/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            giu.SetResult(true);
            Assert.Equal(HttpStatusCode.OK, (await busy).StatusCode);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task Chat_QuaGio_TraVe504()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9") { ChatTimeout = TimeSpan.FromMilliseconds(200) };
        server.Chat = _ => { Thread.Sleep(3000); return new { }; };
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var res = await http.PostAsync("http://127.0.0.1:" + port + "/chat", new StringContent("{\"text\":\"đặt sleeve\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.GatewayTimeout, res.StatusCode);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task Loi500_KhongLoStackTrace_CoTenException_VaGhiLog()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        var logged = new List<string>();
        using var server = new HttpBridgeServer(port, "TestA", "9.9") { Log = logged.Add };
        server.ListTools = () => throw new InvalidOperationException(@"bí mật C:\Users\ai-do\file.rvt");
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var res = await http.GetAsync("http://127.0.0.1:" + port + "/tools");
            Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("ai-do", body);
            Assert.DoesNotContain("   at ", body);
            Assert.Contains("InvalidOperationException", body);
            Assert.Contains(logged, l => l.Contains("ai-do") && l.Contains("500"));
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task Progress_IdKhongCoThat_TraVe404()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9");
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            var res = await http.GetAsync("http://127.0.0.1:" + port + "/progress/khong-co-that");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task Progress_KhongCoToken_TraVe401()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9");
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            var res = await http.GetAsync("http://127.0.0.1:" + port + "/progress/bat-ky");
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }
}
