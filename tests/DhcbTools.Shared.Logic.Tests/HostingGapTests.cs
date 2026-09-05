using System.Reflection;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Nhánh còn thiếu của tầng vỏ (hosting): sổ job, kết quả lệnh, log file, phiên bản và kho token.
/// Đây là phần chạy trong Revit/AutoCAD thật, nên mọi đường lỗi đều phải im lặng chứ không được
/// làm sập lệnh đang chạy.
/// </summary>
public class HostingGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dhcb-hosting-gap-" + Guid.NewGuid().ToString("N"));

    public HostingGapTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* thư mục tạm */ }
    }

    // ── BridgeJob ───────────────────────────────────────────────────────────

    /// <summary>Job đã xong thì không huỷ được nữa — kết quả đã có, huỷ đi là mất.</summary>
    [Fact]
    public void Abandon_JobDaXong_TraFalse()
    {
        var job = new BridgeJob("id", "KiemTra", DateTime.UtcNow);
        job.Complete("xong", DateTime.UtcNow);

        Assert.False(job.Abandon("client bỏ đi", DateTime.UtcNow));
        Assert.Equal(BridgeJobStatus.Done, job.Status);
    }

    /// <summary>
    /// Móc huỷ báo "việc đã được nhận rồi": job phải chuyển sang Started chứ không được đánh dấu Abandoned —
    /// lệnh đang chạy thật, client sẽ còn quay lại hỏi kết quả.
    /// </summary>
    [Fact]
    public void Abandon_ViecDaDuocNhan_KhongDanhDauAbandoned()
    {
        var job = new BridgeJob("id", "KiemTra", DateTime.UtcNow) { TryAbandonWork = () => false };

        Assert.False(job.Abandon("client bỏ đi", DateTime.UtcNow));
        Assert.True(job.Started);
        Assert.Equal(BridgeJobStatus.Running, job.Status);
    }

    [Fact]
    public void BridgeJobStore_Count_DemSoMucDangGiu()
    {
        var store = new BridgeJobStore();
        store.Add("KiemTra", DateTime.UtcNow);
        store.Add("ClashDetection", DateTime.UtcNow);

        Assert.Equal(2, store.Count);
    }

    // ── BridgeRequest / BridgeQuery ─────────────────────────────────────────

    [Fact]
    public void BridgeRequest_ThieuConfig_ConfigJsonLaObjectRong()
    {
        Assert.Equal("{}", new BridgeRequest().ConfigJson);
    }

    [Fact]
    public void BridgeRequest_CoConfig_ConfigJsonLaChuoiMotDong()
    {
        var request = new BridgeRequest { Config = JObject.Parse("{\"dryRun\":true}") };

        Assert.Equal("{\"dryRun\":true}", request.ConfigJson);
    }

    /// <summary>Panel/MCP gửi "config" thay vì "params": vẫn phải đọc được, nếu không mọi tham số bị bỏ qua.</summary>
    [Fact]
    public void BridgeQuery_GuiConfigThayViParams_VanDocDuoc()
    {
        Assert.Equal("{\"limit\":200}", new BridgeQuery { Config = JObject.Parse("{\"limit\":200}") }.ParamsJson);
        Assert.Equal("{\"limit\":50}", new BridgeQuery { Params = JObject.Parse("{\"limit\":50}") }.ParamsJson);
        Assert.Equal("{}", new BridgeQuery().ParamsJson);
    }

    [Fact]
    public void BridgeWorkItem_GiuLaiRequestDaNhan()
    {
        var request = new BridgeRequest { Command = "KiemTra" };

        Assert.Same(request, new BridgeWorkItem<BridgeRequest, string>(request).Request);
    }

    // ── CommandResult ───────────────────────────────────────────────────────

    /// <summary>Tên cũ bên Revit và tên mới phải là cùng một con số.</summary>
    [Fact]
    public void CommandResult_TenCuVaTenMoi_CungMotConSo()
    {
        var result = CommandResult.Ok("xong");
        result.AffectedElementCount = 7;

        Assert.Equal(7, result.AffectedCount);
        Assert.Equal(7, result.AffectedElementCount);
    }

    [Fact]
    public void CommandResult_Fail_GiuDanhSachLoi()
    {
        var result = CommandResult.Fail("hỏng", new[] { "lỗi 1", "lỗi 2" });

        Assert.False(result.Success);
        Assert.Equal(new[] { "lỗi 1", "lỗi 2" }, result.Errors);
    }

    [Fact]
    public void CommandResult_ThemCanhBao_KhongDoiTrangThaiThanhCong()
    {
        var result = CommandResult.Ok("xong")
            .WithMessage("cảnh báo 1")
            .WithMessages(new[] { "cảnh báo 2", "cảnh báo 3" });

        Assert.True(result.Success);
        Assert.Equal(new[] { "cảnh báo 1", "cảnh báo 2", "cảnh báo 3" }, result.Messages);
    }

    // ── DhcbVersion ─────────────────────────────────────────────────────────

    [Fact]
    public void DhcbVersion_AssemblyNull_Tra0()
    {
        Assert.Equal("0", DhcbVersion.Of(null!));
    }

    /// <summary>Có AssemblyInformationalVersion: cắt phần "+&lt;git sha&gt;" mà SourceLink gắn thêm.</summary>
    [Fact]
    public void DhcbVersion_CoInformationalVersion_CatPhanSauDauCong()
    {
        var version = DhcbVersion.Of(typeof(HostingGapTests).Assembly);

        Assert.NotEqual("0", version);
        Assert.DoesNotContain("+", version);
    }

    /// <summary>Assembly không có AssemblyInformationalVersion: rơi về AssemblyVersion.</summary>
    [Fact]
    public void DhcbVersion_KhongCoInformationalVersion_RoiVeAssemblyVersion()
    {
        var dynamic = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("KhongCoThongTinPhienBan"), System.Reflection.Emit.AssemblyBuilderAccess.Run);

        Assert.Equal("0.0.0.0", DhcbVersion.Of(dynamic));
    }

    [Fact]
    public void DhcbVersion_Current_TraPhienBanCuaAssemblyGoi()
    {
        Assert.Equal(DhcbVersion.Of(typeof(HostingGapTests).Assembly), DhcbVersion.Current());
    }

    // ── DhcbLog ─────────────────────────────────────────────────────────────

    [Fact]
    public void DhcbLog_PathFor_NamTrongThuMucLogsTheoNgay()
    {
        var path = DhcbLog.PathFor("Revit");

        Assert.StartsWith(DhcbLog.DefaultDirectory, path);
        Assert.EndsWith("Revit-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log", path);
    }

    [Fact]
    public void DhcbLog_GhiDongVaGhiException_KhongNem()
    {
        var app = "TestApp" + Guid.NewGuid().ToString("N").Substring(0, 6);
        try
        {
            DhcbLog.Write(app, "một dòng");
            DhcbLog.Error(app, "khi mở model", new InvalidOperationException("nổ"));

            var text = File.ReadAllText(DhcbLog.PathFor(app));
            Assert.Contains("một dòng", text);
            Assert.Contains("LỖI khi mở model: System.InvalidOperationException: nổ", text);
        }
        finally
        {
            try { File.Delete(DhcbLog.PathFor(app)); } catch { /* dọn dẹp */ }
        }
    }

    /// <summary>Tên app chứa ký tự không hợp lệ cho đường dẫn: nuốt lỗi, không làm hỏng lệnh đang chạy.</summary>
    [Fact]
    public void DhcbLog_TenAppHong_NuotLoiKhongNem()
    {
        DhcbLog.Write("khong/hop/le\0", "một dòng");
    }

    /// <summary>Dọn log: xoá file cũ hơn hạn giữ, không đụng file mới.</summary>
    [Fact]
    public void DhcbLog_Prune_XoaFileCuGiuFileMoi()
    {
        var app = "TestPrune" + Guid.NewGuid().ToString("N").Substring(0, 6);
        Directory.CreateDirectory(DhcbLog.DefaultDirectory);
        var cu = Path.Combine(DhcbLog.DefaultDirectory, app + "-2020-01-01.log");
        var moi = DhcbLog.PathFor(app);
        File.WriteAllText(cu, "cũ");
        File.WriteAllText(moi, "mới");
        File.SetLastWriteTime(cu, DateTime.Now.AddDays(-DhcbLog.RetentionDays - 1));

        try
        {
            DhcbLog.Prune(app);

            Assert.False(File.Exists(cu));
            Assert.True(File.Exists(moi));
        }
        finally
        {
            try { File.Delete(cu); } catch { /* dọn dẹp */ }
            try { File.Delete(moi); } catch { /* dọn dẹp */ }
        }
    }

    /// <summary>
    /// File cũ đang bị khoá nên xoá không được: bỏ qua và đi tiếp (lần khởi động sau thử lại), chứ không
    /// được để một file kẹt chặn cả bước dọn log.
    /// </summary>
    [Fact]
    public void DhcbLog_Prune_FileKhongXoaDuoc_BoQuaKhongNem()
    {
        var app = "TestPruneLock" + Guid.NewGuid().ToString("N").Substring(0, 6);
        Directory.CreateDirectory(DhcbLog.DefaultDirectory);
        var cu = Path.Combine(DhcbLog.DefaultDirectory, app + "-2020-01-01.log");
        File.WriteAllText(cu, "cũ");
        File.SetLastWriteTime(cu, DateTime.Now.AddDays(-DhcbLog.RetentionDays - 1));

        try
        {
            DhcbLog.Prune(app, _ => throw new IOException("file đang bị khoá"));

            Assert.True(File.Exists(cu));
        }
        finally
        {
            try { File.Delete(cu); } catch { /* dọn dẹp */ }
        }
    }

    /// <summary>Tên app không dùng được làm mẫu tìm file: nuốt lỗi, dọn log không được phép chặn khởi động.</summary>
    [Fact]
    public void DhcbLog_Prune_TenAppHong_NuotLoiKhongNem()
    {
        Directory.CreateDirectory(DhcbLog.DefaultDirectory);

        DhcbLog.Prune("khong\0hop\0le");
    }

    /// <summary>Chưa có thư mục log thì không có gì để dọn — trả về ngay, không ném.</summary>
    [Fact]
    public void DhcbLog_Prune_ChuaCoThuMuc_KhongNem()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        var home = Environment.GetEnvironmentVariable("HOME");
        var trong = Path.Combine(_dir, "chua-co");
        try
        {
            Environment.SetEnvironmentVariable("APPDATA", trong);
            Environment.SetEnvironmentVariable("HOME", trong);
            DhcbLog.Prune("Revit");
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPDATA", appData);
            Environment.SetEnvironmentVariable("HOME", home);
        }
    }

    // ── BridgeTokenStore ────────────────────────────────────────────────────

    /// <summary>Biến môi trường ghi đè file — đường dùng khi chạy trong CI/agent.</summary>
    [Fact]
    public void BridgeTokenStore_CoBienMoiTruong_DungTokenDo()
    {
        var truoc = Environment.GetEnvironmentVariable(BridgeTokenStore.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(BridgeTokenStore.EnvironmentVariable, "  token-tu-moi-truong  ");

            Assert.Equal("token-tu-moi-truong", BridgeTokenStore.LoadOrCreate(Path.Combine(_dir, "t.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BridgeTokenStore.EnvironmentVariable, truoc);
        }
    }

    /// <summary>
    /// Thu ACL hỏng: ghi cảnh báo nhưng vẫn sinh token và vẫn ghi được file — %APPDATA% vốn đã là thư mục
    /// riêng của user, chặn khởi động vì lý do này là mất nhiều hơn được.
    /// </summary>
    [Fact]
    public void BridgeTokenStore_ThuAclHong_CanhBaoNhungVanTaoToken()
    {
        var path = Path.Combine(_dir, "token.txt");
        var logs = new List<string>();

        var token = BridgeTokenStore.LoadOrCreate(path, logs.Add, restrictToOwner: _ => false);

        Assert.True(token.Length >= 32);
        Assert.Equal(token, File.ReadAllText(path));
        Assert.Contains(logs, l => l.Contains("không thu được quyền file token"));
    }

    /// <summary>File token cũ quá ngắn (hỏng/bị cắt): sinh token mới đè lên, không dùng lại token yếu.</summary>
    [Fact]
    public void BridgeTokenStore_TokenCuQuaNgan_SinhTokenMoi()
    {
        var path = Path.Combine(_dir, "ngan.txt");
        File.WriteAllText(path, "qua-ngan");

        var token = BridgeTokenStore.LoadOrCreate(path);

        Assert.True(token.Length >= 32);
        Assert.Equal(token, File.ReadAllText(path));
    }

    // ── RequiredConfig ──────────────────────────────────────────────────────

    [Fact]
    public void ConfigException_GiuLaiExceptionGoc()
    {
        var goc = new InvalidOperationException("gốc");

        Assert.Same(goc, new ConfigException("ngoài", goc).InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("chuỗi")]
    [InlineData(42)]
    public void MissingMembers_GiaTriKhongPhaiConfig_KhongCoTruongNaoThieu(object? value)
    {
        Assert.Empty(RequiredConfig.MissingMembers(value));
    }

    [Fact]
    public void MissingMembers_Dictionary_KhongLanVao()
    {
        Assert.Empty(RequiredConfig.MissingMembers(new Dictionary<string, string> { ["a"] = "b" }));
    }

    /// <summary>Danh sách config lồng: tên trường thiếu phải chỉ đúng phần tử ("[0].name").</summary>
    [Fact]
    public void MissingMembers_DanhSachConfigLong_ChiDungPhanTuThieu()
    {
        var levels = new List<LevelStub> { new LevelStub(), new LevelStub { Name = "Tầng 1" } };

        Assert.Equal(new[] { "[0].name" }, RequiredConfig.MissingMembers(levels));
    }

    /// <summary>Mảng kiểu của repo cũng phải được lặn vào (IsOwnType đi qua kiểu phần tử).</summary>
    [Fact]
    public void MissingMembers_MangConfigLong_VanBatDuocTruongThieu()
    {
        var config = new ConfigWithArrayStub { Levels = new[] { new LevelStub() } };

        Assert.Equal(new[] { "levels.[0].name" }, RequiredConfig.MissingMembers(config));
    }

    /// <summary>Property ném khi đọc: bỏ qua, không làm hỏng cả bước kiểm config.</summary>
    [Fact]
    public void MissingMembers_PropertyNemKhiDoc_BoQua()
    {
        Assert.Empty(RequiredConfig.MissingMembers(new ThrowingStub()));
    }

    [Fact]
    public void ThrowIfIncomplete_DuTruong_KhongNem()
    {
        RequiredConfig.ThrowIfIncomplete(new LevelStub { Name = "Tầng 1" }, "LevelStub");
    }

    /// <summary>
    /// <see cref="RequiredConfig"/> so khớp theo TÊN attribute (để chạy được cả trên net48 với bản
    /// polyfill trong repo), nên một attribute cùng tên khai ở đây là đủ để giả lập trường bắt buộc —
    /// mà không phải dùng từ khoá <c>required</c> (nó lại chặn việc dựng object thiếu trường trong test).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    private sealed class RequiredMemberAttribute : Attribute
    {
    }

    private sealed class LevelStub
    {
        [RequiredMember]
        public string? Name { get; set; }

        public double ElevationMm { get; set; }
    }

    private sealed class ConfigWithArrayStub
    {
        public LevelStub[]? Levels { get; set; }
    }

    private sealed class ThrowingStub
    {
        public string Boom => throw new InvalidOperationException("không đọc được");
    }
}
