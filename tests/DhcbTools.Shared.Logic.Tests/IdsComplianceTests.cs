using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using DhcbTools.Shared.Logic.Ifc;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tuân thủ IDS 1.0 / ISO 10303-21 — các khoảng trống lộ ra ở audit vòng 2 (2026-09-23): cardinality mức
/// specification, so sánh số theo giá trị, ràng buộc hội, độ dài chuỗi, lọc ifcVersion, loại property IFC,
/// PredefinedType đúng vị trí, GlobalId nén 22 ký tự.
/// </summary>
public class IdsComplianceTests
{
    private const string Header = "<ids xmlns=\"http://standards.buildingsmart.org/IDS\"><specifications>";
    private const string Footer = "</specifications></ids>";
    private const string Xs = "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"";

    private static IReadOnlyList<IdsSpecification> Parse(string body) => IdsSpec.Parse(Header + body + Footer);

    private static IfcIdsModel Ifc(string body) =>
        IfcIdsModel.Parse("ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n" + body + "\nENDSEC;\nEND-ISO-10303-21;");

    private static string G(string tag) => ("0" + tag).PadRight(22, '0');

    // ── IdsValue ─────────────────────────────────────────────────────────

    [Fact]
    public void IdsValue_SoSanhSo_TheoGiaTri_KhongTheoChuoi()
    {
        var v = new IdsValue { Simple = "0.3" };
        Assert.True(v.Accepts("0.29999999999999999"));
        Assert.True(v.Accepts("0.30"));
        Assert.True(v.Accepts(".3"));
        Assert.False(v.Accepts("0.31"));

        Assert.True(new IdsValue { Simple = "3" }.Accepts("3."));
        Assert.True(new IdsValue { Simple = "true" }.Accepts("TRUE"));
        Assert.True(new IdsValue { Simple = "TRUE" }.Accepts(".T."));
        Assert.False(new IdsValue { Simple = "true" }.Accepts("FALSE"));
        Assert.False(new IdsValue { Simple = "1" }.Accepts("NaN"));

        var e = new IdsValue();
        e.Enumeration.Add("1.5");
        e.Enumeration.Add("EI60");
        Assert.True(e.Accepts("1.50"));
        Assert.True(e.Accepts("ei60"));
        Assert.False(e.Accepts("1.51"));
    }

    /// <summary>xs:restriction là HỘI: pattern VÀ chặn trên đều phải đúng.</summary>
    [Fact]
    public void IdsValue_RangBuocHoi_TatCaDeuPhaiDung()
    {
        var v = new IdsValue { Pattern = "[0-9]+", MaxInclusive = 120 };
        v.CompilePattern();
        Assert.True(v.Accepts("90"));
        Assert.False(v.Accepts("130"), "khớp pattern nhưng vượt chặn trên");
        Assert.False(v.Accepts("9a"), "trong chặn nhưng không khớp pattern");
        Assert.Contains("và", v.Describe());

        var both = new IdsValue { MinLength = 2, MaxLength = 4, Length = null };
        both.Enumeration.Add("AB");
        both.Enumeration.Add("ABCDE");
        Assert.True(both.Accepts("AB"));
        Assert.False(both.Accepts("ABCDE"), "trong enumeration nhưng dài quá maxLength");
    }

    [Fact]
    public void IdsValue_DoDaiChuoi()
    {
        var exact = new IdsValue { Length = 22 };
        Assert.True(exact.Accepts(new string('0', 22)));
        Assert.False(exact.Accepts(new string('0', 21)));
        Assert.Contains("dài đúng 22", exact.Describe());

        var range = new IdsValue { MinLength = 2, MaxLength = 3 };
        Assert.False(range.Accepts("a"));
        Assert.True(range.Accepts("ab"));
        Assert.True(range.Accepts("abc"));
        Assert.False(range.Accepts("abcd"));
        Assert.False(range.IsAny);
        Assert.Contains("dài ≥ 2 và dài ≤ 3", range.Describe());
    }

    [Fact]
    public void IdsValue_KhoangSo_GiaTriKhongPhaiSo_Truot()
    {
        var v = new IdsValue { MinInclusive = 1, MaxExclusive = 5 };
        Assert.True(v.Accepts("1"));
        Assert.True(v.Accepts("4.99"));
        Assert.False(v.Accepts("5"));
        Assert.False(v.Accepts("năm"));
    }

    [Fact]
    public void IdsValue_PatternKhongHopLe_NemLucDocFile_KhongPhaiLucKiem()
    {
        var ex = Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"p\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name><value><xs:restriction " + Xs + " base=\"xs:string\"><xs:pattern value=\"(unclosed\"/></xs:restriction></value></attribute></requirements></specification>"));
        Assert.Contains("xs:pattern", ex.Message);

        // Không gọi CompilePattern trước thì Accepts tự biên dịch — vẫn ném IdsParseException, không ArgumentException.
        var lazy = new IdsValue { Pattern = "(" };
        Assert.Throws<IdsParseException>(() => lazy.Accepts("x"));
        var ok = new IdsValue { Pattern = "A-\\d+" };
        Assert.True(ok.Accepts("A-12"));
        Assert.False(ok.Accepts("A-12-rác"));
    }

    [Fact]
    public void IdsValue_PatternQuaLau_KhongTreo_Truot()
    {
        var evil = new IdsValue { Pattern = "(a+)+$" };
        evil.CompilePattern();
        var input = new string('a', 40) + "!";
        var started = DateTime.UtcNow;
        Assert.False(evil.Accepts(input));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "phải bị chặn bởi PatternTimeout");
    }

    // ── IdsSpec: cardinality mức specification, ifcVersion, simpleValue rỗng ─

    [Fact]
    public void IdsSpec_MinMaxOccurs_DocDung()
    {
        var specs = Parse(
            "<specification name=\"cam\" ifcVersion=\"IFC4 IFC4X3_ADD2\" minOccurs=\"0\" maxOccurs=\"0\"><applicability><entity><name><simpleValue>IFCPIPESEGMENT</simpleValue></name></entity></applicability><requirements/></specification>"
            + "<specification name=\"tuychon\" ifcVersion=\"IFC2X3\" minOccurs=\"0\" maxOccurs=\"unbounded\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability><requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>"
            + "<specification name=\"batbuoc\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability><requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>");

        Assert.True(specs[0].IsProhibited);
        Assert.False(specs[0].IsOptional);
        Assert.Equal(new[] { "IFC4", "IFC4X3_ADD2" }, specs[0].IfcVersions);
        Assert.Empty(specs[0].Requirements);

        Assert.True(specs[1].IsOptional);
        Assert.False(specs[1].IsProhibited);
        Assert.Null(specs[1].MaxOccurs);
        Assert.True(specs[1].AppliesTo("ifc2x3"));
        Assert.False(specs[1].AppliesTo("IFC4"));
        Assert.True(specs[1].AppliesTo(null));

        Assert.Equal(1, specs[2].MinOccurs);
        Assert.False(specs[2].IsOptional);
        Assert.True(new IdsSpecification().AppliesTo("IFC4"), "không khai ifcVersion → áp cho mọi file");
    }

    [Fact]
    public void IdsSpec_MaxOccursSai_TuChoi()
    {
        var ex = Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"x\" ifcVersion=\"IFC4\" maxOccurs=\"-1\"><applicability/><requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>"));
        Assert.Contains("maxOccurs", ex.Message);
    }

    [Fact]
    public void IdsSpec_KhongCoRequirements_ChiChapNhanKhiCam()
    {
        Assert.Throws<IdsParseException>(() => Parse("<specification name=\"x\" ifcVersion=\"IFC4\"><applicability/><requirements/></specification>"));
        var ok = Parse("<specification name=\"x\" ifcVersion=\"IFC4\" minOccurs=\"0\" maxOccurs=\"0\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability></specification>");
        Assert.True(Assert.Single(ok).IsProhibited);
    }

    [Fact]
    public void IdsSpec_SimpleValueRong_TuChoi()
    {
        var ex = Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"x\" ifcVersion=\"IFC4\"><applicability/><requirements><attribute><name><simpleValue>Name</simpleValue></name><value><simpleValue> </simpleValue></value></attribute></requirements></specification>"));
        Assert.Contains("<simpleValue> rỗng", ex.Message);
    }

    [Fact]
    public void IdsSpec_DoDaiChuoi_VaDataType_DocDuoc()
    {
        var specs = Parse(
            "<specification name=\"gid\" ifcVersion=\"IFC4\"><applicability/><requirements>"
            + "<attribute><name><simpleValue>GlobalId</simpleValue></name><value><xs:restriction " + Xs + " base=\"xs:string\"><xs:length value=\"22\"/><xs:minLength value=\"1\"/><xs:maxLength value=\"30\"/></xs:restriction></value></attribute>"
            + "<property dataType=\"IFCTHERMALTRANSMITTANCEMEASURE\"><propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet><baseName><simpleValue>ThermalTransmittance</simpleValue></baseName><value><simpleValue>0.3</simpleValue></value></property>"
            + "</requirements></specification>");
        var attr = specs[0].Requirements[0];
        Assert.Equal(22, attr.Value.Length);
        Assert.Equal(1, attr.Value.MinLength);
        Assert.Equal(30, attr.Value.MaxLength);
        Assert.Equal("IFCTHERMALTRANSMITTANCEMEASURE", specs[0].Requirements[1].DataType);

        Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"x\" ifcVersion=\"IFC4\"><applicability/><requirements><attribute><name><simpleValue>Name</simpleValue></name><value><xs:restriction " + Xs + " base=\"xs:string\"><xs:length value=\"-2\"/></xs:restriction></value></attribute></requirements></specification>"));
    }

    // ── IdsEvaluator ─────────────────────────────────────────────────────

    [Fact]
    public void IdsEvaluator_SpecificationCam_MoiPhanTuLotLaViPham()
    {
        var specs = Parse("<specification name=\"Không ống trên mái\" ifcVersion=\"IFC4\" minOccurs=\"0\" maxOccurs=\"0\"><applicability><entity><name><simpleValue>IFCPIPESEGMENT</simpleValue></name></entity></applicability></specification>");
        var pipe = new FakeIdsElement { IfcEntity = "IfcPipeSegment", Label = "#1" };
        var wall = new FakeIdsElement { IfcEntity = "IfcWall", Label = "#2" };

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { pipe, wall });

        var spec = Assert.Single(result.Specifications);
        Assert.Equal(1, spec.Applicable);
        Assert.Equal(0, spec.Passed);
        Assert.Equal(1, spec.Failed);
        Assert.Contains("cấm", Assert.Single(spec.Failures).Reason);

        // Không phần tử nào lọt → specification cấm ĐẠT (không phải "0 phần tử — không kiểm được gì").
        var clean = IdsEvaluator.Check(specs, new IIdsElement[] { wall });
        Assert.Equal(0, clean.Specifications[0].Applicable);
        Assert.True(clean.Specifications[0].NoApplicableElements);
    }

    [Fact]
    public void IdsEvaluator_IfcVersionKhongKhop_BoQuaVaNoiRo()
    {
        var specs = Parse(
            "<specification name=\"chỉ 2x3\" ifcVersion=\"IFC2X3\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability><requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>"
            + "<specification name=\"cả hai\" ifcVersion=\"IFC2X3 IFC4\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability><requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>");
        var wall = new FakeIdsElement { IfcEntity = "IfcWall", Label = "w" };

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { wall }, "IFC4");

        Assert.True(result.Specifications[0].Skipped);
        Assert.Contains("IFC2X3", result.Specifications[0].SkipReason);
        Assert.False(result.Specifications[0].NoApplicableElements);
        Assert.Equal(0, result.EmptySpecificationCount);
        Assert.False(result.Specifications[1].Skipped);
        Assert.Equal(1, result.Specifications[1].Applicable);

        var messages = IdsReport.Messages(result, Array.Empty<string>()).ToList();
        Assert.Contains(messages, m => m.StartsWith("chỉ 2x3: bỏ qua"));
        var html = IdsReport.Html("m", "a.ids", IdsReport.IfcScopeNote, result, Array.Empty<string>());
        Assert.Contains("bỏ qua: ifcVersion", html);

        // Không biết lược đồ (đường Revit) → không lọc.
        Assert.False(IdsEvaluator.Check(specs, new IIdsElement[] { wall }).Specifications[0].Skipped);
    }

    /// <summary>propertySet khai bằng enumeration: thử từng pset; khai bằng pattern: trượt (không rơi về "mọi pset").</summary>
    [Fact]
    public void IdsEvaluator_PropertySet_Enumeration_VaPattern()
    {
        var specs = Parse(
            "<specification name=\"e\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability><requirements>"
            + "<property><propertySet><xs:restriction " + Xs + " base=\"xs:string\"><xs:enumeration value=\"Pset_WallCommon\"/><xs:enumeration value=\"Pset_Khac\"/></xs:restriction></propertySet><baseName><simpleValue>FireRating</simpleValue></baseName></property>"
            + "</requirements></specification>"
            + "<specification name=\"p\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability><requirements>"
            + "<property><propertySet><xs:restriction " + Xs + " base=\"xs:string\"><xs:pattern value=\"Pset_.*\"/></xs:restriction></propertySet><baseName><simpleValue>FireRating</simpleValue></baseName></property>"
            + "</requirements></specification>");
        var wall = new FakeIdsElement { IfcEntity = "IfcWall", Label = "w" };
        wall.Properties["Pset_Khac.FireRating"] = "EI60";
        var other = new FakeIdsElement { IfcEntity = "IfcWall", Label = "o" };
        other.Properties["Pset_XLa.FireRating"] = "EI60";

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { wall, other });

        Assert.Equal(1, result.Specifications[0].Passed);
        Assert.Equal("o", Assert.Single(result.Specifications[0].Failures).Element);
        Assert.Equal(0, result.Specifications[1].Passed);

        // Không khai propertySet: tên property trần ở bất kỳ pset nào; không có → trượt.
        var bare = Parse("<specification name=\"b\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability><requirements>"
            + "<property><baseName><simpleValue>LoadBearing</simpleValue></baseName></property></requirements></specification>");
        var bareResult = IdsEvaluator.Check(bare, new IIdsElement[] { wall });
        Assert.Equal(0, bareResult.Specifications[0].Passed);
        Assert.Equal(1, bareResult.Specifications[0].Failed);
    }

    // ── IfcModel: loại property, tập pset ─────────────────────────────────

    [Fact]
    public void IfcModel_DocDungLoaiProperty()
    {
        var model = Ifc(
            $"#1=IFCWALL('{G("W")}',$,'Tuong',$,$,$,$,'T-1',$);\n"
            + "#2=IFCPROPERTYSINGLEVALUE('Single',$,IFCLABEL('A'),$);\n"
            + "#3=IFCPROPERTYENUMERATEDVALUE('Status',$,(IFCLABEL('New')),$);\n"
            + "#4=IFCPROPERTYLISTVALUE('Sizes',$,(IFCLENGTHMEASURE(10.),IFCLENGTHMEASURE(20.)),$);\n"
            + "#5=IFCPROPERTYBOUNDEDVALUE('FireRating',$,IFCLABEL('120'),IFCLABEL('60'),$,$);\n"
            + "#6=IFCPROPERTYBOUNDEDVALUE('Temp',$,IFCREAL(30.),IFCREAL(10.),$,IFCREAL(21.));\n"
            + "#7=IFCPROPERTYSINGLEVALUE('Sub',$,IFCLABEL('S'),$);\n"
            + "#8=IFCCOMPLEXPROPERTY('Complex',$,'usage',(#7));\n"
            + "#9=IFCPROPERTYREFERENCEVALUE('Ref',$,$,$);\n"
            + "#12=IFCPROPERTYSINGLEVALUE($,$,IFCLABEL('khong ten'),$);\n"
            + "#13=IFCPROPERTYENUMERATEDVALUE('EnumLe',$,IFCLABEL('X'),$);\n"
            + "#10=IFCPROPERTYSET('0Pset000000000000000000',$,'Pset_A',$,(#2,#3,#4,#5,#6,#8,#9,#12,#13));\n"
            + "#11=IFCRELDEFINESBYPROPERTIES('0Rel0000000000000000000',$,$,$,(#1),#10);");
        var wall = model.Elements().Single();

        Assert.Equal("A", wall.Property("Pset_A", "Single"));
        Assert.Equal("New", wall.Property("Pset_A", "Status"));
        Assert.Equal("10.;20.", wall.Property("Pset_A", "Sizes"));
        Assert.Equal("120", wall.Property("Pset_A", "FireRating"));
        Assert.Equal("21.", wall.Property("Pset_A", "Temp"));
        Assert.Equal("S", wall.Property("Pset_A.Complex", "Sub"));
        Assert.Null(wall.Property("Pset_A", "Ref"));
        Assert.True(model.Model.TryProperty(1, "Pset_A.Ref", out _), "property tham chiếu vẫn 'có mặt'");
        Assert.Equal("X", wall.Property("Pset_A", "EnumLe"));
        Assert.False(model.Model.TryProperty(1, "Pset_A.", out _), "property không tên bị bỏ");
    }

    [Fact]
    public void IfcModel_RelatingPropertyDefinition_LaTap_KhongMat()
    {
        var model = Ifc(
            $"#1=IFCWALL('{G("W")}',$,'Tuong',$,$,$,$,'T-1',$);\n"
            + "#2=IFCPROPERTYSINGLEVALUE('A',$,IFCLABEL('1'),$);\n"
            + "#3=IFCPROPERTYSINGLEVALUE('B',$,IFCLABEL('2'),$);\n"
            + "#10=IFCPROPERTYSET('0PsetA00000000000000000',$,'Pset_A',$,(#2));\n"
            + "#11=IFCPROPERTYSET('0PsetB00000000000000000',$,'Pset_B',$,(#3));\n"
            + "#12=IFCRELDEFINESBYPROPERTIES('0Rel0000000000000000000',$,$,$,(#1),(#10,#11));");
        var wall = model.Elements().Single();
        Assert.Equal("1", wall.Property("Pset_A", "A"));
        Assert.Equal("2", wall.Property("Pset_B", "B"));
    }

    [Fact]
    public void IfcStepParser_SoHieuQuaInt32_LaIfcParseException()
    {
        var ex = Assert.Throws<IfcParseException>(() => Ifc("#4294967296=IFCWALL('0aB0000000000000000000',$,'x',$,$,$,$,$,$);"));
        Assert.Contains("4294967296", ex.Message);
    }

    // ── IfcIdsElement: PredefinedType, Tag, IsType ───────────────────────

    [Fact]
    public void IfcIdsElement_PredefinedTypeRong_KhongLayEnumKeTiep()
    {
        var model = Ifc(
            $"#1=IFCDOOR('{G("D")}',$,'Cua',$,$,$,$,'D1',2100.,900.,$,.DOUBLE_DOOR_SINGLE_SWING.,$);\n"
            + $"#2=IFCPILE('{G("P")}',$,'Coc',$,$,$,$,'P1',.BORED.,.CAST_IN_PLACE.);\n"
            + $"#3=IFCPILE('{G("Q")}',$,'Coc2',$,$,$,$,'P2',$,.CAST_IN_PLACE.);\n"
            + $"#4=IFCSPACE('{G("S")}',$,'101',$,$,$,$,'Phong',.ELEMENT.,.INTERNAL.,$);");
        var byId = model.Elements().Cast<IfcIdsElement>().ToDictionary(e => e.Id);

        Assert.Equal(string.Empty, byId[1].PredefinedType);
        Assert.Null(byId[1].Attribute("PredefinedType"));
        Assert.Equal("BORED", byId[2].PredefinedType);
        Assert.Equal(string.Empty, byId[3].PredefinedType);
        Assert.Equal("INTERNAL", byId[4].PredefinedType);
        // Space: vị trí 7 là LongName, không phải Tag.
        Assert.Null(byId[4].Attribute("Tag"));
        Assert.Equal("Phong", byId[4].Attribute("LongName"));
        Assert.Equal("D1", byId[1].Attribute("Tag"));
    }

    [Fact]
    public void IfcIdsElement_IfcDoorStyle_LaKieu()
    {
        var model = Ifc(
            $"#1=IFCDOORSTYLE('{G("DS")}',$,'Style',$,$,$,$,'DS-1',.SINGLE_SWING_LEFT.,.ALUMINIUM.,.F.,.F.);\n"
            + $"#2=IFCDOOR('{G("D")}',$,'Cua',$,$,$,$,'D1',2100.,900.);\n"
            + "#3=IFCRELDEFINESBYTYPE('0Rel0000000000000000000',$,$,$,(#2),#1);");
        var door = model.Elements().Cast<IfcIdsElement>().Single(e => e.Id == 2);
        // Kiểu 2x3 không có PredefinedType → rỗng, không đọc nhầm OperationType.
        Assert.Equal(string.Empty, door.PredefinedType);
        var style = model.Elements().Cast<IfcIdsElement>().Single(e => e.Id == 1);
        Assert.Null(style.Attribute("ObjectType"));
    }

    // ── IfcGuid ──────────────────────────────────────────────────────────

    [Fact]
    public void IfcGuid_NenGiaiNen_VongTron()
    {
        Assert.Equal("0000000000000000000000", IfcGuid.Compress(Guid.Empty));
        Assert.Equal(Guid.Empty, IfcGuid.Expand("0000000000000000000000"));

        var rng = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var bytes = new byte[16];
            rng.NextBytes(bytes);
            var guid = new Guid(bytes);
            var text = IfcGuid.Compress(guid);
            Assert.True(IfcGuid.IsValid(text), text);
            Assert.Equal(guid, IfcGuid.Expand(text));
        }

        // Giá trị lớn nhất: mọi bit 1 → ký tự đầu '3'.
        var max = new Guid(Enumerable.Repeat((byte)0xFF, 16).ToArray());
        Assert.StartsWith("3", IfcGuid.Compress(max));
        Assert.Equal(max, IfcGuid.Expand(IfcGuid.Compress(max)));
    }

    [Fact]
    public void IfcGuid_ChuoiSai_TuChoi()
    {
        Assert.False(IfcGuid.IsValid(null));
        Assert.False(IfcGuid.IsValid("4000000000000000000000"));
        Assert.False(IfcGuid.IsValid("000000000000000000000!"));
        Assert.Throws<FormatException>(() => IfcGuid.Expand("ngắn"));
        Assert.Throws<FormatException>(() => IfcGuid.Expand("000000000000000000000!"));
        Assert.Throws<FormatException>(() => IfcGuid.Expand("4000000000000000000000"));
    }

    [Fact]
    public void IfcGuid_TuUniqueIdRevit()
    {
        // UniqueId = GUID phiên + "-" + ElementId hex 8: 8 hex cuối XOR ElementId rồi nén.
        var uid = "a0a4b5c6-1234-4abc-8def-00000000ffff-0000000f";
        var text = IfcGuid.FromRevitUniqueId(uid)!;
        Assert.True(IfcGuid.IsValid(text));
        Assert.Equal(Guid.Parse("a0a4b5c6-1234-4abc-8def-00000000fff0"), IfcGuid.Expand(text));

        Assert.Null(IfcGuid.FromRevitUniqueId(null));
        Assert.Null(IfcGuid.FromRevitUniqueId("không phải"));
        Assert.Null(IfcGuid.FromRevitUniqueId("a0a4b5c6-1234-4abc-8def-00000000ffff-zzzzzzzz"));
        Assert.Null(IfcGuid.FromRevitUniqueId("g0a4b5c6-1234-4abc-8def-00000000ffff-0000000f"));
    }
}
