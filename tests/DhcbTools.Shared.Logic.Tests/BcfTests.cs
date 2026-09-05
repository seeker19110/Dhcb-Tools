using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DhcbTools.Shared.Logic.Bcf;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tầng thuần của <c>BcfExport</c> (đề xuất B3 — đưa va chạm sang Navisworks/Solibri/BIMcollab).
/// Điều phải giữ: (1) file mở lại được — zip có <c>bcf.version</c> và mỗi vấn đề một thư mục GUID chứa
/// <c>markup.bcf</c>; (2) <b>GUID sinh từ khoá va chạm phải ổn định</b> — xuất lại lần hai mà đổi GUID
/// là nhận xét người duyệt đã ghi bị tách thành vấn đề mới; (3) toạ độ camera đúng vị trí và không bao
/// giờ NaN; (4) thứ tự thẻ trong Topic theo XSD 2.1.
/// </summary>
public class BcfTests
{
    private static BcfIssue Issue(string key = "1-2@0,0,0", double x = 3, double y = 4, double z = 5)
        => new BcfIssue(BcfWriter.GuidFromKey(key), "Ducts × Structural Framing")
        {
            Description = "Va chạm thử",
            Target = new BcfPoint(x, y, z),
        };

    private static ZipArchive Zip(params BcfIssue[] issues)
    {
        var stream = new MemoryStream();
        BcfWriter.Write(stream, issues, new BcfProject { Name = "Dự án A" });
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private static XElement Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return XElement.Parse(reader.ReadToEnd());
    }

    // ── Cấu trúc zip ─────────────────────────────────────────────────────────

    [Fact]
    public void Zip_CoBcfVersionVaMotThuMucChoMoiVanDe()
    {
        var a = Issue("a");
        var b = Issue("b");
        using var zip = Zip(a, b);

        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("bcf.version", names);
        Assert.Contains("extensions.xml", names);
        Assert.Contains("project.bcfp", names);
        Assert.Contains(a.Guid + "/markup.bcf", names);
        Assert.Contains(a.Guid + "/viewpoint.bcfv", names);
        Assert.Contains(b.Guid + "/markup.bcf", names);

        Assert.Equal("2.1", Read(zip, "bcf.version").Attribute("VersionId")!.Value);
    }

    [Fact]
    public void Zip_HaiVanDeTrungGuidVanRaHaiThuMuc()
    {
        // Trùng thư mục trong zip = phần mềm đọc BCF hoặc bỏ bớt một vấn đề, hoặc báo file hỏng.
        var guid = BcfWriter.GuidFromKey("x");
        using var zip = Zip(
            new BcfIssue(guid, "A") { Target = new BcfPoint(0, 0, 0) },
            new BcfIssue(guid, "B") { Target = new BcfPoint(1, 1, 1) });

        var markups = zip.Entries.Where(e => e.FullName.EndsWith("markup.bcf", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, markups.Count);
        Assert.Equal(2, markups.Select(e => e.FullName).Distinct().Count());
    }

    [Fact]
    public void Zip_KhongCoAnhThiKhongCoSnapshot()
    {
        using var zip = Zip(Issue());
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".png", StringComparison.Ordinal));
    }

    [Fact]
    public void Zip_CoAnhThiMarkupKhaiSnapshot()
    {
        var issue = Issue();
        issue.Snapshot = new byte[] { 137, 80, 78, 71 };
        using var zip = Zip(issue);

        Assert.Contains(issue.Guid + "/snapshot.png", zip.Entries.Select(e => e.FullName));
        Assert.Equal("snapshot.png", Read(zip, issue.Guid + "/markup.bcf").Element("Viewpoints")!.Element("Snapshot")!.Value);
    }

    [Fact]
    public void WriteFile_TaoThuMucDichNeuChuaCo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dhcb-bcf-" + Guid.NewGuid().ToString("N"), "sub");
        var path = Path.Combine(dir, "clash.bcf");
        try
        {
            BcfWriter.WriteFile(path, new[] { Issue() });
            Assert.True(File.Exists(path));
            using var zip = ZipFile.OpenRead(path);
            Assert.Contains("bcf.version", zip.Entries.Select(e => e.FullName));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(Path.GetDirectoryName(dir)!, true);
        }
    }

    // ── GUID ổn định ─────────────────────────────────────────────────────────

    [Fact]
    public void Guid_CungKhoaThiCungGuid()
        => Assert.Equal(BcfWriter.GuidFromKey("123-456@1000,2000,3000"), BcfWriter.GuidFromKey("123-456@1000,2000,3000"));

    [Fact]
    public void Guid_KhacKhoaThiKhacGuid()
        => Assert.NotEqual(BcfWriter.GuidFromKey("a"), BcfWriter.GuidFromKey("b"));

    [Fact]
    public void Guid_LaUuidHopLeVersion5()
    {
        var text = BcfWriter.GuidFromKey("bất kỳ");
        Assert.True(Guid.TryParse(text, out _));
        Assert.Equal('5', text[14]);
        Assert.Contains(text[19], new[] { '8', '9', 'a', 'b' });
    }

    // ── markup.bcf ───────────────────────────────────────────────────────────

    [Fact]
    public void Markup_ThuTuTheTheoXsd()
    {
        var issue = Issue();
        issue.Labels.Add("Với model liên kết");
        issue.Stage = "Thi công";

        var topic = XElement.Parse(BcfWriter.MarkupXml(issue)).Element("Topic")!;
        Assert.Equal(
            new[] { "Title", "Priority", "Labels", "CreationDate", "CreationAuthor", "Stage", "Description" },
            topic.Elements().Select(e => e.Name.LocalName).ToArray());
        Assert.Equal(issue.Guid, topic.Attribute("Guid")!.Value);
        Assert.Equal("Clash", topic.Attribute("TopicType")!.Value);
        Assert.Equal("Open", topic.Attribute("TopicStatus")!.Value);
    }

    [Fact]
    public void Markup_KhongCoCameraThiKhongKhaiViewpoint()
    {
        var markup = XElement.Parse(BcfWriter.MarkupXml(new BcfIssue(BcfWriter.GuidFromKey("k"), "Không toạ độ")));
        Assert.Null(markup.Element("Viewpoints"));
    }

    [Fact]
    public void Markup_NgayGioGhiTheoIso8601Utc()
    {
        var issue = Issue();
        issue.CreationDate = new DateTime(2026, 9, 5, 7, 8, 9, DateTimeKind.Utc);
        var topic = XElement.Parse(BcfWriter.MarkupXml(issue)).Element("Topic")!;
        Assert.Equal("2026-09-05T07:08:09Z", topic.Element("CreationDate")!.Value);
    }

    // ── viewpoint.bcfv ───────────────────────────────────────────────────────

    [Fact]
    public void Viewpoint_CameraLuiKhoiTamDungKhoangCach()
    {
        var issue = new BcfIssue(BcfWriter.GuidFromKey("k"), "T")
        {
            Target = new BcfPoint(10, 0, 0),
            ViewDirection = new BcfPoint(-1, 0, 0),
            ViewDistance = 4,
        };

        var camera = XElement.Parse(BcfWriter.ViewpointXml(issue)).Element("PerspectiveCamera")!;
        var eye = camera.Element("CameraViewPoint")!;
        Assert.Equal("14", eye.Element("X")!.Value);
        Assert.Equal("0", eye.Element("Y")!.Value);
        Assert.Equal("0", eye.Element("Z")!.Value);
        Assert.Equal("-1", camera.Element("CameraDirection")!.Element("X")!.Value);
    }

    [Fact]
    public void Viewpoint_HuongNhinLuonDaChuanHoa()
    {
        var issue = new BcfIssue(BcfWriter.GuidFromKey("k"), "T")
        {
            Target = new BcfPoint(0, 0, 0),
            ViewDirection = new BcfPoint(0, 0, -7),
        };
        var direction = XElement.Parse(BcfWriter.ViewpointXml(issue)).Element("PerspectiveCamera")!.Element("CameraDirection")!;
        Assert.Equal("-1", direction.Element("Z")!.Value);
    }

    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 1, -1)]
    [InlineData(0, 0, 0)]
    public void Viewpoint_VectorLenKhongBaoGioNaN(double x, double y, double z)
    {
        var up = BcfWriter.UpVector(new BcfPoint(x, y, z));
        Assert.False(double.IsNaN(up.X) || double.IsNaN(up.Y) || double.IsNaN(up.Z));
        Assert.Equal(1.0, up.Length, 6);
    }

    [Fact]
    public void Viewpoint_VectorLenVuongGocVoiHuongNhin()
    {
        var direction = new BcfPoint(-1, 1, -1).Normalised();
        var up = BcfWriter.UpVector(direction);
        Assert.Equal(0.0, (direction.X * up.X) + (direction.Y * up.Y) + (direction.Z * up.Z), 6);
    }

    [Fact]
    public void Viewpoint_PhanTuLienQuanGhiDuHaiPhia()
    {
        var issue = Issue();
        issue.Components.Add(new BcfComponent("111", null, "MEP.rvt"));
        issue.Components.Add(new BcfComponent("222", "3Hf$k0", "KetCau.rvt"));

        var components = XElement.Parse(BcfWriter.ViewpointXml(issue))
            .Element("Components")!.Element("Selection")!.Elements("Component").ToList();
        Assert.Equal(2, components.Count);
        Assert.Equal("111", components[0].Element("AuthoringToolId")!.Value);
        Assert.Null(components[0].Attribute("IfcGuid"));
        Assert.Equal("3Hf$k0", components[1].Attribute("IfcGuid")!.Value);
        Assert.Equal("KetCau.rvt", components[1].Element("OriginatingSystem")!.Value);
    }

    [Fact]
    public void Viewpoint_SoDungDauChamThapPhanVaKhongCoAmKhong()
    {
        var issue = new BcfIssue(BcfWriter.GuidFromKey("k"), "T")
        {
            Target = new BcfPoint(1.5, -0.0000001, 0),
            ViewDirection = new BcfPoint(0, 0, -1),
            ViewDistance = 2,
        };
        var xml = BcfWriter.ViewpointXml(issue);
        Assert.Contains("<X>1.5</X>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("-0<", xml, StringComparison.Ordinal);
        Assert.DoesNotContain(",", xml, StringComparison.Ordinal);
    }

    // ── extensions.xml ───────────────────────────────────────────────────────

    [Fact]
    public void Extensions_KhaiDuNhanDaDung()
    {
        var issue = Issue();
        issue.Labels.Add("Với model liên kết");
        var xml = XElement.Parse(BcfWriter.ExtensionsXml(new[] { issue }));

        Assert.Contains("Với model liên kết", xml.Element("TopicLabels")!.Elements().Select(e => e.Value));
        Assert.Contains("Clash", xml.Element("TopicTypes")!.Elements().Select(e => e.Value));
        Assert.Contains("Open", xml.Element("TopicStatuses")!.Elements().Select(e => e.Value));
        Assert.Contains("DHCB Tools", xml.Element("Users")!.Elements().Select(e => e.Value));
    }

    [Fact]
    public void Extensions_KhongLapGiaTri()
    {
        var xml = XElement.Parse(BcfWriter.ExtensionsXml(new[] { Issue("a"), Issue("b") }));
        var statuses = xml.Element("TopicStatuses")!.Elements().Select(e => e.Value).ToList();
        Assert.Equal(statuses.Count, statuses.Distinct().Count());
    }

    [Fact]
    public void KhongCoVanDeNaoThiVanRaFileHopLe()
    {
        using var zip = Zip();
        Assert.Contains("bcf.version", zip.Entries.Select(e => e.FullName));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith("markup.bcf", StringComparison.Ordinal));
    }
}
