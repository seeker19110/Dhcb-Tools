using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>§58: hai điểm vướng của vai MEP (§57) — lỗi thiếu family phải liệt kê family có thật; ConnectorChecker ra CSV.</summary>
public class FamilyCandidatesConnectorReportTests
{
    private static List<FamilySymbolInfo> Symbols() => new()
    {
        new("M_Round Duct", "Standard", "Ducts", 0),
        new("REDY_Pipe_ Support (None Insulation)", "Standard", "Pipe Accessories", 630),
        new("REDY_Pipe_ Support (drain pipe)", "Standard", "Pipe Accessories", 377),
        new("Pipe Support chiller", "DN100", "Pipe Accessories", 4),
        new("HeatRecoveryUnit", "Type 1", "Mechanical Equipment", 37),
        new("Sleeve_Round", "DN50", "Generic Models", 0),
        new("Round Elbow", "1.5 D", "Duct Fittings", 738),
        new("E1 30 x 42 Horizontal", "E1", "Title Blocks", 16),
    };

    [Fact]
    public void Rank_ChuThichXuongCuoi_VaNhanDienCategory()
    {
        var order = FamilyCandidates.Rank("DHCB_Hanger", Symbols(), new string[0]).Select(s => s.Category).ToList();
        Assert.Contains(order.Last(), new[] { "Duct Fittings", "Title Blocks" });
        Assert.Contains(order[order.Count - 2], new[] { "Duct Fittings", "Title Blocks" });
        Assert.True(FamilyCandidates.IsAnnotationCategory("Generic Annotations"));
        Assert.True(FamilyCandidates.IsAnnotationCategory("Pipe Tags"));
        Assert.False(FamilyCandidates.IsAnnotationCategory("Pipe Accessories"));
    }

    [Fact]
    public void Rank_FittingXuongCuoi_DuNhieuInstanceNhat()
    {
        var order = FamilyCandidates.Rank("DHCB_Hanger", Symbols(), new string[0]).Select(s => s.Family).ToList();
        Assert.Contains("Round Elbow", order.Skip(order.Count - 2));   // hai chỗ cuối là fitting và khung tên
        Assert.Equal("REDY_Pipe_ Support (None Insulation)", order[0]);
    }

    [Fact]
    public void NotFound_UuTienTenGanGiong_RoiCategory_RoiSoInstance()
    {
        var msg = FamilyCandidates.NotFoundMessage("DHCB_Hanger", Symbols(), new[] { "Pipe Accessories" });
        Assert.StartsWith("Không tìm thấy FamilySymbol \"DHCB_Hanger\" trong mô hình. Family có trong mô hình (8 type), gần nhất trước: ", msg);
        // Không tên nào chứa "dhcb"/"hanger" → category ưu tiên đứng trước, trong đó nhiều instance trước.
        var order = FamilyCandidates.Rank("DHCB_Hanger", Symbols(), new[] { "Pipe Accessories" }).Select(s => s.Family).ToList();
        Assert.Equal("REDY_Pipe_ Support (None Insulation)", order[0]);
        Assert.Equal("REDY_Pipe_ Support (drain pipe)", order[1]);
        Assert.Equal("Pipe Support chiller", order[2]);
        Assert.Contains("REDY_Pipe_ Support (None Insulation): Standard (Pipe Accessories, 630 instance)", msg);
        Assert.EndsWith("Khai tên family hoặc \"Family: Type\".", msg);
    }

    [Fact]
    public void NotFound_TuKhoaTrungTen_DungDauDuItInstance()
    {
        var order = FamilyCandidates.Rank("DHCB_Sleeve", Symbols(), new[] { "Pipe Accessories" }).Select(s => s.Family).ToList();
        Assert.Equal("Sleeve_Round", order[0]);   // chứa "sleeve" dù 0 instance và không thuộc category ưu tiên
    }

    [Fact]
    public void NotFound_TranTamVaKhongCoFamily()
    {
        var many = Enumerable.Range(0, 12).Select(i => new FamilySymbolInfo("F" + i, "T", "Generic Models", i)).ToList();
        var msg = FamilyCandidates.NotFoundMessage("X", many);
        Assert.Contains("(12 type)", msg);
        Assert.Contains("… và 4 type nữa (FamilyAudit liệt kê đủ)", msg);
        Assert.Equal("Không tìm thấy FamilySymbol \"X\" trong mô hình. Mô hình không có family nạp được nào (FamilySymbol).",
            FamilyCandidates.NotFoundMessage("X", new List<FamilySymbolInfo>()));
    }

    [Fact]
    public void ConnectorReport_Csv_VaMessage_VaSummary()
    {
        var rows = new List<OpenConnectorRow>
        {
            new(1709919, "Pipes", 19950.24, 35010, 11700, "DomainPiping", "Round", "LEVEL 02"),
            new(5, "Duct \"A\", B", 1, 2.55, 3, "DomainHvac", "Rectangular", "L1"),
        };
        var csv = ConnectorReport.Csv(rows);
        var lines = csv.TrimEnd('\n').Split('\n');
        Assert.Equal("ElementId,Category,Level,Domain,Shape,X_mm,Y_mm,Z_mm", lines[0]);
        Assert.Equal("1709919,Pipes,LEVEL 02,DomainPiping,Round,19950.2,35010.0,11700.0", lines[1]);
        Assert.Equal("5,\"Duct \"\"A\"\", B\",L1,DomainHvac,Rectangular,1.0,2.5,3.0", lines[2]);
        Assert.Equal("Element 1709919 at (19950.2,35010.0,11700.0) mm - DomainPiping", ConnectorReport.MessageLine(rows[0]));
        Assert.Equal("Tìm thấy 2 connector hở trên 2 phần tử.", ConnectorReport.Summary(2, 2, null));
        Assert.Equal("Tìm thấy 2 connector hở trên 2 phần tử. CSV: \"c.csv\".", ConnectorReport.Summary(2, 2, "c.csv"));
        Assert.Equal(ConnectorReport.Header + "\n", ConnectorReport.Csv(new List<OpenConnectorRow>()));
        Assert.Equal(string.Empty, ConnectorReport.SkippedNote(0));
        Assert.Equal(" 3 phần tử không đọc được connector (bỏ qua) — con số hở là cận dưới.", ConnectorReport.SkippedNote(3));
    }

    [Fact]
    public void HealthReportNotes_ConnectorScan()
    {
        Assert.Equal(string.Empty, DhcbTools.Shared.Logic.Checks.HealthReportNotes.ConnectorScanNote(0, null));
        Assert.Equal(" (2 phần tử không đọc được — cận dưới)", DhcbTools.Shared.Logic.Checks.HealthReportNotes.ConnectorScanNote(2, null));
        Assert.Equal(" (quét đổ giữa chừng: boom — số đếm dở)", DhcbTools.Shared.Logic.Checks.HealthReportNotes.ConnectorScanNote(5, "boom"));
    }
}
