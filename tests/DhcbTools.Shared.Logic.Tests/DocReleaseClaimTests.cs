using System.Text.RegularExpressions;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giữ các câu "đã/chưa phát hành phiên bản X" trong tài liệu khớp với ma trận thật của
/// <c>release.yml</c> và installer. Cùng khuôn với <see cref="DocCommandTableTests"/>.
///
/// Vì sao cần: lỗi này đã tái phát hai lần trong cùng một ngày (2026-09-06). §51 gỡ nhãn "chưa chạy
/// lần nào" cho bốn lệnh đã chạy thật; vài giờ sau đánh giá lại vẫn thấy roadmap ghi "máy chỉ có Revit
/// 2024.3, release.yml chưa đóng gói 2026" trong khi §51 đã chạy Revit 2026 và ma trận release đã có
/// 2026. Bốn nơi nhất quán với nhau nên đọc lại bằng mắt không bắt được — phải đối chiếu với mã.
/// </summary>
public class DocReleaseClaimTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    private static string Doc(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    /// <summary>Danh sách phiên bản trong <c>matrix: revit: [..]</c> / <c>acad: [..]</c> của release.yml.</summary>
    internal static List<int> ReleaseMatrix(string key)
    {
        var yml = Doc(".github/workflows/release.yml");
        var m = Regex.Match(yml, @"^\s*" + key + @":\s*\[([0-9,\s]+)\]", RegexOptions.Multiline);
        Assert.True(m.Success, $"release.yml không còn `{key}: [...]` — đổi ma trận thì sửa test này theo.");
        return m.Groups[1].Value.Split(',').Select(v => int.Parse(v.Trim())).OrderBy(v => v).ToList();
    }

    private static readonly string[] ClaimDocs =
    {
        "README.md", "docs/roadmap.md", "docs/progress.md", "docs/agent-khep-vong.md", "docs/phat-hanh-v1.1.md",
    };

    [Fact]
    public void Khong_doc_nao_noi_chua_dong_goi_phien_ban_da_co_trong_release()
    {
        var revit = ReleaseMatrix("revit");
        var acad = ReleaseMatrix("acad");
        var loi = new List<string>();
        foreach (var doc in ClaimDocs)
        {
            var text = Doc(doc);
            foreach (Match m in Regex.Matches(text, @"chưa[^|\n.—]{0,60}?(đóng gói|phát hành)[^|\n.—]{0,25}"))
            {
                var claim = m.Value;
                foreach (var v in revit.Concat(acad).Distinct())
                {
                    // Chỉ nhìn tới hết mệnh đề (dừng ở ".", "—", "|"): "chưa đóng gói 2027. Revit 2026 đã chạy" không được
                    // kéo 2026 của câu sau vào. "chưa đóng gói Revit 2026/2027" vẫn bắt cả số sau dấu gạch chéo.
                    if (Regex.IsMatch(claim, @"(?<![0-9])" + v + @"(?![0-9])"))
                    {
                        loi.Add($"{doc}: \"{claim.Trim()}\" — nhưng {v} đã nằm trong ma trận release.yml");
                    }
                }
            }
        }
        Assert.True(loi.Count == 0, string.Join("\n", loi));
    }

    [Fact]
    public void Roadmap_khong_con_cau_may_chi_co_mot_phien_ban_khi_release_da_dong_goi_nhieu_hon()
    {
        var revit = ReleaseMatrix("revit");
        var roadmap = Doc("docs/roadmap.md");
        foreach (Match m in Regex.Matches(roadmap, @"máy chỉ có Revit (\d{4})"))
        {
            var only = int.Parse(m.Groups[1].Value);
            Assert.True(revit.Max() <= only,
                $"roadmap.md: \"{m.Value}\" nhưng release.yml đã đóng gói Revit {revit.Max()} — câu này lỗi thời (§54).");
        }
    }

    [Fact]
    public void Khoang_phien_ban_o_tieu_de_agent_khep_vong_bang_ma_tran_release()
    {
        var revit = ReleaseMatrix("revit");
        var expected = $"Revit {revit.Min()}–{revit.Max()}";
        Assert.Contains($"# Agent khép vòng cho {expected}", Doc("docs/agent-khep-vong.md"));
        Assert.Contains($"Agent khép vòng cho {expected}", Doc("docs/roadmap.md"));
    }

    [Fact]
    public void Installer_co_thu_muc_cho_moi_phien_ban_trong_ma_tran()
    {
        var iss = Doc("installer/dhcb-tools.iss");
        foreach (var v in ReleaseMatrix("revit"))
        {
            Assert.Contains($"revit-{v}\\", iss);
        }
        foreach (var v in ReleaseMatrix("acad"))
        {
            Assert.Contains($"autocad-{v}\\", iss);
        }
    }
}
