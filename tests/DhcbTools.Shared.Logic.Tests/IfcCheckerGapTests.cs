using DhcbTools.Shared.Logic.Ifc;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Nhánh còn thiếu của bộ kiểm IFC: quy tắc trần số lượng, ba mức nghiêm trọng khi in báo cáo,
/// và các đầu vào hỏng ở cửa vào (spec null, file quy tắc rỗng).
/// </summary>
public class IfcCheckerGapTests
{
    private const string HaiTuong = """
        ISO-10303-21;
        HEADER;
        FILE_SCHEMA(('IFC4'));
        ENDSEC;
        DATA;
        #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong 1',$,$,$,$,$,$);
        #2=IFCWALL('1Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong 2',$,$,$,$,$,$);
        ENDSEC;
        END-ISO-10303-21;
        """;

    [Fact]
    public void Check_SpecNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => IfcChecker.Check(HaiTuong, null!));
    }

    /// <summary>Vượt trần số lượng: báo lỗi kèm cả số đếm thật lẫn con số quy tắc đòi.</summary>
    [Fact]
    public void VuotTranSoLuong_BaoLoiKemCaHaiConSo()
    {
        var spec = new IfcCheckSpec { Rules = { new IfcTypeRule { Type = "IfcWall", MaxCount = 1 } } };

        var result = IfcChecker.Check(HaiTuong, spec);

        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("có 2, quy tắc yêu cầu tối đa 1."));
    }

    [Fact]
    public void DuoiTran_KhongBaoGi()
    {
        var spec = new IfcCheckSpec { Rules = { new IfcTypeRule { Type = "IfcWall", MaxCount = 5 } } };

        Assert.True(IfcChecker.Check(HaiTuong, spec).Ok);
    }

    /// <summary>Bản in phải phân biệt được ba mức — đọc log mà không biết mức nào là lỗi thì vô dụng.</summary>
    [Fact]
    public void Render_PhanBietDuBaMucNghiemTrong()
    {
        var result = new IfcCheckResult(
            new[]
            {
                new IfcFinding(IfcSeverity.Loi, "thiếu tường"),
                new IfcFinding(IfcSeverity.CanhBao, "không khai lược đồ"),
                new IfcFinding(IfcSeverity.ThongTin, "925.815 thực thể"),
            },
            entityCount: 3,
            schema: "IFC4");

        var text = result.Render();

        Assert.Contains("[Lỗi] thiếu tường", text);
        Assert.Contains("[Cảnh báo] không khai lược đồ", text);
        Assert.Contains("[Thông tin] 925.815 thực thể", text);
        Assert.Contains("Không đạt: 1 lỗi, 1 cảnh báo.", text);
    }

    [Fact]
    public void Render_KhongCoLoiVaKhongKhaiLuocDo()
    {
        var result = new IfcCheckResult(Array.Empty<IfcFinding>(), entityCount: 0, schema: string.Empty);

        Assert.Contains("(không khai)", result.Render());
        Assert.Contains("Đạt: không có lỗi.", result.Render());
    }

    /// <summary>File không khai FILE_SCHEMA: cảnh báo (bên nhận không biết đọc theo bản IFC nào), không phải lỗi.</summary>
    [Fact]
    public void KhongKhaiLuocDo_ChiCanhBao()
    {
        var khongCoSchema = HaiTuong.Replace("FILE_SCHEMA(('IFC4'));\n", string.Empty);

        var result = IfcChecker.Check(khongCoSchema, new IfcCheckSpec());

        Assert.True(result.Ok);
        Assert.Equal(1, result.WarningCount);
        Assert.Contains("[Cảnh báo]", result.Render());
    }

    [Fact]
    public void IfcCheckSpec_JsonNull_NemArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => IfcCheckSpec.FromJson("null"));

        Assert.Contains("rỗng", ex.Message);
    }
}
