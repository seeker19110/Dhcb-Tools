using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Progress
{
    /// <summary>
    /// Đánh giá chênh lệch tiến độ 4D thi công giữa kế hoạch và thực tế.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class ProgressVarianceSummary
    {
        public ProgressVarianceSummary(
            int totalTasks,
            int completedTasks,
            int delayedTasks,
            double overallCompletionPercentage,
            double schedulePerformanceIndex,
            IReadOnlyList<string> criticalDelayNotes)
        {
            TotalTasks = totalTasks;
            CompletedTasks = completedTasks;
            DelayedTasks = delayedTasks;
            OverallCompletionPercentage = overallCompletionPercentage;
            SchedulePerformanceIndex = schedulePerformanceIndex;
            CriticalDelayNotes = criticalDelayNotes ?? Array.Empty<string>();
        }

        public int TotalTasks { get; }
        public int CompletedTasks { get; }
        public int DelayedTasks { get; }
        public double OverallCompletionPercentage { get; }
        public double SchedulePerformanceIndex { get; }
        public IReadOnlyList<string> CriticalDelayNotes { get; }

        public string StatusSummary
        {
            get
            {
                if (SchedulePerformanceIndex >= 1.0)
                    return $"ĐÚNG TIẾN ĐỘ (SPI = {SchedulePerformanceIndex:F2}, Hoàn thành {OverallCompletionPercentage:F1}%)";
                return $"CHẬM TIẾN ĐỘ (SPI = {SchedulePerformanceIndex:F2}, {DelayedTasks} công việc trễ hạn)";
            }
        }
    }

    public static class ProgressVarianceEngine
    {
        public static ProgressVarianceSummary CalculateVariance(
            IReadOnlyList<(string TaskId, DateTime PlannedStart, DateTime PlannedEnd, DateTime? ActualEnd, double ProgressWeight)> tasks,
            DateTime currentDate)
        {
            if (tasks == null || tasks.Count == 0)
                return new ProgressVarianceSummary(0, 0, 0, 0, 1.0, Array.Empty<string>());

            int completed = 0;
            int delayed = 0;
            double earnedValueProgress = 0;
            double plannedValueProgress = 0;
            var notes = new List<string>();

            double totalWeight = tasks.Sum(t => Math.Max(0.01, t.ProgressWeight));

            foreach (var t in tasks)
            {
                double w = Math.Max(0.01, t.ProgressWeight) / totalWeight;

                if (t.ActualEnd.HasValue && t.ActualEnd.Value <= currentDate)
                {
                    completed++;
                    earnedValueProgress += w;
                }

                if (currentDate > t.PlannedEnd && (!t.ActualEnd.HasValue || t.ActualEnd.Value > t.PlannedEnd))
                {
                    delayed++;
                    notes.Add($"Công việc '{t.TaskId}' trễ {(currentDate - t.PlannedEnd).Days} ngày so với kế hoạch.");
                }

                if (currentDate >= t.PlannedStart)
                {
                    if (currentDate >= t.PlannedEnd)
                        plannedValueProgress += w;
                    else
                    {
                        double span = (t.PlannedEnd - t.PlannedStart).TotalDays;
                        double elapsed = (currentDate - t.PlannedStart).TotalDays;
                        plannedValueProgress += w * (span > 0 ? elapsed / span : 1.0);
                    }
                }
            }

            double spi = plannedValueProgress > 0 ? earnedValueProgress / plannedValueProgress : 1.0;

            return new ProgressVarianceSummary(
                totalTasks: tasks.Count,
                completedTasks: completed,
                delayedTasks: delayed,
                overallCompletionPercentage: earnedValueProgress * 100.0,
                schedulePerformanceIndex: spi,
                criticalDelayNotes: notes);
        }
    }
}
