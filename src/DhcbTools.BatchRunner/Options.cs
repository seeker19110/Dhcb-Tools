using System.Diagnostics;
using System.Text;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.AsBuilt;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Ids;
using DhcbTools.Shared.Logic.Ifc;
using Newtonsoft.Json.Linq;

namespace DhcbTools.BatchRunner;
/// <summary>Tham số dòng lệnh của BatchRunner; <see cref="Parse"/> trả null khi phải in <see cref="Usage"/>.</summary>
internal sealed class Options
{
    public string JobPath { get; private set; } = string.Empty;
    public bool DryRun { get; private set; }
    public string LogDir { get; private set; } = "logs";
    public int MaxMinutes { get; private set; } = 480;
    public string? RevitExe { get; private set; }
    public string? AccoreConsole { get; private set; }
    public string? PluginDll { get; private set; }
    public bool ReportOnly { get; private set; }
    public bool Analyze { get; private set; }
    public bool AutoDetectVersion { get; private set; } = true;
    public string? VerifyLog { get; private set; }
    public string? VerifyIfc { get; private set; }
    public string? IfcSpec { get; private set; }
    public string? VerifyIds { get; private set; }
    public string? IdsReport { get; private set; }
    public string? Dossier { get; private set; }
    public string? DossierSpec { get; private set; }
    public string? DossierReport { get; private set; }

    public const string Usage = """
        DhcbTools.BatchRunner --job <job.json> [--dry-run] [--log-dir logs] [--max-minutes 480]
                              [--revit-exe <Revit.exe>] [--accoreconsole <accoreconsole.exe>] [--plugin-dll <DhcbTools.AutoCAD.dll>]
                              [--report-only] [--analyze] [--no-autodetect]
        DhcbTools.BatchRunner --verify-log <run-HHmmss.jsonl>
        DhcbTools.BatchRunner --verify-ifc <file.ifc> [--ifc-spec <quy-tac.json>]
        DhcbTools.BatchRunner --verify-ifc <file.ifc> --verify-ids <yeu-cau.ids> [--ids-report <bao-cao.html>]
        DhcbTools.BatchRunner --dossier <thu-muc-ho-so> --dossier-spec <danh-muc.json> [--dossier-report <bao-cao.html>]
        (Revit: phiên bản tự nhận từ header .rvt; step "PlotPdf" trong job AutoCAD sinh -PLOT ra PDF)
        (--verify-log kiểm chuỗi băm của log đã ghi: 0 nguyên vẹn · 1 hỏng, in ra đúng dòng · 2 không có file)
        (--verify-ifc đọc lại file IFC vừa xuất: 0 đạt · 1 có lỗi · 2 không có file hay quy tắc hỏng)
        (--verify-ids kiểm chính file IFC theo IDS 1.0: 0 không phần tử nào không đạt · 1 có · 2 không có file / IDS hỏng)
        (--dossier đối chiếu danh mục hồ sơ hoàn công với file thật: 0 đủ mục bắt buộc · 1 còn thiếu · 2 thiếu thư mục/danh mục)
        """;

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Thiếu giá trị cho " + args[i]);
            try
            {
                switch (args[i])
                {
                    case "--job": o.JobPath = Next(); break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--log-dir": o.LogDir = Next(); break;
                    case "--max-minutes": o.MaxMinutes = int.Parse(Next()); break;
                    case "--revit-exe": o.RevitExe = Next(); break;
                    case "--accoreconsole": o.AccoreConsole = Next(); break;
                    case "--plugin-dll": o.PluginDll = Next(); break;
                    case "--report-only": o.ReportOnly = true; break;
                    case "--analyze": o.Analyze = true; break;
                    case "--no-autodetect": o.AutoDetectVersion = false; break;
                    case "--verify-log": o.VerifyLog = Next(); break;
                    case "--verify-ifc": o.VerifyIfc = Next(); break;
                    case "--ifc-spec": o.IfcSpec = Next(); break;
                    case "--verify-ids": o.VerifyIds = Next(); break;
                    case "--ids-report": o.IdsReport = Next(); break;
                    case "--dossier": o.Dossier = Next(); break;
                    case "--dossier-spec": o.DossierSpec = Next(); break;
                    case "--dossier-report": o.DossierReport = Next(); break;
                    case "-h": case "--help": return null;
                    default:
                        Console.Error.WriteLine("Tham số không biết: " + args[i]);
                        return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return null;
            }
        }

        // --verify-log, --verify-ifc và --dossier đứng một mình được: chúng không chạy job nào cả.
        return string.IsNullOrEmpty(o.JobPath)
               && string.IsNullOrEmpty(o.VerifyLog)
               && string.IsNullOrEmpty(o.VerifyIfc)
               && string.IsNullOrEmpty(o.Dossier)
            ? null
            : o;
    }
}
