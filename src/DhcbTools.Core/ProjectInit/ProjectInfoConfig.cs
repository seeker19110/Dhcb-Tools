using System.Collections.Generic;

namespace DhcbTools.Core.ProjectInit;

public sealed class ProjectInfoConfig
{
    public string? ProjectNumber { get; init; }
    public string? ProjectName { get; init; }
    public string? ProjectStatus { get; init; }
    public string? ClientName { get; init; }
    public string? BuildingName { get; init; }
    public string? Address { get; init; }
    public string? OrganizationName { get; init; }

    /// <summary>Tham số khác của Project Information, tra theo tên.</summary>
    public Dictionary<string, string> ExtraParameters { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Chỉ xem trước, không ghi. Mặc định bật như mọi lệnh ghi khác (nguyên tắc 2 của lộ trình).
    /// Trước đây lớp này KHÔNG có trường DryRun trong khi catalog vẫn chào ra — nghĩa là khoá ép
    /// <c>dryRun=true</c> của bộ kiểm thử trong Revit không có tác dụng với riêng lệnh này, và nó
    /// ghi thẳng vào model mẫu. <c>CatalogFieldTests</c> nay chốt chặn cho cả nhóm lệnh ghi.
    /// </summary>
    public bool DryRun { get; init; } = true;
}
