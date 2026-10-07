using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using DhcbTools.Shared.Logic.Ifc;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Đường IFC so giá trị CÓ KIỂU (2026-10-07): thuộc tính theo lược đồ, property kèm kiểu dữ liệu và đơn vị, phân loại
/// thừa kế theo hệ, ánh xạ kiểu IFC2X3. Bộ ca buildingSMART 271 → 330/334; ở đây chốt từng luật trên file dựng tay để
/// đỏ ngay trên CI khi hồi quy, không phải đợi job đối chiếu.
/// </summary>
public class IdsTypedTests
{
    private const string Xs = "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"";

    private static string G(string tag) => ("0" + tag).PadRight(22, '0');

    private static string File(string schema, string data) =>
        "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION((''),'2;1');\nFILE_NAME('','',(''),(''),'','','');\nFILE_SCHEMA(('" + schema + "'));\nENDSEC;\nDATA;\n"
        + data + "ENDSEC;\nEND-ISO-10303-21;\n";

    private static IReadOnlyList<IdsSpecification> Specs(string applicability, string requirements, string occurs = "") =>
        IdsSpec.Parse("<ids xmlns=\"http://standards.buildingsmart.org/IDS\"><specifications><specification name=\"s\" ifcVersion=\"IFC4\">"
                      + "<applicability" + occurs + ">" + applicability + "</applicability><requirements>" + requirements + "</requirements>"
                      + "</specification></specifications></ids>");

    private static string Entity(string name) => "<entity><name><simpleValue>" + name + "</simpleValue></name></entity>";

    private static string Simple(string tag, string value) => "<" + tag + "><simpleValue>" + value + "</simpleValue></" + tag + ">";

    private static string Pattern(string tag, string pattern) =>
        "<" + tag + "><xs:restriction " + Xs + " base=\"xs:string\"><xs:pattern value=\"" + pattern + "\"/></xs:restriction></" + tag + ">";

    private static string Attribute(string name, string? value = null, string cardinality = "required") =>
        "<attribute cardinality=\"" + cardinality + "\">" + Simple("name", name) + (value == null ? string.Empty : Simple("value", value)) + "</attribute>";

    private static string Property(string set, string name, string? value = null, string? dataType = null, string cardinality = "required") =>
        "<property cardinality=\"" + cardinality + "\"" + (dataType == null ? string.Empty : " dataType=\"" + dataType + "\"") + ">"
        + Simple("propertySet", set) + Simple("baseName", name) + (value == null ? string.Empty : Simple("value", value)) + "</property>";

    /// <summary>Kiểm một file IFC theo một specification; trả (số phần tử áp dụng, đạt hết không).</summary>
    private static (int Applicable, bool Passed) Run(string ifc, IReadOnlyList<IdsSpecification> specs)
    {
        var model = IfcIdsModel.Parse(ifc);
        var result = IdsEvaluator.Check(specs, model.Elements(specs), model.Model.Schema);
        return (result.Specifications[0].Applicable, result.AllPassed);
    }

    private static bool Passes(string ifc, string applicability, string requirements) => Run(ifc, Specs(applicability, requirements)).Passed;

    // ── Giá trị có kiểu ─────────────────────────────────────────────────

    [Fact]
    public void GiaTriCoKieu_EpChuoiIdsTheoKieuCuaMoHinh()
    {
        Assert.True(IdsTypedValue.String("Abc").EqualsText("Abc"));
        Assert.False(IdsTypedValue.String("Abc").EqualsText("abc"));
        Assert.True(IdsTypedValue.Real(42).EqualsText("42"));
        Assert.True(IdsTypedValue.Real(1234.5).EqualsText("1.2345e3"));
        Assert.False(IdsTypedValue.Real(42.3).EqualsText("42,3"));
        Assert.True(IdsTypedValue.Integer(42).EqualsText("42"));
        Assert.False(IdsTypedValue.Integer(42).EqualsText("42.0"));
        Assert.True(IdsTypedValue.Boolean(true).EqualsText("true"));
        Assert.True(IdsTypedValue.Boolean(true).EqualsText("1"));
        Assert.False(IdsTypedValue.Boolean(true).EqualsText("TRUE"));
        Assert.True(IdsTypedValue.Boolean(false).EqualsText("0"));
        Assert.False(IdsTypedValue.Boolean(false).EqualsText("true"));
        Assert.False(IdsTypedValue.Reference(5).EqualsText("#5"));
        Assert.Equal("#5", IdsTypedValue.Reference(5).ToString());
        Assert.Equal("(2 mục)", IdsTypedValue.Aggregate(2).Text);
        Assert.Equal(IdsTypedKind.Real, IdsTypedValue.Integer(2000).Scale(0.001, 0).Kind);
        Assert.Equal(2, IdsTypedValue.Integer(2000).Scale(0.001, 0).Number, 9);
        Assert.Equal("x", IdsTypedValue.String("x").Scale(0.001, 0).Text);
    }

    [Fact]
    public void AcceptsTyped_PatternKhongApChoSo_BienChiApChoSo_ThamChieuChiDatKhiKhongRangBuoc()
    {
        var any = new IdsValue();
        Assert.True(any.AcceptsTyped(IdsTypedValue.Reference(1)));
        var simple = new IdsValue { Simple = "Foo" };
        Assert.False(simple.AcceptsTyped(IdsTypedValue.Reference(1)));
        Assert.False(simple.AcceptsTyped(IdsTypedValue.Aggregate(1)));
        Assert.True(simple.AcceptsTyped(IdsTypedValue.String("Foo")));

        var pattern = new IdsValue { Pattern = "\\d+" };
        Assert.True(pattern.AcceptsTyped(IdsTypedValue.String("42")));
        Assert.False(pattern.AcceptsTyped(IdsTypedValue.Integer(42)));

        var bounds = new IdsValue { MinInclusive = 0, MaxExclusive = 10 };
        Assert.True(bounds.AcceptsTyped(IdsTypedValue.Real(0)));
        Assert.False(bounds.AcceptsTyped(IdsTypedValue.Real(10)));
        Assert.False(bounds.AcceptsTyped(IdsTypedValue.Boolean(true)));
        Assert.True(new IdsValue { Simple = "true" }.AcceptsTyped(IdsTypedValue.Boolean(true)));

        var enumeration = new IdsValue();
        enumeration.Enumeration.Add("1");
        enumeration.Enumeration.Add("42");
        Assert.True(enumeration.AcceptsTyped(IdsTypedValue.Real(42)));
        Assert.False(enumeration.AcceptsTyped(IdsTypedValue.Real(7)));
        Assert.False(new IdsValue { Length = 3 }.AcceptsTyped(IdsTypedValue.Integer(42)));
    }

    // ── Bảng lược đồ ────────────────────────────────────────────────────

    [Fact]
    public void BangLuocDo_ThuocTinhTheoDungViTri_KeThuaVaDanXuat()
    {
        Assert.Equal("IFC4X3_ADD2", IfcSchemaTable.Normalize("IFC4X3_ADD1"));
        Assert.Equal("IFC2X3", IfcSchemaTable.Normalize("ifc2x3"));
        Assert.Equal("IFC4", IfcSchemaTable.Normalize(null));

        var risers = IfcSchemaTable.Attribute("IFC4", "IfcStairFlight", "NumberOfRisers")!;
        Assert.Equal(8, risers.Index);
        Assert.False(risers.Derived);
        Assert.Null(IfcSchemaTable.Attribute("IFC4", "IFCSTAIRFLIGHT", "numberofrisers"));
        Assert.Null(IfcSchemaTable.Attributes("IFC2X3", "IFCAIRTERMINAL"));
        Assert.True(IfcSchemaTable.Attribute("IFC4", "IFCGEOMETRICREPRESENTATIONSUBCONTEXT", "Precision")!.Derived);

        // Bảng hỏng/thiếu lớp cha không làm sập — lớp vẫn có thuộc tính riêng.
        var parsed = IfcSchemaTable.Parse("# chú thích\n\n@X\nA<MISSING|One:S|~Ghost\n");
        Assert.Equal("One", Assert.Single(parsed["X"]["A"]).Name);
    }

    // ── Facet attribute ─────────────────────────────────────────────────

    private static readonly string Attributes = File("IFC4",
        $"#1=IFCSTAIRFLIGHT('{G("Stair")}',$,'',$,$,$,$,$,42,$,$,$,$);\n"
        + "#2=IFCSURFACESTYLEREFRACTION(1234.5,$);\n"
        + "#3=IFCTASKTIME($,$,$,$,'P0D',$,$,$,$,$,$,$,$,.F.,$,$,$,$,$,$);\n"
        + "#4=IFCMATERIALLAYERSET((#5),'Foo',$);\n"
        + "#5=IFCMATERIALLAYER($,1.,$,$,$,$,$);\n"
        + "#6=IFCSURFACESTYLERENDERING(#7,$,IFCNORMALISEDRATIOMEASURE(0.5),$,$,$,$,$,.FLAT.);\n"
        + "#7=IFCCOLOURRGB($,1.,1.,1.);\n"
        + "#8=IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#9,$,.MODEL_VIEW.,$);\n"
        + "#9=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,$,$);\n"
        + "#10=IFCFOOBAR('x');\n");

    [Theory]
    [InlineData("IFCSTAIRFLIGHT", "NumberOfRisers", "42", true)]
    [InlineData("IFCSTAIRFLIGHT", "NumberOfRisers", "42.0", false)]
    [InlineData("IFCSTAIRFLIGHT", "Name", null, false)] // '' luôn trượt
    [InlineData("IFCSTAIRFLIGHT", "Description", null, false)] // $ luôn trượt
    [InlineData("IFCSTAIRFLIGHT", "IsDefinedBy", null, false)] // inverse: không kiểm được
    [InlineData("IFCSURFACESTYLEREFRACTION", "RefractionIndex", "1.2345e3", true)]
    [InlineData("IFCTASKTIME", "IsCritical", "false", true)]
    [InlineData("IFCTASKTIME", "IsCritical", "FALSE", false)]
    [InlineData("IFCTASKTIME", "ScheduleDuration", "P0D", true)]
    [InlineData("IFCMATERIALLAYERSET", "MaterialLayers", null, true)] // tập hợp có phần tử: có giá trị
    [InlineData("IFCMATERIALLAYERSET", "MaterialLayers", "Foo", false)] // nhưng không so được
    [InlineData("IFCSURFACESTYLERENDERING", "SurfaceColour", null, true)] // trỏ thực thể: có giá trị
    [InlineData("IFCSURFACESTYLERENDERING", "DiffuseColour", "0.5", true)] // select bọc số: so theo số
    [InlineData("IFCSURFACESTYLERENDERING", "DiffuseColour", "Foobar", false)]
    [InlineData("IFCGEOMETRICREPRESENTATIONSUBCONTEXT", "Precision", null, false)] // dẫn xuất
    [InlineData("IFCFOOBAR", "Name", null, false)] // lớp không có trong lược đồ
    public void Attribute_TheoLuocDo(string entity, string name, string? value, bool expected)
    {
        Assert.Equal(expected, Passes(Attributes, Entity(entity), Attribute(name, value)));
    }

    [Fact]
    public void Attribute_TenKhaiBangPattern_MoiThuocTinhCoGiaTriDeuPhaiDat()
    {
        Assert.True(Passes(Attributes, Entity("IFCMATERIALLAYERSET"), "<attribute>" + Pattern("name", ".*Name.*") + "</attribute>"));
        Assert.False(Passes(Attributes, Entity("IFCMATERIALLAYERSET"),
            "<attribute>" + Pattern("name", ".*Name.*") + Simple("value", "Bar") + "</attribute>"));
    }

    [Fact]
    public void Attribute_TuyChon_DeTrongThiDat_GhiRongThiTruot()
    {
        Assert.True(Passes(Attributes, Entity("IFCSTAIRFLIGHT"), Attribute("Description", "x", "optional")));
        Assert.False(Passes(Attributes, Entity("IFCSTAIRFLIGHT"), Attribute("Name", "x", "optional")));
        Assert.True(Passes(Attributes, Entity("IFCSTAIRFLIGHT"), Attribute("NumberOfRisers", "42", "optional")));
    }

    [Fact]
    public void PhanTuKhongCoGlobalId_ChiThemKhiApplicabilityNeuLop()
    {
        var model = IfcIdsModel.Parse(Attributes);
        Assert.Single(model.Elements());
        Assert.Single(model.Elements(System.Array.Empty<IdsSpecification>()));
        Assert.Single(model.Elements(Specs(string.Empty, Attribute("Name"), " minOccurs=\"0\"")));
        Assert.Equal(2, model.Elements(Specs(Entity("IFCTASKTIME"), Attribute("Name"))).Count);
    }

    // ── Facet property ──────────────────────────────────────────────────

    private static readonly string Properties = File("IFC4",
        $"#1=IFCPROJECT('{G("Project")}',$,$,$,$,$,$,$,#12);\n"
        + "#2=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);\n"
        + "#3=IFCSIUNIT(*,.AREAUNIT.,.MILLI.,.SQUARE_METRE.);\n"
        + "#4=IFCSIUNIT(*,.VOLUMEUNIT.,.MILLI.,.CUBIC_METRE.);\n"
        + "#5=IFCSIUNIT(*,.MASSUNIT.,$,.GRAM.);\n"
        + "#6=IFCSIUNIT(*,.THERMODYNAMICTEMPERATUREUNIT.,$,.DEGREE_CELSIUS.);\n"
        + "#7=IFCCONVERSIONBASEDUNIT(#90,.PLANEANGLEUNIT.,'degree',#8);\n"
        + "#8=IFCMEASUREWITHUNIT(IFCPLANEANGLEMEASURE(0.0174532925199433),#9);\n"
        + "#9=IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.);\n"
        + "#10=IFCDERIVEDUNIT((),.THERMALTRANSMITTANCEUNIT.,$);\n"
        + "#11=IFCCONVERSIONBASEDUNIT(#90,.TIMEUNIT.,'hour',$);\n"
        + "#12=IFCUNITASSIGNMENT((#2,#3,#4,#5,#6,#7,#10,#11,#999));\n"
        + "#13=IFCUNITASSIGNMENT((#91));\n"
        + "#91=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);\n"
        + "#90=IFCDIMENSIONALEXPONENTS(0,0,0,0,0,0,0);\n"
        + $"#20=IFCWALL('{G("Wall")}',$,'W',$,$,$,$,$,$);\n"
        + $"#21=IFCWALLTYPE('{G("WallType")}',$,'T',$,$,(#50),$,$,$,.SOLIDWALL.);\n"
        + $"#22=IFCRELDEFINESBYTYPE('{G("RelType")}',$,$,$,(#20),#21);\n"
        + $"#23=IFCRELDEFINESBYPROPERTIES('{G("RelProps")}',$,$,$,(#20),(#30,#40,#60,#61,#62,#998));\n"
        + $"#30=IFCPROPERTYSET('{G("P")}',$,'P',$,(#31,#32,#33,#34,#35,#36,#37,#38,#39,#41,#42,#43,#44,#45,#46,#47,#999));\n"
        + "#31=IFCPROPERTYSINGLEVALUE('Length',$,IFCLENGTHMEASURE(2000.),$);\n"
        + "#32=IFCPROPERTYSINGLEVALUE('Label',$,IFCLABEL('X'),$);\n"
        + "#33=IFCPROPERTYSINGLEVALUE('Angle',$,IFCPLANEANGLEMEASURE(90.),#7);\n"
        + "#34=IFCPROPERTYENUMERATEDVALUE('Enum',$,(IFCLABEL('A'),IFCLABEL('B')),$);\n"
        + "#35=IFCPROPERTYLISTVALUE('List',$,(IFCLENGTHMEASURE(1000.),IFCLENGTHMEASURE(3000.)),$);\n"
        + "#36=IFCPROPERTYBOUNDEDVALUE('Bounded',$,IFCMASSMEASURE(5000.),IFCMASSMEASURE(1000.),$,$);\n"
        + "#37=IFCPROPERTYTABLEVALUE('Table',$,(IFCLABEL('k')),(IFCAREAMEASURE(1000000.)),$,$,$,$);\n"
        + "#38=IFCCOMPLEXPROPERTY('Complex',$,'u',(#32));\n"
        + "#39=IFCPROPERTYSINGLEVALUE('Empty',$,$,$);\n"
        + "#41=IFCPROPERTYSINGLEVALUE($,$,IFCLABEL('nameless'),$);\n"
        + "#42=IFCPROPERTYSINGLEVALUE('Temp',$,IFCTHERMODYNAMICTEMPERATUREMEASURE(20.),$);\n"
        + "#43=IFCPROPERTYSINGLEVALUE('Time',$,IFCTIMEMEASURE(2.),$);\n"
        + "#44=IFCPROPERTYSINGLEVALUE('Ratio',$,IFCRATIOMEASURE(0.5),$);\n"
        + "#45=IFCPROPERTYSINGLEVALUE('Flag',$,IFCBOOLEAN(.T.),$);\n"
        + "#46=IFCPROPERTYSINGLEVALUE('Count',$,IFCINTEGER(3),$);\n"
        + "#47=IFCPROPERTYSINGLEVALUE('U',$,IFCTHERMALTRANSMITTANCEMEASURE(0.3),$);\n"
        + $"#40=IFCELEMENTQUANTITY('{G("Q")}',$,'Q',$,$,(#48,#49,#51));\n"
        + "#48=IFCQUANTITYVOLUME('Volume',$,$,1000000000.,$);\n"
        + "#49=IFCQUANTITYLENGTH('Missing',$,$,$,$);\n"
        + "#51=IFCQUANTITYCOUNT('Count',$,$,3.,$);\n"
        + $"#50=IFCPROPERTYSET('{G("TypeP")}',$,'P',$,(#52,#53));\n"
        + "#52=IFCPROPERTYSINGLEVALUE('Label',$,IFCLABEL('FromType'),$);\n"
        + "#53=IFCPROPERTYSINGLEVALUE('Inherited',$,IFCLABEL('Yes'),$);\n"
        + $"#60=IFCPROPERTYSET('{G("Nameless")}',$,$,$,(#32));\n"
        + $"#61=IFCFOOPROPERTIES('{G("Foo")}',$,'Foo',$);\n"
        + $"#62=IFCDOORPANELPROPERTIES('{G("Panel")}',$,'Panel',$,$,.SWINGING.,$,.LEFT.,#7);\n"
        + $"#63=IFCDOORPANELPROPERTIES('{G("Panel2")}',$,$,$,$,.SWINGING.,$,.LEFT.,$);\n"
        + $"#64=IFCRELDEFINESBYPROPERTIES('{G("RelProps2")}',$,$,$,(#20),#63);\n"
        + "#70=IFCMATERIAL('Concrete',$,$);\n"
        + "#71=IFCMATERIALPROPERTIES('Mat',$,(#32),#70);\n"
        + "#72=IFCEXTENDEDMATERIALPROPERTIES(#70,(#45),$,'Ext');\n"
        + "#73=IFCMATERIALPROPERTIES('Orphan',$,(#32),$);\n"
        + $"#74=IFCSLABTYPE('{G("Unused")}',$,'Unused',$,$,(#50),$,$,$,.FLOOR.);\n");

    [Theory]
    [InlineData("P", "Length", "2", "IFCLENGTHMEASURE", true)] // 2000 mm = 2 m
    [InlineData("P", "Length", "2000", null, false)]
    [InlineData("P", "Length", "2", "IFCREAL", false)] // sai kiểu dữ liệu
    [InlineData("P", "Label", "X", "IFCLABEL", true)] // phần tử đè kiểu
    [InlineData("P", "Inherited", "Yes", null, true)] // thừa kế từ kiểu
    [InlineData("P", "Angle", "1.5707963267949", null, true)] // độ → radian theo đơn vị riêng
    [InlineData("P", "Enum", "B", "IFCLABEL", true)] // liệt kê: một giá trị khớp là đủ
    [InlineData("P", "List", "3", null, true)]
    [InlineData("P", "Bounded", "1", "IFCMASSMEASURE", true)] // gam → kg
    [InlineData("P", "Table", "1", "IFCAREAMEASURE", true)] // cột đúng kiểu
    [InlineData("P", "Table", "k", "IFCLABEL", true)]
    [InlineData("P", "Table", "k", "IFCAREAMEASURE", false)]
    [InlineData("P", "Table", "k", "IFCREAL", false)] // không cột nào đúng kiểu
    [InlineData("P", "Complex", null, null, false)] // không kiểm được
    [InlineData("P", "Empty", null, null, false)]
    [InlineData("P", "Temp", "20", null, true)] // độ C: không đổi, như IfcTester
    [InlineData("P", "Time", "2", null, true)] // đơn vị dự án hỏng: không đổi
    [InlineData("P", "Ratio", "0.5", null, true)]
    [InlineData("P", "Flag", "true", "IFCBOOLEAN", true)]
    [InlineData("P", "Count", "3", "IFCINTEGER", true)]
    [InlineData("P", "U", "0.3", null, true)] // đơn vị dẫn xuất: không đổi
    [InlineData("Q", "Volume", "1", "IFCVOLUMEMEASURE", true)] // mm³ → m³
    [InlineData("Q", "Volume", "1", "IFCAREAMEASURE", false)]
    [InlineData("Q", "Missing", null, null, false)]
    [InlineData("Q", "Count", "3", "IFCCOUNTMEASURE", true)]
    [InlineData("Panel", "PanelOperation", "SWINGING", "IFCDOORPANELOPERATIONENUM", true)] // pset định nghĩa sẵn
    [InlineData("Panel", "ShapeAspectStyle", null, null, false)] // trỏ thực thể: bỏ
    [InlineData("Foo", "Bar", null, null, false)]
    [InlineData("Nope", "Label", null, null, false)]
    public void Property_TheoKieuVaDonVi(string set, string name, string? value, string? dataType, bool expected)
    {
        Assert.Equal(expected, Passes(Properties, Entity("IFCWALL"), Property(set, name, value, dataType)));
    }

    [Fact]
    public void Property_TenKhaiBangPattern_MoiPropertyKhopDeuPhaiDat()
    {
        Assert.True(Passes(Properties, Entity("IFCWALL"), "<property>" + Simple("propertySet", "P") + Pattern("baseName", "L.*") + "</property>"));
        Assert.False(Passes(Properties, Entity("IFCWALL"),
            "<property>" + Simple("propertySet", "P") + Pattern("baseName", "L.*") + Simple("value", "X") + "</property>"));
        Assert.True(Passes(Properties, Entity("IFCWALL"), "<property>" + Pattern("propertySet", "[PQ]") + Simple("baseName", "Count") + "</property>"));
        Assert.True(Passes(Properties, Entity("IFCWALL"), "<property>" + Simple("baseName", "Count") + "</property>"));
    }

    [Fact]
    public void Property_TuyChon_KhongCoThiDat_CoMaSaiThiTruot()
    {
        Assert.True(Passes(Properties, Entity("IFCWALL"), Property("Nope", "Label", "X", cardinality: "optional")));
        Assert.False(Passes(Properties, Entity("IFCWALL"), Property("P", "Label", "Y", cardinality: "optional")));
    }

    [Fact]
    public void Property_CuaVatLieu_IFC4VaIFC2X3()
    {
        Assert.True(Passes(Properties, Entity("IFCMATERIAL"), Property("Mat", "Label", "X", "IFCLABEL")));
        Assert.True(Passes(Properties, Entity("IFCMATERIAL"), Property("Ext", "Flag", "true")));
        Assert.False(Passes(Properties, Entity("IFCMATERIALPROPERTIES"), Property("Orphan", "Label")));
    }

    [Fact]
    public void Property_KieuKhongCoPhanTu_VanCoPsetCuaChinhNo()
    {
        Assert.True(Passes(Properties, Entity("IFCWALLTYPE"), Property("P", "Inherited", "Yes")));
        Assert.True(Passes(Properties, Entity("IFCSLABTYPE"), Property("P", "Inherited", "Yes"))); // kiểu chưa phần tử nào dùng
    }

    [Theory]
    [InlineData("IFCLENGTHMEASURE", "LENGTHUNIT")]
    [InlineData("IFCPOSITIVELENGTHMEASURE", "LENGTHUNIT")]
    [InlineData("IFCNONNEGATIVELENGTHMEASURE", "LENGTHUNIT")]
    [InlineData("IFCPLANEANGLEMEASURE", "PLANEANGLEUNIT")]
    [InlineData("IFCLABEL", null)]
    [InlineData("LENGTHMEASURE", null)]
    [InlineData(null, null)]
    public void LoaiDonViCuaSoDo(string? dataType, string? expected)
    {
        Assert.Equal(expected, IfcIdsModel.UnitTypeOf(dataType));
    }

    // ── Facet classification ────────────────────────────────────────────

    private static readonly string Classified = File("IFC4",
        $"#1=IFCPROJECT('{G("Project")}',$,$,$,$,$,$,$,$);\n"
        + "#2=IFCCLASSIFICATION($,$,$,'Foobar',$,$,$);\n"
        + $"#3=IFCRELASSOCIATESCLASSIFICATION('{G("Rel1")}',$,$,$,(#1),#2);\n"
        + "#4=IFCCLASSIFICATIONREFERENCE($,'11',$,#2,$,$);\n"
        + $"#5=IFCWALL('{G("Wall")}',$,'Wall',$,$,$,$,$,$);\n"
        + $"#6=IFCRELASSOCIATESCLASSIFICATION('{G("Rel2")}',$,$,$,(#5,#5),#4);\n"
        + $"#7=IFCWALLTYPE('{G("Type")}',$,'Type',$,$,$,$,$,$,.SOLIDWALL.);\n"
        + $"#8=IFCRELDEFINESBYTYPE('{G("RelType")}',$,$,$,(#5,#12),#7);\n"
        + "#9=IFCCLASSIFICATION($,$,$,'Foobaz',$,$,$);\n"
        + "#10=IFCCLASSIFICATIONREFERENCE($,'X',$,#9,$,$);\n"
        + $"#11=IFCRELASSOCIATESCLASSIFICATION('{G("Rel3")}',$,$,$,(#7),#10);\n"
        + $"#12=IFCWALL('{G("Wall2")}',$,'Wall 2',$,$,$,$,$,$);\n"
        + "#13=IFCMATERIAL('M',$,$);\n"
        + "#14=IFCEXTERNALREFERENCERELATIONSHIP($,$,#4,(#13));\n"
        + "#15=IFCDOCUMENTREFERENCE($,'D',$,$,$);\n"
        + "#16=IFCEXTERNALREFERENCERELATIONSHIP($,$,#15,(#13));\n"
        + "#17=IFCEXTERNALREFERENCERELATIONSHIP($,$,$,(#13));\n"
        + $"#18=IFCRELASSOCIATESCLASSIFICATION('{G("Rel4")}',$,$,$,(#12),$);\n"
        + "#19=IFCCLASSIFICATIONREFERENCE($,$,$,#2,$,$);\n"
        + $"#20=IFCSLAB('{G("Slab")}',$,$,$,$,$,$,$,$);\n"
        + $"#21=IFCRELASSOCIATESCLASSIFICATION('{G("Rel5")}',$,$,$,(#20),#19);\n");

    private static string Classification(string? value, string? system, string cardinality = "required") =>
        "<classification cardinality=\"" + cardinality + "\">" + (value == null ? string.Empty : Simple("value", value))
        + (system == null ? string.Empty : Simple("system", system)) + "</classification>";

    [Theory]
    [InlineData("IFCPROJECT", null, "Foobar", true)] // gắn thẳng hệ, không mã
    [InlineData("IFCPROJECT", "1", "Foobar", false)]
    [InlineData("IFCWALL", "11", "Foobar", false)] // tường 2 chỉ có của kiểu
    [InlineData("IFCWALL", "X", "Foobaz", true)] // hệ khác: kiểu vẫn thừa kế
    [InlineData("IFCMATERIAL", "11", null, true)] // tài nguyên không gốc qua external reference
    [InlineData("IFCSLAB", null, "Foobar", true)]
    public void Classification_TheoHe(string entity, string? value, string? system, bool expected)
    {
        Assert.Equal(expected, Passes(Classified, Entity(entity), Classification(value, system)));
    }

    [Fact]
    public void Classification_PhanTuGhiDeKieuTheoTungHe_HeKhaiBangPattern()
    {
        var model = IfcIdsModel.Parse(Classified);
        var wall = model.Elements().OfType<IfcIdsElement>().Single(e => e.Id == 5);
        Assert.Equal(new[] { "11", "X" }, wall.ClassificationReferences().Select(r => r.Value).Distinct().OrderBy(v => v).ToArray());
        Assert.Equal(new[] { "11" }, wall.Classifications("Foobar").Distinct().ToArray());

        Assert.True(Passes(Classified, Entity("IFCWALL"), "<classification>" + Simple("value", "X") + Pattern("system", "\\w+") + "</classification>"));
        Assert.False(Passes(Classified, Entity("IFCPROJECT"), Classification("Foo", null, "optional"))); // có phân loại mà sai mã
        Assert.True(Passes(Classified, Entity("IFCWALL"), Classification("Foo", null, "prohibited")));
        Assert.True(Passes(Classified, Entity("IFCWALLTYPE"), Classification("X", "Foobaz")));
    }

    // ── Ánh xạ kiểu IFC2X3 ──────────────────────────────────────────────

    private static readonly string Ifc2x3 = File("IFC2X3",
        $"#1=IFCFLOWTERMINAL('{G("Terminal")}',$,'AIR-01',$,$,$,$,$);\n"
        + $"#2=IFCAIRTERMINALTYPE('{G("AirType")}',$,$,$,$,$,$,$,$,.DIFFUSER.);\n"
        + $"#3=IFCRELDEFINESBYTYPE('{G("Rel1")}',$,$,$,(#1),#2);\n"
        + $"#4=IFCWALL('{G("Wall")}',$,$,$,$,$,$,$);\n"
        + $"#5=IFCWALLTYPE('{G("WallType")}',$,$,$,$,$,$,$,$,.STANDARD.);\n"
        + $"#6=IFCRELDEFINESBYTYPE('{G("Rel2")}',$,$,$,(#4),#5);\n"
        + $"#7=IFCDOOR('{G("Door")}',$,$,$,$,$,$,$,$,$);\n"
        + $"#8=IFCDOORSTYLE('{G("Style")}',$,$,$,$,$,$,$,.SINGLE_SWING_LEFT.,.WOOD.,.F.,.F.);\n"
        + $"#9=IFCRELDEFINESBYTYPE('{G("Rel3")}',$,$,$,(#7),#8);\n");

    [Fact]
    public void Ifc2x3_LopIfc4QuaBangAnhXaKieu()
    {
        var elements = IfcIdsModel.Parse(Ifc2x3).Elements().OfType<IfcIdsElement>().ToDictionary(e => e.Id);
        Assert.Equal("IFCAIRTERMINAL", elements[1].EntityAlias);
        Assert.Null(elements[2].EntityAlias); // chính kiểu: không có kiểu
        Assert.Null(elements[4].EntityAlias); // IfcWall có sẵn trong IFC2X3
        Assert.Null(elements[7].EntityAlias); // IfcDoorStyle không kết thúc "TYPE"

        var spec = IdsSpec.Parse("<ids xmlns=\"http://standards.buildingsmart.org/IDS\"><specifications><specification name=\"s\" ifcVersion=\"IFC2X3\">"
                                 + "<applicability>" + Entity("IFCAIRTERMINAL") + "</applicability><requirements>"
                                 + "<entity><name><simpleValue>IFCAIRTERMINAL</simpleValue></name><predefinedType><simpleValue>DIFFUSER</simpleValue></predefinedType></entity>"
                                 + Attribute("Name", "AIR-01") + "</requirements></specification></specifications></ids>");
        var (applicable, passed) = Run(Ifc2x3, spec);
        Assert.Equal(1, applicable);
        Assert.True(passed);

        Assert.Null(IfcIdsModel.Parse(Properties).Elements().OfType<IfcIdsElement>().Single(e => e.Id == 20).EntityAlias); // IFC4
    }

    // ── Đọc giá trị STEP ────────────────────────────────────────────────

    [Fact]
    public void GiaTriStep_DocTheoCachGhi()
    {
        var entity = IfcStepParser.Parse(File("IFC4",
            "#1=IFCX('',42,4.2,-1.E3,.T.,.F.,.U.,.NOTDEFINED.,#5,(),(1,2),IFCLABEL('a'),$,*,IFCLABEL());\n")).Data[0];
        var typed = Enumerable.Range(0, 15).Select(i => IfcIdsElement.Typed(entity.At(i))).ToList();
        Assert.Null(typed[0]);
        Assert.Equal(IdsTypedKind.Integer, typed[1]!.Kind);
        Assert.Equal(IdsTypedKind.Real, typed[2]!.Kind);
        Assert.Equal(-1000, typed[3]!.Number);
        Assert.Equal("true", typed[4]!.Text);
        Assert.Equal("false", typed[5]!.Text);
        Assert.Null(typed[6]);
        Assert.Equal("NOTDEFINED", typed[7]!.Text);
        Assert.Equal(IdsTypedKind.Reference, typed[8]!.Kind);
        Assert.Null(typed[9]);
        Assert.Equal(IdsTypedKind.Aggregate, typed[10]!.Kind);
        Assert.Equal("a", typed[11]!.Text);
        Assert.Null(typed[12]);
        Assert.Null(typed[13]);
        Assert.Null(typed[14]);

        var value = new IdsAttributeValue("A", IdsTypedValue.String("x"), isNull: true);
        Assert.False(value.IsNull); // có giá trị thì không thể "trống $"
        Assert.Equal("A", value.Name);
        var property = new IdsPropertyValue("P", "N", System.Array.Empty<KeyValuePair<string?, IdsTypedValue>>());
        Assert.Equal("P", property.PropertySet);
    }
}
