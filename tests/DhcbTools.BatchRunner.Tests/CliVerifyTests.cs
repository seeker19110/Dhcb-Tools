using Xunit;

namespace DhcbTools.BatchRunner.Tests;

/// <summary>
/// Đường <c>--verify-ifc</c> / <c>--verify-ids</c> với ĐẦU VÀO HỎNG. Mọi ca ở đây từng chỉ được thử bằng
/// tay một lần (§44); lỗi làm sập runner nằm đúng trong nhóm này.
/// </summary>
public class CliVerifyTests
{
    /// <summary>
    /// §44 T5 — file rác đặt tên .ifc từng làm ngoại lệ lọt ra runtime (mã thoát 127). Phải là 2 (lỗi cấu
    /// hình/đầu vào) kèm câu nói rõ, vì Task Scheduler đọc mã thoát chứ không đọc stack trace.
    /// </summary>
    [Fact]
    public void FileIfcRac_KemIds_MaThoat2_KhongNemNgoaiLe()
    {
        using var cli = new Cli();
        var ifc = cli.Write("hong.ifc", Fixtures.JunkIfc);
        var ids = cli.Write("yeu-cau.ids", Fixtures.ValidIds);

        var (code, output) = cli.Run("--verify-ifc", ifc, "--verify-ids", ids);

        Assert.Equal(2, code);
        Assert.Contains("Không đọc được file IFC", output);
    }

    /// <summary>Cùng file rác nhưng chỉ kiểm cấu trúc: đường này vốn đã lành — chốt để nó không hỏng theo.</summary>
    [Fact]
    public void FileIfcRac_ChiVerifyIfc_MaThoat1_BaoLoiDoc()
    {
        using var cli = new Cli();
        var (code, output) = cli.Run("--verify-ifc", cli.Write("hong.ifc", Fixtures.JunkIfc));

        Assert.Equal(1, code);
        Assert.Contains("Không đọc được file", output);
    }

    [Fact]
    public void ThieuFile_IfcHoacIds_MaThoat2()
    {
        using var cli = new Cli();
        var ifc = cli.Write("toa-a.ifc", Fixtures.OneWallIfc);
        var ids = cli.Write("yeu-cau.ids", Fixtures.ValidIds);

        var missingIfc = cli.Run("--verify-ifc", cli.Path_("khong-co.ifc"), "--verify-ids", ids);
        Assert.Equal(2, missingIfc.Code);
        Assert.Contains("Không có file IFC", missingIfc.Output);

        var missingIds = cli.Run("--verify-ifc", ifc, "--verify-ids", cli.Path_("khong-co.ids"));
        Assert.Equal(2, missingIds.Code);
        Assert.Contains("Không có file IDS", missingIds.Output);
    }

    /// <summary>IDS không khai specification nào: từ chối file, KHÔNG được báo "0 phần tử không đạt".</summary>
    [Fact]
    public void IdsKhongCoSpecification_MaThoat2_KhongBaoDat()
    {
        using var cli = new Cli();
        var (code, output) = cli.Run(
            "--verify-ifc", cli.Write("toa-a.ifc", Fixtures.OneWallIfc),
            "--verify-ids", cli.Write("rong.ids", Fixtures.EmptyIds));

        Assert.Equal(2, code);
        Assert.Contains("không có <specification>", output);
        Assert.DoesNotContain("0 phần tử không đạt", output);
    }

    [Fact]
    public void IdsKhongPhaiXml_MaThoat2()
    {
        using var cli = new Cli();
        var (code, output) = cli.Run(
            "--verify-ifc", cli.Write("toa-a.ifc", Fixtures.OneWallIfc),
            "--verify-ids", cli.Write("hong.ids", "<ids"));

        Assert.Equal(2, code);
        Assert.Contains("File IDS không dùng được", output);
    }

    /// <summary>Mọi phần tử đạt → 0; có phần tử không đạt → 1. Mã thoát là thứ job đêm dựa vào.</summary>
    [Fact]
    public void DatVaKhongDat_RaDungMaThoat()
    {
        using var cli = new Cli();
        var ifc = cli.Write("toa-a.ifc", Fixtures.OneWallIfc);

        var pass = cli.Run("--verify-ifc", ifc, "--verify-ids", cli.Write("dat.ids", Fixtures.ValidIds));
        Assert.Equal(0, pass.Code);
        Assert.Contains("0 phần tử không đạt", pass.Output);

        var needDescription = Fixtures.ValidIds.Replace(
            "<simpleValue>Name</simpleValue>", "<simpleValue>Description</simpleValue>");
        var fail = cli.Run("--verify-ifc", ifc, "--verify-ids", cli.Write("truot.ids", needDescription));
        Assert.Equal(1, fail.Code);
        Assert.Contains("1 phần tử không đạt", fail.Output);
    }

    /// <summary>§44 T4 — <c>--ids-report</c> trỏ vào thư mục chưa tồn tại: tự tạo, ghi cả .html lẫn .csv.</summary>
    [Fact]
    public void IdsReport_TuTaoThuMucChuaCo_GhiCaHtmlVaCsv()
    {
        using var cli = new Cli();
        var report = cli.Path_("bao-cao", "sau", "ids.html");

        var (code, _) = cli.Run(
            "--verify-ifc", cli.Write("toa-a.ifc", Fixtures.OneWallIfc),
            "--verify-ids", cli.Write("yeu-cau.ids", Fixtures.ValidIds),
            "--ids-report", report);

        Assert.Equal(0, code);
        Assert.True(File.Exists(report), "thiếu " + report);
        Assert.True(File.Exists(Path.ChangeExtension(report, ".csv")), "thiếu .csv cạnh .html");
        Assert.Contains("Kiểm mô hình theo IDS", File.ReadAllText(report));
    }

    /// <summary>IDS lệch chuẩn XSD vẫn kiểm được, nhưng phải cảnh báo kèm số dòng (§40) — không đổi mã thoát.</summary>
    [Fact]
    public void IdsLechChuan_VanKiem_NhungCanhBao()
    {
        using var cli = new Cli();
        var (code, output) = cli.Run(
            "--verify-ifc", cli.Write("toa-a.ifc", Fixtures.OneWallIfc),
            "--verify-ids", cli.Write("lech.ids", Fixtures.OffSpecIds));

        Assert.Equal(0, code);
        Assert.Contains("lệch chuẩn", output);
        Assert.Contains("ifcVersion", output);
    }

    /// <summary>§44 T1 — <c>--verify-ids</c> thiếu <c>--verify-ifc</c>: in usage, mã 2, không đụng gì tới đĩa.</summary>
    [Fact]
    public void VerifyIdsThieuVerifyIfc_InUsage_MaThoat2()
    {
        using var cli = new Cli();
        var (code, output) = cli.Run("--verify-ids", cli.Write("yeu-cau.ids", Fixtures.ValidIds));

        Assert.Equal(2, code);
        Assert.Contains("--verify-ids", output);
    }

    [Fact]
    public void ThamSoThieuGiaTri_HoacKhongCoThamSo_MaThoat2()
    {
        using var cli = new Cli();
        Assert.Equal(2, cli.Run("--verify-ifc").Code);
        Assert.Equal(2, cli.Run().Code);
        Assert.Equal(2, cli.Run("--tham-so-la").Code);
    }

    [Fact]
    public void VerifyLog_ThieuFile_MaThoat2()
    {
        using var cli = new Cli();
        var (code, output) = cli.Run("--verify-log", cli.Path_("khong-co.jsonl"));

        Assert.Equal(2, code);
        Assert.Contains("Không có file log", output);
    }
}
