using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class MepLayoutTests
{
    [Fact]
    public void MmToFeet_VaNguocLai_RoundTrip()
    {
        Assert.Equal(3000, MepLayout.FeetToMillimetres(MepLayout.MmToFeet(3000)), 9);
        Assert.Equal(1, MepLayout.MmToFeet(304.8), 9);
    }

    [Fact]
    public void HangerPositions_DatTaiNuaKhoangCachRoiCachDeu()
    {
        var positions = MepLayout.HangerPositions(lengthFt: 10, spacingFt: 3);

        Assert.Equal(new[] { 1.5, 4.5, 7.5 }, positions);
    }

    [Fact]
    public void HangerPositions_MoiViTriNamTrongDoan()
    {
        var positions = MepLayout.HangerPositions(lengthFt: 12.7, spacingFt: 2.5);

        Assert.All(positions, p => Assert.InRange(p, 0, 12.7));
    }

    [Fact]
    public void HangerPositions_KhoangCachGiuaHaiHangerKhongVuotSpacing()
    {
        var positions = MepLayout.HangerPositions(lengthFt: 20, spacingFt: 4);

        for (var i = 1; i < positions.Count; i++)
        {
            Assert.True(positions[i] - positions[i - 1] <= 4 + 1e-9);
        }
    }

    [Fact]
    public void HangerPositions_DoanRatNgan_DatDungMotHangerOGiua()
    {
        var positions = MepLayout.HangerPositions(lengthFt: 1, spacingFt: 3);

        Assert.Single(positions);
        Assert.Equal(0.5, positions[0], 9);
    }

    [Fact]
    public void HangerPositions_DoanNganHonSpacingNhungDaiHonNuaSpacing_KhongDatTrungHaiHanger()
    {
        // Lỗi của bản cũ: vòng lặp đã đặt một hanger tại spacing/2 rồi nhánh "đảm bảo có ít nhất một
        // hanger" (lengthFt < spacingFt) đặt thêm một cái ở giữa → hai hanger chồng nhau trên đoạn ngắn.
        var positions = MepLayout.HangerPositions(lengthFt: 2, spacingFt: 3);

        Assert.Single(positions);
        Assert.Equal(1.5, positions[0], 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void HangerPositions_DoanKhongCoChieuDai_TraVeRong(double lengthFt)
    {
        Assert.Empty(MepLayout.HangerPositions(lengthFt, spacingFt: 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HangerPositions_SpacingKhongDuong_NemLoi(double spacingFt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MepLayout.HangerPositions(10, spacingFt));
    }

    [Fact]
    public void SplitPositions_CatDeuTheoChieuDaiToiDa()
    {
        var positions = MepLayout.SplitPositions(lengthFt: 25, maxSegmentFt: 10);

        Assert.Equal(new[] { 10.0, 20.0 }, positions);
    }

    [Fact]
    public void SplitPositions_DoanDaDuNgan_KhongCat()
    {
        Assert.Empty(MepLayout.SplitPositions(lengthFt: 9, maxSegmentFt: 10));
    }

    [Fact]
    public void SplitPositions_DaiDungBangMaxTrongDungSai_KhongCat()
    {
        Assert.Empty(MepLayout.SplitPositions(lengthFt: 10, maxSegmentFt: 10));
        Assert.Empty(MepLayout.SplitPositions(lengthFt: 10 + 5 / MepLayout.FeetToMm, maxSegmentFt: 10));
    }

    [Fact]
    public void SplitPositions_KhongTaoMauThuaSieuNganOCuoi()
    {
        // 20 ft + 5 mm với đoạn tối đa 10 ft: điểm cắt thứ hai tại 20 ft chỉ để lại 5 mm → bỏ.
        var positions = MepLayout.SplitPositions(lengthFt: 20 + 5 / MepLayout.FeetToMm, maxSegmentFt: 10);

        Assert.Equal(new[] { 10.0 }, positions);
    }

    [Fact]
    public void SplitPositions_MoiDoanSauKhiCatKhongVuotMax()
    {
        const double length = 33.3;
        const double max = 7;
        var positions = MepLayout.SplitPositions(length, max);

        var previous = 0.0;
        foreach (var p in positions)
        {
            Assert.True(p - previous <= max + 1e-9);
            previous = p;
        }
        Assert.True(length - previous <= max + 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void SplitPositions_MaxKhongDuong_NemLoi(double maxSegmentFt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MepLayout.SplitPositions(10, maxSegmentFt));
    }

    [Fact]
    public void Elevations_TinhDayDinhTimTheoMilimet()
    {
        var elevations = MepLayout.Elevations(minZFt: 0, maxZFt: 1);

        Assert.Equal(0, elevations.BottomMm, 9);
        Assert.Equal(304.8, elevations.TopMm, 9);
        Assert.Equal(152.4, elevations.CentreMm, 9);
    }

    [Fact]
    public void Elevations_DaoNguocDauVao_VanTraVeDayNhoHonDinh()
    {
        var elevations = MepLayout.Elevations(minZFt: 5, maxZFt: 1);

        Assert.True(elevations.BottomMm < elevations.TopMm);
        Assert.Equal(MepLayout.FeetToMillimetres(3), elevations.CentreMm, 9);
    }

    [Fact]
    public void BoundingBoxesIntersect_HaiHopChongNhau_TraVeTrue()
    {
        Assert.True(MepLayout.BoundingBoxesIntersect(
            0, 0, 0, 10, 10, 10,
            5, 5, 5, 15, 15, 15));
    }

    [Fact]
    public void BoundingBoxesIntersect_HaiHopRoiNhau_TraVeFalse()
    {
        Assert.False(MepLayout.BoundingBoxesIntersect(
            0, 0, 0, 10, 10, 10,
            11, 0, 0, 20, 10, 10));
    }

    [Fact]
    public void BoundingBoxesIntersect_ChamBienDungBangDungSai_TinhLaGiao()
    {
        Assert.True(MepLayout.BoundingBoxesIntersect(
            0, 0, 0, 10, 10, 10,
            10, 0, 0, 20, 10, 10));
    }

    [Fact]
    public void BoundingBoxesIntersect_CachNhauTrongDungSai_TinhLaGiao()
    {
        Assert.True(MepLayout.BoundingBoxesIntersect(
            0, 0, 0, 10, 10, 10,
            10.2, 0, 0, 20, 10, 10,
            toleranceFt: 0.5));
    }

    // ── IsNearAny — phép chống trùng dùng chung cho SleeveAuto và HangerAuto ──────────────

    [Fact]
    public void IsNearAny_KhongCoDiemNao_TraVeFalse()
    {
        Assert.False(MepLayout.IsNearAny(1, 2, 3, new (double, double, double)[0], toleranceFt: 1));
    }

    [Fact]
    public void IsNearAny_TrongBanKinh_TraVeTrue()
    {
        var existing = new[] { (10.0, 0.0, 0.0), (0.0, 0.0, 0.0) };
        Assert.True(MepLayout.IsNearAny(0.2, 0, 0, existing, toleranceFt: 0.5));
    }

    [Fact]
    public void IsNearAny_NgoaiBanKinh_TraVeFalse()
    {
        var existing = new[] { (0.0, 0.0, 0.0) };
        Assert.False(MepLayout.IsNearAny(0.6, 0, 0, existing, toleranceFt: 0.5));
    }

    [Fact]
    public void IsNearAny_LechTheoTrucZ_VanTinhLaTrung()
    {
        var existing = new[] { (0.0, 0.0, 0.0) };
        Assert.True(MepLayout.IsNearAny(0, 0, 0.3, existing, toleranceFt: 0.5));
    }

    [Fact]
    public void IsNearAny_DungSaiKhongDuong_TatChongTrung()
    {
        var existing = new[] { (0.0, 0.0, 0.0) };
        Assert.False(MepLayout.IsNearAny(0, 0, 0, existing, toleranceFt: 0));
    }

    [Fact]
    public void IsNearAny_DanhSachNull_TraVeFalse()
    {
        Assert.False(MepLayout.IsNearAny(0, 0, 0, null!, toleranceFt: 1));
    }
}
