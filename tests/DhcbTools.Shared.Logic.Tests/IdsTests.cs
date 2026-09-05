using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Phần tử giả để kiểm luật IDS mà không cần Revit.</summary>
internal sealed class FakeIdsElement : IIdsElement
{
    public string Label { get; set; } = "1 — Doors \"D1\"";

    public string IfcEntity { get; set; } = "IfcDoor";

    public string PredefinedType { get; set; } = string.Empty;

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ClassificationCodes { get; } = new();

    public List<string> MaterialNames { get; } = new();

    public List<string> Parents { get; } = new();

    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;

    public string? Property(string? propertySet, string name)
    {
        var key = string.IsNullOrEmpty(propertySet) ? name : propertySet + "." + name;
        return Properties.TryGetValue(key, out var value) ? value
            : Properties.TryGetValue(name, out var bare) ? bare : null;
    }

    public IEnumerable<string> Classifications(string? system) => ClassificationCodes;

    public IEnumerable<string> Materials => MaterialNames;

    public IEnumerable<string> PartOf => Parents;
}

public class IdsValueTests
{
    [Fact]
    public void ORong_LuonKhongDat_KeCaKhiKhongRangBuocGi()
    {
        var value = new IdsValue();
        Assert.True(value.IsAny);
        // "Không ràng buộc" nghĩa là giá trị nào cũng nhận — nhưng KHÔNG CÓ giá trị thì vẫn trượt.
        Assert.False(value.Accepts(null));
        Assert.False(value.Accepts("   "));
        Assert.True(value.Accepts("bất kỳ"));
    }

    [Fact]
    public void ChuoiCoDinh_KhongPhanBietHoaThuong()
    {
        var value = new IdsValue { Simple = "IfcWall" };
        Assert.True(value.Accepts("ifcwall"));
        Assert.False(value.Accepts("IfcWallStandardCase"));
        Assert.Equal("= \"IfcWall\"", value.Describe());
    }

    [Fact]
    public void DanhSach_NhanMotTrongCac()
    {
        var value = new IdsValue();
        value.Enumeration.Add("A");
        value.Enumeration.Add("B");
        Assert.True(value.Accepts("b"));
        Assert.False(value.Accepts("C"));
        Assert.Equal("thuộc {A, B}", value.Describe());
    }

    [Fact]
    public void Mau_PhaiKhopTOANBOChuoi()
    {
        var value = new IdsValue { Pattern = @"AB-\d\d" };
        Assert.True(value.Accepts("AB-01"));
        // Chỗ này là lý do phải neo hai đầu: Regex .NET mặc định khớp một đoạn, nên "AB-01-rác" lọt.
        Assert.False(value.Accepts("AB-01-rác"));
        Assert.False(value.Accepts("xAB-01"));
        Assert.Equal("khớp mẫu \"AB-\\d\\d\"", value.Describe());
    }

    [Fact]
    public void Khoang_LayCaBienVaKhongLayBien()
    {
        var inclusive = new IdsValue { MinInclusive = 100, MaxInclusive = 200 };
        Assert.True(inclusive.Accepts("100"));
        Assert.True(inclusive.Accepts("200"));
        Assert.False(inclusive.Accepts("99.9"));
        Assert.Equal("≥ 100 và ≤ 200", inclusive.Describe());

        var exclusive = new IdsValue { MinExclusive = 100, MaxExclusive = 200 };
        Assert.False(exclusive.Accepts("100"));
        Assert.True(exclusive.Accepts("100.1"));
        Assert.False(exclusive.Accepts("200"));
        Assert.Equal("> 100 và < 200", exclusive.Describe());
    }

    [Fact]
    public void Khoang_GapChuThiTruot_KhongNem()
    {
        var value = new IdsValue { MinInclusive = 1 };
        Assert.False(value.Accepts("hai trăm"));
    }

    [Fact]
    public void MoTa_KhongRangBuoc_NoiLaCanCoGiaTri() =>
        Assert.Equal("có giá trị (khác rỗng)", new IdsValue().Describe());
}

public class IdsSpecTests
{
    private const string Header = "<ids xmlns=\"http://standards.buildingsmart.org/IDS\"><specifications>";
    private const string Footer = "</specifications></ids>";

    private static IReadOnlyList<IdsSpecification> Parse(string body) => IdsSpec.Parse(Header + body + Footer);

    [Fact]
    public void KhongPhaiXml_NoiRoChoHong()
    {
        var ex = Assert.Throws<IdsParseException>(() => IdsSpec.Parse("<ids"));
        Assert.Contains("không phải XML đọc được", ex.Message);
    }

    [Fact]
    public void TheGocSai_NoiDangThayGi()
    {
        var ex = Assert.Throws<IdsParseException>(() => IdsSpec.Parse("<khac/>"));
        Assert.Contains("<khac>", ex.Message);
    }

    [Fact]
    public void FileRong_BaoLaKhongDocDuoc()
    {
        var ex = Assert.Throws<IdsParseException>(() => IdsSpec.Parse(string.Empty));
        Assert.Contains("không phải XML đọc được", ex.Message);
    }

    [Fact]
    public void KhongCoSpecification_LaLoiChuKhongPhaiDat()
    {
        // "Kiểm xong, không phát hiện gì" trên một bộ quy tắc rỗng là câu nói dối dễ tin nhất.
        var ex = Assert.Throws<IdsParseException>(() => IdsSpec.Parse(Header + Footer));
        Assert.Contains("không có <specification>", ex.Message);
    }

    [Fact]
    public void SpecificationKhongCoYeuCau_LaLoi()
    {
        var ex = Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"Rỗng\"><applicability><entity><name><simpleValue>IfcWall</simpleValue></name></entity></applicability></specification>"));
        Assert.Contains("luôn đạt", ex.Message);
    }

    [Fact]
    public void DocDuTenMoTaVaHaiPhan()
    {
        var specs = Parse(
            "<specification name=\"Cửa có Tag\" description=\"theo BEP\">"
            + "<applicability><entity><name><simpleValue>IfcDoor</simpleValue></name><predefinedType><simpleValue>DOOR</simpleValue></predefinedType></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>");

        var spec = Assert.Single(specs);
        Assert.Equal("Cửa có Tag", spec.Name);
        Assert.Equal("theo BEP", spec.Description);
        var entity = Assert.Single(spec.Applicability);
        Assert.Equal(IdsFacetKind.Entity, entity.Kind);
        Assert.Equal("DOOR", entity.Container!.Simple);
        Assert.Equal("lớp IFC = \"IfcDoor\", predefinedType = \"DOOR\"", entity.Describe());
        Assert.Equal("thuộc tính = \"Tag\" có giá trị (khác rỗng)", Assert.Single(spec.Requirements).Describe());
    }

    [Fact]
    public void DocDuSauLoaiFacet_VaMoTaTungLoai()
    {
        var specs = Parse(
            "<specification name=\"Đủ loại\"><applicability/><requirements>"
            + "<entity><name><simpleValue>IfcWall</simpleValue></name></entity>"
            + "<attribute><name><simpleValue>Name</simpleValue></name></attribute>"
            + "<property><propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet><baseName><simpleValue>FireRating</simpleValue></baseName></property>"
            + "<classification><system><simpleValue>Uniclass</simpleValue></system><value><simpleValue>EF_25_10</simpleValue></value></classification>"
            + "<material><value><simpleValue>Bê tông</simpleValue></value></material>"
            + "<partOf><entity><simpleValue>Tầng 1</simpleValue></entity></partOf>"
            + "</requirements></specification>");

        var facets = Assert.Single(specs).Requirements;
        Assert.Equal(
            new[]
            {
                IdsFacetKind.Entity, IdsFacetKind.Attribute, IdsFacetKind.Property,
                IdsFacetKind.Classification, IdsFacetKind.Material, IdsFacetKind.PartOf,
            },
            facets.Select(f => f.Kind).ToArray());

        Assert.Equal("lớp IFC = \"IfcWall\"", facets[0].Describe());
        Assert.Equal("property = \"Pset_WallCommon\".= \"FireRating\" có giá trị (khác rỗng)", facets[2].Describe());
        Assert.Equal("phân loại = \"Uniclass\": = \"EF_25_10\"", facets[3].Describe());
        Assert.Equal("vật liệu = \"Bê tông\"", facets[4].Describe());
        Assert.Equal("thuộc về = \"Tầng 1\"", facets[5].Describe());
    }

    [Fact]
    public void DocRestriction_DuBonLoaiRangBuocSo_VaEnumeration()
    {
        var specs = Parse(
            "<specification name=\"Ràng buộc\"><applicability/><requirements>"
            + "<property><baseName><simpleValue>FireRating</simpleValue></baseName><value>"
            + "<restriction base=\"xs:string\"><enumeration value=\"EI60\"/><enumeration value=\"EI90\"/></restriction></value></property>"
            + "<property><baseName><simpleValue>Width</simpleValue></baseName><value>"
            + "<restriction base=\"xs:double\"><minInclusive value=\"100\"/><maxInclusive value=\"900\"/>"
            + "<minExclusive value=\"50\"/><maxExclusive value=\"1000\"/></restriction></value></property>"
            + "<attribute><name><simpleValue>Name</simpleValue></name><value>"
            + "<restriction base=\"xs:string\"><pattern value=\"D-.*\"/></restriction></value></attribute>"
            + "</requirements></specification>");

        var facets = Assert.Single(specs).Requirements;
        Assert.Equal(new[] { "EI60", "EI90" }, facets[0].Value.Enumeration.ToArray());
        Assert.Equal(100, facets[1].Value.MinInclusive);
        Assert.Equal(900, facets[1].Value.MaxInclusive);
        Assert.Equal(50, facets[1].Value.MinExclusive);
        Assert.Equal(1000, facets[1].Value.MaxExclusive);
        Assert.Equal("D-.*", facets[2].Value.Pattern);
    }

    [Fact]
    public void GiaTriViet_KhongBocSimpleValue_VanDoc()
    {
        // File ngoài đời viết cả hai kiểu; từ chối kiểu thứ hai thì kỹ sư chỉ thấy "file hỏng".
        var specs = Parse(
            "<specification name=\"Trần\"><applicability/><requirements>"
            + "<attribute><name>Tag</name></attribute></requirements></specification>");
        Assert.Equal("Tag", Assert.Single(specs).Requirements[0].Name.Simple);
    }

    [Fact]
    public void TheRong_KhongCoGiaTri_LaKhongRangBuoc()
    {
        var specs = Parse(
            "<specification name=\"Trống\"><applicability/><requirements>"
            + "<attribute><name><simpleValue>Tag</simpleValue></name><value></value></attribute></requirements></specification>");
        Assert.True(Assert.Single(specs).Requirements[0].Value.IsAny);
    }

    [Fact]
    public void DocCardinality()
    {
        var specs = Parse(
            "<specification name=\"Cấm và tuỳ chọn\"><applicability/><requirements>"
            + "<attribute cardinality=\"prohibited\"><name><simpleValue>Tag</simpleValue></name></attribute>"
            + "<attribute cardinality=\"optional\"><name><simpleValue>Description</simpleValue></name></attribute>"
            + "</requirements></specification>");

        var facets = Assert.Single(specs).Requirements;
        Assert.True(facets[0].IsProhibited);
        Assert.True(facets[1].IsOptional);
    }

    [Fact]
    public void FacetLa_TuChoi_ChuKhongBoQuaImLang()
    {
        var ex = Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"Lạ\"><applicability/><requirements><quantity/></requirements></specification>"));
        Assert.Contains("quantity", ex.Message);
    }

    [Fact]
    public void RangBuocLa_TuChoi_ChuKhongBoQuaImLang()
    {
        var ex = Assert.Throws<IdsParseException>(() => Parse(
            "<specification name=\"Lạ\"><applicability/><requirements><attribute><name><simpleValue>Tag</simpleValue></name>"
            + "<value><restriction base=\"xs:string\"><minLength value=\"3\"/></restriction></value></attribute></requirements></specification>"));
        Assert.Contains("minLength", ex.Message);
    }

    [Fact]
    public void SoKhongDocDuoc_ThiKhongDatChanTren()
    {
        var specs = Parse(
            "<specification name=\"Số hỏng\"><applicability/><requirements>"
            + "<property><baseName><simpleValue>Width</simpleValue></baseName><value>"
            + "<restriction base=\"xs:double\"><minInclusive value=\"không phải số\"/></restriction></value></property>"
            + "</requirements></specification>");
        Assert.Null(Assert.Single(specs).Requirements[0].Value.MinInclusive);
    }
}

public class IdsEvaluatorTests
{
    private static IReadOnlyList<IdsSpecification> Spec(string body) =>
        IdsSpec.Parse("<ids><specifications>" + body + "</specifications></ids>");

    private static FakeIdsElement Door(string? tag = null)
    {
        var element = new FakeIdsElement { IfcEntity = "IfcDoor" };
        if (tag != null)
        {
            element.Attributes["Tag"] = tag;
        }

        return element;
    }

    [Fact]
    public void ApDungLocTheoApplicability_TuongKhongBiKiemBangLuatCuaCua()
    {
        var specs = Spec(
            "<specification name=\"Cửa có Tag\"><applicability><entity><name><simpleValue>IfcDoor</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>");

        var wall = new FakeIdsElement { IfcEntity = "IfcWall", Label = "9 — Walls" };
        var result = IdsEvaluator.Check(specs, new IIdsElement[] { Door("D-01"), Door(), wall });

        var spec = Assert.Single(result.Specifications);
        Assert.Equal(3, result.ElementCount);
        Assert.Equal(2, spec.Applicable);       // tường không lọt bộ lọc
        Assert.Equal(1, spec.Passed);
        var failure = Assert.Single(spec.Failures);
        Assert.Contains("thiếu/sai: cần thuộc tính", failure.Reason);
        Assert.Equal("Cửa có Tag", failure.Specification);
    }

    [Fact]
    public void KhongPhanTuNaoLotBoLoc_KhongPhaiLaDat()
    {
        var specs = Spec(
            "<specification name=\"Bể nước\"><applicability><entity><name><simpleValue>IfcTank</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>");

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { Door("D-01") });

        var spec = Assert.Single(result.Specifications);
        Assert.True(spec.NoApplicableElements);
        Assert.Equal(0, spec.Passed);
        Assert.Empty(spec.Failures);
        Assert.Equal(1, result.EmptySpecificationCount);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public void FacetCam_CoMoiLaSai()
    {
        var specs = Spec(
            "<specification name=\"Không được dùng Comments\"><applicability/>"
            + "<requirements><attribute cardinality=\"prohibited\"><name><simpleValue>Comments</simpleValue></name></attribute></requirements></specification>");

        var dirty = Door("D-01");
        dirty.Attributes["Comments"] = "ghi tạm";
        var result = IdsEvaluator.Check(specs, new IIdsElement[] { Door("D-02"), dirty });

        var spec = Assert.Single(result.Specifications);
        Assert.Equal(1, spec.Passed);
        Assert.Contains("không được có", Assert.Single(spec.Failures).Reason);
    }

    [Fact]
    public void FacetTuyChon_ThieuCungKhongSao()
    {
        var specs = Spec(
            "<specification name=\"Mô tả nếu có\"><applicability/>"
            + "<requirements><attribute cardinality=\"optional\"><name><simpleValue>Description</simpleValue></name></attribute></requirements></specification>");

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { Door() });
        Assert.Equal(1, Assert.Single(result.Specifications).Passed);
    }

    [Fact]
    public void KiemDuPropertyPhanLoaiVatLieuVaThuocVe()
    {
        var specs = Spec(
            "<specification name=\"Tường đủ thông tin\"><applicability/><requirements>"
            + "<property><propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet><baseName><simpleValue>FireRating</simpleValue></baseName>"
            + "<value><restriction base=\"xs:string\"><enumeration value=\"EI60\"/></restriction></value></property>"
            + "<classification><value><simpleValue>EF_25_10</simpleValue></value></classification>"
            + "<material><value><simpleValue>Bê tông</simpleValue></value></material>"
            + "<partOf><entity><simpleValue>Tầng 1</simpleValue></entity></partOf>"
            + "</requirements></specification>");

        var good = new FakeIdsElement { IfcEntity = "IfcWall" };
        good.Properties["Pset_WallCommon.FireRating"] = "EI60";
        good.ClassificationCodes.Add("EF_25_10");
        good.MaterialNames.Add("Bê tông");
        good.Parents.Add("Tầng 1");

        var bad = new FakeIdsElement { IfcEntity = "IfcWall", Label = "2 — Walls" };
        bad.Properties["FireRating"] = "EI30";

        var spec = Assert.Single(IdsEvaluator.Check(specs, new IIdsElement[] { good, bad }).Specifications);
        Assert.Equal(1, spec.Passed);
        var reason = Assert.Single(spec.Failures).Reason;
        Assert.Contains("property", reason);
        Assert.Contains("phân loại", reason);
        Assert.Contains("vật liệu", reason);
        Assert.Contains("thuộc về", reason);
    }

    [Fact]
    public void PropertyKhongKhaiBoThiTimMoiBo()
    {
        var specs = Spec(
            "<specification name=\"Có FireRating ở đâu cũng được\"><applicability/>"
            + "<requirements><property><baseName><simpleValue>FireRating</simpleValue></baseName></property></requirements></specification>");

        var element = new FakeIdsElement();
        element.Properties["FireRating"] = "EI60";
        Assert.Equal(1, Assert.Single(IdsEvaluator.Check(specs, new IIdsElement[] { element }).Specifications).Passed);
    }

    [Fact]
    public void TenKhaiBangDanhSach_ThuCaDanhSach()
    {
        var specs = Spec(
            "<specification name=\"Tag hoặc Name\"><applicability/><requirements><attribute><name>"
            + "<restriction base=\"xs:string\"><enumeration value=\"Tag\"/><enumeration value=\"Mark\"/></restriction></name></attribute></requirements></specification>");

        var element = new FakeIdsElement();
        element.Attributes["Mark"] = "D-01";
        Assert.Equal(1, Assert.Single(IdsEvaluator.Check(specs, new IIdsElement[] { element }).Specifications).Passed);
    }

    [Fact]
    public void TenKhaiBangMau_KhongSuyNguocRaTen_ThiTRUOT_ChuKhongCoiNhuDat()
    {
        // Không suy ngược được tên từ một biểu thức. Trả "đạt" ở đây là bịa ra một kết luận.
        var specs = Spec(
            "<specification name=\"Tên theo mẫu\"><applicability/><requirements><attribute><name>"
            + "<restriction base=\"xs:string\"><pattern value=\"Ta.*\"/></restriction></name></attribute></requirements></specification>");

        var element = new FakeIdsElement();
        element.Attributes["Tag"] = "D-01";
        var spec = Assert.Single(IdsEvaluator.Check(specs, new IIdsElement[] { element }).Specifications);
        Assert.Equal(0, spec.Passed);
        Assert.Single(spec.Failures);
    }

    [Fact]
    public void PredefinedTypeKhongKhai_ThiKhongRangBuoc()
    {
        var specs = Spec(
            "<specification name=\"Mọi cửa\"><applicability><entity><name><simpleValue>IfcDoor</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>");

        var element = Door("D-01");
        element.PredefinedType = string.Empty;
        Assert.Equal(1, Assert.Single(IdsEvaluator.Check(specs, new IIdsElement[] { element }).Specifications).Passed);
    }

    [Fact]
    public void DanhSachKhongDat_CatONguongVaVanDemDu()
    {
        var specs = Spec(
            "<specification name=\"Cửa có Tag\"><applicability/>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>");

        var elements = Enumerable.Range(0, IdsEvaluator.MaxFailuresPerSpecification + 50)
            .Select(i => (IIdsElement)new FakeIdsElement { Label = "phần tử " + i })
            .ToList();

        var spec = Assert.Single(IdsEvaluator.Check(specs, elements).Specifications);
        Assert.Equal(IdsEvaluator.MaxFailuresPerSpecification, spec.Failures.Count);
        // Số phần tử áp dụng vẫn đếm đủ — cắt là cắt DANH SÁCH, không phải cắt kết luận.
        Assert.Equal(IdsEvaluator.MaxFailuresPerSpecification + 50, spec.Applicable);
        Assert.Equal(0, spec.Passed);
    }

    [Fact]
    public void DauVaoRong_KhongNem()
    {
        var result = IdsEvaluator.Check(null!, null!);
        Assert.Empty(result.Specifications);
        Assert.Equal(0, result.ElementCount);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public void BaoCaoMangDuNhanPhanTuVaMoTaSpecification()
    {
        // Ba trường này chỉ để NGƯỜI đọc báo cáo tìm lại phần tử và hiểu specification nói gì —
        // không có test thì chúng lặng lẽ rỗng mà mọi con số vẫn đúng.
        var specs = Spec(
            "<specification name=\"Cửa có Tag\" description=\"theo BEP của chủ đầu tư\"><applicability/>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>");

        var element = new FakeIdsElement { Label = "1544489 — Doors \"D-01\"" };
        var spec = Assert.Single(IdsEvaluator.Check(specs, new IIdsElement[] { element }).Specifications);

        Assert.Equal("Cửa có Tag", spec.Name);
        Assert.Equal("theo BEP của chủ đầu tư", spec.Description);
        Assert.Equal(1, spec.Applicable);
        Assert.Equal("1544489 — Doors \"D-01\"", Assert.Single(spec.Failures).Element);
    }
}
