using Autodesk.Revit.DB;

namespace DhcbTools.Core;

/// <summary>
/// Bộ xử lý cảnh báo cho lệnh chạy <b>không có người ngồi máy</b> (Bridge, batch đêm). Không dùng cho Ribbon:
/// ở đó kỹ sư phải thấy hộp thoại của Revit — xem <see cref="FailurePolicy"/> và
/// <see cref="RevitCompat.ApplyFailurePolicy"/>.
/// <list type="bullet">
/// <item><see cref="FailurePolicy.SuppressWarnings"/>: xoá Warning nhưng ghi mô tả vào
/// <see cref="CoreContext.SuppressedWarnings"/>; Error không đụng tới → Revit rollback transaction.</item>
/// <item><see cref="FailurePolicy.Silent"/>: như trên, cộng thêm tự chấp nhận resolution mặc định của Error có
/// resolution (đủ để batch không treo).</item>
/// </list>
/// </summary>
public sealed class SilentFailuresPreprocessor : IFailuresPreprocessor
{
    private readonly FailurePolicy _policy;

    public SilentFailuresPreprocessor(FailurePolicy policy)
    {
        _policy = policy;
    }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        var failures = failuresAccessor.GetFailureMessages();
        if (failures.Count == 0 || _policy == FailurePolicy.Interactive)
        {
            return FailureProcessingResult.Continue;
        }

        var resolvedError = false;
        foreach (var failure in failures)
        {
            var severity = failure.GetSeverity();
            var text = Describe(failure);

            if (severity == FailureSeverity.Warning)
            {
                CoreContext.SuppressedWarnings.Add(text);
                failuresAccessor.DeleteWarning(failure);
                continue;
            }

            if (severity == FailureSeverity.Error && _policy == FailurePolicy.Silent && failure.HasResolutions())
            {
                CoreContext.SuppressedWarnings.Add("[Lỗi tự giải quyết] " + text);
                failuresAccessor.ResolveFailure(failure);
                resolvedError = true;
            }
        }

        // Có Error chưa giải quyết → để Revit xử lý tiếp (rollback); chỉ ép commit khi đã resolve.
        return resolvedError ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
    }

    private static string Describe(FailureMessageAccessor failure)
    {
        string text;
        try
        {
            text = failure.GetDescriptionText();
        }
        catch (Exception)
        {
            text = failure.GetFailureDefinitionId().Guid.ToString();
        }

        try
        {
            var ids = failure.GetFailingElementIds();
            if (ids.Count > 0)
            {
                text += " [" + string.Join(", ", ids.Take(5).Select(i => RevitCompat.IdValue(i).ToString())) + (ids.Count > 5 ? ", …" : string.Empty) + "]";
            }
        }
        catch (Exception)
        {
            // Không lấy được id thì thôi — mô tả vẫn đủ dùng.
        }

        return text;
    }
}
