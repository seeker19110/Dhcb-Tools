using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Bao lỗi bảo mật #8 trong docs/progress.md: HTTP Bridge không xác thực.</summary>
public class BridgeAuthTests
{
    [Fact]
    public void GenerateToken_DuDaiVaKhongTrungNhau()
    {
        var a = BridgeAuth.GenerateToken();
        var b = BridgeAuth.GenerateToken();

        Assert.True(a.Length >= 40, $"Token quá ngắn: {a.Length} ký tự.");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GenerateToken_ChiChuaKyTuAnToanChoHeader()
    {
        var token = BridgeAuth.GenerateToken();

        Assert.All(token, c => Assert.True(char.IsLetterOrDigit(c) || c == '-' || c == '_', $"Ký tự không an toàn: {c}"));
    }

    [Theory]
    [InlineData("Bearer abc123", "abc123")]
    [InlineData("bearer abc123", "abc123")]
    [InlineData("  Bearer   abc123  ", "abc123")]
    public void ExtractBearerToken_LayDungPhanToken(string header, string expected)
    {
        Assert.Equal(expected, BridgeAuth.ExtractBearerToken(header));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc123")]
    [InlineData("Basic abc123")]
    [InlineData("Bearer ")]
    public void ExtractBearerToken_HeaderSaiDinhDang_TraVeNull(string? header)
    {
        Assert.Null(BridgeAuth.ExtractBearerToken(header!));
    }

    [Fact]
    public void TokensMatch_TrungKhop_TraVeTrue()
    {
        Assert.True(BridgeAuth.TokensMatch("abc123", "abc123"));
    }

    [Theory]
    [InlineData("abc123", "abc124")]
    [InlineData("abc123", "abc")]
    [InlineData("abc123", "abc1234")]
    [InlineData("abc123", "")]
    [InlineData("", "")]
    [InlineData("abc123", null)]
    [InlineData(null, "abc123")]
    public void TokensMatch_KhongTrungKhop_TraVeFalse(string? expected, string? actual)
    {
        Assert.False(BridgeAuth.TokensMatch(expected!, actual!));
    }

    [Fact]
    public void IsAuthorized_DungTokenVaContentType_ChoPhep()
    {
        Assert.True(BridgeAuth.IsAuthorized("tok", "Bearer tok", "application/json"));
        Assert.True(BridgeAuth.IsAuthorized("tok", "Bearer tok", "application/json; charset=utf-8"));
    }

    [Theory]
    [InlineData("tok", "Bearer sai", "application/json")]
    [InlineData("tok", null, "application/json")]
    [InlineData("tok", "Bearer tok", "text/plain")]
    [InlineData("tok", "Bearer tok", "application/x-www-form-urlencoded")]
    [InlineData("tok", "Bearer tok", null)]
    public void IsAuthorized_ThieuMotDieuKien_TuChoi(string expectedToken, string? auth, string? contentType)
    {
        Assert.False(BridgeAuth.IsAuthorized(expectedToken, auth!, contentType!));
    }
    [Theory]
    [InlineData("https://trang-la.example", null)]
    [InlineData("null", null)]                         // Origin của iframe sandbox / file://
    [InlineData(null, "cross-site")]
    [InlineData(null, "same-site")]
    [InlineData(null, "same-origin")]
    [InlineData("http://127.0.0.1:8767", "same-origin")]
    public void IsBrowserRequest_TrangWebKhoiXuong_True(string? origin, string? secFetchSite)
    {
        Assert.True(BridgeAuth.IsBrowserRequest(origin, secFetchSite));
    }

    [Theory]
    [InlineData(null, null)]      // script Python, MCP server, panel gateway, curl
    [InlineData("", "  ")]
    [InlineData(null, "none")]    // kỹ sư tự gõ URL /health vào thanh địa chỉ
    [InlineData(null, " None ")]
    public void IsBrowserRequest_ClientHopLeHoacTuGoUrl_False(string? origin, string? secFetchSite)
    {
        Assert.False(BridgeAuth.IsBrowserRequest(origin, secFetchSite));
    }
}
