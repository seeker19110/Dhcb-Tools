using System;
using System.Collections.Generic;
using System.IO;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Families;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Mep;
using DhcbTools.Shared.Logic.Progress;
using DhcbTools.Shared.Logic.Setout;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests
{
    public class DepthUpgrades123Tests
    {
        [Fact]
        public void ObstacleSpatialIndex3D_NullObstacles_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ObstacleSpatialIndex3D(null!));
        }

        [Fact]
        public void ObstacleSpatialIndex3D_CorrectlyIdentifiesBlockedPoints()
        {
            var boxes = new List<Box3>
            {
                new Box3(1000, 1000, 0, 2000, 2000, 3000),
                new Box3(5000, 5000, 0, 6000, 6000, 3000)
            };

            var index = new ObstacleSpatialIndex3D(boxes, cellSizeMm: 500);
            Assert.Equal(2, index.Count);

            // Inside box 1
            Assert.True(index.IsBlocked(1500, 1500, 1000, clearance: 0));
            // Outside box 1 without clearance
            Assert.False(index.IsBlocked(2500, 2500, 1000, clearance: 0));
            // Outside box 1 with 600mm clearance
            Assert.True(index.IsBlocked(2500, 2500, 1000, clearance: 600));

            var candidates = index.GetCandidateIndices(1500, 1500, 1000, clearance: 100);
            Assert.Contains(0, candidates);
        }

        [Fact]
        public void RouteOptionGenerator_GeneratesEvaluatedCandidates()
        {
            var start = new Point3(0, 0, 0);
            var goal = new Point3(5000, 0, 0);
            var obstacles = new List<Box3>
            {
                new Box3(2000, -500, -500, 2500, 500, 500)
            };

            var options = new PathFinderOptions
            {
                StepMm = 200,
                ClearanceMm = 100,
                TurnPenalty = 10,
                NearObstaclePenalty = 2
            };

            var candidates = RouteOptionGenerator.GenerateCandidates(start, goal, obstacles, options);

            Assert.NotEmpty(candidates);
            foreach (var candidate in candidates)
            {
                Assert.True(candidate.Score >= 0.0 && candidate.Score <= 1.0);
                Assert.True(candidate.Points.Count > 0);
                var pts = candidate.Points;
                Assert.NotNull(pts);
                var sm = candidate.Summary;
                Assert.NotNull(sm);
                Assert.NotNull(candidate.OptionId);
                Assert.NotNull(candidate.Title);
                Assert.True(candidate.LengthMm > 0);
                Assert.True(candidate.TurnCount >= 0);
                Assert.True(candidate.MinObsDistanceMm >= 0);
            }
        }

        [Fact]
        public void RouteOptionGenerator_EdgeCasesAndHelpers()
        {
            Assert.Throws<ArgumentNullException>(() => RouteOptionGenerator.GenerateCandidates(new Point3(0, 0, 0), new Point3(100, 0, 0), null!, null!));

            // Impossible route (blocked start)
            var start = new Point3(0, 0, 0);
            var goal = new Point3(1000, 0, 0);
            var wall = new List<Box3> { new Box3(-500, -500, -500, 1500, 1500, 1500) };
            var opts = new PathFinderOptions { StepMm = 100 };
            var emptyRes = RouteOptionGenerator.GenerateCandidates(start, goal, wall, opts);
            Assert.Empty(emptyRes);

            // Helpers
            Assert.True(RouteOptionGenerator.CalculateLength(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0) }) > 0);
            Assert.Equal(0, RouteOptionGenerator.CalculateLength(null!));
            Assert.Equal(0, RouteOptionGenerator.CalculateLength(new[] { new Point3(0, 0, 0) }));

            Assert.Equal(0, RouteOptionGenerator.CalculateTurns(null!));
            Assert.Equal(0, RouteOptionGenerator.CalculateTurns(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0) }));

            var turns = RouteOptionGenerator.CalculateTurns(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0), new Point3(100, 100, 50), new Point3(100, 100, 200) });
            Assert.Equal(2, turns);
        }

        [Fact]
        public void ClashClassifier_ClassifiesAllTypes()
        {
            // Hard clash
            var hard = ClashClassifier.Classify(101, "Duct", 202, "Pipe", 1000, 2000, 3000, 15000000, 0, 5, 100);
            Assert.Equal(ClashType.HardClash, hard.ClashType);
            Assert.Equal("Duct_101_vs_Pipe_202", hard.Key);
            Assert.Equal(101, hard.IdA);
            Assert.Equal("Duct", hard.CategoryA);
            Assert.Equal(202, hard.IdB);
            Assert.Equal("Pipe", hard.CategoryB);
            Assert.Equal(15000000, hard.OverlapVolumeMm3);
            Assert.Equal(0, hard.DistanceMm);

            // Soft clash
            var soft = ClashClassifier.Classify(105, "CableTray", 303, "Wall", 2000, 2000, 1000, 0, 45, 5, 100);
            Assert.Equal(ClashType.SoftClash, soft.ClashType);

            // Tolerance flaw
            var tol = ClashClassifier.Classify(109, "Beam", 404, "Slab", 3000, 3000, 1000, 0, 200, 5, 100);
            Assert.Equal(ClashType.ToleranceFlaw, tol.ClashType);
            Assert.Contains("dung sai", tol.Recommendation);
        }

        [Fact]
        public void HandoverPackageValidator_NullOrEmptyFiles_ReturnsFalse()
        {
            var res1 = HandoverPackageValidator.ValidatePackage("", null!, null!);
            Assert.False(res1.IsPassed);
            Assert.Equal(0, res1.TotalFiles);
            Assert.Contains("không chứa", res1.AuditNotes[0]);

            var res2 = HandoverPackageValidator.ValidatePackage("", new List<HandoverFile>(), null!);
            Assert.False(res2.IsPassed);
        }

        [Fact]
        public void HandoverPackageValidator_DiskFilesValidation()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "handover_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string file1Name = "model.ifc";
                string file1Path = Path.Combine(tempDir, file1Name);
                File.WriteAllText(file1Path, "IFC STEP DUMMY CONTENT");

                // Compute real hash
                string realHash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(file1Path))
                {
                    var hashBytes = sha.ComputeHash(stream);
                    var sb = new System.Text.StringBuilder();
                    foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
                    realHash = sb.ToString();
                }

                var files = new List<HandoverFile>
                {
                    new HandoverFile(file1Name, "IFC", new FileInfo(file1Path).Length, realHash),
                    new HandoverFile("drawings.pdf", "PDF", 100, "badhash"),
                    new HandoverFile("data.csv", "CSV", 100, "badhash"),
                    new HandoverFile("report.json", "JSON", 100, "badhash")
                };

                // With matching disk file for file 1, and missing disk files for 2..4
                var sheets = new List<SheetIndexRow> { new SheetIndexRow("S1", "Sheet 1", "0", "2026-09-01", "2026-09-01", "A", "B", 1) };
                var res = HandoverPackageValidator.ValidatePackage(tempDir, files, sheets);
                Assert.Equal(4, res.TotalFiles);
                Assert.Equal(4, res.VerifiedHashes);
                Assert.NotNull(res.MissingMandatoryKinds);
                Assert.NotNull(res.HashMismatches);
                Assert.NotNull(res.AuditNotes);

                // Now test hash mismatch on disk file
                var badFiles = new List<HandoverFile>
                {
                    new HandoverFile(file1Name, "IFC", 100, "wronghash00000000000000000000000000000000000000000000000000000000"),
                    new HandoverFile("sheet.pdf", "PDF", 100, "hash"),
                    new HandoverFile("data.csv", "CSV", 100, "hash"),
                    new HandoverFile("report.json", "JSON", 100, "hash")
                };

                var badRes = HandoverPackageValidator.ValidatePackage(tempDir, badFiles, null!);
                Assert.False(badRes.IsPassed);
                Assert.NotEmpty(badRes.HashMismatches);
                Assert.NotEmpty(badRes.Summary);
                Assert.NotEmpty(res.Summary);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SetoutDeviationAnalyzer_EdgeCasesAndStatuses()
        {
            Assert.Empty(SetoutDeviationAnalyzer.AnalyzeDeviations(null!, null!));

            var design = new List<(string Code, string Name, double X, double Y, double Z)>
            {
                ("C1", "Column 1", 1000, 2000, 0),
                ("C2", "Column 2", 3000, 4000, 0),
                ("C3", "Column 3", 5000, 6000, 0),
                ("C4", "Column 4 Missing", 7000, 8000, 0)
            };

            var actual = new List<(string Code, double X, double Y, double Z)>
            {
                ("C1", 1002, 2002, 0), // Total 2.83mm -> InTolerance (max = 10)
                ("C2", 3008, 4008, 0), // Total 11.31mm -> WarningThreshold (max = 10, warning up to 15)
                ("C3", 5020, 6020, 0)  // Total 28.28mm -> OutOfTolerance
            };

            var records = SetoutDeviationAnalyzer.AnalyzeDeviations(design, actual, maxToleranceMm: 10);
            Assert.Equal(3, records.Count);

            Assert.Equal(SetoutDeviationStatus.InTolerance, records[0].Status);
            Assert.Equal("C1", records[0].PointCode);
            Assert.Equal("Column 1", records[0].ElementName);
            Assert.Equal(1000, records[0].DesignX);
            Assert.Equal(2000, records[0].DesignY);
            Assert.Equal(0, records[0].DesignZ);
            Assert.Equal(1002, records[0].ActualX);
            Assert.Equal(2002, records[0].ActualY);
            Assert.Equal(0, records[0].ActualZ);
            Assert.Equal(2.0, records[0].DeltaX);
            Assert.Equal(2.0, records[0].DeltaY);
            Assert.Equal(0.0, records[0].DeltaZ);
            Assert.True(records[0].PlanarErrorMm > 0);
            Assert.True(records[0].MaxAllowedToleranceMm > 0);

            Assert.Equal(SetoutDeviationStatus.WarningThreshold, records[1].Status);
            Assert.Equal(SetoutDeviationStatus.OutOfTolerance, records[2].Status);
        }

        [Fact]
        public void ProgressVarianceEngine_EdgeCasesAndMidProgress()
        {
            // Null/empty
            var emptyRes = ProgressVarianceEngine.CalculateVariance(null!, DateTime.Now);
            Assert.Equal(0, emptyRes.TotalTasks);
            Assert.Equal(1.0, emptyRes.SchedulePerformanceIndex);

            // Task in mid-progress and delayed task with actual end after planned end
            var now = new DateTime(2026, 9, 20);
            var tasks = new List<(string TaskId, DateTime PlannedStart, DateTime PlannedEnd, DateTime? ActualEnd, double ProgressWeight)>
            {
                ("T1", new DateTime(2026, 9, 1), new DateTime(2026, 9, 25), null, 1.0),
                ("T2", new DateTime(2026, 9, 1), new DateTime(2026, 9, 10), new DateTime(2026, 9, 15), 1.0)
            };

            var midRes = ProgressVarianceEngine.CalculateVariance(tasks, now);
            Assert.Equal(2, midRes.TotalTasks);
            Assert.Equal(1, midRes.CompletedTasks);
            Assert.Equal(1, midRes.DelayedTasks);
            Assert.True(midRes.SchedulePerformanceIndex >= 0);
            Assert.Contains("CHẬM TIẾN ĐỘ", midRes.StatusSummary);

            // On track completed project
            var pastNow = new DateTime(2026, 9, 30);
            var doneTasks = new List<(string TaskId, DateTime PlannedStart, DateTime PlannedEnd, DateTime? ActualEnd, double ProgressWeight)>
            {
                ("T1", new DateTime(2026, 9, 1), new DateTime(2026, 9, 10), new DateTime(2026, 9, 10), 1.0)
            };

            var doneRes = ProgressVarianceEngine.CalculateVariance(doneTasks, pastNow);
            string doneSum = doneRes.StatusSummary;
            Assert.Contains("ĐÚNG TIẾN ĐỘ", doneSum);
        }

        [Fact]
        public void ParameterNormalizationEngine_NullAndPassthrough()
        {
            var emptyRes = ParameterNormalizationEngine.NormalizeParameters(null!);
            Assert.Equal(0, emptyRes.TotalParametersChecked);
            Assert.Equal(0, emptyRes.NormalizedCount);
            Assert.NotNull(emptyRes.Changes);

            var rawParams = new[] { "Ten_Thiet_Bi", "NormalParam", "Param-With-Dash" };
            var res = ParameterNormalizationEngine.NormalizeParameters(rawParams);
            Assert.Equal(3, res.TotalParametersChecked);
            Assert.Equal(2, res.NormalizedCount);
            Assert.NotEmpty(res.Changes);
        }
    }
}
