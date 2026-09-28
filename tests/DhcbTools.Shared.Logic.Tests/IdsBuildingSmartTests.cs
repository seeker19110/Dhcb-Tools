using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Các luật lấy từ bộ ca kiểm chính thức của buildingSMART (IDS/Documentation/ImplementersDocumentation/
/// TestCases). Mỗi ca ở đây là bản thu nhỏ của một nhóm ca trong bộ đó: chạy cả bộ qua
/// <c>BatchRunner --verify-ifc … --verify-ids …</c> nâng mức khớp từ 240 lên 272/334 (audit 2026-09-28).
/// </summary>
public class IdsBuildingSmartTests
{
    private static string G(string tag) => ("0" + tag).PadRight(22, '0');

    private static readonly string Ifc =
        "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION((''),'2;1');\nFILE_NAME('','',(''),(''),'','','');\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
        + $"#1=IFCELEMENTASSEMBLY('{G("Asm")}',$,$,$,$,$,$,$,$,$);\n"
        + $"#2=IFCINVENTORY('{G("Inv")}',$,$,$,'BUNNY',.USERDEFINED.,$,$,$,$,$);\n"
        + $"#3=IFCRELASSIGNSTOGROUP('{G("RelGrp")}',$,$,$,(#1),$,#2);\n"
        + $"#10=IFCWALLTYPE('{G("WType")}',$,$,$,$,$,$,$,'WALDO',.USERDEFINED.);\n"
        + $"#11=IFCWALL('{G("Wall")}',$,'Tuong',$,$,$,$,$,.NOTDEFINED.);\n"
        + $"#12=IFCRELDEFINESBYTYPE('{G("RelType")}',$,$,$,(#11),#10);\n"
        + $"#13=IFCSLAB('{G("Slab")}',$,$,$,$,$,$,$,.FLOOR.);\n"
        + "#20=IFCMATERIAL('Bar',$,'Foo');\n"
        + "#21=IFCMATERIALLAYER(#20,200.,$,$,$,$,$);\n"
        + "#22=IFCMATERIALLAYERSET((#21),'Bo lop A',$);\n"
        + $"#23=IFCRELASSOCIATESMATERIAL('{G("RelMat")}',$,$,$,(#11),#22);\n"
        + "ENDSEC;\nEND-ISO-10303-21;\n";

    private static IfcIdsModel Model() => IfcIdsModel.Parse(Ifc);

    private static IfcIdsElement Element(IfcIdsModel model, int id) =>
        (IfcIdsElement)model.Elements().Single(e => ((IfcIdsElement)e).Id == id);

    private static IReadOnlyList<IdsSpecification> Parse(string specs) => IdsSpec.Parse(
        "<ids xmlns=\"http://standards.buildingsmart.org/IDS\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><specifications>"
        + specs + "</specifications></ids>");

    private static string Entity(string name) => "<entity><name><simpleValue>" + name + "</simpleValue></name></entity>";

    private static IdsCheckResult Check(string specs) => IdsEvaluator.Check(Parse(specs), Model().Elements());

    // ── xs:pattern ──────────────────────────────────────────────────────────

    [Fact]
    public void Pattern_LopKyTuXsd_i_c_DichSangNet()
    {
        Assert.Equal(@"[\p{L}_:][\p{L}\p{Mn}\p{Mc}\p{Nd}_:.\-]*", IdsValue.TranslateXsd(@"\i\c*"));
        Assert.Equal(@"[^\p{L}_:][^\p{L}\p{Mn}\p{Mc}\p{Nd}_:.\-]", IdsValue.TranslateXsd(@"\I\C"));
        Assert.Equal(@"[\p{L}_:0-9]\d", IdsValue.TranslateXsd(@"[\i0-9]\d"));

        var name = new IdsValue { Pattern = @"\i\c*" };
        name.CompilePattern();
        Assert.True(name.Accepts("Tuong_01.a-b"));
        Assert.False(name.Accepts("1Tuong"));

        var inClassNegation = new IdsValue { Pattern = @"[\I]" };
        Assert.Throws<IdsParseException>(() => inClassNegation.CompilePattern());
    }

    [Fact]
    public void Pattern_NhieuPatternLaPhepHoac()
    {
        var spec = Parse("<specification name=\"t\"><applicability>" + Entity("IFCWALL") + "</applicability><requirements>"
            + "<attribute><name><simpleValue>Name</simpleValue></name><value><xs:restriction base=\"xs:string\">"
            + "<xs:pattern value=\"[A-Z]{2}[0-9]{2}\"/><xs:pattern value=\"[a-z]{2}[0-9]{2}\"/></xs:restriction></value></attribute>"
            + "</requirements></specification>").Single();
        var value = spec.Requirements.Single().Value;
        Assert.True(value.Accepts("XY99"));
        Assert.True(value.Accepts("xy99"));
        Assert.False(value.Accepts("Xy99"));
    }

    // ── Cardinality mức specification ─────────────────────────────────────

    [Fact]
    public void Occurs_DocTuApplicability_BatBuocRongLaTruot_TuyChonRongLaDat()
    {
        var required = Check("<specification name=\"bat buoc\"><applicability>" + Entity("IFCTANK") + "</applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>");
        var result = Assert.Single(required.Specifications);
        Assert.True(result.Required);
        Assert.True(result.MissingRequired);
        Assert.True(result.IsFailed);
        Assert.Equal(0, required.FailureCount);          // không phần tử nào trượt…
        Assert.Equal(1, required.FailedSpecificationCount); // …nhưng specification thì trượt
        Assert.False(required.AllPassed);

        var optional = Check("<specification name=\"tuy chon\"><applicability minOccurs=\"0\" maxOccurs=\"unbounded\">" + Entity("IFCTANK") + "</applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>");
        Assert.False(Assert.Single(optional.Specifications).MissingRequired);
        Assert.True(optional.AllPassed);
    }

    [Fact]
    public void Occurs_CamTrenApplicability_PhanTuLotVaoLaTruot()
    {
        var check = Check("<specification name=\"cam tuong\"><applicability minOccurs=\"0\" maxOccurs=\"0\">" + Entity("IFCWALL") + "</applicability></specification>");
        var spec = Assert.Single(check.Specifications);
        Assert.Equal(1, spec.Failed);
        Assert.False(check.AllPassed);
    }

    [Fact]
    public void Occurs_CamMaVanCoRequirements_LaFileSai()
    {
        var ex = Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"x\"><applicability minOccurs=\"0\" maxOccurs=\"0\">" + Entity("IFCWALL") + "</applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>"));
        Assert.Contains("CẤM", ex.Message);
    }

    // ── Entity / predefinedType ────────────────────────────────────────────

    /// <summary>
    /// IDS 1.0 viết tên lớp CHỮ HOA và so phân biệt hoa thường ("entities must be specified as uppercase strings"):
    /// "IfcWall" trong IDS không khớp gì — cả đường Revit (IfcEntity "IfcWall" được nâng lên IFCWALL) lẫn đường IFC.
    /// Bản trước nâng tên trong IDS lên chữ hoa cho dễ dãi, nên cùng một file cho kết luận khác IfcTester.
    /// </summary>
    [Fact]
    public void Entity_TenLopVietThuong_KhongKhop_VietHoaThiKhop()
    {
        var lower = Parse("<specification name=\"t\"><applicability><entity><name><xs:restriction base=\"xs:string\">"
            + "<xs:enumeration value=\"IfcWall\"/><xs:enumeration value=\"IfcSlab\"/></xs:restriction></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>").Single();
        Assert.Equal(new[] { "IfcWall", "IfcSlab" }, lower.Applicability.Single().Name.Enumeration);
        var upper = Parse("<specification name=\"t\"><applicability>" + Entity("IFCWALL") + "</applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>").Single();

        var wall = new FakeIdsElement { IfcEntity = "IfcWall", Label = "1" };
        wall.Attributes["Name"] = "T";
        Assert.Equal(0, IdsEvaluator.Check(new[] { lower }, new IIdsElement[] { wall }).Specifications.Single().Applicable);
        Assert.Equal(1, IdsEvaluator.Check(new[] { upper }, new IIdsElement[] { wall }).Specifications.Single().Passed);
        Assert.Equal(0, IdsEvaluator.Check(new[] { lower }, Model().Elements()).Specifications.Single().Applicable);
    }

    [Fact]
    public void PredefinedType_UserDefined_NhanCaChuUSERDEFINEDLanGiaTriTuKhai()
    {
        var model = Model();
        var type = Element(model, 10);
        Assert.Equal("WALDO", type.PredefinedType);
        Assert.True(type.PredefinedTypeIsUserDefined);
        Assert.True(Element(model, 11).PredefinedTypeIsUserDefined);   // NOTDEFINED ở phần tử → xem kiểu
        Assert.False(Element(model, 13).PredefinedTypeIsUserDefined);  // .FLOOR.
        Assert.False(Element(model, 1).PredefinedTypeIsUserDefined);   // không có kiểu, không có enum

        foreach (var expected in new[] { "USERDEFINED", "WALDO" })
        {
            var check = Check("<specification name=\"t\"><applicability>" + Entity("IFCWALLTYPE") + "</applicability><requirements>"
                + "<entity><name><simpleValue>IFCWALLTYPE</simpleValue></name><predefinedType><simpleValue>" + expected + "</simpleValue></predefinedType></entity>"
                + "</requirements></specification>");
            Assert.Equal(1, check.Specifications.Single().Passed);
        }
    }

    // ── partOf ──────────────────────────────────────────────────────────────

    private static string PartOf(string predefined, string relation = "") =>
        "<specification name=\"t\"><applicability>" + Entity("IFCELEMENTASSEMBLY") + "</applicability><requirements>"
        + "<partOf" + relation + "><entity><name><simpleValue>IFCINVENTORY</simpleValue></name><predefinedType><simpleValue>"
        + predefined + "</simpleValue></predefinedType></entity></partOf></requirements></specification>";

    [Fact]
    public void PartOf_DocTenVaPredefinedTypeRieng_KhongGopChu()
    {
        var facet = Parse(PartOf("BUNNY")).Single().Requirements.Single();
        Assert.Equal("IFCINVENTORY", facet.Value.Simple);
        Assert.Equal("BUNNY", facet.Container!.Simple);
        Assert.Contains("predefinedType = \"BUNNY\"", facet.Describe());
    }

    [Fact]
    public void PartOf_PredefinedTypeCuaToTien_KiemTrenDuongIfc()
    {
        Assert.Equal(1, Check(PartOf("BUNNY")).Specifications.Single().Passed);
        Assert.Equal(1, Check(PartOf("BUNNY", " relation=\"IFCRELASSIGNSTOGROUP\"")).Specifications.Single().Passed);
        Assert.Equal(0, Check(PartOf("BUNNY", " relation=\"IFCRELAGGREGATES\"")).Specifications.Single().Passed);
        Assert.Equal(0, Check(PartOf("BUNNIES")).Specifications.Single().Passed);

        var inventory = Element(Model(), 2).PartOfWithPredefinedType;
        Assert.Empty(inventory); // không thuộc về gì

        // Phần tử không biết predefinedType của tổ tiên (đường Revit): trượt, không đạt oan.
        var fake = new FakeIdsElement { IfcEntity = "IfcElementAssembly", Label = "x" };
        fake.Parents.Add(("", "IFCINVENTORY"));
        Assert.Equal(0, IdsEvaluator.Check(Parse(PartOf("BUNNY")), new IIdsElement[] { fake }).Specifications.Single().Passed);
    }

    // ── Vật liệu ────────────────────────────────────────────────────────────

    [Fact]
    public void VatLieu_KhopCategoryVaTenBoLop()
    {
        var materials = Element(Model(), 11).Materials.ToList();
        Assert.Contains("Bar", materials);
        Assert.Contains("Foo", materials);        // Category của IfcMaterial
        Assert.Contains("Bo lop A", materials);   // LayerSetName
    }
}
