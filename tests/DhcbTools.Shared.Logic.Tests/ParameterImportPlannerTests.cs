using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// <c>ParameterImport</c> — tham số type lặp ở mọi dòng cùng type (audit 2026-10-01 vòng 2). Bản cũ ghi từng dòng,
/// so với giá trị HIỆN TẠI: sửa dòng giữa thì dòng sau (giá trị cũ của bản xuất) ghi đè lại, kết quả phụ thuộc
/// thứ tự dòng và bản xem trước báo "sẽ cập nhật 2" cho một việc không xảy ra.
/// </summary>
public class ParameterImportPlannerTests
{
    private const long TypeA = 900;

    /// <summary>Ba cửa cùng type A, cột "Fire Rating" (tham số type), kỹ sư sửa dòng giữa 60 → 90.</summary>
    private static List<ParameterCell> FireRatingEditedInMiddle() => new()
    {
        new ParameterCell(2, TypeA, "Fire Rating", "60", sameAsModel: true, typeLevel: true),
        new ParameterCell(3, TypeA, "Fire Rating", "90", sameAsModel: false, typeLevel: true),
        new ParameterCell(4, TypeA, "Fire Rating", "60", sameAsModel: true, typeLevel: true),
    };

    [Fact]
    public void ThamSoType_SuaMotDong_GhiMotLan_DongCuBoQua()
    {
        var plan = ParameterImportPlanner.Plan(FireRatingEditedInMiddle());

        var write = Assert.Single(plan.Writes);
        Assert.Equal("90", write.Value);
        Assert.Equal(3, write.Row);
        Assert.Equal(2, plan.Unchanged);
        Assert.Empty(plan.Conflicts);
        var note = Assert.Single(plan.Notes);
        Assert.Contains("MỌI phần tử cùng type", note);
        Assert.Contains("dòng 3", note);
        Assert.Contains("2 dòng khác còn giá trị cũ", note);
    }

    /// <summary>Đảo thứ tự dòng không được đổi kết quả — đúng chỗ bản cũ hỏng (dòng sửa nằm trước hay sau).</summary>
    [Fact]
    public void ThamSoType_KetQuaKhongPhuThuocThuTuDong()
    {
        var forward = ParameterImportPlanner.Plan(FireRatingEditedInMiddle());
        var reversed = ParameterImportPlanner.Plan(Enumerable.Reverse(FireRatingEditedInMiddle()));

        Assert.Equal(forward.Writes.Select(w => (w.ElementId, w.Value)), reversed.Writes.Select(w => (w.ElementId, w.Value)));
        Assert.Equal(forward.Unchanged, reversed.Unchanged);
    }

    /// <summary>Kéo giá trị mới xuống cả cột (fill-down trong Excel): vẫn một lần ghi, ghi chú liệt kê các dòng.</summary>
    [Fact]
    public void ThamSoType_SuaCaCotCungGiaTri_VanMotLanGhi()
    {
        var plan = ParameterImportPlanner.Plan(new[]
        {
            new ParameterCell(2, TypeA, "Fire Rating", "90", false, true),
            new ParameterCell(3, TypeA, "Fire Rating", "90", false, true),
        });

        Assert.Single(plan.Writes);
        Assert.Equal(1, plan.Unchanged);
        Assert.Contains("dòng 2, 3", Assert.Single(plan.Notes));
    }

    /// <summary>Hai giá trị mới khác nhau cho cùng một type: không đoán — không ghi, chỉ rõ dòng nào mang gì.</summary>
    [Fact]
    public void ThamSoType_HaiGiaTriMoiKhacNhau_XungDotKhongGhi()
    {
        var plan = ParameterImportPlanner.Plan(new[]
        {
            new ParameterCell(2, TypeA, "Fire Rating", "60", true, true),
            new ParameterCell(3, TypeA, "Fire Rating", "90", false, true),
            new ParameterCell(4, TypeA, "Fire Rating", "120", false, true),
        });

        Assert.Empty(plan.Writes);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Contains("\"Fire Rating\" của type 900", conflict);
        Assert.Contains("dòng 3 = \"90\"", conflict);
        Assert.Contains("dòng 4 = \"120\"", conflict);
    }

    /// <summary>Tham số instance: mỗi phần tử một nhóm — chỉ ô đổi được ghi, không ghi chú "cả type".</summary>
    [Fact]
    public void ThamSoInstance_ChiGhiODoi()
    {
        var plan = ParameterImportPlanner.Plan(new[]
        {
            new ParameterCell(2, 11, "Mark", "D-01", true, false),
            new ParameterCell(3, 12, "Mark", "D-99", false, false),
            new ParameterCell(3, 12, "Comments", "", true, false),
        });

        Assert.Equal(12, Assert.Single(plan.Writes).ElementId);
        Assert.Equal(2, plan.Unchanged);
        Assert.Empty(plan.Notes);
    }

    /// <summary>Cùng một ElementId xuất hiện hai lần với hai giá trị mới: cũng là xung đột, không lấy dòng sau.</summary>
    [Fact]
    public void ThamSoInstance_TrungDong_XungDot()
    {
        var plan = ParameterImportPlanner.Plan(new[]
        {
            new ParameterCell(2, 11, "Mark", "D-01", false, false),
            new ParameterCell(7, 11, "Mark", "D-02", false, false),
        });

        Assert.Empty(plan.Writes);
        Assert.Contains("\"Mark\" của phần tử 11", Assert.Single(plan.Conflicts));
    }

    [Fact]
    public void DauVaoRong_VaNull()
    {
        Assert.Throws<ArgumentNullException>(() => ParameterImportPlanner.Plan(null!));
        var plan = ParameterImportPlanner.Plan(Array.Empty<ParameterCell>());
        Assert.Empty(plan.Writes);
        Assert.Equal(0, plan.Unchanged);

        var cell = new ParameterCell(2, 1, null!, null!, false, false);
        Assert.Equal(string.Empty, cell.Parameter);
        Assert.Equal(string.Empty, cell.Value);
    }
}
