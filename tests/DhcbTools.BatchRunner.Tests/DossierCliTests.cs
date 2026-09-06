using System.Diagnostics;
using System.Text;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

/// <summary>Đường <c>--dossier</c>: đối chiếu danh mục hồ sơ hoàn thành công trình với file thật (mục 11.6).</summary>
public class DossierCliTests
{
    private const string Spec =
        "{\"title\":\"Danh mục thử\",\"legalBasis\":\"Điều 28 NĐ 207/2026\",\"project\":\"Toà A\",\"groups\":["
        + "{\"code\":\"I\",\"name\":\"Pháp lý\",\"items\":["
        + "{\"code\":\"I.1\",\"name\":\"Hợp đồng\",\"patterns\":[\"*hop-dong*\"]},"
        + "{\"code\":\"I.2\",\"name\":\"Giấy phép\",\"patterns\":[\"*giay-phep*\"]},"
        + "{\"code\":\"I.3\",\"name\":\"Ảnh\",\"required\":false,\"patterns\":[\"*.jpg\"]}]}]}";

    [Fact]
    public void ThieuMucBatBuoc_MaThoat1_VaGoiTenMucThieu()
    {
        using var cli = new Cli();
        cli.Write("ho-so/phap-ly/hop-dong-01.pdf", "x");
        cli.Write("ho-so/ban-nhap.tmp", "x");

        var (code, output) = cli.Run(
            "--dossier", cli.Path_("ho-so"), "--dossier-spec", cli.Write("danh-muc.json", Spec));

        Assert.Equal(1, code);
        Assert.Contains("1 mục bắt buộc còn THIẾU", output);
        Assert.Contains("THIẾU I.2 — Giấy phép", output);
        // Mục không bắt buộc thiếu thì không kéo mã thoát lên 1, và không bị gọi tên.
        Assert.DoesNotContain("THIẾU I.3", output);
        Assert.Contains("Chưa xếp mục: ban-nhap.tmp", output);
    }

    [Fact]
    public void DuMucBatBuoc_MaThoat0_VaGhiBaoCao()
    {
        using var cli = new Cli();
        cli.Write("ho-so/hop-dong.pdf", "x");
        cli.Write("ho-so/giay-phep-xay-dung.pdf", "x");
        var report = cli.Path_("bao-cao", "danh-muc.html");

        var (code, output) = cli.Run(
            "--dossier", cli.Path_("ho-so"),
            "--dossier-spec", cli.Write("danh-muc.json", Spec),
            "--dossier-report", report);

        Assert.Equal(0, code);
        Assert.Contains("0 mục bắt buộc còn THIẾU", output);
        Assert.True(File.Exists(report), "thiếu " + report);
        Assert.True(File.Exists(Path.ChangeExtension(report, ".csv")), "thiếu .csv cạnh .html");
        Assert.Contains("Chủ đầu tư xác nhận", File.ReadAllText(report));
    }

    [Fact]
    public void ThieuThuMuc_ThieuDanhMuc_HoacDanhMucHong_MaThoat2()
    {
        using var cli = new Cli();
        var spec = cli.Write("danh-muc.json", Spec);
        cli.Write("ho-so/hop-dong.pdf", "x");

        var noFolder = cli.Run("--dossier", cli.Path_("khong-co"), "--dossier-spec", spec);
        Assert.Equal(2, noFolder.Code);
        Assert.Contains("Không có thư mục hồ sơ", noFolder.Output);

        var noSpec = cli.Run("--dossier", cli.Path_("ho-so"));
        Assert.Equal(2, noSpec.Code);
        Assert.Contains("chưa khai --dossier-spec", noSpec.Output);

        var badSpec = cli.Run("--dossier", cli.Path_("ho-so"), "--dossier-spec", cli.Write("hong.json", "{"));
        Assert.Equal(2, badSpec.Code);
        Assert.Contains("File danh mục không dùng được", badSpec.Output);

        var emptySpec = cli.Run("--dossier", cli.Path_("ho-so"), "--dossier-spec", cli.Write("rong.json", "{\"groups\":[]}"));
        Assert.Equal(2, emptySpec.Code);
        Assert.Contains("không có mục nào", emptySpec.Output);
    }

    /// <summary>
    /// Chạy CHÍNH file exe (qua <c>dotnet &lt;dll&gt;</c>) chứ không gọi <c>Main</c> trong tiến trình test.
    /// <para>
    /// Vì sao phải khác: <c>InvariantGlobalization</c> là thiết lập của <b>runtimeconfig của runner</b>, mà
    /// test host không dùng file đó. Ở chế độ invariant, <c>string.Normalize</c> thành no-op nên bỏ dấu tiếng
    /// Việt ngừng hoạt động — mẫu <c>*khao-sat*</c> trượt file "Báo cáo khảo sát địa chất.pdf" trong exe
    /// nhưng vẫn khớp trong mọi test gọi Main. Chỉ ca này bắt được lớp lỗi đó (§48).
    /// </para>
    /// </summary>
    [Fact]
    public void ChayThatExe_BoDauTiengViet_VanHoatDong()
    {
        using var cli = new Cli();
        cli.Write("ho-so/Báo cáo khảo sát địa chất.pdf", "x");
        var spec = cli.Write("danh-muc.json",
            "{\"groups\":[{\"code\":\"II\",\"name\":\"Khảo sát\",\"items\":["
            + "{\"code\":\"II.1\",\"name\":\"Khảo sát\",\"patterns\":[\"*khao-sat*\"]}]}]}");

        var dll = Path.Combine(AppContext.BaseDirectory, "DhcbTools.BatchRunner.dll");
        Assert.True(File.Exists(dll), "không thấy " + dll);

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { dll, "--dossier", cli.Path_("ho-so"), "--dossier-spec", spec })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "exe không thoát trong 60 s");

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("0 mục bắt buộc còn THIẾU", output);
    }
}
