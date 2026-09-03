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
