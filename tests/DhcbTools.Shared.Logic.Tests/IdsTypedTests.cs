using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Đường IFC CÓ KIỂU của bộ kiểm IDS — bản thu nhỏ của các nhóm ca trong bộ kiểm chính thức của buildingSMART
/// (attribute, classification, entity IFC2X3, property, restriction) mà đường chuỗi cũ trượt: 63 ca lệch còn lại sau
/// audit 2026-09-28, nay 334/334. Mỗi test ghi rõ luật IDS 1.0 nó giữ.
/// </summary>
public class IdsTypedTests
{
    private static string G(string tag) => ("0" + tag).PadRight(22, '0');

    private static IfcIdsModel Ifc(string body, string schema = "IFC4") => IfcIdsModel.Parse(
        "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('" + schema + "'));\nENDSEC;\nDATA;\n" + body + "\nENDSEC;\nEND-ISO-10303-21;");

    private static string Sv(string value) => "<simpleValue>" + value + "</simpleValue>";

    private static string Entity(string name, string predefinedType = "") =>
        "<entity><name>" + Sv(name) + "</name>" + (predefinedType.Length > 0 ? "<predefinedType>" + Sv(predefinedType) + "</predefinedType>" : string.Empty) + "</entity>";

    private static string Restriction(string inner, string xsBase = "double") =>
        "<xs:restriction base=\"xs:" + xsBase + "\">" + inner + "</xs:restriction>";

    private static string Attr(string name, string value = "", string cardinality = "required") =>
        "<attribute cardinality=\"" + cardinality + "\"><name>" + name + "</name>" + (value.Length > 0 ? "<value>" + value + "</value>" : string.Empty) + "</attribute>";

    private static string Prop(string set, string name, string value = "", string dataType = "", string cardinality = "required") =>
        "<property cardinality=\"" + cardinality + "\"" + (dataType.Length > 0 ? " dataType=\"" + dataType + "\"" : string.Empty) + "><propertySet>" + set + "</propertySet><baseName>"
        + name + "</baseName>" + (value.Length > 0 ? "<value>" + value + "</value>" : string.Empty) + "</property>";

    private static string Cls(string value = "", string system = "", string cardinality = "required") =>
        "<classification cardinality=\"" + cardinality + "\">" + (value.Length > 0 ? "<value>" + value + "</value>" : string.Empty)
        + (system.Length > 0 ? "<system>" + system + "</system>" : string.Empty) + "</classification>";

    private static IdsSpecificationResult Result(IfcIdsModel model, string applicability, string requirements)
    {
        var specs = IdsSpec.Parse(
            "<ids xmlns=\"http://standards.buildingsmart.org/IDS\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><specifications>"
            + "<specification name=\"t\" ifcVersion=\"IFC2X3 IFC4\"><applicability>" + applicability + "</applicability>"
            + (requirements.Length > 0 ? "<requirements>" + requirements + "</requirements>" : string.Empty)
            + "</specification></specifications></ids>");
        return IdsEvaluator.Check(specs, model.Elements(), model.Model.Schema).Specifications.Single();
    }

    /// <summary>"pass"/"fail" đúng như tên ca của buildingSMART.</summary>
    private static string Run(IfcIdsModel model, string applicability, string requirements) =>
        Result(model, applicability, requirements).IsFailed ? "fail" : "pass";

    // ── attribute ────────────────────────────────────────────────────────

    /// <summary>Số thực: 42 / 42. / 42.0 / 4.2e1 đều bằng 42. (ép kiểu), bound và enumeration so số; pattern không áp lên số.</summary>
    [Fact]
    public void Attribute_SoThuc_EpKieu_Bound_Enumeration_Pattern()
    {
        var m = Ifc("#1=IFCSURFACESTYLEREFRACTION(42.,$);");
        var on = Entity("IFCSURFACESTYLEREFRACTION");
        var name = Sv("RefractionIndex");
        foreach (var ok in new[] { "42", "42.", "42.0", "4.2e1" })
        {
            Assert.Equal("pass", Run(m, on, Attr(name, Sv(ok))));
        }

        Assert.Equal("fail", Run(m, on, Attr(name, Sv("43"))));
        Assert.Equal("fail", Run(m, on, Attr(name, Sv("abc"))));
        Assert.Equal("pass", Run(m, on, Attr(name, Restriction("<xs:enumeration value=\"41\"/><xs:enumeration value=\"42\"/>"))));
        Assert.Equal("fail", Run(m, on, Attr(name, Restriction("<xs:enumeration value=\"41\"/><xs:enumeration value=\"43\"/>"))));
        Assert.Equal("pass", Run(m, on, Attr(name, Restriction("<xs:minExclusive value=\"41\"/><xs:maxExclusive value=\"43\"/>"))));
        Assert.Equal("fail", Run(m, on, Attr(name, Restriction("<xs:minInclusive value=\"0\"/><xs:maxInclusive value=\"10\"/>"))));
        Assert.Equal("fail", Run(m, on, Attr(name, Restriction("<xs:pattern value=\".*\"/>", "string"))));
        // DispersionFactor = $: vắng — bắt buộc thì trượt, tuỳ chọn thì đạt, cấm thì đạt.
        Assert.Equal("fail", Run(m, on, Attr(Sv("DispersionFactor"))));
        Assert.Equal("pass", Run(m, on, Attr(Sv("DispersionFactor"), Sv("1"), "optional")));
        Assert.Equal("pass", Run(m, on, Attr(Sv("DispersionFactor"), cardinality: "prohibited")));
        Assert.Equal("fail", Run(m, on, Attr(Sv("RefractionIndex"), cardinality: "prohibited")));
    }

    /// <summary>Số nguyên: "42" và "+42" khớp, "42.0" thì không — xs:integer không có phần thập phân.</summary>
    [Fact]
    public void Attribute_SoNguyen_KhongNhanDangThapPhan()
    {
        var m = Ifc($"#1=IFCSTAIRFLIGHT('{G("S")}',$,$,$,$,$,$,$,42,$,$,$,$);");
        var on = Entity("IFCSTAIRFLIGHT");
        Assert.Equal("pass", Run(m, on, Attr(Sv("NumberOfRisers"), Sv("42"))));
        Assert.Equal("pass", Run(m, on, Attr(Sv("NumberOfRisers"), Sv("+42"))));
        Assert.Equal("fail", Run(m, on, Attr(Sv("NumberOfRisers"), Sv("42.0"))));
        Assert.Equal("fail", Run(m, on, Attr(Sv("NumberOfRisers"), Sv("43"))));
    }

    /// <summary>
    /// Boolean chỉ true/false/1/0 chữ thường; logical .U. là vắng; tham chiếu thực thể "có mặt" nhưng mọi ràng buộc giá
    /// trị đều trượt; chuỗi thời lượng so nguyên văn.
    /// </summary>
    [Fact]
    public void Attribute_Boolean_Logical_ThamChieu_ThoiLuong()
    {
        var m = Ifc(
            $"#1=IFCTASK('{G("T")}',$,$,$,$,$,$,$,$,.F.,$,#2,$);\n"
            + "#2=IFCTASKTIME($,$,$,$,'P0D',$,$,$,$,$,$,$,$,.U.,$,$,$,$,$,$);");
        var task = Entity("IFCTASK");
        Assert.Equal("pass", Run(m, task, Attr(Sv("IsMilestone"), Sv("false"))));
        Assert.Equal("pass", Run(m, task, Attr(Sv("IsMilestone"), Sv("0"))));
        Assert.Equal("fail", Run(m, task, Attr(Sv("IsMilestone"), Sv("FALSE"))));
        Assert.Equal("fail", Run(m, task, Attr(Sv("IsMilestone"), Sv("true"))));
        Assert.Equal("fail", Run(m, task, Attr(Sv("IsMilestone"), Restriction("<xs:minInclusive value=\"0\"/>"))));
        Assert.Equal("pass", Run(m, task, Attr(Sv("TaskTime"))));
        Assert.Equal("fail", Run(m, task, Attr(Sv("TaskTime"), Sv("x"))));

        var time = Entity("IFCTASKTIME");
        Assert.Equal("fail", Run(m, time, Attr(Sv("IsCritical"))));
        Assert.Equal("pass", Run(m, time, Attr(Sv("ScheduleDuration"), Sv("P0D"))));
        Assert.Equal("fail", Run(m, time, Attr(Sv("ScheduleDuration"), Sv("P0.0D"))));
    }

    /// <summary>
    /// Chuỗi so nguyên văn ('42' không bằng 42.0), bound trên chuỗi thì đọc số; chuỗi rỗng là "có mà sai" nên thuộc
    /// tính tuỳ chọn cũng trượt; tên thuộc tính không tồn tại là vắng.
    /// </summary>
    [Fact]
    public void Attribute_Chuoi_Rong_KhongTonTai()
    {
        var m = Ifc(
            $"#1=IFCWALL('{G("W")}',$,'',$,$,$,$,$,$);\n"
            + $"#2=IFCSLAB('{G("S")}',$,'42',$,$,$,$,$,$);\n"
            + $"#3=IFCBEAM('{G("B")}',$,'abc',$,$,$,$,$,$);");
        var wall = Entity("IFCWALL");
        Assert.Equal("fail", Run(m, wall, Attr(Sv("Name"), Sv("Foobar"), "optional")));
        Assert.Equal("fail", Run(m, wall, Attr(Sv("Name"))));
        Assert.Equal("pass", Run(m, wall, Attr(Sv("Description"), Sv("Foobar"), "optional")));
        Assert.Equal("fail", Run(m, wall, Attr(Sv("Foo"))));
        Assert.Equal("fail", Run(m, wall, Attr(Sv("name"))));

        var slab = Entity("IFCSLAB");
        Assert.Equal("pass", Run(m, slab, Attr(Sv("Name"), Sv("42"))));
        Assert.Equal("fail", Run(m, slab, Attr(Sv("Name"), Sv("42.0"))));
        Assert.Equal("pass", Run(m, slab, Attr(Sv("Name"), Restriction("<xs:minInclusive value=\"40\"/><xs:maxInclusive value=\"50\"/>"))));
        Assert.Equal("fail", Run(m, Entity("IFCBEAM"), Attr(Sv("Name"), Restriction("<xs:minInclusive value=\"40\"/>"))));
    }

    /// <summary>Danh sách rỗng là "có mà sai"; tên thuộc tính khai bằng pattern khớp mọi thuộc tính hợp tên.</summary>
    [Fact]
    public void Attribute_DanhSachRong_VaTenBangPattern()
    {
        var m = Ifc("#1=IFCMATERIALLAYERSET((),'Foo',$);");
        var on = Entity("IFCMATERIALLAYERSET");
        Assert.Equal("fail", Run(m, on, Attr(Sv("MaterialLayers"))));
        Assert.Equal("fail", Run(m, on, Attr(Sv("MaterialLayers"), cardinality: "optional")));
        Assert.Equal("pass", Run(m, on, Attr(Restriction("<xs:pattern value=\".*Name.*\"/>", "string"))));
        Assert.Equal("pass", Run(m, on, Attr(Restriction("<xs:pattern value=\".*Name.*\"/>", "string"), Sv("Foo"))));
    }

    /// <summary>Số hỏng trong file ("-" trơ trọi) không làm sập lần kiểm: coi như chuỗi, có mặt nhưng không bằng số nào.</summary>
    [Fact]
    public void Attribute_SoHongTrongFile_CoiNhuChuoi()
    {
        var m = Ifc("#1=IFCSURFACESTYLEREFRACTION(-,$);");
        var on = Entity("IFCSURFACESTYLEREFRACTION");
        Assert.Equal("pass", Run(m, on, Attr(Sv("RefractionIndex"))));
        Assert.Equal("fail", Run(m, on, Attr(Sv("RefractionIndex"), Sv("42"))));
    }

    /// <summary>
    /// Applicability rỗng trên đường IFC: mọi IfcObjectDefinition (đối tượng và kiểu) — như tập "có GlobalId, trừ quan hệ
    /// và pset" trước đây; điểm toạ độ, vật liệu, quan hệ không lọt vào.
    /// </summary>
    [Fact]
    public void ApplicabilityRong_ChiDoiTuongVaKieu()
    {
        var m = Ifc(
            $"#1=IFCWALL('{G("W")}',$,'Tuong',$,$,$,$,$,$);\n"
            + $"#2=IFCWALLTYPE('{G("T")}',$,'Kieu',$,$,$,$,$,$,.SOLIDWALL.);\n"
            + $"#3=IFCRELDEFINESBYTYPE('{G("R")}',$,$,$,(#1),#2);\n"
            + "#4=IFCCARTESIANPOINT((0.,0.,0.));\n#5=IFCMATERIAL('M',$,$);");
        var result = Result(m, string.Empty, Attr(Sv("Name")));
        Assert.Equal(2, result.Applicable);
        Assert.Equal(2, result.Passed);
    }

    // ── entity ───────────────────────────────────────────────────────────

    /// <summary>IFC2X3 không có IfcAirTerminal: IfcFlowTerminal có kiểu IfcAirTerminalType được tính là IFCAIRTERMINAL.</summary>
    [Fact]
    public void Entity_Ifc2x3_AnhXaTheoKieu()
    {
        const string body = "#1=IFCFLOWTERMINAL('0F00000000000000000000',$,'AIR-01',$,$,$,$,$);\n"
                            + "#2=IFCAIRTERMINALTYPE('0T00000000000000000000',$,$,$,$,$,$,$,$,.DIFFUSER.);\n"
                            + "#3=IFCRELDEFINESBYTYPE('0R00000000000000000000',$,$,$,(#1),#2);";
        var ifc2x3 = Ifc(body, "IFC2X3");
        Assert.Equal("pass", Run(ifc2x3, Entity("IFCAIRTERMINAL"), string.Empty));
        Assert.Equal("pass", Run(ifc2x3, Entity("IFCAIRTERMINAL"), Entity("IFCAIRTERMINAL", "DIFFUSER")));
        Assert.Equal("pass", Run(ifc2x3, Entity("IFCFLOWTERMINAL"), Attr(Sv("Name"), Sv("AIR-01"))));
        // IFC4 có IfcAirTerminal thật — không ánh xạ.
        Assert.Equal("fail", Run(Ifc(body), Entity("IFCAIRTERMINAL"), string.Empty));
    }

    // ── classification ───────────────────────────────────────────────────

    /// <summary>
    /// Gán thẳng cả một IfcClassification: có hệ, không có mã. Tham chiếu không tới gốc nào: có mã, không có hệ.
    /// Có phân loại nào là "có mặt" — tuỳ chọn mà sai thì trượt.
    /// </summary>
    [Fact]
    public void Classification_GanThangHe_ThamChieuKhongGoc_TuyChon()
    {
        var m = Ifc(
            "#1=IFCCLASSIFICATION($,$,$,'Foobar',$,$,$);\n"
            + $"#2=IFCWALL('{G("W")}',$,$,$,$,$,$,$,$);\n"
            + $"#3=IFCRELASSOCIATESCLASSIFICATION('{G("R1")}',$,$,$,(#2),#1);\n"
            + $"#4=IFCSLAB('{G("S")}',$,$,$,$,$,$,$,$);\n"
            + "#5=IFCCLASSIFICATIONREFERENCE($,'X',$,$,$,$);\n"
            + $"#6=IFCRELASSOCIATESCLASSIFICATION('{G("R2")}',$,$,$,(#4),#5);\n"
            + $"#7=IFCBEAM('{G("B")}',$,$,$,$,$,$,$,$);");
        var wall = Entity("IFCWALL");
        Assert.Equal("pass", Run(m, wall, Cls(system: Sv("Foobar"))));
        Assert.Equal("fail", Run(m, wall, Cls(Sv("X"))));
        Assert.Equal("fail", Run(m, wall, Cls(Sv("X"), cardinality: "optional")));

        var slab = Entity("IFCSLAB");
        Assert.Equal("pass", Run(m, slab, Cls(Sv("X"))));
        Assert.Equal("fail", Run(m, slab, Cls(system: Sv("Foobar"))));
        Assert.Equal("fail", Run(m, slab, Cls(system: Restriction("<xs:pattern value=\"\\w+\"/>", "string"))));

        var beam = Entity("IFCBEAM");
        Assert.Equal("fail", Run(m, beam, Cls()));
        Assert.Equal("pass", Run(m, beam, Cls(Sv("X"), cardinality: "optional")));
    }

    /// <summary>Phần tử kế thừa phân loại của kiểu cho những hệ nó không tự khai; cùng hệ thì của phần tử thắng.</summary>
    [Fact]
    public void Classification_PhanTuDeKieu_TheoTungHe()
    {
        var m = Ifc(
            "#1=IFCCLASSIFICATION($,$,$,'Foobar',$,$,$);\n"
            + "#2=IFCCLASSIFICATIONREFERENCE($,'11',$,#1,$,$);\n"
            + $"#3=IFCRELASSOCIATESCLASSIFICATION('{G("R1")}',$,$,$,(#4),#2);\n"
            + $"#4=IFCWALL('{G("W")}',$,'Wall',$,$,$,$,$,$);\n"
            + $"#5=IFCWALLTYPE('{G("T")}',$,'Type',$,$,$,$,$,$,.ELEMENTEDWALL.);\n"
            + $"#6=IFCRELDEFINESBYTYPE('{G("R2")}',$,$,$,(#4),#5);\n"
            + "#7=IFCCLASSIFICATION($,$,$,'Foobaz',$,$,$);\n"
            + "#8=IFCCLASSIFICATIONREFERENCE($,'X',$,#7,$,$);\n"
            + $"#9=IFCRELASSOCIATESCLASSIFICATION('{G("R3")}',$,$,$,(#5),#8);\n"
            + "#10=IFCCLASSIFICATIONREFERENCE($,'22',$,#1,$,$);\n"
            + $"#11=IFCRELASSOCIATESCLASSIFICATION('{G("R4")}',$,$,$,(#5),#10);");
        var wall = Entity("IFCWALL");
        Assert.Equal("pass", Run(m, wall, Cls(Sv("X"), Sv("Foobaz"))));
        Assert.Equal("pass", Run(m, wall, Cls(Sv("11"))));
        Assert.Equal("fail", Run(m, wall, Cls(Sv("22"))));
        Assert.Equal("pass", Run(m, Entity("IFCWALLTYPE"), Cls(Sv("22"))));

        var element = (IfcIdsElement)m.Elements().Single(e => e.IfcEntity == "IFCWALL");
        Assert.Equal(new[] { "11", "X" }, element.Classifications(null).ToArray());
        Assert.Equal(new[] { "X" }, element.Classifications("foobaz").ToArray());
    }

    /// <summary>
    /// Tài nguyên không có GlobalId (IfcMaterial) nhận phân loại qua IfcExternalReferenceRelationship. Nhưng khi facet
    /// applicability ĐẦU TIÊN là classification thì chỉ IfcObjectDefinition là ứng viên, như IfcTester.
    /// </summary>
    [Fact]
    public void Classification_TaiNguyenKhongGoc_VaTapUngVien()
    {
        var m = Ifc(
            "#1=IFCCLASSIFICATION($,$,$,'Foobar',$,$,$);\n"
            + "#2=IFCCLASSIFICATIONREFERENCE($,'1',$,#1,$,$);\n"
            + "#3=IFCMATERIAL('M',$,$);\n"
            + "#4=IFCEXTERNALREFERENCERELATIONSHIP($,$,#2,(#3));\n"
            + "#5=IFCEXTERNALREFERENCERELATIONSHIP($,$,$,(#3));\n"
            + $"#6=IFCWALL('{G("W")}',$,$,$,$,$,$,$,$);\n"
            + $"#7=IFCRELASSOCIATESCLASSIFICATION('{G("R")}',$,$,$,(#6),#2);");
        Assert.Equal("pass", Run(m, Entity("IFCMATERIAL"), Cls(Sv("1"), Sv("Foobar"))));
        Assert.Equal(1, Result(m, Cls(Sv("1")), Attr(Sv("GlobalId"))).Applicable);
    }

    // ── property ─────────────────────────────────────────────────────────

    private const string Units =
        "#1=IFCPROJECT('0Prj000000000000000000',$,$,$,$,$,$,$,#6);\n"
        + "#2=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);\n"
        + "#3=IFCSIUNIT(*,.AREAUNIT.,.MILLI.,.SQUARE_METRE.);\n"
        + "#4=IFCSIUNIT(*,.MASSUNIT.,$,.GRAM.);\n"
        + "#5=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);\n"
        + "#6=IFCUNITASSIGNMENT((#2,#3,#4,#5,#999,#7));\n"
        + "#7=IFCMONETARYUNIT('VND');\n"
        + "#20=IFCWALL('0W00000000000000000000',$,$,$,$,$,$,$,$);\n";

    /// <summary>Tường mang một pset "S" gồm các property cho sẵn (số hiệu bắt đầu từ #30); <paramref name="extra"/> từ #100.</summary>
    private static IfcIdsModel WallWith(string extra, params string[] properties)
    {
        var body = Units + extra;
        var refs = string.Join(",", properties.Select((_, i) => "#" + (30 + i)));
        body += "#21=IFCPROPERTYSET('0P00000000000000000000',$,'S',$,(" + refs + "));\n"
                + "#22=IFCRELDEFINESBYPROPERTIES('0R00000000000000000000',$,$,$,(#20),#21);\n";
        for (var i = 0; i < properties.Length; i++)
        {
            body += "#" + (30 + i) + "=" + properties[i] + ";\n";
        }

        return Ifc(body);
    }

    private static string OnWall(IfcIdsModel m, string name, string value = "", string dataType = "", string cardinality = "required") =>
        Run(m, Entity("IFCWALL"), Prop(Sv("S"), Sv(name), value, dataType, cardinality));

    /// <summary>Số có đơn vị được đổi về đơn vị chuẩn IDS: mm → m, mm² → m², g → kg, đơn vị quy đổi theo ConversionFactor.</summary>
    [Fact]
    public void Property_DoiDonVi_VeDonViChuanIds()
    {
        var m = WallWith(
            "#100=IFCCONVERSIONBASEDUNIT(*,.LENGTHUNIT.,'INCH',#101);\n"
            + "#101=IFCMEASUREWITHUNIT(IFCLENGTHMEASURE(0.0254),#5);\n"
            + "#102=IFCCONVERSIONBASEDUNIT(*,.LENGTHUNIT.,'loop',#103);\n"
            + "#103=IFCMEASUREWITHUNIT(IFCLENGTHMEASURE(2.),#102);\n"
            + "#104=IFCCONVERSIONBASEDUNIT(*,.LENGTHUNIT.,'bad',#105);\n"
            + "#105=IFCMEASUREWITHUNIT(IFCLABEL('x'),#5);\n"
            + "#106=IFCDERIVEDUNIT((),.THERMALTRANSMITTANCEUNIT.,$);\n"
            + "#107=IFCCONVERSIONBASEDUNIT(*,.LENGTHUNIT.,'none',$);\n"
            + "#108=IFCCONVERSIONBASEDUNIT(*,.LENGTHUNIT.,'half',#109);\n"
            + "#109=IFCMEASUREWITHUNIT(IFCREAL(0.5),$);\n",
            "IFCPROPERTYSINGLEVALUE('Len',$,IFCLENGTHMEASURE(2000.),$)",
            "IFCPROPERTYSINGLEVALUE('LenM',$,IFCLENGTHMEASURE(2.),#5)",
            "IFCPROPERTYSINGLEVALUE('Mass',$,IFCMASSMEASURE(5000.),$)",
            "IFCPROPERTYSINGLEVALUE('Area',$,IFCAREAMEASURE(1000000.),$)",
            "IFCPROPERTYSINGLEVALUE('Inch',$,IFCLENGTHMEASURE(2.),#100)",
            "IFCPROPERTYSINGLEVALUE('Loop',$,IFCLENGTHMEASURE(1.),#102)",
            "IFCPROPERTYSINGLEVALUE('Bad',$,IFCLENGTHMEASURE(3.),#104)",
            "IFCPROPERTYSINGLEVALUE('Derived',$,IFCTHERMALTRANSMITTANCEMEASURE(0.5),#106)",
            "IFCPROPERTYSINGLEVALUE('Num',$,IFCNUMERICMEASURE(7.),$)",
            "IFCPROPERTYSINGLEVALUE('NoFactor',$,IFCLENGTHMEASURE(4.),#107)",
            "IFCPROPERTYSINGLEVALUE('Half',$,IFCLENGTHMEASURE(10.),#108)");
        Assert.Equal("pass", OnWall(m, "Len", Sv("2"), "IFCLENGTHMEASURE"));
        Assert.Equal("fail", OnWall(m, "Len", Sv("2000")));
        Assert.Equal("fail", OnWall(m, "Len", Sv("2"), "IFCAREAMEASURE"));
        Assert.Equal("pass", OnWall(m, "LenM", Sv("2")));
        Assert.Equal("pass", OnWall(m, "Mass", Sv("5")));
        Assert.Equal("pass", OnWall(m, "Area", Sv("1")));
        Assert.Equal("pass", OnWall(m, "Inch", Sv("0.0508")));
        Assert.Equal("pass", OnWall(m, "Loop", Sv("512")));   // vòng quy đổi tự trỏ: dừng ở độ sâu 8, không treo
        Assert.Equal("pass", OnWall(m, "Bad", Sv("3")));      // hệ số không phải số: không đổi
        Assert.Equal("pass", OnWall(m, "Derived", Sv("0.5"))); // đơn vị dẫn xuất: không đổi (như IfcTester)
        Assert.Equal("pass", OnWall(m, "Num", Sv("7")));
        Assert.Equal("pass", OnWall(m, "NoFactor", Sv("4")));
        Assert.Equal("pass", OnWall(m, "Half", Sv("5")));
    }

    [Fact]
    public void Property_UnitTypeCuaKieuDo_GiongIfcopenshell()
    {
        Assert.Equal("LENGTHUNIT", IfcIdsModel.UnitTypeOf("IFCNONNEGATIVELENGTHMEASURE"));
        Assert.Equal("PLANEANGLEUNIT", IfcIdsModel.UnitTypeOf("IfcPositivePlaneAngleMeasure"));
        Assert.Equal("USERDEFINED", IfcIdsModel.UnitTypeOf("IFCNUMERICMEASURE"));
        Assert.Equal("LABELUNIT", IfcIdsModel.UnitTypeOf("LABEL"));
    }

    /// <summary>Không có IfcProject.UnitsInContext: dùng IfcUnitAssignment đầu tiên trong file.</summary>
    [Fact]
    public void Property_DonViDuAn_KhongCoProject_LayUnitAssignmentDauTien()
    {
        var m = Ifc(
            "#1=IFCUNITASSIGNMENT((#2));\n#2=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);\n"
            + $"#3=IFCWALL('{G("W")}',$,$,$,$,$,$,$,$);\n"
            + $"#4=IFCPROPERTYSET('{G("P")}',$,'S',$,(#5));\n#5=IFCPROPERTYSINGLEVALUE('Len',$,IFCLENGTHMEASURE(2000.),$);\n"
            + $"#6=IFCRELDEFINESBYPROPERTIES('{G("R")}',$,$,$,(#3),#4);");
        Assert.Equal("pass", Run(m, Entity("IFCWALL"), Prop(Sv("S"), Sv("Len"), Sv("2"))));
    }

    /// <summary>
    /// Quantity: kiểu đo theo lớp (IfcQuantityLength → IFCLENGTHMEASURE) và đổi đơn vị; giá trị $ là vắng; quantity
    /// phức không hỗ trợ → trượt.
    /// </summary>
    [Fact]
    public void Property_Quantity_KieuDo_DonVi_Phuc()
    {
        var m = Ifc(Units
            + "#21=IFCELEMENTQUANTITY('0Q00000000000000000000',$,'Qto',$,$,(#30,#31,#32));\n"
            + "#22=IFCRELDEFINESBYPROPERTIES('0R00000000000000000000',$,$,$,(#20),#21);\n"
            + "#30=IFCQUANTITYLENGTH('Length',$,$,4200.,$);\n"
            + "#31=IFCQUANTITYAREA('Area',$,$,$,$);\n"
            + "#32=IFCPHYSICALCOMPLEXQUANTITY('Complex',$,(#30),'x',$,$);");
        var wall = Entity("IFCWALL");
        Assert.Equal("pass", Run(m, wall, Prop(Sv("Qto"), Sv("Length"), Sv("4.2"), "IFCLENGTHMEASURE")));
        Assert.Equal("fail", Run(m, wall, Prop(Sv("Qto"), Sv("Length"), dataType: "IFCAREAMEASURE")));
        Assert.Equal("fail", Run(m, wall, Prop(Sv("Qto"), Sv("Area"))));
        Assert.Equal("pass", Run(m, wall, Prop(Sv("Qto"), Sv("Area"), cardinality: "optional")));
        Assert.Equal("fail", Run(m, wall, Prop(Sv("Qto"), Sv("Complex"))));
    }

    /// <summary>
    /// Enumerated/list/bounded/table: một giá trị khớp là đủ (list/bounded/table đổi đơn vị); danh sách rỗng là có mà
    /// sai; table chỉ so cột đúng dataType đã khai; complex/reference property không hỗ trợ; logical .U. là vắng.
    /// </summary>
    [Fact]
    public void Property_NhieuGiaTri_VaLoaiKhongHoTro()
    {
        var m = WallWith(
            string.Empty,
            "IFCPROPERTYENUMERATEDVALUE('Enum',$,(IFCLABEL('A'),IFCLABEL('B')),$)",
            "IFCPROPERTYENUMERATEDVALUE('EnumEmpty',$,(),$)",
            "IFCPROPERTYENUMERATEDVALUE('EnumOne',$,IFCLABEL('X'),$)",
            "IFCPROPERTYLISTVALUE('List',$,(IFCLENGTHMEASURE(1000.),IFCLENGTHMEASURE(3000.)),$)",
            "IFCPROPERTYBOUNDEDVALUE('Bounded',$,IFCLENGTHMEASURE(5000.),IFCLENGTHMEASURE(1000.),$,IFCLENGTHMEASURE(3000.))",
            "IFCPROPERTYBOUNDEDVALUE('BoundedEmpty',$,$,$,$,$)",
            "IFCPROPERTYTABLEVALUE('Table',$,(IFCLABEL('X')),(IFCLENGTHMEASURE(1000.)),$,$,$,$)",
            "IFCCOMPLEXPROPERTY('Complex',$,'u',(#31))",
            "IFCPROPERTYREFERENCEVALUE('Ref',$,$,$)",
            "IFCPROPERTYSINGLEVALUE('Flag',$,IFCLOGICAL(.U.),$)",
            "IFCPROPERTYSINGLEVALUE('Raw',$,'untyped',$)",
            "IFCPROPERTYSINGLEVALUE('Blank',$,IFCLABEL(''),$)");
        Assert.Equal("pass", OnWall(m, "Enum", Sv("B"), "IFCLABEL"));
        Assert.Equal("fail", OnWall(m, "Enum", Sv("C")));
        Assert.Equal("fail", OnWall(m, "Enum", dataType: "IFCTEXT"));
        Assert.Equal("fail", OnWall(m, "EnumEmpty"));
        Assert.Equal("fail", OnWall(m, "EnumOne"));
        Assert.Equal("pass", OnWall(m, "EnumOne", cardinality: "optional"));
        Assert.Equal("pass", OnWall(m, "List", Sv("3")));
        Assert.Equal("fail", OnWall(m, "List", Sv("2")));
        foreach (var ok in new[] { "1", "3", "5" })
        {
            Assert.Equal("pass", OnWall(m, "Bounded", Sv(ok), "IFCLENGTHMEASURE"));
        }

        Assert.Equal("fail", OnWall(m, "Bounded", Sv("2")));
        Assert.Equal("fail", OnWall(m, "Bounded", dataType: "IFCAREAMEASURE"));
        Assert.Equal("fail", OnWall(m, "BoundedEmpty"));
        Assert.Equal("pass", OnWall(m, "Table", Sv("X"), "IFCLABEL"));
        Assert.Equal("pass", OnWall(m, "Table", Sv("1"), "IFCLENGTHMEASURE"));
        Assert.Equal("fail", OnWall(m, "Table"));
        Assert.Equal("fail", OnWall(m, "Table", dataType: "IFCREAL"));
        Assert.Equal("fail", OnWall(m, "Complex"));
        Assert.Equal("fail", OnWall(m, "Ref"));
        Assert.Equal("fail", OnWall(m, "Flag"));
        Assert.Equal("pass", OnWall(m, "Raw"));
        Assert.Equal("fail", OnWall(m, "Raw", dataType: "IFCLABEL"));
        Assert.Equal("fail", OnWall(m, "Blank"));
        Assert.Equal("pass", OnWall(m, "Blank", Sv("x"), cardinality: "optional"));
    }

    /// <summary>propertySet/baseName khai bằng restriction: MỌI pset/property khớp đều phải thoả; pset khớp mà thiếu property là vắng.</summary>
    [Fact]
    public void Property_Pattern_MoiPsetMoiPropertyKhopDeuPhaiThoa()
    {
        var m = Ifc(
            $"#1=IFCWALL('{G("W")}',$,$,$,$,$,$,$,$);\n"
            + $"#2=IFCPROPERTYSET('{G("A")}',$,'Foo_A',$,(#4,#5));\n"
            + $"#3=IFCPROPERTYSET('{G("B")}',$,'Foo_B',$,(#6));\n"
            + "#4=IFCPROPERTYSINGLEVALUE('X1',$,IFCLABEL('a'),$);\n"
            + "#5=IFCPROPERTYSINGLEVALUE('X2',$,IFCLABEL('b'),$);\n"
            + "#6=IFCPROPERTYSINGLEVALUE('Other',$,IFCLABEL('c'),$);\n"
            + $"#7=IFCRELDEFINESBYPROPERTIES('{G("R")}',$,$,$,(#1),(#2,#3));");
        var wall = Entity("IFCWALL");
        var fooSets = Restriction("<xs:pattern value=\"Foo_.*\"/>", "string");
        var xNames = Restriction("<xs:pattern value=\"X.*\"/>", "string");
        Assert.Equal("fail", Run(m, wall, Prop(fooSets, xNames)));
        Assert.Equal("pass", Run(m, wall, Prop(fooSets, xNames, cardinality: "optional")));
        Assert.Equal("pass", Run(m, wall, Prop(Sv("Foo_A"), xNames, Restriction("<xs:enumeration value=\"a\"/><xs:enumeration value=\"b\"/>", "string"))));
        Assert.Equal("fail", Run(m, wall, Prop(Sv("Foo_A"), xNames, Sv("a"))));
        Assert.Equal("fail", Run(m, wall, Prop(Sv("foo_a"), Sv("X1"))));
    }

    /// <summary>Pset định sẵn (IfcDoorPanelProperties): mỗi thuộc tính là một property; không kiểm dataType (như IfcTester).</summary>
    [Fact]
    public void Property_PsetDinhSan()
    {
        var m = Ifc(
            $"#1=IFCDOOR('{G("D")}',$,$,$,$,$,$,$,$,$,$,$,$);\n"
            + $"#2=IFCDOORPANELPROPERTIES('{G("P")}',$,'Panel',$,$,.SWINGING.,$,.LEFT.,$);\n"
            + $"#3=IFCRELDEFINESBYPROPERTIES('{G("R")}',$,$,$,(#1),#2);");
        var door = Entity("IFCDOOR");
        Assert.Equal("pass", Run(m, door, Prop(Sv("Panel"), Sv("PanelOperation"), Sv("SWINGING"), "IFCDOORPANELOPERATIONENUM")));
        Assert.Equal("fail", Run(m, door, Prop(Sv("Panel"), Sv("PanelOperation"), Sv("SLIDING"))));
        Assert.Equal("fail", Run(m, door, Prop(Sv("Panel"), Sv("PanelDepth"))));
    }

    /// <summary>
    /// Kiểu có pset riêng (HasPropertySets); phần tử lấy pset của kiểu rồi pset của chính nó đè lên theo từng property.
    /// Định nghĩa pset thiếu hoặc không tên thì bỏ qua.
    /// </summary>
    [Fact]
    public void Property_KieuVaPhanTu_PhanTuDeLen()
    {
        var m = Ifc(
            $"#1=IFCWALL('{G("W")}',$,$,$,$,$,$,$,$);\n"
            + $"#2=IFCWALLTYPE('{G("T")}',$,$,$,$,(#4),$,$,$,.ELEMENTEDWALL.);\n"
            + $"#3=IFCRELDEFINESBYTYPE('{G("R1")}',$,$,$,(#1),#2);\n"
            + $"#4=IFCPROPERTYSET('{G("P1")}',$,'Foo_Bar',$,(#5,#9));\n"
            + "#5=IFCPROPERTYSINGLEVALUE('Foo',$,IFCLABEL('Bar'),$);\n"
            + $"#6=IFCPROPERTYSET('{G("P2")}',$,'Foo_Bar',$,(#7));\n"
            + "#7=IFCPROPERTYSINGLEVALUE('Foo',$,IFCLABEL('Occ'),$);\n"
            + $"#8=IFCRELDEFINESBYPROPERTIES('{G("R2")}',$,$,$,(#1),(#6,#999,#10));\n"
            + "#9=IFCPROPERTYSINGLEVALUE('FromType',$,IFCLABEL('T'),$);\n"
            + $"#10=IFCPROPERTYSET('{G("P3")}',$,$,$,(#5));");
        Assert.Equal("pass", Run(m, Entity("IFCWALLTYPE"), Prop(Sv("Foo_Bar"), Sv("Foo"), Sv("Bar"))));
        Assert.Equal("pass", Run(m, Entity("IFCWALL"), Prop(Sv("Foo_Bar"), Sv("Foo"), Sv("Occ"))));
        Assert.Equal("fail", Run(m, Entity("IFCWALL"), Prop(Sv("Foo_Bar"), Sv("Foo"), Sv("Bar"))));
        Assert.Equal("pass", Run(m, Entity("IFCWALL"), Prop(Sv("Foo_Bar"), Sv("FromType"), Sv("T"))));
    }

    /// <summary>
    /// Pset của vật liệu/profile (IFC4 IfcMaterialProperties, IfcProfileProperties). Facet property đứng đầu
    /// applicability thì vật liệu/profile cũng là ứng viên (IFC4+), như IfcTester.
    /// </summary>
    [Fact]
    public void Property_VatLieuVaProfile_Ifc4()
    {
        var m = Ifc(
            "#1=IFCMATERIAL('Concrete',$,$);\n"
            + "#2=IFCPROPERTYSINGLEVALUE('Foo',$,IFCLABEL('Bar'),$);\n"
            + "#3=IFCMATERIALPROPERTIES('Custom_Pset',$,(#2),#1);\n"
            + "#4=IFCMATERIALPROPERTIES('Orphan',$,(#2),$);\n"
            + "#5=IFCRECTANGLEPROFILEDEF(.AREA.,'P',$,100.,200.);\n"
            + "#6=IFCPROFILEPROPERTIES('Prof',$,(#2),#5);");
        Assert.Equal("pass", Run(m, Entity("IFCMATERIAL"), Prop(Sv("Custom_Pset"), Sv("Foo"), Sv("Bar"))));
        Assert.Equal("pass", Run(m, Entity("IFCRECTANGLEPROFILEDEF"), Prop(Sv("Prof"), Sv("Foo"))));
        Assert.Equal(1, Result(m, Prop(Sv("Custom_Pset"), Sv("Foo")), Attr(Sv("Name"))).Applicable);
    }

    /// <summary>IFC2X3: vật liệu có pset qua IfcExtendedMaterialProperties; nhưng facet property đứng đầu không nhìn vật liệu.</summary>
    [Fact]
    public void Property_VatLieu_Ifc2x3()
    {
        var m = Ifc(
            "#1=IFCMATERIAL('Concrete');\n"
            + "#2=IFCPROPERTYSINGLEVALUE('Foo',$,IFCLABEL('Bar'),$);\n"
            + "#3=IFCEXTENDEDMATERIALPROPERTIES(#1,(#2),$,'Custom_Pset');", "IFC2X3");
        Assert.Equal("pass", Run(m, Entity("IFCMATERIAL"), Prop(Sv("Custom_Pset"), Sv("Foo"), Sv("Bar"))));
        Assert.Equal(0, Result(m, Prop(Sv("Custom_Pset"), Sv("Foo")), Attr(Sv("Name"))).Applicable);
    }
}
