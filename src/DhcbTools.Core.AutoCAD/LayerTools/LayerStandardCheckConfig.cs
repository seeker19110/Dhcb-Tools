namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình kiểm tra layer theo bộ quy tắc đặt tên, xuất báo cáo HTML.</summary>
public sealed class LayerStandardCheckConfig
{
    /// <summary>File JSON: mảng object {"pattern": "regex", "description": "..."}.</summary>
    public required string RulesPath { get; init; }

    /// <summary>Đường dẫn file HTML báo cáo.</summary>
    public required string OutputPath { get; init; }
}

// LayerNamingRule / LayerRulesFile nay ở Shared.Logic.Checks (LayerRuleSet) để có test trên CI.
