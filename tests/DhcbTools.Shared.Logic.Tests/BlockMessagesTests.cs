using System.Collections.Generic;
using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>§57: kỹ sư AutoCAD khai sai tên block/tag phải được chỉ ngay cái có thật.</summary>
public class BlockMessagesTests
{
    [Fact]
    public void BlockNotFound_LietKeTheoSoLuong_GiamDan_TranMuoi()
    {
        var present = new List<KeyValuePair<string, int>>
        {
            new("AMB013", 4), new("AMB006", 10), new("AVE_RENDER", 8), new("AB00522AAM01", 1), new("AVE_GLOBAL", 1),
        };
        var s = BlockMessages.BlockNotFound("*", present);
        Assert.StartsWith("Không tìm thấy Block \"*\" trong Model Space. Block có trong bản vẽ (5 tên): AMB006 ×10, AVE_RENDER ×8, AMB013 ×4, AB00522AAM01 ×1, AVE_GLOBAL ×1.", s);

        var many = new List<KeyValuePair<string, int>>();
        for (var i = 0; i < 13; i++) many.Add(new("B" + i, 13 - i));
        var m = BlockMessages.BlockNotFound("X", many);
        Assert.Contains("(13 tên)", m);
        Assert.Contains("B9 ×4 … và 3 tên nữa.", m);
        Assert.DoesNotContain("B10", m);
    }

    [Fact]
    public void BlockNotFound_KhongCoBlockNao()
    {
        Assert.Equal("Không tìm thấy Block \"X\" trong Model Space. Model Space không có block reference nào.",
            BlockMessages.BlockNotFound("X", new List<KeyValuePair<string, int>>()));
    }

    [Fact]
    public void AttributeMissingWarning_KhongThieu_Null_ThieuThiLietKeTag()
    {
        Assert.Null(BlockMessages.AttributeMissingWarning(0, 5, "TAG", new List<string>()));
        var w = BlockMessages.AttributeMissingWarning(3, 10, "TAG", new List<string> { "NO", "DESC", "no" });
        Assert.Equal("3/10 block không có attribute \"TAG\" — tag có thật: DESC, NO. Chạy thật sẽ bỏ qua những block này.", w);
        Assert.Equal("2/2 block không có attribute nào — block này không có attribute nào. Chạy thật sẽ bỏ qua những block này.",
            BlockMessages.AttributeMissingWarning(2, 2, null, new List<string>()));
    }
}
