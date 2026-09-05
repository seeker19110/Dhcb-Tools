using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Đường HTTP thật của Bridge: mọi mã lỗi mà client có thể gặp (401/429/405/413/415/404/501/500/504)
/// và đường lệnh chạy nền. Chạy trên loopback với listener thật — cùng thứ chạy trong Revit/AutoCAD.
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class HttpBridgeServerGapTests : IDisposable
{
    private const string Token = "token-test-du-dai-32-ky-tu-tro-len-nhe";

    private readonly string _tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-http-gap-" + Guid.NewGuid().ToString("N") + ".txt");
    private readonly HttpBridgeServer _server;
    private readonly HttpClient _client;

    public HttpBridgeServerGapTests()
    {
        File.WriteAllText(_tokenPath, Token);
        var port = FreePort();
        _server = new HttpBridgeServer(port, "TestBridge", "9.9")
        {
            Timeout = TimeSpan.FromMilliseconds(300),
            ChatTimeout = TimeSpan.FromMilliseconds(300),
        };
        _server.Start(_tokenPath);
        _client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        _server.Dispose(); // gọi hai lần vẫn phải im lặng
        try { File.Delete(_tokenPath); } catch { /* dọn dẹp */ }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static StringContent Json(string body) =>
        new StringContent(body, Encoding.UTF8, "application/json");

    private async Task<(HttpStatusCode Status, JObject Body)> PostAsync(string path, string body)
    {
        var response = await _client.PostAsync(path, Json(body));
        return (response.StatusCode, JObject.Parse(await response.Content.ReadAsStringAsync()));
    }

    // ── Xác thực và giới hạn ────────────────────────────────────────────────

    /// <summary>Sai token nhiều lần liên tiếp: khoá lại và trả 429 cho cả request có token đúng.</summary>
    [Fact]
    public async Task SaiTokenNhieuLan_Khoa429()
    {
        using var xau = new HttpClient { BaseAddress = _client.BaseAddress };
        xau.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sai-token");

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await xau.GetAsync("/tools")).StatusCode);
        }

        var response = await _client.GetAsync("/tools");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("locked", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PhuongThucKhongHoTro_405NoiRoEndpointNao()
    {
        var response = await _client.PutAsync("/execute", Json("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains("POST /execute", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaiContentType_415NoiRoDangGuiGi()
    {
        var response = await _client.PostAsync("/execute", new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Contains("text/plain", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Khai Content-Length lớn hơn trần: chặn ngay, không đọc body.</summary>
    [Fact]
    public async Task BodyKhaiQuaLon_413()
    {
        _server.MaxBodyBytes = 64;

        var response = await _client.PostAsync("/execute", Json(new string('a', 4096)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("Body quá lớn", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Không khai Content-Length (chunked) mà body vẫn quá trần: chặn lúc đọc.</summary>
    [Fact]
    public async Task BodyChunkedQuaLon_413()
    {
        _server.MaxBodyBytes = 64;
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 100_000))));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/execute") { Content = content };
        request.Headers.TransferEncodingChunked = true;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task EndpointKhongTonTai_404NoiRoDuongDan()
    {
        var (status, body) = await PostAsync("/khong-co", "{}");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("/khong-co", (string?)body["error"]);
    }

    /// <summary>Lỗi nội bộ: client chỉ nhận tên loại exception, KHÔNG nhận stack trace/đường dẫn file.</summary>
    [Fact]
    public async Task LoiNoiBo_500KhongLoStackTrace()
    {
        var logs = new List<string>();
        _server.Log = logs.Add;
        _server.ListTools = () => throw new InvalidOperationException("bí mật ở đây");

        var response = await _client.GetAsync("/tools");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("InvalidOperationException", (string?)body["exceptionType"]);
        Assert.DoesNotContain("bí mật ở đây", (string?)body["error"]);
        Assert.Contains(logs, l => l.Contains("bí mật ở đây"));
    }

    // ── POST /execute ───────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_JsonHong_400()
    {
        var (status, body) = await PostAsync("/execute", "{khong-phai-json");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("JSON không hợp lệ", (string?)body["error"]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"command\":\"  \"}")]
    public async Task Execute_ThieuCommand_400(string body)
    {
        var (status, json) = await PostAsync("/execute", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Thiếu trường 'command'.", (string?)json["error"]);
    }

    [Fact]
    public async Task Execute_VoChuaGanExecuteAsync_501()
    {
        var (status, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\"}");

        Assert.Equal(HttpStatusCode.NotImplemented, status);
        Assert.Contains("ExecuteAsync", (string?)body["error"]);
    }

    [Fact]
    public async Task Execute_LenhChayXong_TraKetQua()
    {
        _server.ExecuteAsync = item =>
        {
            item.TryClaim();
            item.Completion.SetResult(CommandResult.Ok("xong", affected: 3).WithChanged(7));
            return Task.CompletedTask;
        };

        var (status, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\"}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, (int?)body["affectedCount"]);
        Assert.Equal(3, (int?)body["affectedElementCount"]);
    }

    /// <summary>Lệnh ném ngay lúc dispatch: thành CommandResult.Fail, không phải 500 trần trụi.</summary>
    [Fact]
    public async Task Execute_LenhNemNgay_TraLoiThucThi()
    {
        _server.ExecuteAsync = _ => throw new InvalidOperationException("hỏng ngay");

        var (status, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\"}");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Contains("Lỗi thực thi: hỏng ngay", (string?)body["summary"]);
    }

    /// <summary>Lệnh ném bất đồng bộ (task faulted): cũng phải thành Fail có thông báo đọc được.</summary>
    [Fact]
    public async Task Execute_TaskFaulted_TraLoiThucThi()
    {
        _server.ExecuteAsync = item =>
        {
            item.TryClaim();
            item.Completion.SetException(new InvalidOperationException("hỏng sau"));
            return Task.CompletedTask;
        };

        var (status, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\"}");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Contains("Lỗi thực thi: hỏng sau", (string?)body["summary"]);
    }

    /// <summary>
    /// Hết giờ khi luồng UI CHƯA nhận việc: lệnh bị huỷ, và phản hồi phải nói rõ "không chạy" —
    /// chữ này quyết định kỹ sư có gửi lại lệnh hay không.
    /// </summary>
    [Fact]
    public async Task Execute_TimeoutChuaNhanViec_504NoiRoLenhKhongChay()
    {
        _server.ExecuteAsync = _ => Task.Delay(5000);

        var (status, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\"}");

        Assert.Equal(HttpStatusCode.GatewayTimeout, status);
        Assert.Contains("Lệnh đã bị huỷ, không chạy", (string?)body["summary"]);
    }

    /// <summary>
    /// Hết giờ NHƯNG luồng UI đã nhận việc: 504 kèm id để hỏi /progress, và tuyệt đối không nói "chưa chạy" —
    /// gửi lại một lệnh ghi đang chạy là chạy hai lần trên model thật.
    /// </summary>
    [Fact]
    public async Task Execute_TimeoutDaNhanViec_504KemIdDeHoiTiep()
    {
        _server.ExecuteAsync = async item =>
        {
            item.TryClaim();
            await Task.Delay(600);
            item.Completion.SetResult(CommandResult.Ok("xong muộn"));
        };

        var (status, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\"}");

        Assert.Equal(HttpStatusCode.GatewayTimeout, status);
        Assert.Contains("KHÔNG gửi lại", (string?)body["summary"]);
        var id = (string?)body["id"];
        Assert.NotNull(id);
        Assert.Equal("/progress/" + id, (string?)body["progressUrl"]);

        await Task.Delay(800);
        var progress = JObject.Parse(await _client.GetStringAsync("/progress/" + id));
        Assert.Equal("done", (string?)progress["status"]);
        Assert.Equal("xong muộn", (string?)progress["result"]!["summary"]);
    }

    // ── Lệnh chạy nền ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TraNgay202KemId()
    {
        _server.ExecuteAsync = item =>
        {
            item.TryClaim();
            item.Completion.SetResult(CommandResult.Ok("xong nền"));
            return Task.CompletedTask;
        };

        var (status, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\",\"async\":true}");

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal("running", (string?)body["status"]);

        await Task.Delay(200);
        var progress = JObject.Parse(await _client.GetStringAsync("/progress/" + (string?)body["id"]));
        Assert.Equal("done", (string?)progress["status"]);
    }

    /// <summary>Lệnh nền ném ngay lúc dispatch: job ghi Error, không im lặng treo ở "running".</summary>
    [Fact]
    public async Task ExecuteAsync_NemNgayLucDispatch_JobGhiError()
    {
        _server.ExecuteAsync = _ => throw new InvalidOperationException("hỏng ngay");

        var (_, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\",\"async\":true}");
        var progress = JObject.Parse(await _client.GetStringAsync("/progress/" + (string?)body["id"]));

        Assert.Equal("error", (string?)progress["status"]);
        Assert.Contains("Lỗi thực thi: hỏng ngay", (string?)progress["error"]);
    }

    /// <summary>Lệnh nền ném sau khi đã nhận việc: job ghi Error kèm thông báo của exception gốc.</summary>
    [Fact]
    public async Task ExecuteAsync_TaskFaulted_JobGhiError()
    {
        _server.ExecuteAsync = item =>
        {
            item.TryClaim();
            item.Completion.SetException(new InvalidOperationException("hỏng sau"));
            return Task.CompletedTask;
        };

        var (_, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\",\"async\":true}");
        await Task.Delay(200);
        var progress = JObject.Parse(await _client.GetStringAsync("/progress/" + (string?)body["id"]));

        Assert.Equal("error", (string?)progress["status"]);
        Assert.Contains("Lỗi thực thi: hỏng sau", (string?)progress["error"]);
    }

    [Fact]
    public async Task ExecuteAsync_TaskBiHuy_JobNoiRoBiHuy()
    {
        _server.ExecuteAsync = item =>
        {
            item.TryClaim();
            item.Completion.SetCanceled();
            return Task.CompletedTask;
        };

        var (_, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\",\"async\":true}");
        await Task.Delay(200);
        var progress = JObject.Parse(await _client.GetStringAsync("/progress/" + (string?)body["id"]));

        Assert.Equal("error", (string?)progress["status"]);
        Assert.Contains("bị huỷ trước khi chạy xong", (string?)progress["error"]);
    }

    /// <summary>Hàng đợi đầy: từ chối bằng 429 kèm hướng dẫn, thay vì để một client dồn lệnh vô hạn.</summary>
    [Fact]
    public async Task ExecuteAsync_HangDoiDay_429()
    {
        _server.Jobs.MaxQueued = 1;
        _server.ExecuteAsync = _ => Task.Delay(5000);

        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync("/execute", "{\"command\":\"A\",\"async\":true}")).Status);
        var (status, body) = await PostAsync("/execute", "{\"command\":\"B\",\"async\":true}");

        Assert.Equal(HttpStatusCode.TooManyRequests, status);
        Assert.Contains("Hàng đợi đầy", (string?)body["error"]);
    }

    /// <summary>Quá hạn nhận việc: job chuyển Abandoned và nói rõ "lệnh KHÔNG chạy, gửi lại được".</summary>
    [Fact]
    public async Task ExecuteAsync_QuaHanNhanViec_Abandoned()
    {
        _server.ExecuteAsync = _ => Task.Delay(5000);

        var (_, body) = await PostAsync("/execute", "{\"command\":\"KiemTra\",\"async\":true,\"timeoutSeconds\":1}");
        await Task.Delay(1500);
        var progress = JObject.Parse(await _client.GetStringAsync("/progress/" + (string?)body["id"]));

        Assert.Equal("abandoned", (string?)progress["status"]);
        Assert.Contains("KHÔNG chạy", (string?)progress["error"]);
    }

    [Fact]
    public async Task Progress_IdKhongCo_404()
    {
        var response = await _client.GetAsync("/progress/khong-co-id-nay");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Client ngắt kết nối trước khi Bridge kịp trả lời: nuốt lỗi ghi, KHÔNG được để một client bỏ đi
    /// biến thành exception nổi lên và làm hỏng vòng lặp nhận request.
    /// </summary>
    [Fact]
    public async Task ClientNgatTruocKhiTraLoi_KhongLamHongServer()
    {
        var daNhanViec = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.ExecuteAsync = async item =>
        {
            item.TryClaim();
            daNhanViec.TrySetResult(true);
            await Task.Delay(400);
            item.Completion.SetResult(CommandResult.Ok("xong"));
        };

        var body = "{\"command\":\"KiemTra\"}";
        var request = "POST /execute HTTP/1.1\r\n"
                      + "Host: 127.0.0.1\r\n"
                      + "Authorization: Bearer " + Token + "\r\n"
                      + "Content-Type: application/json\r\n"
                      + "Content-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n"
                      + body;

        using (var socket = new TcpClient())
        {
            await socket.ConnectAsync(IPAddress.Loopback, _client.BaseAddress!.Port);
            var bytes = Encoding.UTF8.GetBytes(request);
            await socket.GetStream().WriteAsync(bytes, 0, bytes.Length);
            await daNhanViec.Task;
        }

        // Server vẫn phục vụ bình thường sau khi client kia bỏ đi.
        await Task.Delay(700);
        Assert.Contains("\"ok\"", await _client.GetStringAsync("/health"));
    }

    // ── POST /query ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_JsonHong_400()
    {
        var (status, body) = await PostAsync("/query", "{khong-phai-json");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("JSON không hợp lệ", (string?)body["error"]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"query\":\"  \"}")]
    public async Task Query_ThieuQuery_400(string body)
    {
        var (status, json) = await PostAsync("/query", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Thiếu trường 'query'.", (string?)json["error"]);
    }

    [Fact]
    public async Task Query_VoChuaGanQueryAsync_501()
    {
        var (status, body) = await PostAsync("/query", "{\"query\":\"levels\"}");

        Assert.Equal(HttpStatusCode.NotImplemented, status);
        Assert.Contains("QueryAsync", (string?)body["error"]);
    }

    [Fact]
    public async Task Query_TraKetQua()
    {
        _server.QueryAsync = item =>
        {
            item.TryClaim();
            item.Completion.SetResult(new { levels = new[] { "Tầng 1" } });
            return Task.CompletedTask;
        };

        var (status, body) = await PostAsync("/query", "{\"query\":\"levels\"}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Tầng 1", (string?)body["levels"]![0]);
    }

    /// <summary>Truy vấn chỉ đọc nên timeout được phép nói "gửi lại được" — khác hẳn lệnh ghi.</summary>
    [Fact]
    public async Task Query_TimeoutDaNhanViec_NoiRoGuiLaiDuoc()
    {
        _server.QueryAsync = async item =>
        {
            item.TryClaim();
            await Task.Delay(2000);
        };

        var (status, body) = await PostAsync("/query", "{\"query\":\"levels\"}");

        Assert.Equal(HttpStatusCode.GatewayTimeout, status);
        Assert.Contains("gửi lại được", (string?)body["error"]);
    }

    [Fact]
    public async Task Query_NemNgay_TraLoiTruyVan()
    {
        _server.QueryAsync = _ => throw new InvalidOperationException("hỏng");

        var (status, body) = await PostAsync("/query", "{\"query\":\"levels\"}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Lỗi truy vấn: hỏng", (string?)body["error"]);
    }

    // ── POST /chat ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Chat_VoChuaGanChat_501()
    {
        var (status, body) = await PostAsync("/chat", "{\"text\":\"xuất layer\"}");

        Assert.Equal(HttpStatusCode.NotImplemented, status);
        Assert.Contains("Chat", (string?)body["error"]);
    }

    [Fact]
    public async Task Chat_JsonHong_400()
    {
        _server.Chat = text => new { command = text };

        var (status, body) = await PostAsync("/chat", "{khong-phai-json");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("JSON không hợp lệ", (string?)body["error"]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"text\":\"  \"}")]
    public async Task Chat_ThieuText_400(string body)
    {
        _server.Chat = text => new { command = text };

        var (status, json) = await PostAsync("/chat", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Thiếu trường 'text'.", (string?)json["error"]);
    }

    [Fact]
    public async Task Chat_TraDeXuatLenh()
    {
        _server.Chat = text => new { command = "LayerExport", text };

        var (status, body) = await PostAsync("/chat", "{\"text\":\"xuất layer\"}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("LayerExport", (string?)body["command"]);
    }

    /// <summary>Model AI offline chậm: chặn bằng ChatTimeout để /chat không nuốt hết suất in-flight.</summary>
    [Fact]
    public async Task Chat_QuaCham_504()
    {
        _server.Chat = _ =>
        {
            Thread.Sleep(2000);
            return new { command = "LayerExport" };
        };

        var (status, body) = await PostAsync("/chat", "{\"text\":\"xuất layer\"}");

        Assert.Equal(HttpStatusCode.GatewayTimeout, status);
        Assert.Contains("Chat không trả lời", (string?)body["error"]);
    }

    /// <summary>Chat ném: exception gốc được ném lại nguyên vẹn để nhánh 500 nói đúng loại lỗi.</summary>
    [Fact]
    public async Task Chat_Nem_500KemDungLoaiException()
    {
        _server.Chat = _ => throw new NotSupportedException("chưa hỗ trợ");

        var (status, body) = await PostAsync("/chat", "{\"text\":\"xuất layer\"}");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("NotSupportedException", (string?)body["exceptionType"]);
    }
}
