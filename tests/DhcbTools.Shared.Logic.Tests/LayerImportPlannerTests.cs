using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// <c>LayerImport</c> — một tên layer xuất hiện nhiều dòng trong CSV (audit 2026-10-01, việc để lại). Bản cũ áp từng
/// dòng theo thứ tự: dòng sau ghi đè dòng trước, và layer chưa có thì xem trước báo "tạo mới" hai lần.
/// </summary>
public class LayerImportPlannerTests
{
    private static (int Row, LayerCsvRow Layer) Line(int row, string csv) => (row, LayerCsvRow.Parse(csv, row));

    [Fact]
    public void TenKhongTrung_ApMoiDong_GiuThuTu()
    {
        var plan = LayerImportPlanner.Plan(new[]
        {
            Line(2, "A-WALL,3,DASHED,25,true,Tường"),
            Line(3, "A-DOOR,4,,,,"),
        });

        Assert.Equal(new[] { 2, 3 }, plan.Rows.Select(r => r.Row));
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Conflicts);
    }

    /// <summary>Dán trùng một dòng (kể cả khác hoa thường ở tên và linetype): áp một lần, nói rõ bản lặp.</summary>
    [Fact]
    public void DongLapGiongHet_ApMotLan_GhiChuBanLap()
    {
        var plan = LayerImportPlanner.Plan(new[]
        {
            Line(2, "A-WALL,3,DASHED,25,true,Tường"),
            Line(5, "a-wall,3,dashed,25,true,Tường"),
        });

        Assert.Equal(2, Assert.Single(plan.Rows).Row);
        Assert.Empty(plan.Conflicts);
        Assert.Equal("Dòng 5: layer \"a-wall\" lặp lại dòng 2 với cùng giá trị — bỏ qua bản lặp.", Assert.Single(plan.Notes));
    }

    /// <summary>Hai giá trị khác nhau cho một layer: không đoán theo thứ tự dòng — không áp dòng nào.</summary>
    [Theory]
    [InlineData("A-WALL,5,DASHED,25,true,Tường")]          // màu ACI
    [InlineData("A-WALL,16711680,DASHED,25,true,Tường")]   // true color thay cho ACI
    [InlineData("A-WALL,3,CENTER,25,true,Tường")]          // linetype
    [InlineData("A-WALL,3,DASHED,50,true,Tường")]          // lineweight
    [InlineData("A-WALL,3,DASHED,25,false,Tường")]         // plottable
    [InlineData("A-WALL,3,DASHED,25,true,tường")]          // mô tả khác hoa thường vẫn là khác
    [InlineData("A-WALL,3,DASHED,25,true,")]               // mô tả rỗng = xoá mô tả, khác giữ nguyên
    [InlineData("A-WALL,3,DASHED,25,true")]                // thiếu cột mô tả = giữ nguyên, khác "Tường"
    [InlineData("A-WALL,,DASHED,25,true,Tường")]           // ô màu trống = giữ nguyên, khác 3: không tự trộn
    public void HaiDongKhacNhau_XungDot_KhongApDongNao(string other)
    {
        var plan = LayerImportPlanner.Plan(new[]
        {
            Line(2, "A-WALL,3,DASHED,25,true,Tường"),
            Line(3, "A-DOOR,4,,,,"),
            Line(7, other),
        });

        Assert.Equal(3, Assert.Single(plan.Rows).Row);
        Assert.Empty(plan.Notes);
        Assert.Equal(
            "Layer \"A-WALL\" có 2 dòng mang giá trị khác nhau (dòng 2, 7) — không nhập dòng nào của layer này; "
            + "sửa CSV cho thống nhất rồi nhập lại.",
            Assert.Single(plan.Conflicts));
    }

    /// <summary>Đảo thứ tự dòng không đổi kết quả — đúng chỗ bản cũ hỏng (dòng sau thắng).</summary>
    [Fact]
    public void KetQuaKhongPhuThuocThuTuDong()
    {
        var lines = new[]
        {
            Line(2, "A-WALL,3,,,,"),
            Line(3, "A-WALL,5,,,,"),
            Line(4, "A-DOOR,4,,,,"),
            Line(5, "A-DOOR,4,,,,"),
        };

        var forward = LayerImportPlanner.Plan(lines);
        var reversed = LayerImportPlanner.Plan(lines.Reverse());

        Assert.Equal(new[] { "A-DOOR" }, forward.Rows.Select(r => r.Layer.Name));
        Assert.Equal(forward.Rows.Select(r => r.Layer.Name), reversed.Rows.Select(r => r.Layer.Name));
        Assert.Equal(forward.Conflicts.Count, reversed.Conflicts.Count);
        Assert.Equal(forward.Notes.Count, reversed.Notes.Count);
    }

    [Fact]
    public void BaDongTrongDoMotDongKhac_VanLaXungDot()
    {
        var plan = LayerImportPlanner.Plan(new[]
        {
            Line(2, "A-WALL,3,,,,"),
            Line(3, "A-WALL,3,,,,"),
            Line(4, "A-WALL,5,,,,"),
        });

        Assert.Empty(plan.Rows);
        Assert.Empty(plan.Notes);
        Assert.Contains("có 3 dòng mang giá trị khác nhau (dòng 2, 3, 4)", Assert.Single(plan.Conflicts));
    }

    [Fact]
    public void DauVaoRong_VaNull()
    {
        Assert.Throws<ArgumentNullException>(() => LayerImportPlanner.Plan(null!));
        var plan = LayerImportPlanner.Plan(Array.Empty<(int, LayerCsvRow)>());
        Assert.Empty(plan.Rows);
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Conflicts);
    }
}
