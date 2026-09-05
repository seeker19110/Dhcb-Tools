using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DhcbTools.Shared.Logic.Bcf;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Xuất BCF (đề xuất B3 — thay việc chụp màn hình va chạm dán vào Word gửi tư vấn). Toàn bộ là XML + zip
/// nên kiểm được đúng cách một máy đọc BCF sẽ làm: <b>mở lại chính file vừa ghi</b> và đọc ra.
/// <para>
/// Ba thứ được chốt: ① IFC GUID đi và về không mất bit nào — sai một bit là file BCF chỉ vào nhầm phần tử;
/// ② cấu trúc zip và thứ tự thẻ đúng đặc tả 2.1, vì máy đọc nghiêm ngặt từ chối cả file khi sai thứ tự;
/// ③ số luôn dấu chấm thập phân, không phụ thuộc culture của máy chạy.
/// </para>
/// </summary>
public class BcfTests
{
    // ── IFC GUID ─────────────────────────────────────────────────────────────

    [Fact]
    public void IfcGuid_GuidRong_LaHaiMuoiHaiSoKhong()
    {
        Assert.Equal(new string('0', 22), IfcGuid.From(Guid.Empty));
    }

    [Fact]
    public void IfcGuid_LuonDaiDungHaiMuoiHaiKyTu_VaChiDungBangChuIfc()
    {
        const string allowed = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
        var random = new Random(20260905);
        for (var i = 0; i < 200; i++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            var text = IfcGuid.From(new Guid(bytes));

            Assert.Equal(22, text.Length);
            Assert.All(text, c => Assert.Contains(c, allowed));
        }
    }

    [Fact]
    public void IfcGuid_DiVaVe_KhongMatBitNao()
    {
        var random = new Random(5092026);
        var guids = new List<Guid> { Guid.Empty, new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), new Guid("3c8ba9b2-1d47-4e0a-9c5f-0a1b2c3d4e5f") };
        for (var i = 0; i < 200; i++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            guids.Add(new Guid(bytes));
        }

        foreach (var guid in guids)
        {
            var text = IfcGuid.From(guid);
            Assert.True(IfcGuid.TryParse(text, out var back), "Không đọc lại được " + text);
            Assert.Equal(guid, back);
        }
    }

    [Fact]
    public void IfcGuid_HaiGuidKhacNhau_ChoHaiChuoiKhacNhau()
    {
        var random = new Random(1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            Assert.True(seen.Add(IfcGuid.From(new Guid(bytes))));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000000000000")]              // 19 ký tự
    [InlineData("00000000000000000000000")]          // 23 ký tự
    [InlineData("00000000000000000000!0")]           // ký tự ngoài bảng
    public void IfcGuid_ChuoiSai_TraFalse_KhongNem(string? text)
    {
        Assert.False(IfcGuid.TryParse(text, out var guid));
        Assert.Equal(Guid.Empty, guid);
    }

    // ── Camera ───────────────────────────────────────────────────────────────

    [Fact]
    public void Camera_NhinDungVaoDiemDaChon_HuongLaVectorDonVi()
    {
        var camera = BcfCamera.LookingAt(10, 20, 3, distance: 9);

        var length = Math.Sqrt(camera.DirectionX * camera.DirectionX + camera.DirectionY * camera.DirectionY + camera.DirectionZ * camera.DirectionZ);
        Assert.Equal(1, length, 6);

        // Đi từ vị trí camera theo hướng nhìn đúng bằng khoảng cách thì tới đúng điểm ngắm.
        Assert.Equal(10, camera.X + camera.DirectionX * 9, 6);
        Assert.Equal(20, camera.Y + camera.DirectionY * 9, 6);
        Assert.Equal(3, camera.Z + camera.DirectionZ * 9, 6);

        // Camera đứng cao hơn điểm ngắm — góc quen thuộc của ảnh chụp va chạm.
        Assert.True(camera.Z > 3);
    }

    [Fact]
    public void Camera_VectorLen_VuongGocVoiHuongNhin_VaLaVectorDonVi()
    {
        var camera = BcfCamera.LookingAt(0, 0, 0);

        var dot = camera.DirectionX * camera.UpX + camera.DirectionY * camera.UpY + camera.DirectionZ * camera.UpZ;
        Assert.Equal(0, dot, 6);

        var length = Math.Sqrt(camera.UpX * camera.UpX + camera.UpY * camera.UpY + camera.UpZ * camera.UpZ);
        Assert.Equal(1, length, 6);
    }

    // ── Ghi file rồi đọc lại ─────────────────────────────────────────────────

    private static ZipArchive WriteAndOpen(IEnumerable<BcfTopic> topics, out MemoryStream stream)
    {
        stream = new MemoryStream();
        BcfWriter.Write(stream, topics);
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private static XDocument Read(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static BcfTopic SampleTopic()
    {
        var topic = new BcfTopic("Va chạm ống D100 × dầm")
        {
            Guid = new Guid("11111111-2222-3333-4444-555555555555"),
            TopicType = "Clash",
            TopicStatus = "Open",
            Description = "Ống nước cắt dầm tại trục A-1, cao độ +3.200",
            Author = "DHCB Tools",
            CreationDate = new DateTime(2026, 9, 5, 8, 30, 0, DateTimeKind.Utc),
            Camera = BcfCamera.LookingAt(1.5, 2.5, 3.2),
            SnapshotPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 },
            Comment = "Đề nghị kết cấu duyệt lỗ mở.",
        };
        topic.Labels.Add("MEP × Kết cấu");
        topic.Components.Add(new BcfComponent(IfcGuid.From(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "123456", "DHCB Tools"));
        topic.Components.Add(new BcfComponent(IfcGuid.From(new Guid("aaaaaaaa-bbbb-cccc-dddd-ffffffffffff")), "654321", "DHCB Tools"));
        return topic;
    }

    [Fact]
    public void GhiFile_CoBcfVersionOGoc_VaMotThuMucChoMoiVanDe()
    {
        using var zip = WriteAndOpen(new[] { SampleTopic() }, out var stream);

        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("bcf.version", names);
        Assert.Contains("11111111-2222-3333-4444-555555555555/markup.bcf", names);
        Assert.Contains("11111111-2222-3333-4444-555555555555/viewpoint.bcfv", names);
        Assert.Contains("11111111-2222-3333-4444-555555555555/snapshot.png", names);

        var version = Read(zip, "bcf.version");
        Assert.Equal("2.1", version.Root!.Attribute("VersionId")!.Value);
        stream.Dispose();
    }

    [Fact]
    public void GhiFile_MarkupDungThuTuTheTheoDacTa()
    {
        using var zip = WriteAndOpen(new[] { SampleTopic() }, out var stream);
        var markup = Read(zip, "11111111-2222-3333-4444-555555555555/markup.bcf");

        var topic = markup.Root!.Element("Topic")!;
        Assert.Equal("11111111-2222-3333-4444-555555555555", topic.Attribute("Guid")!.Value);
        Assert.Equal("Clash", topic.Attribute("TopicType")!.Value);
        Assert.Equal("Open", topic.Attribute("TopicStatus")!.Value);

        // Thứ tự trong XSD 2.1: Title, Priority?, Labels*, CreationDate, CreationAuthor, Description?
        Assert.Equal(
            new[] { "Title", "Labels", "CreationDate", "CreationAuthor", "Description" },
            topic.Elements().Select(e => e.Name.LocalName));

        Assert.Equal("Va chạm ống D100 × dầm", topic.Element("Title")!.Value);
        Assert.Equal("2026-09-05T08:30:00Z", topic.Element("CreationDate")!.Value);
        Assert.Equal("MEP × Kết cấu", topic.Element("Labels")!.Value);

        Assert.Equal("Đề nghị kết cấu duyệt lỗ mở.", markup.Root.Element("Comment")!.Element("Comment")!.Value);

        var viewpoints = markup.Root.Element("Viewpoints")!;
        Assert.Equal("viewpoint.bcfv", viewpoints.Element("Viewpoint")!.Value);
        Assert.Equal("snapshot.png", viewpoints.Element("Snapshot")!.Value);
        stream.Dispose();
    }

    [Fact]
    public void GhiFile_ViewpointCoPhanTuVaCamera_DauChamThapPhan()
    {
        using var zip = WriteAndOpen(new[] { SampleTopic() }, out var stream);
        var viewpoint = Read(zip, "11111111-2222-3333-4444-555555555555/viewpoint.bcfv");

        var components = viewpoint.Root!.Element("Components")!;
        // XSD 2.1: Selection trước Visibility.
        Assert.Equal(new[] { "Selection", "Visibility" }, components.Elements().Select(e => e.Name.LocalName));

        var selected = components.Element("Selection")!.Elements("Component").ToList();
        Assert.Equal(2, selected.Count);
        Assert.All(selected, c => Assert.Equal(22, c.Attribute("IfcGuid")!.Value.Length));
        Assert.Equal("123456", selected[0].Element("AuthoringToolId")!.Value);

        // IFC GUID trong file phải đọc ngược lại đúng guid đã đưa vào.
        Assert.True(IfcGuid.TryParse(selected[0].Attribute("IfcGuid")!.Value, out var back));
        Assert.Equal(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), back);

        var camera = viewpoint.Root.Element("PerspectiveCamera")!;
        Assert.Equal("60", camera.Element("FieldOfView")!.Value);
        var x = camera.Element("CameraViewPoint")!.Element("X")!.Value;
        Assert.DoesNotContain(",", x);
        Assert.True(double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _));
        stream.Dispose();
    }

    [Fact]
    public void GhiFile_AnhChupGiuNguyenTungByte()
    {
        using var zip = WriteAndOpen(new[] { SampleTopic() }, out var stream);

        using var entry = zip.GetEntry("11111111-2222-3333-4444-555555555555/snapshot.png")!.Open();
        using var memory = new MemoryStream();
        entry.CopyTo(memory);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 }, memory.ToArray());
        stream.Dispose();
    }

    [Fact]
    public void GhiFile_TopicKhongCoGocNhin_VanHopLe_ChiThieuViewpoint()
    {
        var topic = new BcfTopic("Cảnh báo Revit: phần tử trùng nhau")
        {
            Guid = new Guid("99999999-8888-7777-6666-555555555555"),
            TopicType = "Warning",
        };

        using var zip = WriteAndOpen(new[] { topic }, out var stream);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("99999999-8888-7777-6666-555555555555/markup.bcf", names);
        Assert.DoesNotContain("99999999-8888-7777-6666-555555555555/viewpoint.bcfv", names);
        Assert.DoesNotContain("99999999-8888-7777-6666-555555555555/snapshot.png", names);

        var markup = Read(zip, "99999999-8888-7777-6666-555555555555/markup.bcf");
        Assert.Null(markup.Root!.Element("Viewpoints"));
        Assert.Equal("DHCB Tools", markup.Root.Element("Topic")!.Element("CreationAuthor")!.Value);
        stream.Dispose();
    }

    [Fact]
    public void GhiFile_HaiTopicTrungGuid_KhongDeCaiNaoBiGhiDeMatTich()
    {
        var a = new BcfTopic("Vấn đề 1") { Guid = new Guid("12121212-1212-1212-1212-121212121212") };
        var b = new BcfTopic("Vấn đề 2") { Guid = new Guid("12121212-1212-1212-1212-121212121212") };

        using var zip = WriteAndOpen(new[] { a, b }, out var stream);

        var markups = zip.Entries.Where(e => e.FullName.EndsWith("markup.bcf", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, markups.Count);

        var titles = markups.Select(m =>
        {
            using var reader = new StreamReader(m.Open());
            return XDocument.Parse(reader.ReadToEnd()).Root!.Element("Topic")!.Element("Title")!.Value;
        }).OrderBy(t => t).ToList();

        Assert.Equal(new[] { "Vấn đề 1", "Vấn đề 2" }, titles);
        stream.Dispose();
    }

    [Fact]
    public void GhiFile_KhongCoTopicNao_VanLaFileZipHopLe()
    {
        using var zip = WriteAndOpen(new List<BcfTopic>(), out var stream);

        Assert.Single(zip.Entries);
        Assert.Equal("bcf.version", zip.Entries[0].FullName);
        stream.Dispose();
    }

    [Fact]
    public void GhiFile_RaDiaVaDocLaiDuoc()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhcb-bcf-test-" + Guid.NewGuid().ToString("N") + BcfWriter.FileExtension);
        try
        {
            BcfWriter.Write(path, new[] { SampleTopic() });

            Assert.True(File.Exists(path));
            using var zip = ZipFile.OpenRead(path);
            Assert.Contains(zip.Entries, e => e.FullName == "bcf.version");
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("/markup.bcf", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
