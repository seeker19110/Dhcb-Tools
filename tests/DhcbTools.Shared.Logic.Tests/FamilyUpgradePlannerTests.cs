using System.Collections.Generic;
using System.IO;
using DhcbTools.Shared.Logic.Families;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>§63: phần thuần của FamilyUpgrade — ghép đường dẫn đích, chặn ghi đè nguồn, câu chữ.</summary>
public class FamilyUpgradePlannerTests
{
    private static string P(params string[] parts) => Path.GetFullPath(Path.Combine(parts));

    [Fact]
    public void TargetPath_GiuCayThuMucCon()
    {
        var src = P("D:", "lib");
        var outp = P("D:", "out", "2026");
        Assert.Equal(Path.Combine(outp, "mep", "sleeve.rfa"), FamilyUpgradePlanner.TargetPath(src, outp, P("D:", "lib", "mep", "sleeve.rfa")));
        Assert.Equal(Path.Combine(outp, "sleeve.rfa"), FamilyUpgradePlanner.TargetPath(src, outp, P("D:", "lib", "sleeve.rfa")));
        // File ngoài cây nguồn: rơi về tên file, không ghi ra ngoài outputFolder.
        Assert.Equal(Path.Combine(outp, "la.rfa"), FamilyUpgradePlanner.TargetPath(src, outp, P("D:", "khac", "la.rfa")));
    }

    [Fact]
    public void ValidateFolders_ChanDichNamTrongNguon()
    {
        Assert.Null(FamilyUpgradePlanner.ValidateFolders(P("D:", "lib"), P("D:", "out")));
        Assert.Contains("nằm trong sourceFolder", FamilyUpgradePlanner.ValidateFolders(P("D:", "lib"), P("D:", "lib", "2026")));
        Assert.Contains("nằm trong sourceFolder", FamilyUpgradePlanner.ValidateFolders(P("D:", "lib"), P("D:", "lib")));
        // "libx" chỉ trùng tiền tố chuỗi chứ không nằm trong "lib" — không được báo lỗi.
        Assert.Null(FamilyUpgradePlanner.ValidateFolders(P("D:", "lib"), P("D:", "libx")));
        Assert.Contains("Thiếu sourceFolder", FamilyUpgradePlanner.ValidateFolders(" ", P("D:", "out")));
        Assert.Contains("Thiếu outputFolder", FamilyUpgradePlanner.ValidateFolders(P("D:", "lib"), ""));
    }

    [Fact]
    public void Plan_ChiLayRfa_SapTheoDuongDan_CatTheoMaxFiles()
    {
        var src = P("D:", "lib");
        var outp = P("D:", "out");
        var files = new List<string> { P("D:", "lib", "b.rfa"), P("D:", "lib", "a.rfa"), P("D:", "lib", "ghi-chu.txt"), P("D:", "lib", "c.RFA") };
        var plan = FamilyUpgradePlanner.Plan(src, outp, files, 0);
        Assert.Equal(new[] { "a.rfa", "b.rfa", "c.RFA" }, plan.ConvertAll(i => i.Name));
        Assert.Equal(2, FamilyUpgradePlanner.Plan(src, outp, files, 2).Count);
        Assert.Equal(3, FamilyUpgradePlanner.Plan(src, outp, files, 9).Count);
    }

    [Fact]
    public void CauChu()
    {
        var item = new FamilyUpgradeItem(P("D:", "lib", "a.rfa"), P("D:", "out", "a.rfa"));
        Assert.EndsWith("(đã tìm cả thư mục con).", FamilyUpgradePlanner.NoFilesMessage("D:/lib", true));
        Assert.Contains("recursive:true", FamilyUpgradePlanner.NoFilesMessage("D:/lib", false));
        Assert.StartsWith("Sẽ nâng cấp a.rfa → ", FamilyUpgradePlanner.PreviewLine(item));
        Assert.Equal("[Xem trước] Sẽ nâng cấp 3 family sang định dạng Revit 2026. Bản gốc giữ nguyên.", FamilyUpgradePlanner.PreviewSummary(3, "2026"));
        Assert.Contains("bỏ qua (overwrite=false)", FamilyUpgradePlanner.SkipExistingMessage(item));
        Assert.Contains("từ bản Revit mới hơn", FamilyUpgradePlanner.OpenFailedMessage(item, "2024", "corrupt"));
        Assert.Equal("Đã nâng cấp 5 family sang Revit 2026 → D:/out.", FamilyUpgradePlanner.DoneSummary(5, 0, 0, "D:/out", "2026"));
        Assert.Equal("Đã nâng cấp 5 family sang Revit 2026 → D:/out; bỏ qua 2; lỗi 1.", FamilyUpgradePlanner.DoneSummary(5, 2, 1, "D:/out", "2026"));
    }
}
