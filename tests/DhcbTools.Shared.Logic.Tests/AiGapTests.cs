using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Nhánh còn thiếu của lớp hiểu câu lệnh tiếng Việt: câu rỗng, hai lệnh sát điểm, và các trường
/// config chỉ vài lệnh mới có (đường dẫn theo đuôi file, loại phần tử, tạo layer thiếu).
/// </summary>
public class AiGapTests
{
    [Fact]
    public void Parse_CauRong_NoiRoLaCauRong()
    {
        var intent = CommandIntentParser.Parse("   ", "revit");

        Assert.Null(intent.Command);
        Assert.Equal(0, intent.Confidence);
        Assert.Equal("Câu lệnh rỗng.", intent.Explanation);
    }

    /// <summary>Đường dẫn .rte và .rvt được gán đúng trường theo đuôi file, mỗi đường dẫn dùng một lần.</summary>
    [Fact]
    public void Parse_ProjectFromTemplate_GanDuongDanTheoDuoiFile()
    {
        var intent = CommandIntentParser.Parse(
            @"tạo file từ template C:\chuan\mau.rte xuất ra C:\du-an\ra.rvt", "revit");

        Assert.Equal("ProjectFromTemplate", intent.Command);
        Assert.Equal(@"C:\chuan\mau.rte", (string?)intent.Config["templatePath"]);
        Assert.Equal(@"C:\du-an\ra.rvt", (string?)intent.Config["outputPath"]);
    }

    [Fact]
    public void Parse_TransferStandards_GanSourcePathTheoDuoiRvt()
    {
        var intent = CommandIntentParser.Parse(@"chuyển chuẩn từ C:\chuan\goc.rvt", "revit");

        Assert.Equal("TransferStandards", intent.Command);
        Assert.Equal(@"C:\chuan\goc.rvt", (string?)intent.Config["sourcePath"]);
    }

    [Fact]
    public void Parse_ParameterRuleCheck_GanRulesPathTheoDuoiJson()
    {
        var intent = CommandIntentParser.Parse(@"kiểm tra tham số theo C:\qc\quy-tac.json", "revit");

        Assert.Equal("ParameterRuleCheck", intent.Command);
        Assert.Equal(@"C:\qc\quy-tac.json", (string?)intent.Config["rulesPath"]);
    }

    /// <summary>Tên type trong nháy kép được lấy vào trường typeName và chỉ dùng một lần.</summary>
    [Fact]
    public void Parse_RouteFromLines_LayTenTrongNhayKep()
    {
        var intent = CommandIntentParser.Parse("dựng tuyến ống gió type \"Rect Duct\"", "revit");

        Assert.Equal("RouteFromLines", intent.Command);
        Assert.Equal("Rect Duct", (string?)intent.Config["typeName"]);
    }

    [Theory]
    [InlineData("dựng tuyến ống gió", "Duct")]
    [InlineData("dựng tuyến máng cáp", "CableTray")]
    [InlineData("dựng tuyến ống luồn", "Conduit")]
    [InlineData("dựng tuyến ống nước", "Pipe")]
    public void Parse_RouteFromLines_DoanLoaiPhanTuTuCau(string text, string expected)
    {
        var intent = CommandIntentParser.Parse(text, "revit");

        Assert.Equal(expected, (string?)intent.Config["elementType"]);
    }

    [Fact]
    public void Parse_LayerImport_HieuYeuCauTaoLayerThieu()
    {
        var intent = CommandIntentParser.Parse(@"nhập layer từ C:\a\layer.csv, tạo layer thiếu", "autocad");

        Assert.Equal("LayerImport", intent.Command);
        Assert.True((bool?)intent.Config["createMissing"]);
    }

    /// <summary>
    /// Câu khớp hai lệnh sát điểm nhau (xuất/nhập tham số): độ tin cậy bị hạ xuống để kỹ sư phải chọn,
    /// thay vì tool âm thầm chạy một trong hai.
    /// </summary>
    [Fact]
    public void Parse_HaiLenhSatDiem_HaDoTinCay()
    {
        var intent = CommandIntentParser.Parse("xuất tham số nhập tham số", "revit");

        Assert.Equal("ParameterExport", intent.Command);
        Assert.Contains("ParameterImport", intent.Alternatives);
        Assert.Equal(0.63, intent.Confidence, 2);
    }

    [Fact]
    public void TryParseNumber_ChuoiRong_TraFalse()
    {
        Assert.False(CommandIntentParser.TryParseNumber("   ", out _));
        Assert.False(CommandIntentParser.TryParseNumber(null, out _));
    }

    [Fact]
    public void ExtractLengthsMm_ChuoiRong_TraDanhSachRong()
    {
        Assert.Empty(CommandIntentParser.ExtractLengthsMm(string.Empty));
    }

    /// <summary>Lệnh mới có đặc tả, chưa có mã: không được chào ra cho agent.</summary>
    [Fact]
    public void CommandDescriptor_Pending_DanhDauChuaCoMaNguon()
    {
        var cmd = new CommandDescriptor("ChuaViet", "revit", "Mô tả", false).Pending();

        Assert.False(cmd.Implemented);
    }

    [Fact]
    public void CommandCatalog_DefaultBool_TheoLopConfigThat()
    {
        Assert.True(CommandCatalog.DefaultBool("includeLinkedModels"));
        Assert.False(CommandCatalog.DefaultBool("khong-co-truong-nay"));
        Assert.False(CommandCatalog.DefaultBool(null!));
    }

    [Fact]
    public void DictionarySuggester_Tokenize_ChuoiRong_TraDanhSachRong()
    {
        Assert.Empty(DictionarySuggester.Tokenize("   "));
    }

    [Fact]
    public void DictionarySuggester_NameScore_MotBenRong_Bang0()
    {
        Assert.Equal(0, DictionarySuggester.NameScore("   ", "diameter"));
    }

    /// <summary>Chỉ khác dấu tiếng Việt và hoa thường thì vẫn là cùng một tên → điểm tuyệt đối.</summary>
    [Fact]
    public void DictionarySuggester_NameScore_ChiKhacDauVaHoaThuong_Bang1()
    {
        Assert.Equal(1.0, DictionarySuggester.NameScore("Đường Kính", "duong kinh"));
    }

    [Fact]
    public void DictionarySuggester_Suggest_DictionaryNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DictionarySuggester.Suggest(Array.Empty<string>(), null!, Array.Empty<ParameterCandidate>()));
    }

    [Fact]
    public void LayerMappingSuggester_Tokenize_ChuoiRong_TraTapRong()
    {
        Assert.Empty(LayerMappingSuggester.Tokenize("   "));
    }
}
