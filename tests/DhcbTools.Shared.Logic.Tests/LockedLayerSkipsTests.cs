using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Audit 2026-10-01 vòng 2: lệnh ghi AutoCAD bỏ qua đối tượng trên layer khoá thay vì sập vì eOnLockedLayer.</summary>
public class LockedLayerSkipsTests
{
    [Fact]
    public void KhongBoQuaGi_KhongCoCauBao()
    {
        var skips = new LockedLayerSkips();

        Assert.Equal(0, skips.Count);
        Assert.Null(skips.Message("attribute"));
    }

    [Fact]
    public void GomTheoLayer_KhongPhanBietHoaThuong_NhieuNhatTruoc()
    {
        var skips = new LockedLayerSkips();
        skips.Add("KHUNG-TEN");
        skips.Add("Ghi-chu");
        skips.Add("khung-ten");
        skips.Add(null!);

        Assert.Equal(4, skips.Count);
        Assert.Equal(
            "Bỏ qua 4 đối tượng văn bản nằm trên layer đang khoá (\"KHUNG-TEN\" ×2, \"\" ×1, \"Ghi-chu\" ×1) — "
            + "mở khoá layer rồi chạy lại nếu muốn sửa cả chúng.",
            skips.Message("đối tượng văn bản"));
    }

    [Fact]
    public void QuaNhieuLayer_CatGonVaNoiConBaoNhieu()
    {
        var skips = new LockedLayerSkips();
        for (var i = 0; i < LockedLayerSkips.MaxLayers + 2; i++)
        {
            skips.Add("L" + i);
        }

        var message = skips.Message("entity")!;
        Assert.Contains("\"L0\" ×1", message);
        Assert.DoesNotContain("\"L6\"", message);
        Assert.Contains("… và 2 layer khác", message);
    }
}
