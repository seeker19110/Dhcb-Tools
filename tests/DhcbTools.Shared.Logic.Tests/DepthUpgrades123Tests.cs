using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Families;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Mep;
using DhcbTools.Shared.Logic.Progress;
using DhcbTools.Shared.Logic.Setout;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests
{
    /// <summary>
    /// Lát 1-2-3 (PR #160) sau audit 2026-09-23: mỗi ca ở đây khẳng định một HÀNH VI (tuyến né được chướng
    /// ngại, file thiếu làm gói trượt, cặp xa nhau không thành topic…) chứ không chỉ "không ném".
    /// </summary>
    public class DepthUpgrades123Tests
    {
        // ── RouteOptionGenerator ────────────────────────────────────────────────

        private static readonly PathFinderOptions RouteOptions = new PathFinderOptions
        {
            StepMm = 200,
            ClearanceMm = 100,
            TurnPenalty = 10,
            NearObstaclePenalty = 2,
        };

        [Fact]
        public void RouteOptionGenerator_NeChuongNgai_MoiPhuongAnDeuKhongCatQuaHop()
        {
            var start = new Point3(0, 0, 0);
            var goal = new Point3(5000, 0, 0);
            var box = new Box3(2000, -500, -500, 2500, 500, 500);

            var candidates = RouteOptionGenerator.GenerateCandidates(start, goal, new[] { box }, RouteOptions);

            Assert.NotEmpty(candidates);
            foreach (var c in candidates)
            {
                // Đường gấp khúc đi qua tâm ô lưới nên đầu/cuối lệch tối đa một bước.
                Assert.InRange(Math.Abs(c.Points[0].X - start.X), 0, RouteOptions.StepMm);
                Assert.InRange(Math.Abs(c.Points[c.Points.Count - 1].X - goal.X), 0, RouteOptions.StepMm);
                Assert.True(c.LengthMm >= 5000 - (2 * RouteOptions.StepMm), "không tuyến nào ngắn hơn Manhattan");
                Assert.True(c.MinObsDistanceMm >= 0);
                Assert.All(c.Points, p => Assert.False(box.Contains(p.X, p.Y, p.Z, 0), "đỉnh tuyến nằm trong chướng ngại"));
                Assert.InRange(c.Score, 0.0, 1.0);
                Assert.Contains("Điểm:", c.Summary);
            }

            // Sắp theo điểm giảm dần.
            for (var i = 1; i < candidates.Count; i++)
            {
                Assert.True(candidates[i - 1].Score >= candidates[i].Score);
            }
        }

        /// <summary>Không chướng ngại: ba chiến lược ra cùng đường thẳng → gộp làm MỘT, tiêu đề ghi cả ba, điểm 1,00 vì thật sự tối ưu.</summary>
        [Fact]
        public void RouteOptionGenerator_KhongChuongNgai_GopBaChienLuocCungHinhHoc()
        {
            var candidates = RouteOptionGenerator.GenerateCandidates(new Point3(0, 0, 0), new Point3(3000, 0, 0), null, RouteOptions);

            var only = Assert.Single(candidates);
            Assert.Equal(RouteStrategy.Shortest, only.Strategy);
            Assert.Equal("OPT-1", only.OptionId);
            Assert.Contains("Tuyến ngắn nhất", only.Title);
            Assert.Contains("Tuyến ít rẽ nhất", only.Title);
            Assert.Equal(0, only.TurnCount);
            Assert.Equal(1.0, only.Score, 6);
            Assert.Equal(double.MaxValue, only.MinObsDistanceMm);
        }

        /// <summary>Điểm là thước đo TUYỆT ĐỐI: một phương án vòng vèo duy nhất không được 1,00 chỉ vì nó đứng một mình.</summary>
        [Fact]
        public void RouteOptionGenerator_MotPhuongAnVongVeo_KhongDuoc1Diem()
        {
            // Tường chắn ngang buộc mọi tuyến phải vòng lên và rẽ ít nhất 2 lần.
            var wall = new Box3(1500, -3000, -3000, 1700, 3000, 3000);
            var candidates = RouteOptionGenerator.GenerateCandidates(new Point3(0, 0, 0), new Point3(4000, 0, 0), new[] { wall }, RouteOptions, 4000, 4000);

            Assert.NotEmpty(candidates);
            Assert.All(candidates, c => Assert.True(c.TurnCount >= 2));
            Assert.All(candidates, c => Assert.True(c.LengthMm > 4000));
            Assert.All(candidates, c => Assert.True(c.Score < 1.0, "vòng vèo mà vẫn 1,00 là chấm sai"));
        }

        [Fact]
        public void RouteOptionGenerator_BiChanHoanToan_TraRong()
        {
            var wall = new List<Box3> { new Box3(-500, -500, -500, 1500, 1500, 1500) };
            var res = RouteOptionGenerator.GenerateCandidates(new Point3(0, 0, 0), new Point3(1000, 0, 0), wall, new PathFinderOptions { StepMm = 100 });
            Assert.Empty(res);

            Assert.Throws<ArgumentNullException>(() => RouteOptionGenerator.GenerateCandidates(new Point3(0, 0, 0), new Point3(100, 0, 0), null, null!));
        }

        [Fact]
        public void RouteOptionGenerator_CalculateTurns_DemTheoHuong_KhongTheoDoDaiDoan()
        {
            // Thẳng tắp chia đoạn dài ngắn khác nhau: 0 rẽ (bản đầu đếm 1 vì so hiệu vector thô).
            Assert.Equal(0, RouteOptionGenerator.CalculateTurns(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0), new Point3(400, 0, 0) }));
            // Rẽ 90° rồi lên: 2 rẽ, kể cả đoạn đứng chia làm hai.
            Assert.Equal(2, RouteOptionGenerator.CalculateTurns(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0), new Point3(100, 100, 0), new Point3(100, 100, 50), new Point3(100, 100, 200) }));
            // Điểm trùng (đoạn dài 0) không có hướng — bỏ qua, không đếm.
            Assert.Equal(0, RouteOptionGenerator.CalculateTurns(new[] { new Point3(0, 0, 0), new Point3(0, 0, 0), new Point3(100, 0, 0) }));
            Assert.Equal(0, RouteOptionGenerator.CalculateTurns(null));
            Assert.Equal(0, RouteOptionGenerator.CalculateTurns(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0) }));

            Assert.Equal(100, RouteOptionGenerator.CalculateLength(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0) }));
            Assert.Equal(0, RouteOptionGenerator.CalculateLength(null));
            Assert.Equal(0, RouteOptionGenerator.CalculateLength(new[] { new Point3(0, 0, 0) }));
        }

        [Fact]
        public void RouteCandidateOption_ThieuDoiSo_Nem()
        {
            var pts = new[] { new Point3(0, 0, 0) };
            Assert.Throws<ArgumentNullException>(() => new RouteCandidateOption(null!, "t", RouteStrategy.Shortest, pts, 1, 0, 0, 0.5));
            Assert.Throws<ArgumentNullException>(() => new RouteCandidateOption("id", null!, RouteStrategy.Shortest, pts, 1, 0, 0, 0.5));
            Assert.Throws<ArgumentNullException>(() => new RouteCandidateOption("id", "t", RouteStrategy.Shortest, null!, 1, 0, 0, 0.5));
            Assert.Equal(1.0, new RouteCandidateOption("id", "t", RouteStrategy.Shortest, pts, 1, 0, 0, 7).Score);
            Assert.Equal(0.0, new RouteCandidateOption("id", "t", RouteStrategy.Shortest, pts, 1, 0, 0, -1).Score);
        }

        // ── ClashClassifier ────────────────────────────────────────────────────

        [Fact]
        public void ClashClassifier_GiaoSau_HardClash()
        {
            var hard = ClashClassifier.Classify(101, "Duct", 202, "Pipe", 1000, 2000, 3000, 15_000_000, 0, 5, 100);
            Assert.Equal(ClashType.HardClash, hard.ClashType);
            Assert.True(hard.IsIssue);
            Assert.Equal("Duct_101_vs_Pipe_202", hard.Key);
            Assert.Equal(101, hard.IdA);
            Assert.Equal("Duct", hard.CategoryA);
            Assert.Equal(202, hard.IdB);
            Assert.Equal("Pipe", hard.CategoryB);
            Assert.Equal(15_000_000, hard.OverlapVolumeMm3);
            Assert.Equal(0, hard.DistanceMm);
            Assert.Equal(1000, hard.XMm);
            Assert.Equal(2000, hard.YMm);
            Assert.Equal(3000, hard.ZMm);
            Assert.Null(hard.PenetrationDepthMm);
            Assert.Contains("dời tuyến", hard.Recommendation);
        }

        /// <summary>Vết cà 5 mm trên mặt ống 200×200 = 200.000 mm³ — bản đầu so với 5³ = 125 mm³ nên xếp Hard; có độ sâu thật thì là dung sai.</summary>
        [Fact]
        public void ClashClassifier_GiaoNong_CoDoSau_ToleranceFlaw()
        {
            var tol = ClashClassifier.Classify(109, "Beam", 404, "Slab", 3000, 3000, 1000, 200_000, 0, 5, 100, penetrationDepthMm: 5);
            Assert.Equal(ClashType.ToleranceFlaw, tol.ClashType);
            Assert.Contains("dung sai", tol.Recommendation);
            Assert.Equal(5, tol.PenetrationDepthMm);

            // Không có độ sâu → suy căn bậc ba: 100 mm³ → 4,6 mm ≤ 5.
            Assert.Equal(ClashType.ToleranceFlaw, ClashClassifier.Classify(1, "A", 2, "B", 0, 0, 0, 100, 0).ClashType);
            // 1.000 mm³ → 10 mm > 5 → Hard.
            Assert.Equal(ClashType.HardClash, ClashClassifier.Classify(1, "A", 2, "B", 0, 0, 0, 1000, 0).ClashType);
        }

        [Fact]
        public void ClashClassifier_KhongGiao_ThieuKhoangHo_SoftClash()
        {
            var soft = ClashClassifier.Classify(105, "CableTray", 303, "Wall", 2000, 2000, 1000, 0, 45, 5, 100);
            Assert.Equal(ClashType.SoftClash, soft.ClashType);
            Assert.Contains("khoảng hở", soft.Recommendation);
        }

        /// <summary>Hai phần tử cách nhau 200 mm với khoảng hở yêu cầu 100: KHÔNG va chạm — bản đầu xếp thành ToleranceFlaw, sinh topic rác.</summary>
        [Fact]
        public void ClashClassifier_XaNhau_None_KhongThanhTopic()
        {
            var none = ClashClassifier.Classify(109, "Beam", 404, "Slab", 3000, 3000, 1000, 0, 200, 5, 100);
            Assert.Equal(ClashType.None, none.ClashType);
            Assert.False(none.IsIssue);
        }

        /// <summary>Camera BCF phải ở mét: tâm (1000, 2000, 3000) mm → target (1, 2, 3) m, camera lùi 2,5 m.</summary>
        [Fact]
        public void ClassifiedClash_CameraTheoMet_HuongNhinDonVi()
        {
            var c = ClashClassifier.Classify(1, "A", 2, "B", 1000, 2000, 3000, 1, 0);
            Assert.Equal(1.0, c.TargetM.X, 9);
            Assert.Equal(2.0, c.TargetM.Y, 9);
            Assert.Equal(3.0, c.TargetM.Z, 9);
            Assert.Equal(3.5, c.CameraM.X, 9);
            Assert.Equal(4.5, c.CameraM.Y, 9);
            Assert.Equal(5.0, c.CameraM.Z, 9);
            Assert.Equal(1.0, c.ViewDirection.Length, 9);
            Assert.True(c.ViewDirection.X < 0 && c.ViewDirection.Y < 0 && c.ViewDirection.Z < 0, "nhìn từ camera VỀ tâm");
            Assert.Equal(string.Empty, ClashClassifier.Classify(1, null!, 2, null!, 0, 0, 0, 0, 500).CategoryA);
        }

        // ── HandoverPackageValidator ───────────────────────────────────────────

        [Fact]
        public void HandoverPackageValidator_NullOrEmptyFiles_ReturnsFalse()
        {
            var res1 = HandoverPackageValidator.ValidatePackage("", null, null);
            Assert.False(res1.IsPassed);
            Assert.Equal(0, res1.TotalFiles);
            Assert.Contains("không chứa", res1.AuditNotes[0]);
            Assert.Equal(HandoverPackageValidator.MandatoryFileKinds, res1.MissingMandatoryKinds);

            var res2 = HandoverPackageValidator.ValidatePackage("", new List<HandoverFile>(), null);
            Assert.False(res2.IsPassed);
        }

        /// <summary>File có trong manifest nhưng KHÔNG có trên đĩa: gói trượt, số "đã xác minh" không được đếm file thiếu.</summary>
        [Fact]
        public void HandoverPackageValidator_FileThieuTrenDia_KhongDat()
        {
            using var dir = new TempDir();
            var ifc = dir.Write("model.ifc", "IFC STEP DUMMY CONTENT");
            var realHash = HandoverPackage.Sha256Of(ifc);

            var files = new List<HandoverFile>
            {
                new HandoverFile("model.ifc", "IFC", new FileInfo(ifc).Length, realHash),
                new HandoverFile("drawings.pdf", "PDF", 100, "badhash"),
                new HandoverFile("data.csv", "CSV", 100, "badhash"),
                new HandoverFile("report.json", "JSON", 100, "badhash"),
            };
            var sheets = new List<SheetIndexRow> { new SheetIndexRow("S1", "Sheet 1", "0", "2026-09-01", "2026-09-01", "A", "B", 1) };

            var res = HandoverPackageValidator.ValidatePackage(dir.Path, files, sheets);

            Assert.False(res.IsPassed);
            Assert.Equal(4, res.TotalFiles);
            Assert.Equal(1, res.VerifiedHashes);
            Assert.Equal(new[] { "drawings.pdf", "data.csv", "report.json" }, res.MissingFiles);
            Assert.Empty(res.HashMismatches);
            Assert.Empty(res.MissingMandatoryKinds);
            Assert.Contains("S1", res.UnboundSheets);
            Assert.Contains("3 file trong manifest không có trên đĩa", res.Summary);
            Assert.Contains("1/4", res.Summary);
        }

        [Fact]
        public void HandoverPackageValidator_DuFileDungBam_Dat()
        {
            using var dir = new TempDir();
            var names = new[] { ("model.ifc", "IFC"), ("A-101 - Mặt bằng.pdf", "PDF"), ("setout.csv", "CSV"), ("handover.json", "JSON") };
            var files = names.Select(n =>
            {
                var p = dir.Write(n.Item1, "nội dung " + n.Item1);
                return new HandoverFile(n.Item1, n.Item2, new FileInfo(p).Length, HandoverPackage.Sha256Of(p));
            }).ToList();
            var sheets = new List<SheetIndexRow> { new SheetIndexRow("A-101", "Mặt bằng", "0", "", "", "", "", 1) };

            var res = HandoverPackageValidator.ValidatePackage(dir.Path, files, sheets);

            Assert.True(res.IsPassed, res.Summary);
            Assert.Equal(4, res.VerifiedHashes);
            Assert.Empty(res.UnboundSheets);
            Assert.Contains("ĐẠT CHUẨN", res.Summary);
            Assert.Contains("tất cả đều có file", res.AuditNotes[0]);
        }

        /// <summary>Băm sai → mismatch; băm ngắn/rỗng trong manifest không được làm hỏng cả lượt kiểm (Substring).</summary>
        [Fact]
        public void HandoverPackageValidator_BamSaiHoacNgan_BaoMismatch_KhongNem()
        {
            using var dir = new TempDir();
            dir.Write("model.ifc", "x");
            dir.Write("d.pdf", "y");
            var files = new List<HandoverFile>
            {
                new HandoverFile("model.ifc", "IFC", 1, "wronghash00000000000000000000000000000000000000000000000000000000"),
                new HandoverFile("d.pdf", "PDF", 1, ""),
                new HandoverFile("d.pdf", "pdf", 1, null!),
            };

            var res = HandoverPackageValidator.ValidatePackage(dir.Path, files, null);

            Assert.False(res.IsPassed);
            Assert.Equal(3, res.HashMismatches.Count);
            Assert.Contains("kỳ vọng wronghas", res.HashMismatches[0]);
            Assert.Contains("kỳ vọng (trống)", res.HashMismatches[1]);
            Assert.Contains("CSV", res.MissingMandatoryKinds);
            Assert.Contains("sai lệch chuỗi băm", res.Summary);
        }

        /// <summary>Đường dẫn tuyệt đối hoặc "..": không được băm file ngoài gói — coi là thiếu.</summary>
        [Fact]
        public void HandoverPackageValidator_DuongDanThoatGoi_CoiLaThieu()
        {
            using var dir = new TempDir();
            var outside = Path.Combine(Path.GetTempPath(), "dhcb-outside-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(outside, "ngoài");
            try
            {
                var files = new List<HandoverFile>
                {
                    new HandoverFile(outside, "IFC", 1, HandoverPackage.Sha256Of(outside)),
                    // Dùng dấu phân cách của hệ điều hành: "..\x" trên Linux chỉ là một tên file lạ, không phải đi lên.
                    new HandoverFile(".." + Path.DirectorySeparatorChar + Path.GetFileName(outside), "PDF", 1, "x"),
                    new HandoverFile("", "CSV", 1, "x"),
                };

                var res = HandoverPackageValidator.ValidatePackage(dir.Path, files, null);

                Assert.Equal(0, res.VerifiedHashes);
                Assert.Equal(3, res.MissingFiles.Count);
                Assert.All(res.MissingFiles, m => Assert.Contains("ngoài thư mục gói", m));
            }
            finally
            {
                File.Delete(outside);
            }
        }

        /// <summary>Không có thư mục gói: không xác minh được gì → không đạt, ghi chú nói rõ; Kind null không ném.</summary>
        [Fact]
        public void HandoverPackageValidator_KhongCoThuMuc_KhongDat_KindNullKhongNem()
        {
            var files = new List<HandoverFile> { new HandoverFile("a.ifc", null!, 1, "h") };
            var res = HandoverPackageValidator.ValidatePackage(null, files, null);

            Assert.False(res.IsPassed);
            Assert.Equal(0, res.VerifiedHashes);
            Assert.Single(res.MissingFiles);
            Assert.Contains("Không có thư mục gói", res.AuditNotes[0]);
            Assert.Equal(4, res.MissingMandatoryKinds.Count);
        }

        [Fact]
        public void HandoverPackageValidator_TiemHamBam_DungHamDo()
        {
            using var dir = new TempDir();
            dir.Write("m.ifc", "x"); dir.Write("d.pdf", "x"); dir.Write("s.csv", "x"); dir.Write("h.json", "x");
            var files = new[] { ("m.ifc", "IFC"), ("d.pdf", "PDF"), ("s.csv", "CSV"), ("h.json", "JSON") }
                .Select(n => new HandoverFile(n.Item1, n.Item2, 1, "GIA")).ToList();

            var res = HandoverPackageValidator.ValidatePackage(dir.Path, files, new List<SheetIndexRow>(), _ => "gia");

            Assert.True(res.IsPassed);
            Assert.Equal(4, res.VerifiedHashes);
        }

        [Fact]
        public void HandoverValidationResult_NullDanhSach_ThanhRong()
        {
            var r = new HandoverValidationResult(false, 0, 0, null!, null!, null!);
            Assert.Empty(r.MissingMandatoryKinds);
            Assert.Empty(r.HashMismatches);
            Assert.Empty(r.AuditNotes);
            Assert.Empty(r.MissingFiles);
            Assert.Empty(r.UnboundSheets);
            Assert.StartsWith("KHÔNG ĐẠT", r.Summary);
        }

        // ── SetoutDeviationAnalyzer ────────────────────────────────────────────

        [Fact]
        public void SetoutDeviationAnalyzer_DiemChuaDo_VanCoBanGhi_MaTrungLayLanDoSau()
        {
            Assert.Empty(SetoutDeviationAnalyzer.AnalyzeDeviations(null, null));

            var design = new List<(string Code, string Name, double X, double Y, double Z)>
            {
                ("C1", "Cột 1", 1000, 2000, 0),
                ("C2", "Cột 2", 3000, 4000, 0),
                ("C3", "Cột 3", 5000, 6000, 0),
                ("C4", "Cột 4 chưa đo", 7000, 8000, 0),
            };

            var actual = new List<(string Code, double X, double Y, double Z)>
            {
                ("C1", 1002, 2002, 0), // 2,83 mm → InTolerance (max = 10)
                ("c2", 3008, 4008, 0), // 11,31 mm → WarningThreshold
                ("C3", 5000, 6000, 0), // đo lần 1 — bị lần 2 đè
                ("C3", 5020, 6020, 0), // 28,28 mm → OutOfTolerance
            };

            var records = SetoutDeviationAnalyzer.AnalyzeDeviations(design, actual, maxToleranceMm: 10);

            Assert.Equal(4, records.Count);
            Assert.Equal(SetoutDeviationStatus.InTolerance, records[0].Status);
            Assert.Equal("C1", records[0].PointCode);
            Assert.Equal("Cột 1", records[0].ElementName);
            Assert.Equal(2.0, records[0].DeltaX);
            Assert.Equal(2.0, records[0].DeltaY);
            Assert.Equal(0.0, records[0].DeltaZ);
            Assert.True(records[0].PlanarErrorMm > 0);
            Assert.Equal(10, records[0].MaxAllowedToleranceMm);
            Assert.True(records[0].HasSurvey);

            Assert.Equal(SetoutDeviationStatus.WarningThreshold, records[1].Status);
            Assert.Equal(SetoutDeviationStatus.OutOfTolerance, records[2].Status);
            Assert.Equal(5020, records[2].ActualX);

            var missing = records[3];
            Assert.Equal(SetoutDeviationStatus.NotSurveyed, missing.Status);
            Assert.False(missing.HasSurvey);
            Assert.Equal("C4", missing.PointCode);
            Assert.Equal(7000, missing.DesignX);
            Assert.True(double.IsNaN(missing.ActualX));
            Assert.True(double.IsNaN(missing.TotalDistanceErrorMm));
        }

        // ── ProgressVarianceEngine ─────────────────────────────────────────────

        [Fact]
        public void ProgressVarianceEngine_Rong_SpiBang1()
        {
            var emptyRes = ProgressVarianceEngine.CalculateVariance((IReadOnlyList<ProgressTask>?)null, DateTime.Now);
            Assert.Equal(0, emptyRes.TotalTasks);
            Assert.Equal(1.0, emptyRes.SchedulePerformanceIndex);
            Assert.Contains("ĐÚNG TIẾN ĐỘ", emptyRes.StatusSummary);
            Assert.Equal(0, ProgressVarianceEngine.CalculateVariance(new List<ProgressTask>(), DateTime.Now).TotalTasks);
        }

        /// <summary>Trễ đo đến ngày XONG THẬT: kế hoạch 10/9, xong 15/9, xem ngày 20/9 → trễ 5 ngày, không phải 10.</summary>
        [Fact]
        public void ProgressVarianceEngine_TreDoDenNgayXongThat()
        {
            var now = new DateTime(2026, 9, 20);
            var tasks = new List<(string TaskId, DateTime PlannedStart, DateTime PlannedEnd, DateTime? ActualEnd, double ProgressWeight)>
            {
                ("T1", new DateTime(2026, 9, 1), new DateTime(2026, 9, 25), null, 1.0),
                ("T2", new DateTime(2026, 9, 1), new DateTime(2026, 9, 10), new DateTime(2026, 9, 15), 1.0),
            };

            var res = ProgressVarianceEngine.CalculateVariance(tasks, now);

            Assert.Equal(2, res.TotalTasks);
            Assert.Equal(1, res.CompletedTasks);
            Assert.Equal(1, res.DelayedTasks);
            var note = Assert.Single(res.CriticalDelayNotes);
            Assert.Contains("'T2' trễ 5 ngày", note);
            Assert.Contains("(đã xong)", note);
            Assert.Contains("CHẬM TIẾN ĐỘ", res.StatusSummary);
        }

        /// <summary>Công việc dở dang đúng nhịp (50 % tại giữa kỳ): SPI = 1, không bị kéo về 0 như bản chỉ tính xong/chưa.</summary>
        [Fact]
        public void ProgressVarianceEngine_DangLamDungNhip_SpiBang1()
        {
            var tasks = new List<ProgressTask>
            {
                new ProgressTask("T1", new DateTime(2026, 9, 1), new DateTime(2026, 9, 21), null, 2.0, 50),
            };

            var res = ProgressVarianceEngine.CalculateVariance(tasks, new DateTime(2026, 9, 11));

            Assert.Equal(1.0, res.SchedulePerformanceIndex, 6);
            Assert.Equal(50.0, res.OverallCompletionPercentage, 6);
            Assert.Equal(0, res.DelayedTasks);
            Assert.Contains("ĐÚNG TIẾN ĐỘ", res.StatusSummary);
            Assert.Contains("50.0%", res.StatusSummary);

            // Chưa tới ngày bắt đầu: kế hoạch = 0 → SPI mặc định 1; % hoàn thành kẹp trong 0..100.
            var early = ProgressVarianceEngine.CalculateVariance(
                new List<ProgressTask> { new ProgressTask("T2", new DateTime(2026, 9, 10), new DateTime(2026, 9, 10), null, 1.0, 250) },
                new DateTime(2026, 9, 1));
            Assert.Equal(1.0, early.SchedulePerformanceIndex);
            Assert.Equal(100.0, early.OverallCompletionPercentage, 6);
        }

        [Fact]
        public void ProgressVarianceEngine_XongDungHan_DungTienDo()
        {
            var doneTasks = new List<(string TaskId, DateTime PlannedStart, DateTime PlannedEnd, DateTime? ActualEnd, double ProgressWeight)>
            {
                ("T1", new DateTime(2026, 9, 1), new DateTime(2026, 9, 10), new DateTime(2026, 9, 10), 1.0),
                ("T2", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5), new DateTime(2026, 9, 5), 1.0), // span 0 ngày
            };

            var res = ProgressVarianceEngine.CalculateVariance(doneTasks, new DateTime(2026, 9, 30));

            Assert.Contains("ĐÚNG TIẾN ĐỘ", res.StatusSummary);
            Assert.Equal(2, res.CompletedTasks);
            Assert.Empty(res.CriticalDelayNotes);
        }

        // ── ParameterNormalizationEngine ───────────────────────────────────────

        [Fact]
        public void ParameterNormalizationEngine_NullVaPassthrough()
        {
            var emptyRes = ParameterNormalizationEngine.NormalizeParameters(null);
            Assert.Equal(0, emptyRes.TotalParametersChecked);
            Assert.Equal(0, emptyRes.NormalizedCount);
            Assert.NotNull(emptyRes.Changes);

            var res = ParameterNormalizationEngine.NormalizeParameters(new[] { "Ten_Thiet_Bi", "NormalParam", "Param-With-Dash", null, "  ", " Nha_San_Xuat " });
            Assert.Equal(6, res.TotalParametersChecked);
            Assert.Equal(3, res.NormalizedCount);
            Assert.Equal("IfcRoot:Name", res.Changes[0].StandardizedName);
            Assert.Equal("Param_With_Dash", res.Changes[1].StandardizedName);
            Assert.Equal("Nha_San_Xuat", res.Changes[2].OriginalName);
            Assert.Equal("Pset_ManufacturerTypeInformation:Manufacturer", res.Changes[2].StandardizedName);
        }

        /// <summary>Bảng ánh xạ là đầu vào từ từ điển dự án, không cứng trong mã.</summary>
        [Fact]
        public void ParameterNormalizationEngine_BangAnhXaTuBenNgoai()
        {
            var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "DHCB_MaCauKien", "IfcElement:Tag" } };
            var res = ParameterNormalizationEngine.NormalizeParameters(new[] { "dhcb_macaukien", "Ten_Thiet_Bi" }, custom);

            var only = Assert.Single(res.Changes);
            Assert.Equal("IfcElement:Tag", only.StandardizedName);
            Assert.DoesNotContain(ParameterNormalizationEngine.DefaultMappings.Values, v => v.Contains("Pset_Equipment"));
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dhcb-handover-" + Guid.NewGuid().ToString("N"));

            public TempDir() => Directory.CreateDirectory(Path);

            public string Write(string name, string content)
            {
                var p = System.IO.Path.Combine(Path, name);
                File.WriteAllText(p, content);
                return p;
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, true); } catch { /* dọn dẹp */ }
            }
        }
    }
}
