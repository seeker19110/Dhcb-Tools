using DhcbTools.Shared.Logic.Checks;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tiền đề của lệnh — chốt chặn cho <b>lớp lỗi</b> của bug #14, không chỉ cho một nguyên nhân của nó.
/// <para>
/// Bug #14: bản sao model làm mất trạng thái nạp link, <c>ClashDetection</c> báo <b>0</b> va chạm thay
/// vì <b>479</b>, và không ai phát hiện vì "0 va chạm" trông y hệt một kết quả sạch. Nguyên nhân đã vá
/// (<c>BatchJobRunner</c> nạp lại link khi mở file), nhưng đường Ribbon/Bridge không đi qua chỗ vá đó,
/// và bất kỳ lệnh nào cũng có thể trả 0 vì tiền đề hỏng chứ không vì mô hình sạch.
/// </para>
/// <para>Quy tắc duy nhất được test ở đây: <b>tiền đề hỏng thì dừng, không trả kết quả 0 trông sạch</b>.</para>
/// </summary>
public class PreconditionTests
{
    // ── Model liên kết ───────────────────────────────────────────────────────

    [Fact]
    public void KhongCoLinkNao_ThiKhongCoGiDeSai()
    {
        var pre = Precondition.LinkedModels("ClashDetection", 0, new string[0], "includeLinkedModels");

        Assert.Equal(PreconditionVerdict.Dat, pre.Verdict);
        Assert.Empty(pre.Message);
    }

    [Fact]
    public void NapDuLink_ThiConSoNoiDungVeMoHinh()
    {
        var pre = Precondition.LinkedModels("ClashDetection", 3, new string[0], "includeLinkedModels");

        Assert.Equal(PreconditionVerdict.Dat, pre.Verdict);
    }

    /// <summary>Đúng tình huống §21: cả ba link đều chưa nạp sau SaveAs → 0 va chạm thay vì 479.</summary>
    [Fact]
    public void MoiLinkDeuChuaNap_ThiCHAN_ChuKhongTraKetQuaTrongSach()
    {
        var pre = Precondition.LinkedModels(
            "ClashDetection", 3, new[] { "KT.rvt", "KC.rvt", "MEP.rvt" }, "includeLinkedModels");

        Assert.Equal(PreconditionVerdict.Chan, pre.Verdict);
        Assert.True(pre.Blocks);
        Assert.StartsWith("E-PRECOND:", pre.Message);
        Assert.Contains("KT.rvt", pre.Message);
        // Thông báo phải nói cả hai đường đi tiếp, nếu không kỹ sư chỉ biết là bị chặn.
        Assert.Contains("Reload", pre.Message);
        Assert.Contains("includeLinkedModels", pre.Message);
    }

    /// <summary>Nạp một phần có thể là cố ý (tắt bớt link cho nhẹ máy) — cảnh báo, không chặn.</summary>
    [Fact]
    public void MotPhanChuaNap_ThiCanhBao_VaVanChayTiep()
    {
        var pre = Precondition.LinkedModels("SleeveAuto", 3, new[] { "KC.rvt" }, "includeLinkedModels");

        Assert.Equal(PreconditionVerdict.CanhBao, pre.Verdict);
        Assert.False(pre.Blocks);
        Assert.Contains("1/3", pre.Message);
    }

    // ── Tập đầu vào rỗng ─────────────────────────────────────────────────────

    [Fact]
    public void TapDauVaoRong_ThiCHAN_VaNoiRoLaNoiVeDauVao()
    {
        var pre = Precondition.NonEmptyInput("ClashDetection", "phần tử nhóm A (Ducts)", 0, "Kiểm lại categoriesA.");

        Assert.True(pre.Blocks);
        Assert.StartsWith("E-PRECOND:", pre.Message);
        Assert.Contains("Kiểm lại categoriesA.", pre.Message);
    }

    [Fact]
    public void CoDauVao_ThiKhongNoiGi()
    {
        Assert.Equal(PreconditionVerdict.Dat,
            Precondition.NonEmptyInput("ClashDetection", "phần tử nhóm A", 12, "").Verdict);
    }

    // ── Gộp ──────────────────────────────────────────────────────────────────

    /// <summary>Gộp nhiều lỗi vào một thông báo thì kỹ sư không biết sửa cái nào trước.</summary>
    [Fact]
    public void Gop_TraVeCaiCHANDauTien()
    {
        var a = Precondition.NonEmptyInput("X", "nhóm A", 0, "sửa A");
        var b = Precondition.NonEmptyInput("X", "nhóm B", 0, "sửa B");

        var gop = Precondition.First(a, b);

        Assert.Contains("nhóm A", gop.Message);
        Assert.DoesNotContain("nhóm B", gop.Message);
    }

    /// <summary>Chặn phải thắng cảnh báo dù đứng sau.</summary>
    [Fact]
    public void Gop_CHANThangCanhBao_DuDungSau()
    {
        var canhBao = Precondition.LinkedModels("X", 3, new[] { "KC.rvt" }, "includeLinkedModels");
        var chan = Precondition.NonEmptyInput("X", "nhóm A", 0, "sửa A");

        Assert.True(Precondition.First(canhBao, chan).Blocks);
    }

    [Fact]
    public void Gop_KhongCoGi_ThiDat()
    {
        Assert.Equal(PreconditionVerdict.Dat, Precondition.First().Verdict);
    }
}
