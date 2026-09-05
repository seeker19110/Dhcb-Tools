using System.Text;
using DhcbTools.BatchRunner;

namespace DhcbTools.BatchRunner.Tests;

/// <summary>
/// Chạy <see cref="Program.Main"/> như dòng lệnh thật và bắt lại mọi thứ nó in ra.
/// <para>
/// Vì sao gọi <c>Main</c> chứ không gọi thẳng hàm bên trong: hai lỗi ở §44 nằm ở chỗ <b>nối</b> các hàm
/// (ngoại lệ chưa bắt lọt ra tới runtime, mã thoát thành 127), không nằm trong hàm nào cả. Gọi hàm con
/// thì cả hai lỗi đó vẫn xanh.
/// </para>
/// </summary>
internal sealed class Cli : IDisposable
{
    private readonly string _root;

    public Cli()
    {
        _root = Path.Combine(Path.GetTempPath(), "dhcb-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Thư mục tạm riêng của mỗi ca test.</summary>
    public string Root => _root;

    public string Path_(params string[] parts) => System.IO.Path.Combine(new[] { _root }.Concat(parts).ToArray());

    /// <summary>Ghi một file trong thư mục tạm (tự tạo thư mục cha) và trả về đường dẫn tuyệt đối.</summary>
    public string Write(string relative, string content)
    {
        var path = Path_(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>Chạy CLI; trả về mã thoát kèm toàn bộ stdout + stderr.</summary>
    public (int Code, string Output) Run(params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var realOut = Console.Out;
        var realErr = Console.Error;
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var code = Program.Main(args);
            return (code, outWriter + errWriter.ToString());
        }
        finally
        {
            Console.SetOut(realOut);
            Console.SetError(realErr);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Thư mục tạm còn khoá thì kệ; không đáng làm đỏ một ca test vì việc dọn dẹp.
        }
    }
}

/// <summary>Nội dung file mẫu nhỏ nhất mà mỗi đường đọc chấp nhận — đủ để phân biệt "hỏng" với "rỗng".</summary>
internal static class Fixtures
{
    /// <summary>IFC4 hợp lệ, không có thực thể nào.</summary>
    public const string EmptyIfc =
        "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION((''),'2;1');\nFILE_NAME('t','',(''),(''),'','','');\n"
        + "FILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n";

    /// <summary>IFC4 có đúng một bức tường mang GlobalId — đủ cho IDS có phần tử để kiểm.</summary>
    public const string OneWallIfc =
        "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION((''),'2;1');\nFILE_NAME('t','',(''),(''),'','','');\n"
        + "FILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
        + "#1=IFCWALL('0aaaaaaaaaaaaaaaaaaaaa',$,'Tuong 1',$,$,$,$,'W-01',.NOTDEFINED.);\n"
        + "ENDSEC;\nEND-ISO-10303-21;\n";

    /// <summary>Không phải STEP: đúng thứ kỹ sư lỡ đặt tên .ifc (§44).</summary>
    public const string JunkIfc = "hello\nthis is not an ifc file\n";

    /// <summary>IDS 1.0 hợp lệ: mọi phần tử phải có Name.</summary>
    public const string ValidIds =
        "<ids xmlns=\"http://standards.buildingsmart.org/IDS\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
        + "<info><title>t</title></info><specifications>"
        + "<specification name=\"Moi phan tu co Name\" ifcVersion=\"IFC4\"><applicability/>"
        + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements>"
        + "</specification></specifications></ids>";

    /// <summary>IDS hợp lệ XML nhưng không khai specification nào — phải bị TỪ CHỐI, không phải "0 lỗi".</summary>
    public const string EmptyIds =
        "<ids xmlns=\"http://standards.buildingsmart.org/IDS\"><info><title>t</title></info><specifications/></ids>";

    /// <summary>IDS lệch chuẩn XSD (restriction không thuộc xs:, thiếu ifcVersion) — kiểm được, nhưng phải cảnh báo.</summary>
    public const string OffSpecIds =
        "<ids xmlns=\"http://standards.buildingsmart.org/IDS\">"
        + "<info><title>t</title></info><specifications>"
        + "<specification name=\"Moi phan tu co Name\"><applicability/>"
        + "<requirements><attribute><name><simpleValue>Name</simpleValue></name>"
        + "<value><restriction base=\"xs:string\"><pattern value=\".+\"/></restriction></value></attribute></requirements>"
        + "</specification></specifications></ids>";
}
