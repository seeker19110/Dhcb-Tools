using System;
using System.Collections.Generic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests
{
    public class DepthUpgrades123Tests
    {
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
                Assert.NotEmpty(candidate.Points);
                Assert.NotNull(candidate.Summary);
            }
        }

        [Fact]
        public void ClashClassifier_ClassifiesHardAndSoftClashesWithCameraView()
        {
            // Hard clash test
            var hard = ClashClassifier.Classify(
                idA: 101, categoryA: "Duct",
                idB: 202, categoryB: "Pipe",
                xMm: 1000, yMm: 2000, zMm: 3000,
                overlapVolumeMm3: 15000000, distanceMm: 0,
                toleranceMm: 5, requiredClearanceMm: 100);

            Assert.Equal(ClashType.HardClash, hard.ClashType);
            Assert.Equal("Duct_101_vs_Pipe_202", hard.Key);
            Assert.True(hard.CameraX > hard.XMm);
            Assert.Contains("Yêu cầu dời tuyến", hard.Recommendation);

            // Soft clash test
            var soft = ClashClassifier.Classify(
                idA: 105, categoryA: "CableTray",
                idB: 303, categoryB: "Wall",
                xMm: 2000, yMm: 2000, zMm: 1000,
                overlapVolumeMm3: 0, distanceMm: 45,
                toleranceMm: 5, requiredClearanceMm: 100);

            Assert.Equal(ClashType.SoftClash, soft.ClashType);
            Assert.Contains("khoảng hở", soft.Recommendation);
        }

        [Fact]
        public void HandoverPackageValidator_ValidatesPackageStructureAndMissingKinds()
        {
            var files = new List<HandoverFile>
            {
                new HandoverFile("model.ifc", "IFC", 102400, "abc123sha"),
                new HandoverFile("drawings.pdf", "PDF", 50000, "def456sha")
                // Missing CSV, JSON
            };

            var result = HandoverPackageValidator.ValidatePackage(baseFolder: string.Empty, files: files, sheetIndex: Array.Empty<SheetIndexRow>());

            Assert.False(result.IsPassed);
            Assert.Contains("CSV", result.MissingMandatoryKinds);
            Assert.Contains("JSON", result.MissingMandatoryKinds);
            Assert.Contains("KHÔNG ĐẠT", result.Summary);
        }

        [Fact]
        public void HandoverPackageValidator_PassesCompleteManifest()
        {
            var files = new List<HandoverFile>
            {
                new HandoverFile("model.ifc", "IFC", 100, "hash1"),
                new HandoverFile("sheet.pdf", "PDF", 200, "hash2"),
                new HandoverFile("data.csv", "CSV", 50, "hash3"),
                new HandoverFile("report.json", "JSON", 25, "hash4")
            };

            var sheets = new List<SheetIndexRow>
            {
                new SheetIndexRow("A-101", "Ground Floor Plan", "01", "2026-09-01", "2026-09-02", "Designer", "Checker", 4)
            };

            var result = HandoverPackageValidator.ValidatePackage(baseFolder: string.Empty, files: files, sheetIndex: sheets);

            Assert.True(result.IsPassed);
            Assert.Empty(result.MissingMandatoryKinds);
            Assert.Contains("ĐẠT CHUẨN NĐ 207", result.Summary);
        }
    }
}
