using DhcbTools.Shared.Hosting;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class PreviewSnapshotTests
{
    [Fact]
    public void Changed_config_invalidates_preview()
    {
        Assert.NotEqual(PreviewSnapshot.Capture("A", Array.Empty<string>()),
            PreviewSnapshot.Capture("B", Array.Empty<string>()));
    }

    [Fact]
    public void Changed_file_is_detected_even_with_same_size_and_timestamp()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "old");
            var timestamp = File.GetLastWriteTimeUtc(path);
            var before = PreviewSnapshot.Capture("A", new[] { path, path, " " });
            Assert.Equal(before, PreviewSnapshot.Capture("A", new[] { path }));
            File.WriteAllText(path, "new");
            File.SetLastWriteTimeUtc(path, timestamp);
            Assert.NotEqual(before, PreviewSnapshot.Capture("A", new[] { path }));
            File.Delete(path);
            Assert.NotEqual(before, PreviewSnapshot.Capture("A", new[] { path }));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void File_held_open_for_writing_by_another_app_is_still_hashed()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "dwg");
            var expected = PreviewSnapshot.Capture("A", new[] { path });
            // Giống AutoCAD giữ DWG đang mở: có quyền ghi, chỉ cho người khác đọc.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                Assert.Equal(expected, PreviewSnapshot.Capture("A", new[] { path }));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Exclusively_locked_file_falls_back_to_metadata_instead_of_throwing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "dwg");
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Contains("locked:", PreviewSnapshot.Capture("A", new[] { path }));
        }
        finally { File.Delete(path); }
    }
}
