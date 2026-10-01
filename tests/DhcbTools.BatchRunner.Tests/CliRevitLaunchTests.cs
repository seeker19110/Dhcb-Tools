using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

/// <summary>
/// Đường mở Revit của runner, phần chạy được mà không cần Revit: các nhánh KHÔNG mở được Revit phải dọn
/// <c>%APPDATA%\DHCB\pending-job.json</c> (audit 2026-10-01). Sót file đó là lần sau kỹ sư mở Revit (năm khác, có
/// add-in) thì Revit âm thầm chạy job của đêm trước rồi tự đóng giữa phiên làm việc.
/// <para>
/// Chỉ chạy trên Linux: thư mục <c>ApplicationData</c> của .NET ở đó theo <c>XDG_CONFIG_HOME</c> nên chuyển được sang
/// thư mục tạm. Trên Windows nó là %APPDATA% thật của người chạy test — không được đụng vào.
/// </para>
/// </summary>
public class CliRevitLaunchTests
{
    private static string WriteJob(Cli cli) => cli.Write("job.json", new JObject
    {
        ["name"] = "Đêm thử",
        ["app"] = "revit",
        ["revitVersion"] = 2024,
        ["saveMode"] = "None",
        ["files"] = new JArray(new JObject { ["path"] = cli.Path_("khong-co.rvt") }),
        ["steps"] = new JArray(new JObject { ["command"] = "HealthReport" }),
    }.ToString());

    /// <summary>
    /// Chạy CLI với ApplicationData trỏ vào thư mục tạm của ca test. Thư mục phải CÓ SẴN: .NET trả chuỗi rỗng cho
    /// thư mục đặc biệt chưa tồn tại, và runner khi đó ghi "DHCB\pending-job.json" vào thư mục hiện hành — ca test
    /// sẽ xanh vô nghĩa vì đi tìm file ở chỗ khác.
    /// </summary>
    private static (int Code, string Output) RunWithAppData(Cli cli, params string[] args)
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Directory.CreateDirectory(cli.Path_("appdata"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", cli.Path_("appdata"));
        try
        {
            return cli.Run(args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public void ChuaCaiAddin_MaThoat2_KhongSotPendingJob()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var cli = new Cli();
        var revitExe = cli.Write("Revit.exe", "khong phai chuong trinh");

        var (code, output) = RunWithAppData(cli, "--job", WriteJob(cli), "--revit-exe", revitExe,
            "--no-autodetect", "--log-dir", cli.Path_("logs"));

        Assert.Equal(2, code);
        Assert.Contains("Không tìm thấy DhcbTools.Revit.dll", output);
        Assert.False(File.Exists(cli.Path_("appdata", "DHCB", "pending-job.json")), "pending-job.json bị bỏ lại");
    }

    /// <summary>--revit-exe trỏ vào file không chạy được: bản cũ để Win32Exception lọt ra (runner sập) và sót pending.</summary>
    [Fact]
    public void RevitExeKhongChayDuoc_MaThoat2_KhongSotPendingJob()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var cli = new Cli();
        var revitExe = cli.Write("Revit.exe", "khong phai chuong trinh");
        cli.Write(Path.Combine("appdata", "Autodesk", "Revit", "Addins", "2024", "DhcbTools.Revit.dll"), "gia");

        var (code, output) = RunWithAppData(cli, "--job", WriteJob(cli), "--revit-exe", revitExe,
            "--no-autodetect", "--log-dir", cli.Path_("logs"));

        Assert.Equal(2, code);
        Assert.Contains("Không khởi động được Revit", output);
        Assert.False(File.Exists(cli.Path_("appdata", "DHCB", "pending-job.json")), "pending-job.json bị bỏ lại");
    }
}
