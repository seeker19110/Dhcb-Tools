using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Progress
{
    /// <summary>Một công việc trong kế hoạch 4D: mốc kế hoạch, mốc thực tế và % hoàn thành hiện tại.</summary>
    public sealed class ProgressTask
    {
        public ProgressTask(string taskId, DateTime plannedStart, DateTime plannedEnd, DateTime? actualEnd, double progressWeight, double percentComplete)
        {
            TaskId = taskId ?? string.Empty;
            PlannedStart = plannedStart;
            PlannedEnd = plannedEnd;
            ActualEnd = actualEnd;
            ProgressWeight = progressWeight;
            PercentComplete = Math.Max(0, Math.Min(100, percentComplete));
        }

        public string TaskId { get; }
        public DateTime PlannedStart { get; }
        public DateTime PlannedEnd { get; }
        public DateTime? ActualEnd { get; }
        public double ProgressWeight { get; }

        /// <summary>0–100. Công việc có <see cref="ActualEnd"/> được coi là 100 bất kể giá trị này.</summary>
        public double PercentComplete { get; }
    }

    /// <summary>
    /// Đánh giá chênh lệch tiến độ 4D thi công giữa kế hoạch và thực tế.
    /// </summary>
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

        /// <summary>SPI = giá trị đạt được / giá trị kế hoạch. ≥ 1 là đúng hoặc vượt tiến độ.</summary>
        public double SchedulePerformanceIndex { get; }

        public IReadOnlyList<string> CriticalDelayNotes { get; }

        public string StatusSummary
        {
            get
            {
                if (SchedulePerformanceIndex >= 1.0)
                    return string.Format(CultureInfo.InvariantCulture, "ĐÚNG TIẾN ĐỘ (SPI = {0:F2}, Hoàn thành {1:F1}%)", SchedulePerformanceIndex, OverallCompletionPercentage);
                return string.Format(CultureInfo.InvariantCulture, "CHẬM TIẾN ĐỘ (SPI = {0:F2}, {1} công việc trễ hạn)", SchedulePerformanceIndex, DelayedTasks);
            }
        }
    }

    /// <summary>
    /// Tính SPI theo Earned Value: giá trị kế hoạch tích luỹ tuyến tính theo thời gian, giá trị đạt được tích
    /// luỹ theo % hoàn thành — công việc đang làm dở đúng nhịp thì SPI = 1, không bị kéo về 0.
    /// </summary>
    public static class ProgressVarianceEngine
    {
        /// <summary>Dạng cũ: không có % hoàn thành — công việc chưa xong tính 0 %, chỉ đúng khi bảng tiến độ chỉ ghi mốc xong.</summary>
        public static ProgressVarianceSummary CalculateVariance(
            IReadOnlyList<(string TaskId, DateTime PlannedStart, DateTime PlannedEnd, DateTime? ActualEnd, double ProgressWeight)>? tasks,
            DateTime currentDate)
        {
            var mapped = tasks?
                .Select(t => new ProgressTask(t.TaskId, t.PlannedStart, t.PlannedEnd, t.ActualEnd, t.ProgressWeight, t.ActualEnd.HasValue ? 100 : 0))
                .ToList();
            return CalculateVariance(mapped, currentDate);
        }

        public static ProgressVarianceSummary CalculateVariance(IReadOnlyList<ProgressTask>? tasks, DateTime currentDate)
        {
            if (tasks == null || tasks.Count == 0)
                return new ProgressVarianceSummary(0, 0, 0, 0, 1.0, Array.Empty<string>());

            var completed = 0;
            var delayed = 0;
            double earned = 0;
            double planned = 0;
            var notes = new List<string>();

            var totalWeight = tasks.Sum(t => Math.Max(0.01, t.ProgressWeight));

            foreach (var t in tasks)
            {
                var w = Math.Max(0.01, t.ProgressWeight) / totalWeight;
                var done = t.ActualEnd.HasValue && t.ActualEnd.Value <= currentDate;

                if (done)
                {
                    completed++;
                    earned += w;
                }
                else
                {
                    earned += w * (t.PercentComplete / 100.0);
                }

                if (currentDate > t.PlannedEnd && (!t.ActualEnd.HasValue || t.ActualEnd.Value > t.PlannedEnd))
                {
                    delayed++;
                    // Trễ đo đến ngày XONG THẬT nếu đã xong; chỉ công việc còn dở mới đo đến hôm nay.
                    var measuredTo = t.ActualEnd ?? currentDate;
                    var days = (measuredTo - t.PlannedEnd).Days;
                    notes.Add(string.Format(CultureInfo.InvariantCulture, "Công việc '{0}' trễ {1} ngày so với kế hoạch{2}.", t.TaskId, days, t.ActualEnd.HasValue ? " (đã xong)" : string.Empty));
                }

                if (currentDate >= t.PlannedStart)
                {
                    if (currentDate >= t.PlannedEnd)
                    {
                        planned += w;
                    }
                    else
                    {
                        var span = (t.PlannedEnd - t.PlannedStart).TotalDays;
                        var elapsed = (currentDate - t.PlannedStart).TotalDays;
                        planned += w * (span > 0 ? elapsed / span : 1.0);
                    }
                }
            }

            var spi = planned > 0 ? earned / planned : 1.0;

            return new ProgressVarianceSummary(
                totalTasks: tasks.Count,
                completedTasks: completed,
                delayedTasks: delayed,
                overallCompletionPercentage: earned * 100.0,
                schedulePerformanceIndex: spi,
                criticalDelayNotes: notes);
        }
    }
}
