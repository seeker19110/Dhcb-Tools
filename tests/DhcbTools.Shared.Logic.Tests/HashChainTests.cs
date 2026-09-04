using System;
using System.Collections.Generic;
using System.IO;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Evidence;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Chuỗi băm cho nhật ký batch (mục 11.5). NĐ 207/2026 chấp nhận nhật ký thi công điện tử khi có
/// **dấu thời gian không thể chỉnh sửa ngược**; batch runner đã ghi <c>run-HHmmss.jsonl</c> mỗi lượt,
/// nên việc còn lại là nối các dòng thành chuỗi để sửa một dòng cũ thì lộ ra.
/// <para>
/// Ràng buộc được chốt ở đây, theo thứ tự quan trọng: (1) sửa nội dung một dòng phải chỉ ra **đúng dòng
/// đó**; (2) kẻ sửa **biết thuật toán** và tính lại băm của chính dòng vừa sửa vẫn phải bị bắt ở dòng kế
/// tiếp — đó mới là lý do phải nối chuỗi thay vì băm từng dòng rời; (3) xoá hay đảo chỗ dòng cũng phải
/// gãy. Nếu ba cái này không đúng thì cả tính năng chỉ là một cột hex vô nghĩa trong log.
/// </para>
/// </summary>
public class HashChainTests
{
    /// <summary>Dựng một chuỗi hợp lệ từ vài mẩu nội dung, đúng cách <c>RunLog.Append</c> làm.</summary>
    private static List<string> Chain(params string[] bodies)
    {
        var lines = new List<string>();
        var prev = HashChain.Genesis;
        foreach (var body in bodies)
        {
            var payload = Payload(body, prev);
            var hash = HashChain.ComputeHash(payload);
            lines.Add(HashChain.Seal(payload, hash));
            prev = hash;
        }

        return lines;
    }

    private static string Payload(string body, string prev) =>
        "{\"m\":\"" + body + "\",\"prevHash\":\"" + prev + "\"}";

    /// <summary>Đọc prevHash bằng cắt chuỗi thuần: test không nên phụ thuộc thư viện JSON nào.</summary>
    private static string? PrevOf(string line)
    {
        const string key = "\"prevHash\":\"";
        var at = line.IndexOf(key, StringComparison.Ordinal);
        return at < 0 ? null : line.Substring(at + key.Length, HashChain.HashLength);
    }

    private static ChainVerification Verify(IReadOnlyList<string> lines) => HashChain.Verify(lines, PrevOf);

    [Fact]
    public void Seal_RoiTrySplit_TraLaiDungNoiDungVaBam()
    {
        var payload = Payload("a", HashChain.Genesis);
        var hash = HashChain.ComputeHash(payload);

        Assert.True(HashChain.TrySplit(HashChain.Seal(payload, hash), out var back, out var backHash));
        Assert.Equal(payload, back);
        Assert.Equal(hash, backHash);
    }

    [Fact]
    public void ChuoiNguyenVen_BaoIntact_VaDemDuSoDong()
    {
        var result = Verify(Chain("a", "b", "c"));

        Assert.Equal(ChainStatus.Intact, result.Status);
        Assert.True(result.Ok);
        Assert.Equal(3, result.CheckedLines);
        Assert.Null(result.ProblemLine);
    }

    /// <summary>Yêu cầu chính của mục 11.5: chỉ ra <b>đúng dòng bị sửa</b>, không chỉ nói "log hỏng".</summary>
    [Fact]
    public void SuaMotDongOGiua_ChiRaDungDongDo()
    {
        var lines = Chain("a", "b", "c");
        lines[1] = lines[1].Replace("\"m\":\"b\"", "\"m\":\"b-đã-sửa\"");

        var result = Verify(lines);

        Assert.Equal(ChainStatus.ContentChanged, result.Status);
        Assert.Equal(2, result.ProblemLine);
        Assert.Contains("Dòng 2", result.Message);
    }

    /// <summary>
    /// Ca đáng giá nhất: kẻ sửa <b>biết thuật toán</b>, sửa dòng 2 rồi tính lại băm cho chính dòng 2 nên
    /// dòng đó tự khớp. Băm từng dòng rời sẽ cho qua; nối chuỗi thì dòng 3 vẫn trỏ vào băm cũ nên gãy.
    /// </summary>
    [Fact]
    public void SuaRoiTinhLaiBamCuaChinhDongDo_VanGayODongKeTiep()
    {
        var lines = Chain("a", "b", "c");
        var forged = Payload("b-đã-sửa", PrevOf(lines[1])!);
        lines[1] = HashChain.Seal(forged, HashChain.ComputeHash(forged));

        var result = Verify(lines);

        Assert.Equal(ChainStatus.ChainBroken, result.Status);
        Assert.Equal(3, result.ProblemLine);
    }

    [Fact]
    public void XoaMotDong_GayODongKeSau()
    {
        var lines = Chain("a", "b", "c");
        lines.RemoveAt(1);

        var result = Verify(lines);

        Assert.Equal(ChainStatus.ChainBroken, result.Status);
        Assert.Equal(2, result.ProblemLine);
    }

    [Fact]
    public void DaoChoHaiDong_Gay()
    {
        var lines = Chain("a", "b", "c");
        (lines[1], lines[2]) = (lines[2], lines[1]);

        Assert.Equal(ChainStatus.ChainBroken, Verify(lines).Status);
    }

    /// <summary>Chèn thêm một dòng tự khớp vào giữa vẫn gãy, vì nó không nối vào mắt xích nào.</summary>
    [Fact]
    public void ChenThemDong_Gay()
    {
        var lines = Chain("a", "b");
        var extra = Payload("chèn", HashChain.Genesis);
        lines.Insert(1, HashChain.Seal(extra, HashChain.ComputeHash(extra)));

        var result = Verify(lines);

        Assert.Equal(ChainStatus.ChainBroken, result.Status);
        Assert.Equal(2, result.ProblemLine);
    }

    /// <summary>Gỡ dấu vết cũng là một cách sửa log — phải báo, không được im lặng cho qua.</summary>
    [Fact]
    public void DongChuaMangDauVet_BaoNotSealed()
    {
        var lines = Chain("a", "b");
        lines[1] = Payload("b", PrevOf(lines[1])!);

        var result = Verify(lines);

        Assert.Equal(ChainStatus.NotSealed, result.Status);
        Assert.Equal(2, result.ProblemLine);
    }

    [Fact]
    public void DongDauKhongPhaiGenesis_GayNgayODong1()
    {
        var payload = Payload("a", new string('a', HashChain.HashLength));
        var lines = new List<string> { HashChain.Seal(payload, HashChain.ComputeHash(payload)) };

        var result = Verify(lines);

        Assert.Equal(ChainStatus.ChainBroken, result.Status);
        Assert.Equal(1, result.ProblemLine);
    }

    [Fact]
    public void LogRong_KhongCoGiDeKiem_VanLaIntact()
    {
        var result = Verify(new List<string>());

        Assert.Equal(ChainStatus.Intact, result.Status);
        Assert.Equal(0, result.CheckedLines);
        Assert.Contains("rỗng", result.Message);
    }

    /// <summary>Dòng rỗng do biên tập viên thêm vào không phải là sửa nội dung — bỏ qua, không báo gãy.</summary>
    [Fact]
    public void DongRongXenGiua_BiBoQua()
    {
        var lines = Chain("a", "b");
        lines.Insert(1, "   ");

        Assert.Equal(ChainStatus.Intact, Verify(lines).Status);
    }

    /// <summary>Nội dung log chứa nguyên văn chuỗi giống trường hash: cái thật vẫn là cái cuối dòng.</summary>
    [Fact]
    public void NoiDungChuaChuoiGiongTruongHash_VanTachDung()
    {
        var payload = Payload("summary có ,\\\"hash\\\":\\\"gia\\\" bên trong", HashChain.Genesis);
        var hash = HashChain.ComputeHash(payload);

        Assert.True(HashChain.TrySplit(HashChain.Seal(payload, hash), out var back, out var backHash));
        Assert.Equal(payload, back);
        Assert.Equal(hash, backHash);
    }

    [Theory]
    [InlineData("{\"m\":\"a\"}")]                                  // chưa gắn gì
    [InlineData("{\"m\":\"a\",\"hash\":\"abc\"}")]                 // băm ngắn hơn 64
    [InlineData("{\"m\":\"a\",\"hash\":\"zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz\"}")]
    public void TrySplit_TuChoiDongKhongDungDinhDang(string line)
    {
        Assert.False(HashChain.TrySplit(line, out _, out _));
    }

    [Fact]
    public void Seal_TuChoiPayloadKhongPhaiObjectJson()
    {
        Assert.Throws<ArgumentException>(() => HashChain.Seal("không phải json", new string('0', HashChain.HashLength)));
    }

    /// <summary>Băm phải ổn định theo thời gian: log kiểm lại sau 30 ngày mà đổi thuật toán là mất sạch.</summary>
    [Fact]
    public void ComputeHash_LaSha256HexThuong_OnDinh()
    {
        // SHA-256 của chuỗi rỗng — hằng số công khai, đổi là biết ngay.
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", HashChain.ComputeHash(string.Empty));
    }
}

/// <summary>
/// Vòng ghi thật ra file: <c>RunLog.Append</c> là điểm ghi duy nhất của cả batch Revit lẫn AutoCAD, nên
/// gắn dấu vết ở đó phải phủ hết mọi đường ghi mà không làm hỏng thứ đang đọc log (báo cáo HTML, phân
/// tích cảnh báo, mã thoát).
/// </summary>
public class RunLogChainTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "dhcb-chain-" + Guid.NewGuid() + ".jsonl");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private void Ghi(params string[] files)
    {
        foreach (var f in files)
        {
            RunLog.Append(_path, new RunLogEntry { File = f, Command = "HealthReport", Success = true, Affected = 1 });
        }
    }

    [Fact]
    public void GhiRoiKiemLai_ChuoiNguyenVen()
    {
        Ghi("a.rvt", "b.rvt", "c.rvt");

        var result = RunLog.VerifyFile(_path);

        Assert.Equal(ChainStatus.Intact, result.Status);
        Assert.Equal(3, result.CheckedLines);
    }

    [Fact]
    public void DongDauMangGenesis_DongSauNoiVaoDongTruoc()
    {
        Ghi("a.rvt", "b.rvt");

        var all = RunLog.ReadAll(_path);

        Assert.Equal(HashChain.Genesis, all[0].PrevHash);
        Assert.Equal(all[0].Hash, all[1].PrevHash);
        Assert.NotNull(all[0].Hash);
    }

    /// <summary>Sửa log bằng tay như người thật sẽ làm: mở file, đổi một chữ trong summary.</summary>
    [Fact]
    public void SuaTayMotDong_ChiRaDungDongDo()
    {
        Ghi("a.rvt", "b.rvt", "c.rvt");
        var lines = File.ReadAllLines(_path);
        lines[1] = lines[1].Replace("b.rvt", "b-khac.rvt");
        File.WriteAllLines(_path, lines);

        var result = RunLog.VerifyFile(_path);

        Assert.Equal(ChainStatus.ContentChanged, result.Status);
        Assert.Equal(2, result.ProblemLine);
    }

    [Fact]
    public void XoaTayMotDong_BaoChuoiDut()
    {
        Ghi("a.rvt", "b.rvt", "c.rvt");
        var lines = new List<string>(File.ReadAllLines(_path));
        lines.RemoveAt(1);
        File.WriteAllLines(_path, lines);

        Assert.Equal(ChainStatus.ChainBroken, RunLog.VerifyFile(_path).Status);
    }

    /// <summary>Log của bản cài cũ (chưa có chuỗi băm) phải bị báo, không được coi như đạt.</summary>
    [Fact]
    public void LogCuKhongCoChuoiBam_BaoNotSealed()
    {
        File.WriteAllText(_path, "{\"file\":\"a.rvt\",\"command\":\"HealthReport\",\"success\":true}\n");

        var result = RunLog.VerifyFile(_path);

        Assert.Equal(ChainStatus.NotSealed, result.Status);
        Assert.Equal(1, result.ProblemLine);
    }

    /// <summary>Thêm dấu vết không được làm hỏng thứ đang đọc log: mọi trường cũ phải còn nguyên.</summary>
    [Fact]
    public void TruongCu_VanDocLaiDuocDayDu()
    {
        RunLog.Append(_path, new RunLogEntry
        {
            File = "ARC.rvt",
            Command = "ClashDetection",
            Success = false,
            Affected = 479,
            Summary = "có va chạm",
            ElapsedMs = 3821,
            Messages = { "connector hở" },
            Errors = { "E-PARAM-MISSING" },
        });

        var back = Assert.Single(RunLog.ReadAll(_path));

        Assert.Equal("ARC.rvt", back.File);
        Assert.Equal("ClashDetection", back.Command);
        Assert.False(back.Success);
        Assert.Equal(479, back.Affected);
        Assert.Equal("có va chạm", back.Summary);
        Assert.Equal(3821, back.ElapsedMs);
        Assert.Equal("connector hở", back.Messages[0]);
        Assert.Equal("E-PARAM-MISSING", back.Errors[0]);
        Assert.Equal(1, RunLog.ExitCode(new[] { back }));
    }

    [Fact]
    public void FileChuaTonTai_KhongNem()
    {
        var result = RunLog.VerifyFile(Path.Combine(Path.GetTempPath(), "khong-co-" + Guid.NewGuid() + ".jsonl"));

        Assert.Equal(ChainStatus.Intact, result.Status);
        Assert.Equal(0, result.CheckedLines);
    }
}
