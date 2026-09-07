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
}
