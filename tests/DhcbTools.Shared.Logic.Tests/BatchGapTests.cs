using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Testing;
using DhcbTools.Shared.Logic.Usage;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Nhánh còn thiếu của tầng batch/log/kiểm thử: cấu hình hỏng phải bị chặn trước khi runner mở Revit
/// (mỗi lần mở là vài phút), và log hỏng không được làm sập cả lô.
/// </summary>
public class BatchGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dhcb-batch-gap-" + Guid.NewGuid().ToString("N"));

    public BatchGapTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* thư mục tạm */ }
    }

    [Fact]
    public void AcadScriptGen_ThieuDuongDanDll_NemArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => AcadScriptGen.Build("  ", Array.Empty<string>(), null, "run.log", "a.dwg"));
    }

    [Fact]
    public void AcadScriptGen_PlotPdf_ThieuDuongDanPdf_NemArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AcadScriptGen.PlotPdf("  "));
    }

    /// <summary>JSON hợp lệ nhưng là <c>null</c>: phải báo "file job rỗng", không nổ NullReference sau đó.</summary>
    [Fact]
    public void BatchJob_ParseJsonNull_NemInvalidData()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BatchJob.Parse("null"));

        Assert.Contains("rỗng", ex.Message);
    }

    [Fact]
    public void BatchJob_Validate_BatDuocMoiLoiCauHinh()
    {
        var job = new BatchJob
        {
            App = "sketchup",
            Files = { new BatchJobFile { Path = "  " } },
            Steps = { new BatchJobStep { Command = "  " } },
        };

        var errors = job.Validate();

        Assert.Contains("files[0] thiếu 'path'", errors);
        Assert.Contains("steps[0] thiếu 'command'", errors);
        Assert.Contains("'app' phải là revit hoặc autocad", errors);
    }

    [Fact]
    public void BatchJob_ToJsonRoiLoadLai_GiuNguyenCauHinh()
    {
        var job = new BatchJob
        {
            App = "revit",
            StopOnError = true,
            SaveMode = SaveMode.None,
            Files = { new BatchJobFile { Path = "a.rvt" } },
            Steps = { new BatchJobStep { Command = "KiemTra", SkipIfPreviousFailed = true } },
        };

        var path = Path.Combine(_dir, "job.json");
        File.WriteAllText(path, job.ToJson());
        var loaded = BatchJob.Load(path);

        Assert.True(loaded.Steps[0].SkipIfPreviousFailed);
        Assert.True(loaded.StopOnError);
        Assert.Equal("a.rvt", loaded.Files[0].Path);
    }

    [Fact]
    public void JobTokens_Expand_ContextNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => JobTokens.Expand("{fileName}", null!));
    }

    [Fact]
    public void JobTokens_ExpandIn_TokenNull_KhongLamGi()
    {
        JobTokens.ExpandIn(null, new JobTokenContext("out", "ban-ve", DateTime.Now));
    }

    [Fact]
    public void JobTokens_ExpandIn_ContextNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => JobTokens.ExpandIn(new JObject(), null!));
    }

    /// <summary>Chuỗi định dạng ngày không hợp lệ ("dd-ff-dd") thì giữ nguyên token, không làm hỏng cả job.</summary>
    [Fact]
    public void JobTokens_ChuoiNgayKhongHopLe_GiuNguyenToken()
    {
        var context = new JobTokenContext("out", "ban-ve", new DateTime(2026, 9, 5));

        // 8 chữ "f" vượt số chữ số thập phân giây tối đa (7) → FormatException.
        Assert.Equal("{ffffffff}", JobTokens.Expand("{ffffffff}", context));
    }

    /// <summary>Mảng lồng mảng: token bên trong vẫn phải được thay (đệ quy qua nhánh JArray không-chuỗi).</summary>
    [Fact]
    public void JobTokens_ExpandIn_MangLongMang_ThayCaBenTrong()
    {
        var token = JArray.Parse("[[\"{fileName}\"]]");

        JobTokens.ExpandIn(token, new JobTokenContext("out", "ban-ve", DateTime.Now));

        Assert.Equal("ban-ve", (string?)token[0]![0]!);
    }

    [Fact]
    public void RunLog_Deserialize_DongHongTraNull()
    {
        Assert.Null(RunLog.Deserialize("{khong-phai-json"));
    }

    [Fact]
    public void RvtFileInfo_DocTuFileThat_NhanRaPhienBan()
    {
        var path = Path.Combine(_dir, "a.rvt");
        File.WriteAllBytes(path, System.Text.Encoding.Unicode.GetBytes("BasicFileInfo Format: 2024 "));

        Assert.Equal(2024, RvtFileInfo.DetectVersion(path));
    }

    [Fact]
    public void RvtFileInfo_FileKhongTonTai_TraNull()
    {
        Assert.Null(RvtFileInfo.DetectVersion(Path.Combine(_dir, "khong-co.rvt")));
    }

    /// <summary>Buffer không chứa dấu hiệu phiên bản nào: trả null để runner dùng revitVersion của job.</summary>
    [Fact]
    public void RvtFileInfo_BufferKhongCoDauHieu_TraNull()
    {
        Assert.Null(RvtFileInfo.DetectVersion(new byte[64]));
    }

    [Fact]
    public void TestSuite_JsonRong_NemArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TestSuite.Parse("   "));
    }

    [Fact]
    public void TestExpectation_ObservedNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TestExpectation().Evaluate(null!));
    }

    [Fact]
    public void TestExpectation_MessagesKhongChuaChuoiCanTim_BaoTruot()
    {
        var expectation = new TestExpectation { MessagesContain = { "đã sửa 3 phần tử" } };
        var observed = new TestObservation { Summary = "xong" };

        var failures = expectation.Evaluate(observed);

        Assert.Contains(failures, f => f.Contains("không dòng Messages nào chứa"));
    }

    /// <summary>Tên file log không đúng khuôn hoặc ngày không có thật: trả danh sách rỗng, không ném.</summary>
    [Theory]
    [InlineData("khong-dung-khuon.txt")]
    [InlineData("usage-Revit-2024-13-45.log")]
    public void UsageLog_TenFileKhongDung_TraDanhSachRong(string fileName)
    {
        Assert.Empty(UsageLog.Parse(fileName, new[] { "gì đó" }));
    }
}
