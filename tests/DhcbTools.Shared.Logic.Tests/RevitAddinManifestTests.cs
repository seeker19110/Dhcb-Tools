using System.Xml.Linq;
using DhcbTools.Shared.Logic.Batch;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Manifest .addin đặt cạnh journal — điều kiện để add-in được nạp khi batch chạy.
/// <para>
/// Revit khởi động bằng journal chỉ đăng ký add-in có .addin cùng thư mục với journal. Thiếu file này
/// thì add-in bị bỏ qua hoàn toàn: không lỗi, không hộp thoại, Revit ngồi im tới hết giờ — đúng những
/// gì đo được ngày 2026-09-03 (phiên tương tác nạp 48 external application, phiên journal chỉ 38).
/// </para>
/// </summary>
public class RevitAddinManifestTests
{
    private const string Dll = @"C:\Users\ai\AppData\Roaming\Autodesk\Revit\Addins\2024\DhcbTools.Revit.dll";

    [Fact]
    public void LaXmlHopLe()
    {
        var doc = XDocument.Parse(RevitAddinManifest.Build(Dll));

        Assert.Equal("RevitAddIns", doc.Root!.Name.LocalName);
        Assert.Single(doc.Root.Elements("AddIn"));
    }

    [Fact]
    public void KhaiBaoDungLoaiVaLop()
    {
        var addin = XDocument.Parse(RevitAddinManifest.Build(Dll)).Root!.Element("AddIn")!;

        Assert.Equal("Application", addin.Attribute("Type")!.Value);
        Assert.Equal("DhcbTools.Revit.App", addin.Element("FullClassName")!.Value);
    }

    /// <summary>Đường dẫn tuyệt đối để không phải chép DLL sang cạnh journal.</summary>
    [Fact]
    public void DungDuongDanTuyetDoiToiDll()
    {
        var addin = XDocument.Parse(RevitAddinManifest.Build(Dll)).Root!.Element("AddIn")!;

        Assert.Equal(Dll, addin.Element("Assembly")!.Value);
    }

    /// <summary>
    /// AddInId phải trùng file .addin cài kèm add-in — lệch nhau là Revit coi như hai add-in khác nhau
    /// và người dùng phải duyệt lại hộp thoại bảo mật thêm một lần nữa.
    /// </summary>
    [Fact]
    public void AddInIdTrungVoiFileCaiKem()
    {
        var addin = XDocument.Parse(RevitAddinManifest.Build(Dll)).Root!.Element("AddIn")!;

        Assert.Equal("2E9F5B1A-8F2D-4C7E-9B3A-1D6C4E8F2A70", addin.Element("AddInId")!.Value);
    }

    /// <summary>Đường dẫn có "&" làm hỏng XML nếu không thoát — Revit khi đó bỏ qua cả file.</summary>
    [Fact]
    public void ThoatKyTuDacBietTrongDuongDan()
    {
        var path = @"C:\Du an\R&D\Addins\DhcbTools.Revit.dll";

        var addin = XDocument.Parse(RevitAddinManifest.Build(path)).Root!.Element("AddIn")!;

        Assert.Equal(path, addin.Element("Assembly")!.Value);
    }

    [Fact]
    public void DuongDanRong_Nem()
    {
        Assert.Throws<ArgumentException>(() => RevitAddinManifest.Build(""));
        Assert.Throws<ArgumentException>(() => RevitAddinManifest.Build("   "));
    }
}
