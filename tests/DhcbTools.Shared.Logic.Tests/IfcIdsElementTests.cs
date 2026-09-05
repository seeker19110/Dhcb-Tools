using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Mục 11.4: cùng bộ luật IDS chạy trên chính file IFC. File IFC nhỏ dựng tay dưới đây có đủ mọi quan hệ
/// mà sáu loại facet cần: kiểu (IfcRelDefinesByType), Pset ở phần tử và ở kiểu, vật liệu qua LayerSetUsage
/// và qua kiểu, phân loại có chuỗi ReferencedSource, cấu trúc không gian và nhóm/hệ.
/// Số liệu đối chiếu với IfcTester trên IFC thật ở bang-chung-test.md §41.
/// </summary>
public class IfcIdsElementTests
{
    private static string G(string tag) => ("0" + tag).PadRight(22, '0');

    private static readonly string Ifc =
        "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION((''),'2;1');\nFILE_NAME('','',(''),(''),'','','');\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
        + $"#1=IFCPROJECT('{G("Project")}',$,'Du an',$,$,$,$,$,$);\n"
        + $"#2=IFCSITE('{G("Site")}',$,'Site',$,$,$,$,$,.ELEMENT.,$,$,$,$,$);\n"
        + $"#3=IFCBUILDING('{G("Bldg")}',$,'Toa A',$,$,$,$,$,.ELEMENT.,$,$,$);\n"
        + $"#4=IFCBUILDINGSTOREY('{G("Storey")}',$,'L1',$,$,$,$,$,.ELEMENT.,3000.);\n"
        + $"#5=IFCRELAGGREGATES('{G("RelAgg1")}',$,$,$,#1,(#2));\n"
        + $"#6=IFCRELAGGREGATES('{G("RelAgg2")}',$,$,$,#2,(#3));\n"
        + $"#7=IFCRELAGGREGATES('{G("RelAgg3")}',$,$,$,#3,(#4));\n"
        + $"#10=IFCWALLTYPE('{G("WallType")}',$,'WT',$,$,(#40),$,'WT-1',$,.SOLIDWALL.);\n"
        + $"#11=IFCWALL('{G("Wall1")}',$,'Tuong 1',$,$,$,$,'W-01',.NOTDEFINED.);\n"
        + $"#12=IFCWALL('{G("Wall2")}',$,'Tuong 2','mo ta','Tuong dac biet',$,$,'W-02',.USERDEFINED.);\n"
        + $"#13=IFCDOOR('{G("Door1")}',$,'Cua 1',$,$,$,$,'D-01',2100.,900.,.DOOR.,.SINGLE_SWING_LEFT.,$);\n"
        + $"#14=IFCRELDEFINESBYTYPE('{G("RelType")}',$,$,$,(#11),#10);\n"
        + $"#15=IFCRELCONTAINEDINSPATIALSTRUCTURE('{G("RelCont")}',$,$,$,(#11,#12,#13),#4);\n"
        + "#20=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.F.),$);\n"
        + "#21=IFCPROPERTYSINGLEVALUE('FireRating',$,IFCLABEL('2 HR'),$);\n"
        + $"#22=IFCPROPERTYSET('{G("Pset1")}',$,'Pset_WallCommon',$,(#20,#21));\n"
        + $"#23=IFCRELDEFINESBYPROPERTIES('{G("RelProp")}',$,$,$,(#12),#22);\n"
        + $"#40=IFCPROPERTYSET('{G("Pset2")}',$,'Pset_WallCommon',$,(#41));\n"
        + "#41=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);\n"
        + "#30=IFCMATERIAL('Be tong',$,$);\n"
        + "#31=IFCMATERIALLAYER(#30,200.,$,'Lop loi',$,'Concrete',$);\n"
        + "#32=IFCMATERIALLAYERSET((#31),'Tuong be tong',$);\n"
        + "#33=IFCMATERIALLAYERSETUSAGE(#32,.AXIS2.,.NEGATIVE.,0.,$);\n"
        + $"#34=IFCRELASSOCIATESMATERIAL('{G("RelMat1")}',$,$,$,(#12),#33);\n"
        + "#35=IFCMATERIAL('Thep',$,$);\n"
        + $"#36=IFCRELASSOCIATESMATERIAL('{G("RelMat2")}',$,$,$,(#10),#35);\n"
        + "#50=IFCCLASSIFICATION('CSI','1998',$,'Uniformat',$,$,$);\n"
        + "#51=IFCCLASSIFICATIONREFERENCE($,'B2010',$,#50,$,$);\n"
        + "#52=IFCCLASSIFICATIONREFERENCE($,'B2010158',$,#51,$,$);\n"
        + $"#53=IFCRELASSOCIATESCLASSIFICATION('{G("RelCls")}',$,$,$,(#12),#52);\n"
        + $"#60=IFCSYSTEM('{G("System")}',$,'HT',$,$);\n"
        + $"#61=IFCRELASSIGNSTOGROUP('{G("RelGrp")}',$,$,$,(#13,#17),$,#60);\n"
        // Các dạng vật liệu còn lại + phần tử không có kiểu, không nằm trong tầng.
        + $"#16=IFCWALL('{G("Wall3")}',$,'Tuong 3',$,$,$,$,'W-03',.NOTDEFINED.);\n"
        + $"#37=IFCRELASSOCIATESMATERIAL('{G("RelMat3")}',$,$,$,(#16),#33);\n" // cùng LayerSetUsage với tường 2 → cache
        + $"#17=IFCCOLUMN('{G("Column1")}',$,'Cot 1',$,$,$,$,'C-01',.COLUMN.);\n"
        + "#80=IFCMATERIALPROFILESETUSAGE(#81,$,$);\n"
        + "#81=IFCMATERIALPROFILESET('Cot thep',$,(#82),$);\n"
        + "#82=IFCMATERIALPROFILE('I200',$,#35,$,$,'Steel');\n"
        + $"#83=IFCRELASSOCIATESMATERIAL('{G("RelMat4")}',$,$,$,(#17),#80);\n"
        + $"#18=IFCSLAB('{G("Slab1")}',$,'San 1',$,$,$,$,'S-01',.FLOOR.);\n"
        + "#90=IFCMATERIALLIST((#30,#35));\n"
        + $"#91=IFCRELASSOCIATESMATERIAL('{G("RelMat5")}',$,$,$,(#18),#90);\n"
        + "#70=IFCMATERIALCONSTITUENTSET('Cua go',$,(#71));\n"
        + "#71=IFCMATERIALCONSTITUENT('Canh',$,#72,$,'Wood');\n"
        + "#72=IFCMATERIAL('Go soi',$,$);\n"
        + $"#73=IFCRELASSOCIATESMATERIAL('{G("RelMat6")}',$,$,$,(#13),#70);\n"
        + $"#19=IFCBUILDINGELEMENTPROXY('{G("Proxy1")}',$,'Phu kien',$,$,$,$,'P-01',.NOTDEFINED.);\n"
        + $"#95=IFCRELNESTS('{G("RelNest")}',$,$,$,#13,(#19));\n" // lồng trong cửa → tổ tiên đi qua cửa rồi tới tầng
        + $"#92=IFCRELASSOCIATESMATERIAL('{G("RelMat7")}',$,$,$,(#19),#99);\n" // trỏ tới thực thể không tồn tại
        + "#38=IFCMATERIALLAYER($,10.,$,'Lop rong',$,$,$);\n" // lớp không có Material
        + "#39=IFCMATERIALLAYERSET((#38),'Rong',$);\n"
        + $"#93=IFCRELASSOCIATESMATERIAL('{G("RelMat8")}',$,$,$,(#18),#39);\n"
        + $"#94=IFCRELASSOCIATESMATERIAL('{G("RelMat9")}',$,$,$,$,#30);\n" // RelatedObjects rỗng
        // Vòng aggregate (file hỏng) — không được treo.
        + $"#96=IFCELEMENTASSEMBLY('{G("Asm1")}',$,'To hop 1',$,$,$,$,$,$,.NOTDEFINED.);\n"
        + $"#97=IFCELEMENTASSEMBLY('{G("Asm2")}',$,'To hop 2',$,$,$,$,$,$,.NOTDEFINED.);\n"
        + $"#98=IFCRELAGGREGATES('{G("RelAggA")}',$,$,$,#96,(#97));\n"
        + $"#100=IFCRELAGGREGATES('{G("RelAggB")}',$,$,$,#97,(#96));\n"
        // Kiểu USERDEFINED + cửa thứ hai NOTDEFINED lấy PredefinedType của kiểu.
        + $"#101=IFCDOORTYPE('{G("DoorType")}',$,'DT',$,$,$,$,'DT-1','Cua dac biet',.USERDEFINED.,$,$,$);\n"
        + $"#102=IFCDOOR('{G("Door2")}',$,'Cua 2',$,$,$,$,'D-02',2000.,800.,.NOTDEFINED.,.NOTDEFINED.,$);\n"
        + $"#103=IFCRELDEFINESBYTYPE('{G("RelType2")}',$,$,$,(#102),#101);\n"
        // IfcSpace: CompositionType đứng trước PredefinedType; IfcLogical UNKNOWN.
        + $"#104=IFCSPACE('{G("Space1")}',$,'101',$,$,$,$,'Phong hop',.ELEMENT.,.INTERNAL.,$);\n"
        + "#105=IFCPROPERTYSINGLEVALUE('Handicap',$,IFCLOGICAL(.U.),$);\n"
        + $"#106=IFCPROPERTYSET('{G("Pset3")}',$,'Pset_SpaceCommon',$,(#105));\n"
        + $"#107=IFCRELDEFINESBYPROPERTIES('{G("RelProp2")}',$,$,$,(#104),#106);\n"
        + "ENDSEC;\nEND-ISO-10303-21;\n";

    private static IfcIdsModel Model() => IfcIdsModel.Parse(Ifc);

    private static IfcIdsElement Element(IfcIdsModel model, int id) =>
        (IfcIdsElement)model.Elements().Single(e => ((IfcIdsElement)e).Id == id);

    [Fact]
    public void PhanTu_GomDoiTuongCoGlobalId_TruQuanHeVaPset()
    {
        var types = Model().Elements().Select(e => e.IfcEntity).ToList();
        Assert.Equal(3, types.Count(t => t == "IFCWALL"));
        Assert.Contains("IFCWALLTYPE", types);
        Assert.Contains("IFCDOOR", types);
        Assert.Contains("IFCBUILDINGSTOREY", types);
        Assert.Contains("IFCSYSTEM", types);
        Assert.DoesNotContain(types, t => t.StartsWith("IFCREL"));
        Assert.DoesNotContain("IFCPROPERTYSET", types);
        // Không có GlobalId thì không phải đối tượng IDS nói tới.
        Assert.DoesNotContain("IFCMATERIAL", types);
    }

    [Fact]
    public void Entity_SoDungLop_KhongTinhLopCon_GiongIfcTester()
    {
        var specs = IdsSpec.Parse(
            "<ids><specifications><specification name=\"t\"><applicability><entity><name><simpleValue>IfcWall</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification></specifications></ids>");
        var result = IdsEvaluator.Check(specs, Model().Elements());
        // IfcWallType không phải IfcWall.
        Assert.Equal(3, Assert.Single(result.Specifications).Applicable);
    }

    [Fact]
    public void PredefinedType_TuPhanTu_TuKieu_VaUserDefined()
    {
        var model = Model();
        // NOTDEFINED ở phần tử → lấy của kiểu.
        Assert.Equal("SOLIDWALL", Element(model, 11).PredefinedType);
        // USERDEFINED → ObjectType.
        Assert.Equal("Tuong dac biet", Element(model, 12).PredefinedType);
        // IfcDoor: enum đầu tiên sau Tag là PredefinedType (sau OverallHeight/OverallWidth).
        Assert.Equal("DOOR", Element(model, 13).PredefinedType);
        Assert.Equal("SOLIDWALL", Element(model, 10).PredefinedType);
    }

    [Fact]
    public void ThuocTinh_TheoViTriLuocDo_VaBangRiengCuaLop()
    {
        var model = Model();
        var wall = Element(model, 12);
        Assert.Equal("Tuong 2", wall.Attribute("Name"));
        Assert.Equal("mo ta", wall.Attribute("Description"));
        Assert.Equal("W-02", wall.Attribute("Tag"));
        Assert.Equal(G("Wall2"), wall.Attribute("GlobalId"));
        Assert.Equal("USERDEFINED", wall.Attribute("PredefinedType"));

        var door = Element(model, 13);
        Assert.Equal("2100.", door.Attribute("OverallHeight"));
        Assert.Equal("900.", door.Attribute("OverallWidth"));
        // Tên không có trong bảng → null, tức facet trượt chứ không âm thầm đạt.
        Assert.Null(door.Attribute("ThuocTinhLa"));
        Assert.Equal("3000.", Element(model, 4).Attribute("Elevation"));
    }

    [Fact]
    public void Property_BooleanThanhTRUEFALSE_VaThuaKeTuKieu()
    {
        var model = Model();
        var wall2 = Element(model, 12);
        // IFCBOOLEAN(.F.) phải so được với "FALSE" của IDS — không phải "F".
        Assert.Equal("FALSE", wall2.Property("Pset_WallCommon", "IsExternal"));
        Assert.Equal("2 HR", wall2.Property("Pset_WallCommon", "FireRating"));
        Assert.Equal("2 HR", wall2.Property(null, "FireRating"));
        Assert.Null(wall2.Property("Pset_WallCommon", "KhongCo"));

        // Tường 1 không có Pset riêng → thừa kế từ IfcWallType.
        Assert.Equal("TRUE", Element(model, 11).Property("Pset_WallCommon", "IsExternal"));
    }

    [Fact]
    public void VatLieu_QuaLayerSetUsage_VaThuaKeTuKieu()
    {
        var model = Model();
        var direct = Element(model, 12).Materials.ToList();
        Assert.Contains("Be tong", direct);
        Assert.Contains("Lop loi", direct);
        Assert.Contains("Concrete", direct);

        Assert.Equal(new[] { "Thep" }, Element(model, 11).Materials.ToList());
        // Tường 3 dùng chung LayerSetUsage với tường 2 → cùng danh sách (đi qua cache).
        Assert.Equal(direct, Element(model, 16).Materials.ToList());
    }

    [Fact]
    public void VatLieu_ConstituentSet_ProfileSet_MaterialList_VaThamChieuHong()
    {
        var model = Model();
        var door = Element(model, 13).Materials.ToList();
        Assert.Contains("Go soi", door);
        Assert.Contains("Canh", door);
        Assert.Contains("Wood", door);

        var column = Element(model, 17).Materials.ToList();
        Assert.Contains("Thep", column);
        Assert.Contains("I200", column);
        Assert.Contains("Steel", column);

        var slab = Element(model, 18).Materials.ToList();
        Assert.Contains("Be tong", slab);
        Assert.Contains("Thep", slab);
        Assert.Contains("Lop rong", slab);

        // Trỏ tới #99 không tồn tại → không có vật liệu, không ném.
        Assert.Empty(Element(model, 19).Materials);
    }

    [Fact]
    public void PredefinedType_KhongCoKieu_KieuUserDefined_IfcSpace_IfcProject()
    {
        var model = Model();
        Assert.Equal(string.Empty, Element(model, 16).PredefinedType);
        // Cửa 2 NOTDEFINED, kiểu USERDEFINED → ElementType của kiểu.
        Assert.Equal("Cua dac biet", Element(model, 102).PredefinedType);
        Assert.Equal("Cua dac biet", Element(model, 101).PredefinedType);
        // IfcSpace: bỏ qua CompositionType ở vị trí 8.
        Assert.Equal("INTERNAL", Element(model, 104).PredefinedType);
        Assert.Equal("Phong hop", Element(model, 104).Attribute("LongName"));
        // Tầng và dự án không có PredefinedType.
        Assert.Equal(string.Empty, Element(model, 4).PredefinedType);
        Assert.Equal(string.Empty, Element(model, 1).PredefinedType);
        Assert.Null(Element(model, 1).Attribute("PredefinedType"));
    }

    [Fact]
    public void ThuocTinh_ObjectTypeVaElementType_TheoPhanTuHayKieu()
    {
        var model = Model();
        Assert.Equal("Tuong dac biet", Element(model, 12).Attribute("ObjectType"));
        Assert.Null(Element(model, 12).Attribute("ElementType"));
        Assert.Equal("Cua dac biet", Element(model, 101).Attribute("ElementType"));
        Assert.Null(Element(model, 101).Attribute("ObjectType"));
    }

    [Fact]
    public void Property_IfcLogicalUnknown_GiuChuUNKNOWN_TucKhongDat()
    {
        var space = Element(Model(), 104);
        Assert.Equal("UNKNOWN", space.Property("Pset_SpaceCommon", "Handicap"));
        var specs = IdsSpec.Parse(
            "<ids><specifications><specification name=\"t\"><applicability><entity><name><simpleValue>IFCSPACE</simpleValue></name></entity></applicability>"
            + "<requirements><property><propertySet><simpleValue>Pset_SpaceCommon</simpleValue></propertySet><baseName><simpleValue>Handicap</simpleValue></baseName>"
            + "<value><simpleValue>TRUE</simpleValue></value></property></requirements></specification></specifications></ids>");
        Assert.Equal(0, Assert.Single(IdsEvaluator.Check(specs, Model().Elements()).Specifications).Passed);
    }

    [Fact]
    public void ThuocVe_LongTrongCua_NhomKhongCoTang_VaVongAggregateKhongTreo()
    {
        var model = Model();
        // Phụ kiện lồng trong cửa: tổ tiên là cửa → tầng → toà → site → dự án.
        var proxy = Element(model, 19).PartOf.ToList();
        Assert.Equal(new[] { "IFCDOOR", "IFCBUILDINGSTOREY", "IFCBUILDING", "IFCSITE", "IFCPROJECT" }, proxy);

        // Cột chỉ thuộc hệ, không nằm trong tầng nào.
        Assert.Equal(new[] { "IFCSYSTEM" }, Element(model, 17).PartOf.ToList());

        // Hai tổ hợp aggregate lẫn nhau: dừng ở guard, không treo, không trùng tên.
        var asm = Element(model, 96).PartOf.ToList();
        Assert.Equal(new[] { "IFCELEMENTASSEMBLY" }, asm);
    }

    [Fact]
    public void PhanLoai_TheoHe_GomCaThamChieuCha()
    {
        var model = Model();
        var wall = Element(model, 12);
        var uniformat = wall.Classifications("Uniformat").ToList();
        Assert.Contains("B2010158", uniformat);
        Assert.Contains("B2010", uniformat);
        Assert.Empty(wall.Classifications("Uniclass"));
        Assert.Equal(2, wall.Classifications(null).Count());
        Assert.Empty(Element(model, 11).Classifications(null));
    }

    [Fact]
    public void ThuocVe_TenLop_CuaTangToaNhaVaHe()
    {
        var model = Model();
        var wall = Element(model, 11).PartOf.ToList();
        Assert.Equal(new[] { "IFCBUILDINGSTOREY", "IFCBUILDING", "IFCSITE", "IFCPROJECT" }, wall);

        var door = Element(model, 13).PartOf.ToList();
        Assert.Contains("IFCBUILDINGSTOREY", door);
        Assert.Contains("IFCSYSTEM", door);
        Assert.DoesNotContain("IFCSYSTEM", wall);
    }

    [Fact]
    public void DauCuoi_IsExternalFalse_MotDatMotTruot()
    {
        var specs = IdsSpec.Parse(
            "<ids><specifications><specification name=\"Tường trong\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability>"
            + "<requirements><property><propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet><baseName><simpleValue>IsExternal</simpleValue></baseName>"
            + "<value><simpleValue>FALSE</simpleValue></value></property></requirements></specification></specifications></ids>");
        var spec = Assert.Single(IdsEvaluator.Check(specs, Model().Elements()).Specifications);
        Assert.Equal(3, spec.Applicable);
        Assert.Equal(1, spec.Passed);
        Assert.Equal(2, spec.Failed);
        Assert.Contains(spec.Failures, f => f.Element.StartsWith("#11 "));
        Assert.Contains(spec.Failures, f => f.Element.StartsWith("#16 "));
    }

    [Fact]
    public void SoKhongDat_LaApplicableTruPassed_KhongPhaiDanhSachDaCat()
    {
        // Lỗi cũ lộ ở §41: 785 tường sai FireRating hiện thành "200 không đạt" vì đếm theo danh sách đã cắt.
        var specs = IdsSpec.Parse(
            "<ids><specifications><specification name=\"Có Tag\"><applicability/>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification></specifications></ids>");
        var elements = Enumerable.Range(0, IdsEvaluator.MaxFailuresPerSpecification + 50)
            .Select(i => (IIdsElement)new FakeIdsElement { Label = "phần tử " + i })
            .ToList();

        var result = IdsEvaluator.Check(specs, elements);
        var spec = Assert.Single(result.Specifications);
        Assert.Equal(IdsEvaluator.MaxFailuresPerSpecification + 50, spec.Failed);
        Assert.Equal(IdsEvaluator.MaxFailuresPerSpecification, spec.Failures.Count);
        Assert.True(spec.FailuresTruncated);
        Assert.Equal(IdsEvaluator.MaxFailuresPerSpecification + 50, result.FailureCount);
        Assert.Contains(IdsReport.Messages(result, new string[0]), m => m.Contains((IdsEvaluator.MaxFailuresPerSpecification + 50) + " phần tử không đạt"));
        Assert.Contains("trên " + (IdsEvaluator.MaxFailuresPerSpecification + 50) + " phần tử không đạt", IdsReport.Html("m", "f", IdsReport.IfcScopeNote, result, new string[0]));
    }
}
