using System.Net;
using System.Net.Sockets;
using DhcbTools.Shared.Hosting;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Hai instance cùng mở một cổng Bridge: instance thứ hai phải báo lỗi rõ tên thay vì nuốt
/// <c>HttpListenerException</c> chung chung (mục "Còn lại" trong bang-chung-test-autocad-live.md).
/// </summary>
public class HttpBridgeServerTests
{
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public void Second_server_on_same_port_throws_BridgePortInUseException()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var first = new HttpBridgeServer(port, "TestA", "1");
        using var second = new HttpBridgeServer(port, "TestB", "1");
        var logged = new List<string>();
        second.Log = logged.Add;

        try
        {
            first.Start(tokenPath);
            Assert.True(first.IsRunning);

            var ex = Assert.Throws<BridgePortInUseException>(() => second.Start(tokenPath));
            Assert.Equal(port, ex.Port);
            Assert.Contains(port.ToString(), ex.Message);
            Assert.Contains("TestB", ex.Message);
            Assert.False(second.IsRunning);
            Assert.Contains(logged, l => l.Contains("KHÔNG mở được cổng"));
            Assert.DoesNotContain(logged, l => l.Contains(first.Token!));
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task Health_answers_without_token()
    {
        var port = FreePort();
        var tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-test-" + Guid.NewGuid().ToString("N") + ".txt");
        using var server = new HttpBridgeServer(port, "TestA", "9.9");
        try
        {
            server.Start(tokenPath);
            using var http = new HttpClient();
            var body = await http.GetStringAsync("http://127.0.0.1:" + port + "/health");
            Assert.Contains("\"ok\"", body);
            Assert.Contains("9.9", body);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }
}
