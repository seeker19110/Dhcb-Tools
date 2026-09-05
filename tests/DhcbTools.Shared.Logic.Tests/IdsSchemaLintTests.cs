using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// §39 để lại: file IDS "gần đúng" DHCB đọc được nhưng IfcTester từ chối. Bộ soát phải NÓI RA điều đó
/// (kèm dòng) mà không chặn — chặn là việc của <see cref="IdsSpec.Parse"/>.
/// </summary>
public class IdsSchemaLintTests
{
    private const string Ns = "xmlns=\"http://standards.buildingsmart.org/IDS\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"";

    private static string Wrap(string specs, string ns = Ns) =>
        "<ids " + ns + ">\n<info><title>t</title></info>\n<specifications>\n" + specs + "\n</specifications>\n</ids>";

    private const string GoodSpec =
        "<specification name=\"Cửa có Tag\" ifcVersion=\"IFC4\">\n<applicability><entity><name><simpleValue>IfcDoor</simpleValue></name></entity></applicability>\n"
        + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute>\n"
        + "<material><value><xs:restriction base=\"xs:string\"><xs:pattern value=\".+\"/></xs:restriction></value></material></requirements>\n</specification>";

    [Fact]
    public void FileDungChuan_KhongCanhBao()
    {
        Assert.Empty(IdsSchemaLint.Check(Wrap(GoodSpec)));
    }

    [Fact]
    public void FixtureThat_KhongCanhBao()
    {
        // Hai fixture trong repo đã được IfcTester 0.8.5 mở được (§39) — bộ soát không được kêu oan.
        foreach (var name in new[] { "yeu-cau-thong-tin.ids", "yeu-cau-thong-tin-rong.ids" })
        {
            var path = System.IO.Path.Combine(FixtureDir(), name);
            Assert.Empty(IdsSchemaLint.Check(System.IO.File.ReadAllText(path)));
        }
    }

    [Fact]
    public void RestrictionKhongThuocXs_CanhBaoKemDong_VaParseVanChay()
    {
        // Đúng lỗi §39: fixture cũ viết <restriction> trần. DHCB đọc được, IfcTester từ chối.
        var xml = Wrap(GoodSpec.Replace("<xs:restriction", "<restriction").Replace("</xs:restriction>", "</restriction>").Replace("<xs:pattern", "<pattern"));
        var warnings = IdsSchemaLint.Check(xml);

        Assert.Contains(warnings, w => w.Contains("<restriction> phải thuộc namespace XML Schema") && w.StartsWith("dòng 7"));
        Assert.Contains(warnings, w => w.Contains("<pattern> phải viết <xs:pattern>"));
        // Không chặn: bộ đọc vẫn ra đúng 1 specification, 2 yêu cầu.
        var spec = Assert.Single(IdsSpec.Parse(xml));
        Assert.Equal(2, spec.Requirements.Count);
    }

    [Fact]
    public void ThieuNamespaceGoc_NoiMotLanChoMoiTenThe_KhongTranMaxWarnings()
    {
        var warnings = IdsSchemaLint.Check(Wrap(GoodSpec, "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""));
        Assert.Contains(warnings, w => w.Contains("thẻ gốc <ids> phải khai xmlns") && w.Contains("đang không có namespace"));
        Assert.True(warnings.Count <= IdsSchemaLint.MaxWarnings);
        // Mỗi tên thẻ chỉ một dòng "phải thuộc namespace IDS".
        Assert.Equal(1, warnings.Count(w => w.Contains("<attribute> phải thuộc namespace IDS")));
    }

    [Fact]
    public void ThieuIfcVersionVaTitle_CanhBao()
    {
        var xml = "<ids " + Ns + "><info/><specifications>" + GoodSpec.Replace(" ifcVersion=\"IFC4\"", string.Empty) + "</specifications></ids>";
        var warnings = IdsSchemaLint.Check(xml);
        Assert.Contains(warnings, w => w.Contains("<info> thiếu <title>"));
        Assert.Contains(warnings, w => w.Contains("thiếu thuộc tính ifcVersion"));
    }

    [Fact]
    public void IfcVersionLa_CanhBao()
    {
        var warnings = IdsSchemaLint.Check(Wrap(GoodSpec.Replace("IFC4", "IFC4X3")));
        Assert.Contains(warnings, w => w.Contains("ifcVersion=\"IFC4X3\" không thuộc"));
    }

    [Fact]
    public void FacetSaiThuTuTrongApplicability_CanhBao_NhungRequirementsThiTuDo()
    {
        var spec = GoodSpec.Replace(
            "<applicability><entity><name><simpleValue>IfcDoor</simpleValue></name></entity></applicability>",
            "<applicability><attribute><name><simpleValue>Tag</simpleValue></name></attribute><entity><name><simpleValue>IfcDoor</simpleValue></name></entity></applicability>");
        var warnings = IdsSchemaLint.Check(Wrap(spec));
        Assert.Contains(warnings, w => w.Contains("<entity> đứng sai thứ tự trong <applicability>"));
        // GoodSpec bên requirements có attribute rồi material — đúng thứ tự; đảo lại cũng không kêu.
        Assert.DoesNotContain(warnings, w => w.Contains("<requirements>"));
    }

    [Fact]
    public void CardinalityTrongApplicability_VaGiaTriLa_CanhBao()
    {
        var spec = GoodSpec
            .Replace("<applicability><entity>", "<applicability><entity cardinality=\"required\">")
            .Replace("<requirements><attribute>", "<requirements><attribute cardinality=\"maybe\">");
        var warnings = IdsSchemaLint.Check(Wrap(spec));
        Assert.Contains(warnings, w => w.Contains("facet trong <applicability> không có thuộc tính cardinality"));
        Assert.Contains(warnings, w => w.Contains("cardinality=\"maybe\" không thuộc"));
    }

    [Fact]
    public void ThieuConBatBuoc_CanhBao()
    {
        var spec = GoodSpec.Replace(
            "<attribute><name><simpleValue>Tag</simpleValue></name></attribute>",
            "<property><baseName><simpleValue>FireRating</simpleValue></baseName></property><classification><value><simpleValue>Ss_25</simpleValue></value></classification>");
        var warnings = IdsSchemaLint.Check(Wrap(spec));
        Assert.Contains(warnings, w => w.Contains("<property> thiếu <propertySet>"));
        Assert.Contains(warnings, w => w.Contains("<classification> thiếu <system>"));
    }

    [Fact]
    public void GiaTriVietTran_CanhBao()
    {
        var warnings = IdsSchemaLint.Check(Wrap(GoodSpec.Replace("<name><simpleValue>Tag</simpleValue></name>", "<name>Tag</name>")));
        Assert.Contains(warnings, w => w.Contains("<name> phải chứa <simpleValue> hoặc <xs:restriction>") && w.Contains("chữ viết trần"));
    }

    [Fact]
    public void ThieuSpecificationsBocNgoai_CanhBao()
    {
        var warnings = IdsSchemaLint.Check("<ids " + Ns + "><info><title>t</title></info>" + GoodSpec + "</ids>");
        Assert.Contains(warnings, w => w.Contains("thiếu <specifications>"));
    }

    [Fact]
    public void TheGocKhongPhaiIds_ChiMotCanhBaoRoiDung()
    {
        var warnings = IdsSchemaLint.Check("<foo " + Ns + "><specifications>" + GoodSpec + "</specifications></foo>");
        Assert.Contains(warnings, w => w.Contains("thẻ gốc phải là <ids>"));
        Assert.DoesNotContain(warnings, w => w.Contains("<specification>"));
    }

    [Fact]
    public void ThieuInfo_ThieuName_SpecKhongNamTrucTiep_RequirementsTruocApplicability()
    {
        var spec = GoodSpec.Replace(" name=\"Cửa có Tag\"", string.Empty);
        var appl = "<applicability><entity><name><simpleValue>IfcDoor</simpleValue></name></entity></applicability>";
        var i = spec.IndexOf(appl);
        var j = spec.IndexOf("<requirements>");
        var k = spec.IndexOf("</requirements>") + "</requirements>".Length;
        var swapped = spec.Substring(0, i) + spec.Substring(j, k - j) + "\n" + appl + spec.Substring(k);
        var xml = "<ids " + Ns + "><specifications><nhom>" + swapped + "</nhom></specifications></ids>";
        var warnings = IdsSchemaLint.Check(xml);
        Assert.Contains(warnings, w => w.Contains("thiếu <info>"));
        Assert.Contains(warnings, w => w.Contains("thiếu thuộc tính name"));
        Assert.Contains(warnings, w => w.Contains("phải nằm trực tiếp trong <specifications>"));
        Assert.Contains(warnings, w => w.Contains("<requirements> phải đứng sau <applicability>"));
    }

    [Fact]
    public void ThieuApplicability_FacetLa_HaiEntity_EntityCoCardinality()
    {
        var xml = Wrap(
            "<specification name=\"a\" ifcVersion=\"IFC4\"><requirements><foo/><entity cardinality=\"required\"><name><simpleValue>IfcWall</simpleValue></name></entity></requirements></specification>"
            + "<specification name=\"b\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IfcWall</simpleValue></name></entity>"
            + "<entity><name><simpleValue>IfcSlab</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>");
        var warnings = IdsSchemaLint.Check(xml);
        Assert.Contains(warnings, w => w.Contains("thiếu <applicability>"));
        Assert.Contains(warnings, w => w.Contains("facet <foo> không có trong IDS 1.0"));
        Assert.Contains(warnings, w => w.Contains("<entity> trong <requirements> không có thuộc tính cardinality"));
        Assert.Contains(warnings, w => w.Contains("<applicability> chỉ được có một <entity>"));
    }

    [Fact]
    public void PartOf_DataTypeThuong_RestrictionThieuBase_RangBuocLa_PatternThieuValue()
    {
        var xml = Wrap(
            "<specification name=\"a\" ifcVersion=\"IFC4\"><applicability/><requirements>"
            + "<partOf cardinality=\"required\"><entity><name><simpleValue>IFCBUILDINGSTOREY</simpleValue></name></entity></partOf>"
            + "<partOf/>"
            + "<property dataType=\"ifclabel\"><propertySet><simpleValue>P</simpleValue></propertySet><baseName><simpleValue>B</simpleValue></baseName></property>"
            + "<attribute><name><simpleValue>Tag</simpleValue></name><value><xs:restriction><xs:foo/><xs:pattern/></xs:restriction></value></attribute>"
            + "</requirements></specification>");
        var warnings = IdsSchemaLint.Check(xml);
        Assert.Contains(warnings, w => w.Contains("<partOf> thiếu <entity>"));
        Assert.Contains(warnings, w => w.Contains("dataType=\"ifclabel\" phải viết HOA"));
        Assert.Contains(warnings, w => w.Contains("<xs:restriction> thiếu thuộc tính base"));
        Assert.Contains(warnings, w => w.Contains("<foo> không phải ràng buộc XSD"));
        Assert.Contains(warnings, w => w.Contains("<xs:pattern> thiếu thuộc tính value"));
        Assert.DoesNotContain(warnings, w => w.Contains("cardinality=\"required\""));
    }

    [Fact]
    public void QuaMaxWarnings_DungDem_KhongTranDanhSach()
    {
        // 25 specification, mỗi cái một ifcVersion lạ khác nhau → 25 thông điệp khác nhau; cái thứ 20 làm đầy,
        // sau đó mọi Add bị bỏ và vòng lặp dừng sớm.
        var specs = string.Concat(Enumerable.Range(1, 25).Select(i =>
            i == 20
                ? "<specification name=\"s" + i + "\" ifcVersion=\"IFCX" + i + "\"><requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>"
                : "<specification name=\"s" + i + "\" ifcVersion=\"IFCX" + i + "\"><applicability/><requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>"));
        var warnings = IdsSchemaLint.Check(Wrap(specs));
        Assert.Equal(IdsSchemaLint.MaxWarnings, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("ifcVersion=\"IFCX20\""));
        Assert.DoesNotContain(warnings, w => w.Contains("ifcVersion=\"IFCX21\""));
        Assert.DoesNotContain(warnings, w => w.Contains("thiếu <applicability>"));
    }

    [Fact]
    public void KhongPhaiXml_TraVeMotDong_KhongNem()
    {
        var warnings = IdsSchemaLint.Check("<ids");
        Assert.Single(warnings);
        Assert.StartsWith("không đọc được XML", warnings[0]);
    }

    private static string FixtureDir()
    {
        var dir = System.AppContext.BaseDirectory;
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "tests", "suites", "fixtures")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir!, "tests", "suites", "fixtures");
    }
}
