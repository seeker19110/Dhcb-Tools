using Autodesk.Revit.DB;

namespace DhcbTools.Core;

/// <summary>
/// Bộ xử lý cảnh báo dùng chung cho mọi lệnh chạy hàng loạt (Cấp 2-3 trong lộ trình tự động hoá):
/// tự huỷ các cảnh báo không nghiêm trọng, tự chấp nhận các cảnh báo "resolvable" mặc định,
/// và chặn dialog treo máy khi không có người ngồi máy (batch chạy đêm).
/// Lỗi nghiêm trọng (Error, không resolve được) vẫn được giữ lại để rollback transaction như bình thường.
/// </summary>
public sealed class SilentFailuresPreprocessor : IFailuresPreprocessor
{
    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        var failures = failuresAccessor.GetFailureMessages();
        if (failures.Count == 0)
        {
            return FailureProcessingResult.Continue;
        }

        foreach (var failure in failures)
        {
            var severity = failure.GetSeverity();

            if (severity == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteWarning(failure);
                continue;
            }

            if (severity == FailureSeverity.Error && failure.HasResolutions())
            {
                failuresAccessor.ResolveFailure(failure);
            }
        }

        return FailureProcessingResult.ProceedWithCommit;
    }
}
