using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Kế hoạch đổi tên của ElementRename: chống trùng theo TỪNG phạm vi (type trùng tên ở hai family khác nhau là hợp lệ
/// trong Revit), bộ đếm chạy lại mỗi phạm vi, và nhận ra hoán đổi tên cần đi qua tên tạm.
/// </summary>
public class RenamePlannerTests
{
    [Theory]
    [InlineData("types", "Types")]
    [InlineData(" ViewTemplates ", "ViewTemplates")]
    [InlineData("FILTERS", "Filters")]
    [InlineData("materials", "Materials")]
    [InlineData("Families", null)]
    [InlineData(null, null)]
    public void NormalizeKind(string? input, string? expected)
    {
        Assert.Equal(expected, RenamePlanner.NormalizeKind(input));
    }

    [Fact]
    public void RenameItem_TuThemTokenName()
    {
        var item = new RenameItem("Pipe Types", "Thép");
        Assert.Equal("Thép", item.Values["Name"]);

        var withValues = new RenameItem("", "A", new Dictionary<string, string> { ["Name"] = "khác" });
        Assert.Equal("khác", withValues.Values["Name"]);

        var nulls = new RenameItem(null!, null!);
        Assert.Equal(string.Empty, nulls.Scope);
        Assert.Equal(string.Empty, nulls.Name);
    }

    [Fact]
    public void Plan_ChongTrungTheoPhamVi_VaDemLaiMoiPhamVi()
    {
        var items = new[]
        {
            new RenameItem("Pipe Types", "PVC"),
            new RenameItem("Duct Types", "Tôn"),
            new RenameItem("Pipe Types", "Thép"),
        };
        var others = new[] { ("Pipe Types", "ONG-02"), ("Duct Types", "ONG-01") };
        var pattern = new NamePattern("ONG-{n:00}");

        var names = RenamePlanner.Plan(items, others, pattern, out var notes);

        // Pipe Types: ONG-01 (trống), ONG-02 đã có ở type ngoài lô → thêm hậu tố.
        Assert.Equal("ONG-01", names[0]);
        Assert.Equal("ONG-02 (2)", names[2]);
        // Duct Types: bộ đếm chạy lại từ 1; ONG-01 đã có trong phạm vi này → thêm hậu tố.
        Assert.Equal("ONG-01 (2)", names[1]);
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.StartsWith("Pipe Types: ") && n.Contains("ONG-02 (2)"));
        Assert.Contains(notes, n => n.StartsWith("Duct Types: "));
    }

    [Fact]
    public void Plan_PhamViRong_KhongTienToTrongGhiChu()
    {
        var items = new[] { new RenameItem("", "Filter A"), new RenameItem("", "Filter B") };
        var names = RenamePlanner.Plan(items, Array.Empty<(string, string)>(), new NamePattern("F") , out var notes);
        Assert.Equal(new[] { "F", "F (2)" }, names);
        Assert.Single(notes);
        Assert.StartsWith("\"F\"", notes[0]);
    }

    [Fact]
    public void Plan_TimThayChuoiThuong()
    {
        var items = new[] { new RenameItem("", "Ống thép D100"), new RenameItem("", "Ống PVC D50") };
        var names = RenamePlanner.Plan(items, Array.Empty<(string, string)>(), new NamePattern("{Name}") { Find = "Ống ", Replace = "ONG_", FindIsRegex = false }, out _);
        Assert.Equal(new[] { "ONG_thép D100", "ONG_PVC D50" }, names);
    }

    [Fact]
    public void Plan_RegexHong_NemArgumentException()
    {
        var items = new[] { new RenameItem("", "A") };
        Assert.ThrowsAny<ArgumentException>(() => RenamePlanner.Plan(items, Array.Empty<(string, string)>(), new NamePattern("{Name}") { Find = "(" }, out _));
    }

    [Fact]
    public void NeedsTemporaryNames_HoanDoiTrongCungPhamVi()
    {
        var items = new[] { new RenameItem("S", "A"), new RenameItem("S", "B") };
        Assert.True(RenamePlanner.NeedsTemporaryNames(items, new[] { "B", "A" }));
    }

    [Fact]
    public void NeedsTemporaryNames_KhacPhamVi_HoacKhongDoi_KhongCan()
    {
        var crossScope = new[] { new RenameItem("S1", "A"), new RenameItem("S2", "B") };
        Assert.False(RenamePlanner.NeedsTemporaryNames(crossScope, new[] { "B", "A" }));

        // Phần tử giữ nguyên tên không tính là "tên cũ sắp nhả ra".
        var unchanged = new[] { new RenameItem("S", "A"), new RenameItem("S", "B") };
        Assert.False(RenamePlanner.NeedsTemporaryNames(unchanged, new[] { "A", "C" }));
    }
}
