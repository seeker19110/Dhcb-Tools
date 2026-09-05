using System.Net;
using System.Text;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Đường "gọi model local thật" của <see cref="OllamaClient"/>: transport HTTP, đọc cấu hình từ file,
/// và mọi cách một model local có thể trả lời sai mà lệnh gọi vẫn phải rơi về heuristic thay vì nổ.
/// </summary>
public class OllamaClientGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dhcb-ai-gap-" + Guid.NewGuid().ToString("N"));

    public OllamaClientGapTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* thư mục tạm */ }
    }

    [Fact]
    public void LocalAiSettings_DefaultPath_NamTrongThuMucDHCB()
    {
        Assert.EndsWith(Path.Combine("DHCB", "ai.json"), LocalAiSettings.DefaultPath);
    }

    [Fact]
    public void LocalAiSettings_KhongCoFile_TraCauHinhMacDinhTat()
    {
        var settings = LocalAiSettings.Load(Path.Combine(_dir, "khong-co.json"));

        Assert.False(settings.Enabled);
        Assert.Equal("qwen3:8b", settings.Model);
    }

    [Fact]
    public void LocalAiSettings_FileHong_TraCauHinhMacDinh()
    {
        var path = Path.Combine(_dir, "hong.json");
        File.WriteAllText(path, "{khong-phai-json");

        Assert.False(LocalAiSettings.Load(path).Enabled);
    }

    /// <summary>JSON hợp lệ nhưng là <c>null</c>: vẫn phải ra cấu hình mặc định, không phải null.</summary>
    [Fact]
    public void LocalAiSettings_FileChuaNull_TraCauHinhMacDinh()
    {
        var path = Path.Combine(_dir, "null.json");
        File.WriteAllText(path, "null");

        Assert.NotNull(LocalAiSettings.Load(path));
    }

    /// <summary>File đang bị tiến trình khác giữ: coi như chưa cấu hình, không làm hỏng lệnh đang chạy.</summary>
    [Fact]
    public void LocalAiSettings_FileDangBiKhoa_TraCauHinhMacDinh()
    {
        var path = Path.Combine(_dir, "khoa.json");
        File.WriteAllText(path, "{\"enabled\":true}");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(LocalAiSettings.Load(path).Enabled);
        }
    }

    [Fact]
    public void IsLoopback_EndpointKhongPhaiUri_TraFalse()
    {
        Assert.False(new LocalAiSettings { Endpoint = "khong-phai-uri" }.IsLoopback());
    }

    /// <summary>Dựng bằng constructor một tham số thì dùng transport HTTP thật.</summary>
    [Fact]
    public void Constructor_MotThamSo_VanDungDuoc()
    {
        var client = new OllamaClient(new LocalAiSettings { Enabled = true });

        Assert.True(client.IsUsable);
    }

    /// <summary>
    /// Transport HTTP thật, đấu vào một HttpListener loopback: kiểm cả phần ghi body lẫn phần đọc phản hồi.
    /// </summary>
    [Fact]
    public void HttpTransport_GuiPostVaDocPhanHoi()
    {
        using var server = new LoopbackServer(_ => "{\"response\":\"chào\"}");

        var raw = OllamaClient.HttpTransport(server.Url, "{\"prompt\":\"xin chào\"}", timeoutSeconds: 5);

        Assert.Equal("{\"response\":\"chào\"}", raw);
        Assert.Equal("{\"prompt\":\"xin chào\"}", server.LastBody);
    }

    /// <summary>Client dựng không tiêm transport vẫn đi qua HTTP thật tới endpoint loopback đã cấu hình.</summary>
    [Fact]
    public void Generate_QuaTransportHttpThat_TraNoiDungResponse()
    {
        using var server = new LoopbackServer(_ => "{\"response\":\"xong\"}");
        var client = new OllamaClient(new LocalAiSettings { Enabled = true, Endpoint = server.BaseUrl });

        Assert.Equal("xong", client.Generate("làm gì đó", system: "bạn là kỹ sư BIM"));
        Assert.Null(client.LastError);
        Assert.Contains("\"system\":\"bạn là kỹ sư BIM\"", server.LastBody);
    }

    [Fact]
    public void Generate_TransportTraNull_NoiRoKhongNhanDuocPhanHoi()
    {
        var client = new OllamaClient(new LocalAiSettings { Enabled = true }, (_, _, _) => null);

        Assert.Null(client.Generate("gì đó"));
        Assert.Equal("Không nhận được phản hồi từ model local.", client.LastError);
    }

    [Fact]
    public void Generate_TransportNemIOException_NoiRoLoiDoc()
    {
        var client = new OllamaClient(new LocalAiSettings { Enabled = true },
            (_, _, _) => throw new IOException("đứt giữa chừng"));

        Assert.Null(client.Generate("gì đó"));
        Assert.Contains("Lỗi đọc phản hồi model local", client.LastError);
    }

    [Fact]
    public void ReadConfidence_KhongCoTruong_TraMacDinh()
    {
        Assert.Equal(0.5, OllamaClient.ReadConfidence(null));
    }

    /// <summary>Số nguyên JSON quá lớn cho double: về mặc định 0.5 thay vì ném InvalidCastException.</summary>
    [Fact]
    public void ReadConfidence_SoNguyenQuaLon_TraMacDinh()
    {
        var token = JObject.Parse("{\"c\":99999999999999999999999999999999999999999}")["c"];

        Assert.Equal(0.5, OllamaClient.ReadConfidence(token));
    }

    /// <summary>Model chọn lệnh ngoài danh sách ứng viên: trả null (whitelist), kèm lý do trong LastError.</summary>
    [Fact]
    public void SuggestLayerMappings_DungPromptVaLocTypeBia()
    {
        var reply = "{\"response\":\"{\\\"mappings\\\":[{\\\"layer\\\":\\\"M-DUCT\\\",\\\"revitType\\\":\\\"Duct - Rect\\\",\\\"confidence\\\":0.9,\\\"reason\\\":\\\"ống gió\\\"},"
                    + "{\\\"layer\\\":\\\"M-XXX\\\",\\\"revitType\\\":\\\"Type Bia\\\",\\\"confidence\\\":0.9,\\\"reason\\\":\\\"bịa\\\"}]}\"}";
        string? prompt = null;
        var client = new OllamaClient(new LocalAiSettings { Enabled = true }, (_, body, _) =>
        {
            prompt = (string?)JObject.Parse(body)["prompt"];
            return reply;
        });
        var rejected = new List<string>();

        var mappings = client.SuggestLayerMappings(
            new[] { "M-DUCT", "M-XXX" }, new[] { "Duct - Rect" }, rejected);

        Assert.Equal("M-DUCT", Assert.Single(mappings!).Layer);
        Assert.Contains("Type Bia", Assert.Single(rejected));
        Assert.Contains("REVIT TYPES:", prompt);
        Assert.Contains("- M-DUCT", prompt);
    }

    [Fact]
    public void SuggestLayerMappings_ModelTat_TraNull()
    {
        var client = new OllamaClient(new LocalAiSettings());

        Assert.Null(client.SuggestLayerMappings(new[] { "M-DUCT" }, new[] { "Duct - Rect" }, new List<string>()));
    }

    [Fact]
    public void ChooseCommand_KhongCoUngVien_TraNull()
    {
        var client = new OllamaClient(new LocalAiSettings { Enabled = true }, (_, _, _) => "{}");

        Assert.Null(client.ChooseCommand("gì đó", Array.Empty<CommandDescriptor>(), out _, out _));
    }

    /// <summary>Model trả chữ không phải JSON: nói rõ trong LastError rồi rơi về heuristic.</summary>
    [Fact]
    public void ChooseCommand_ModelTraKhongPhaiJson_NoiRoTrongLastError()
    {
        var client = new OllamaClient(new LocalAiSettings { Enabled = true },
            (_, _, _) => "{\"response\":\"không phải json\"}");

        Assert.Null(client.ChooseCommand("gì đó", CommandCatalog.For("revit").Take(2).ToList(), out _, out _));
        Assert.Contains("Model trả JSON không đọc được", client.LastError);
    }

    /// <summary>Model trả <c>command: null</c> (không lệnh nào khớp): trả null, không đoán bừa một lệnh.</summary>
    [Fact]
    public void ChooseCommand_ModelKhongChonLenhNao_TraNull()
    {
        var client = new OllamaClient(new LocalAiSettings { Enabled = true },
            (_, _, _) => "{\"response\":\"{\\\"command\\\":null,\\\"confidence\\\":0.2}\"}");

        Assert.Null(client.ChooseCommand("gì đó", CommandCatalog.For("revit").Take(2).ToList(), out var confidence, out _));
        Assert.Equal(0.2, confidence, 2);
    }

    [Fact]
    public void ParseMappingJson_KhongCoNgoacNhon_TraNull()
    {
        Assert.Null(OllamaClient.ParseMappingJson("chẳng có json nào", new[] { "Duct - Rect" }, new List<string>()));
    }

    /// <summary>JSON hợp lệ nhưng không có mảng "mappings": trả null, không đoán ra một danh sách rỗng.</summary>
    [Fact]
    public void ParseMappingJson_ThieuMangMappings_TraNull()
    {
        Assert.Null(OllamaClient.ParseMappingJson("{\"ket-qua\":[]}", new[] { "Duct - Rect" }, new List<string>()));
    }

    [Fact]
    public void ParseMappingJson_JsonHong_TraNull()
    {
        Assert.Null(OllamaClient.ParseMappingJson("{\"mappings\": [ {\"layer\": }", new[] { "Duct - Rect" }, new List<string>()));
    }

    /// <summary>HttpListener loopback tối giản, chỉ để kiểm transport HTTP thật.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();

        public LoopbackServer(Func<string, string> respond)
        {
            var port = FreePort();
            BaseUrl = "http://127.0.0.1:" + port;
            Url = BaseUrl + "/api/generate";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            Task.Run(() =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = _listener.GetContext(); }
                    catch { return; }

                    using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    {
                        LastBody = reader.ReadToEnd();
                    }

                    var bytes = Encoding.UTF8.GetBytes(respond(LastBody));
                    context.Response.ContentLength64 = bytes.Length;
                    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    context.Response.Close();
                }
            });
        }

        public string BaseUrl { get; }

        public string Url { get; }

        public string LastBody { get; private set; } = string.Empty;

        public void Dispose() => _listener.Close();

        private static int FreePort()
        {
            var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return port;
        }
    }
}
