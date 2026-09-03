using DhcbTools.Shared.Logic.Batch;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Journal khởi động Revit cho batch.
/// <para>
/// Có test riêng vì vòng kiểm thử thật đầu tiên (2026-09-03) chết đúng ở đây: journal có thêm dòng
/// <c>Jrn.Directive "DocSymbol", "[]"</c>, Revit không bind được khi chưa có document nào mở, coi journal
/// sai nhịp và dừng playback ngay dòng đó. Revit rồi treo ở một hộp thoại 10 phút, add-in không bao giờ
/// chạy, và runner báo nhầm là "chưa cài add-in". Không lỗi biên dịch nào bắt được chuyện này.
/// </para>
/// </summary>
public class RevitJournalGenTests
{
    /// <summary>Dòng làm hỏng playback — chốt chặn để không ai vô tình thêm lại.</summary>
    [Fact]
    public void KhongDuocCoDocSymbol()
    {
        Assert.DoesNotContain("DocSymbol", RevitJournalGen.Build());
    }

    [Fact]
    public void CoDuHaiChiThiTatHopThoaiLoi()
    {
        var journal = RevitJournalGen.Build();

        Assert.Contains("PerformAutomaticActionInErrorDialog", journal);
        Assert.Contains("PermissiveJournal", journal);
    }

    /// <summary>Journal phải khai báo Jrn trước khi dùng, nếu không Revit từ chối ngay dòng đầu.</summary>
    [Fact]
    public void KhaiBaoJrnTruocKhiDung()
    {
        var lines = RevitJournalGen.Build()
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var declare = lines.FindIndex(l => l.StartsWith("Set Jrn", StringComparison.Ordinal));
        var firstUse = lines.FindIndex(l => l.StartsWith("Jrn.", StringComparison.Ordinal));

        Assert.True(declare >= 0, "thiếu 'Set Jrn = CrsJournalScript'");
        Assert.True(firstUse > declare, "dùng Jrn trước khi khai báo");
    }

    /// <summary>Mọi dòng phải là chú thích, khai báo, hoặc chỉ thị — không lệnh lạ nào lọt vào.</summary>
    [Fact]
    public void ChiGomDongHopLe()
    {
        foreach (var line in RevitJournalGen.Build().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var ok = line.StartsWith("'", StringComparison.Ordinal)
                     || line.StartsWith("Dim ", StringComparison.Ordinal)
                     || line.StartsWith("Set ", StringComparison.Ordinal)
                     || line.StartsWith("Jrn.Directive", StringComparison.Ordinal);

            Assert.True(ok, $"dòng journal không hợp lệ: \"{line}\"");
        }
    }
}
