using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>Mô tả một lệnh Core cho agent/MCP/lớp AI: tên, bí danh, nền tảng, mô tả và các trường config chính.</summary>
    public sealed class CommandDescriptor
    {
        public CommandDescriptor(string name, string app, string description, bool writesModel, params string[] aliases)
        {
            Name = name;
            App = app;
            Description = description;
            WritesModel = writesModel;
            Aliases = aliases;
        }

        /// <summary>Đúng bằng <c>ICoreCommand.CommandName</c>.</summary>
        public string Name { get; }

        /// <summary>"revit" hoặc "autocad".</summary>
        public string App { get; }

        public string Description { get; }

        /// <summary>Lệnh có sửa mô hình không (để lớp AI luôn ép <c>dryRun:true</c> lần đầu).</summary>
        public bool WritesModel { get; }

        public IReadOnlyList<string> Aliases { get; }

        /// <summary>
        /// Đã có mã nguồn trong Core chưa. Lệnh <c>false</c> là phần đặc tả đã chốt nhưng chưa viết:
        /// nó không được chào ra <c>GET /tools</c>, MCP hay lớp ra lệnh tiếng Việt, để agent không
        /// gọi một lệnh không tồn tại. Xem "Lệnh AutoCAD còn thiếu" trong docs/progress.md.
        /// </summary>
        public bool Implemented { get; private set; } = true;

        /// <summary>Đánh dấu lệnh mới chỉ có đặc tả, chưa có mã nguồn.</summary>
        public CommandDescriptor Pending()
        {
            Implemented = false;
            return this;
        }

        /// <summary>
        /// Lệnh công cụ nội bộ (ví dụ <c>RunTests</c>): có trong bảng dispatch để batch runner gọi được,
        /// nhưng KHÔNG lên Ribbon và KHÔNG chào ra <c>GET /tools</c>/MCP — agent không có việc gì gọi
        /// bộ chạy test, và kỹ sư không cần một nút như thế.
        /// </summary>
        public bool Internal { get; private set; }

        public CommandDescriptor Tooling()
        {
            Internal = true;
            return this;
        }

        /// <summary>Tên trường config → mô tả ngắn (dùng cho MCP inputSchema và cho intent parser).</summary>
        public Dictionary<string, string> ConfigFields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Trường config kèm kiểu, theo đúng thứ tự khai báo — form động (giai đoạn 9.1) dựng ô nhập từ đây.
        /// Cùng dữ liệu với <see cref="ConfigFields"/>, chỉ thêm kiểu và giữ thứ tự.
        /// </summary>
        public List<FieldSpec> Fields { get; } = new List<FieldSpec>();

        /// <summary>Từ khoá tiếng Việt/Anh để nhận dạng ý định.</summary>
        public List<string> Keywords { get; } = new List<string>();

        /// <summary>Khai báo một trường; kiểu suy ra từ tên theo <see cref="FieldKindGuess"/>.</summary>
        public CommandDescriptor Field(string name, string description) =>
            Field(name, description, FieldKindGuess.Of(name));

        /// <summary>Khai báo một trường với kiểu chỉ định — dùng khi suy đoán theo tên sai.</summary>
        public CommandDescriptor Field(string name, string description, FieldKind kind)
        {
            ConfigFields[name] = description;
            Fields.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            Fields.Add(new FieldSpec(name, description, kind));
            return this;
        }

        public CommandDescriptor Words(params string[] words)
        {
            Keywords.AddRange(words);
            return this;
        }

        public bool Matches(string commandOrAlias)
        {
            if (string.Equals(Name, commandOrAlias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var a in Aliases)
            {
                if (string.Equals(a, commandOrAlias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Danh mục lệnh — nguồn sự thật duy nhất cho: dispatch của Bridge (kiểm bằng test đối chiếu §2.6),
    /// <c>GET /tools</c>, MCP server (mục 6.2) và whitelist của lớp ra lệnh tiếng Việt (mục 5.4).
    /// Mọi lệnh Core mới PHẢI thêm vào đây — test <c>CommandCatalogTests</c> sẽ báo khi thiếu.
    /// </summary>
    public static class CommandCatalog
    {
        public const string Revit = "revit";
        public const string AutoCad = "autocad";

        public static readonly IReadOnlyList<CommandDescriptor> All = new List<CommandDescriptor>
        {
            // ── Revit — nền tảng ────────────────────────────────────────────
            new CommandDescriptor("ParameterExport", Revit, "Xuất tham số phần tử theo category ra CSV", false)
                .Field("categories", "danh sách category").Field("parameterNames", "danh sách tham số").Field("outputPath", "file CSV")
                .Words("xuất tham số", "export parameter", "xuất csv tham số"),
            new CommandDescriptor("ParameterImport", Revit, "Nhập CSV ghi ngược tham số vào mô hình", true)
                .Field("inputPath", "file CSV").Field("dryRun", "xem trước")
                .Words("nhập tham số", "import parameter", "ghi tham số từ csv"),
            new CommandDescriptor("RemoveUnusedViews", Revit, "Xoá view không đặt trên sheet và sheet rỗng", true, "Cleanup")
                .Field("dryRun", "xem trước")
                .Words("dọn view", "xoá view thừa", "cleanup view", "dọn sheet"),
            new CommandDescriptor("AutoNumbering", Revit, "Đánh số hàng loạt theo vị trí hình học", true, "AutoNumber")
                .Field("category", "category").Field("parameterName", "tham số đích").Field("prefix", "tiền tố").Field("padWidth", "số chữ số").Field("dryRun", "xem trước")
                .Words("đánh số", "numbering", "đánh số cửa", "đánh số phòng"),
            new CommandDescriptor("BatchExport", Revit, "Xuất PDF/DWG/IFC/NWC hàng loạt", false, "Export")
                .Field("outputFolder", "thư mục").Field("formats", "Pdf/Dwg/Ifc/Nwc").Field("sheetNumbers", "lọc sheet").Field("fileNamePattern", "mẫu tên file {SheetNumber}-{SheetName}").Field("dryRun", "xem trước")
                .Words("xuất pdf", "xuất dwg", "in hàng loạt", "export pdf", "xuất ifc"),
            new CommandDescriptor("HealthReport", Revit, "Báo cáo HTML sức khoẻ mô hình", false, "Health")
                .Field("outputPath", "file HTML")
                .Words("health report", "báo cáo sức khoẻ", "kiểm tra mô hình", "warning"),
            new CommandDescriptor("ProjectInfo", Revit, "Gán thông tin dự án", true)
                .Field("projectName", "tên dự án").Field("projectNumber", "mã dự án").Field("dryRun", "xem trước")
                .Words("thông tin dự án", "project info"),
            new CommandDescriptor("LevelSetup", Revit, "Tạo tầng + view plan từ danh sách", true, "CreateLevels")
                .Field("levels", "[{name, elevationMm}]").Field("dryRun", "xem trước")
                .Words("tạo tầng", "tạo level", "create levels"),
            new CommandDescriptor("GridSetup", Revit, "Tạo trục từ danh sách", true, "CreateGrids")
                .Field("grids", "[{name, positionMm, orientation}]").Field("dryRun", "xem trước")
                .Words("tạo trục", "tạo grid", "create grids"),
            new CommandDescriptor("FamilyLoader", Revit, "Load family theo danh mục", true, "LoadFamilies")
                .Field("familyFolder", "thư mục chứa .rfa").Field("familyNames", "tên family cần nạp (rỗng = mọi .rfa trong thư mục)").Field("overwriteExisting", "ghi đè family đã có").Field("dryRun", "xem trước")
                .Words("load family", "nạp family"),

            // ── Revit — MEPF ────────────────────────────────────────────────
            new CommandDescriptor("SleeveAuto", Revit, "Đặt sleeve tại giao cắt MEP × tường/sàn", true, "Sleeve", "Sleeves")
                .Field("sleeveFamilyName", "family sleeve").Field("clearanceMm", "khe hở").Field("dryRun", "xem trước")
                .Words("sleeve", "lỗ chờ", "đặt sleeve", "opening"),
            new CommandDescriptor("ElevationTag", Revit, "Điền cao độ đáy/đỉnh/tim vào tham số MEP", true, "SetElev")
                .Field("dryRun", "xem trước")
                .Words("cao độ", "elevation", "gán cao độ"),
            new CommandDescriptor("HangerAuto", Revit, "Đặt hanger theo khoảng cách chuẩn", true, "Hanger", "Hangers")
                .Field("hangerFamilyName", "family hanger").Field("spacingMm", "khoảng cách").Field("dryRun", "xem trước")
                .Words("hanger", "giá đỡ", "ty treo", "support"),
            new CommandDescriptor("PipeSplitter", Revit, "Chia ống/duct theo chiều dài cây", true, "PipeSplit", "SplitPipes")
                .Field("maxSegmentMm", "chiều dài tối đa").Field("dryRun", "xem trước")
                .Words("chia ống", "cắt ống", "split pipe", "chia đoạn"),
            new CommandDescriptor("ConnectorChecker", Revit, "Liệt kê connector MEP hở", false, "ConnectorCheck", "CheckConnectors")
                .Field("categories", "danh sách category (rỗng = mọi category MEP)").Field("domains", "Piping/Hvac/Electrical (rỗng = tất cả)")
                .Field("create3dView", "true = GHI một 3D view khoanh vùng vào mô hình (thao tác ghi duy nhất của lệnh, mặc định false)")
                .Field("viewName", "tên 3D view").Field("dryRun", "xem trước: không tạo view")
                .Words("connector hở", "open connector", "kiểm tra connector"),
            new CommandDescriptor("RouteFromLines", Revit, "Routing mức A: dựng duct/pipe/tray từ model line vẽ tay", true, "Routing", "RouteA")
                .Field("lineStyleName", "line style tuyến").Field("elementType", "Duct/Pipe/CableTray/Conduit").Field("typeName", "type").Field("systemType", "hệ")
                .Field("sizeMm", "{width,height} hoặc {diameter}").Field("offsetMm", "cao độ").Field("dryRun", "xem trước")
                .Words("routing", "dựng tuyến", "đi ống theo line", "dựng duct", "dựng ống"),
            new CommandDescriptor("DevicePlacement", Revit, "Routing mức B: rải thiết bị đầu cuối theo phòng", true, "RouteB", "PlaceDevices")
                .Field("deviceFamily", "family thiết bị").Field("roomFilter", "{levelName, nameContains}").Field("pattern", "{spacingXMm, spacingYMm, marginMm}").Field("dryRun", "xem trước")
                .Words("rải sprinkler", "rải miệng gió", "đặt thiết bị theo phòng", "sprinkler", "diffuser"),
            new CommandDescriptor("SizingProposal", Revit, "Đề xuất kích thước duct/pipe theo lưu lượng → CSV", false, "Sizing")
                .Field("outputPath", "file CSV").Field("maxPaPerM", "ma sát Pa/m").Field("maxDuctVelocityMs", "vận tốc gió tối đa m/s").Field("maxPipeVelocityMs", "vận tốc nước tối đa m/s")
                .Words("sizing", "tính kích thước", "chọn size ống", "chọn size duct"),
            new CommandDescriptor("ApplySizing", Revit, "Áp kích thước từ CSV đã duyệt", true)
                .Field("inputPath", "file CSV").Field("dryRun", "xem trước")
                .Words("áp size", "apply sizing"),
            new CommandDescriptor("SystemColor", Revit, "Tạo filter + tô màu theo hệ trong view template", true, "SystemFilters")
                .Field("colors", "{tên hệ: #RRGGBB}").Field("viewTemplateName", "view template").Field("dryRun", "xem trước")
                .Words("tô màu hệ", "filter theo hệ", "màu hệ thống"),
            new CommandDescriptor("SystemName", Revit, "Đặt System Name theo quy tắc {Discipline}-{Abbr}-{Zone}-{N}", true, "SystemNaming")
                .Field("discipline", "MEC/PLB/ELE").Field("zone", "khu").Field("dryRun", "xem trước")
                .Words("đặt tên hệ", "system name", "tên hệ thống"),
            new CommandDescriptor("FlowNumbering", Revit, "Đánh số thiết bị theo thứ tự dòng chảy từ nguồn", true)
                .Field("sourceElementId", "phần tử nguồn").Field("parameterName", "tham số").Field("prefix", "tiền tố").Field("dryRun", "xem trước")
                .Words("đánh số theo tuyến", "flow numbering", "đánh số theo dòng chảy"),

            // ── Revit — dự án & hồ sơ ───────────────────────────────────────
            new CommandDescriptor("ProjectFromTemplate", Revit, "Tạo file mới từ template, bật workshare, tạo workset", true)
                .Field("templatePath", ".rte").Field("outputPath", ".rvt (token {projectCode} {discipline} {revitVersion})").Field("worksets", "danh sách")
                .Words("tạo file từ template", "khởi tạo file", "new project"),
            new CommandDescriptor("TransferStandards", Revit, "Chuyển view template, filter, line style… từ file chuẩn", true)
                .Field("sourcePath", "file chuẩn .rvt").Field("categories", "ViewTemplates/Filters/LineStyles/ObjectStyles/Materials").Field("dryRun", "xem trước")
                .Words("transfer standards", "chuyển chuẩn", "copy view template"),
            new CommandDescriptor("GridFromCsv", Revit, "Tạo trục/level từ CSV (kể cả CSV trích từ bản CAD)", true, "GridFromCad")
                .Field("gridCsvPath", "Name,X1,Y1,X2,Y2").Field("levelCsvPath", "Name,Elevation").Field("dryRun", "xem trước")
                .Words("trục từ cad", "trục từ excel", "grid from csv", "level từ excel"),
            new CommandDescriptor("SheetBatchCreate", Revit, "Tạo sheet hàng loạt từ CSV và đặt view", true, "CreateSheets")
                .Field("inputPath", "SheetNumber,SheetName,TitleBlockType,ViewsToPlace").Field("dryRun", "xem trước")
                .Words("tạo sheet", "sheet hàng loạt", "create sheets"),

            // ── Revit — hồ sơ & style (giai đoạn 7, học từ pyRevit/DiRoots/Ideate/Colour Splasher) ──
            new CommandDescriptor("SheetRename", Revit, "Đổi số/tên sheet hoặc view theo mẫu token + regex, chống trùng", true, "RenameSheets", "RenameViews")
                .Field("target", "Sheets | Views").Field("numberPattern", "mẫu số, ví dụ A-{Level}-{n:00}").Field("namePattern", "mẫu tên").Field("find", "regex tìm").Field("replace", "thay").Field("filterContains", "lọc số/tên chứa").Field("dryRun", "xem trước")
                .Words("đổi tên sheet", "đổi số sheet", "rename sheet", "đổi tên view", "đánh số sheet"),
            new CommandDescriptor("RevisionOnSheets", Revit, "Gán hoặc bỏ một revision trên nhiều sheet", true, "SetRevisions")
                .Field("revisionSequence", "số thứ tự revision").Field("sheetNumberContains", "lọc sheet").Field("remove", "bỏ thay vì gán").Field("dryRun", "xem trước")
                .Words("revision", "gán revision", "phát hành", "set revision"),
            new CommandDescriptor("StylePurge", Revit, "Liệt kê và xoá style không được tham chiếu: view template, filter, line/fill pattern, text/dim type, material", true, "PurgeStyles", "Wipe")
                .Field("kinds", "ViewTemplates/Filters/LinePatterns/FillPatterns/TextTypes/DimensionTypes/Materials").Field("keepNameContains", "giữ lại").Field("keepIfUncertain", "không xoá nhóm nào kiểm tham chiếu bị lỗi (mặc định bật)").Field("dryRun", "xem trước")
                .Words("purge style", "xoá view template thừa", "xoá filter thừa", "dọn style", "wipe"),
            new CommandDescriptor("ColorByParameter", Revit, "Tô màu phần tử trong view theo giá trị tham số (palette tự sinh) + chú giải CSV", true, "ColorSplasher", "ColourSplasher")
                .Field("viewName", "view (rỗng = view đang mở)").Field("categories", "category").Field("parameterName", "tham số").Field("legendCsvPath", "chú giải").Field("reset", "xoá override").Field("dryRun", "xem trước")
                .Words("tô màu theo tham số", "color splasher", "màu theo", "tô màu phần tử"),
            new CommandDescriptor("FamilyAudit", Revit, "Kiểm kê family/type (instance, in-place, không dùng) ra CSV; đổi tên theo mẫu", true, "FamilyReviser")
                .Field("outputPath", "CSV kiểm kê").Field("renamePattern", "mẫu tên family, ví dụ DHCB_{Category:upper}_{Name}").Field("categories", "lọc").Field("dryRun", "xem trước")
                .Words("kiểm kê family", "đổi tên family", "family audit", "family reviser", "family không dùng"),
            new CommandDescriptor("WarningsExport", Revit, "Xuất toàn bộ warning ra CSV kèm ElementId/category, đếm theo loại", false, "ExportWarnings")
                .Field("outputPath", "file CSV")
                .Words("xuất warning", "danh sách warning", "warning csv", "review warnings"),

            // ── Revit — P2 giai đoạn 7 (Naviate/Victaulic/eVolve/pyRevit/SheetLink) ──
            new CommandDescriptor("SlopePipes", Revit, "Đặt hoặc kiểm tra dốc ống thoát nước theo % hoặc bảng tối thiểu theo DN", true, "PipeSlope")
                .Field("slopePercent", "% (rỗng = theo DN)").Field("systemContains", "lọc hệ").Field("levelName", "tầng").Field("lowerEnd", "End|Start").Field("checkOnly", "chỉ kiểm").Field("dryRun", "xem trước")
                .Words("ống dốc", "độ dốc", "slope pipe", "đặt dốc", "kiểm tra dốc"),
            new CommandDescriptor("PipeKick", Revit, "Kick/jog một ống bằng hai cút 45° hoặc 90° (dịch ngang/lên/xuống)", true, "Kick90", "Jog")
                .Field("elementId", "Id ống").Field("offsetMm", "khoảng dịch").Field("offsetDirection", "Up|Down|Left|Right").Field("elbowAngleDeg", "45|90").Field("distanceFromStartMm", "vị trí").Field("dryRun", "xem trước")
                .Words("kick", "kick-90", "jog ống", "né ống", "dịch ống"),
            new CommandDescriptor("SystemBom", Revit, "BOM theo hệ/spool: ống theo mét + số cây, fitting/phụ kiện theo số lượng → CSV", false, "Bom", "Spool")
                .Field("outputPath", "file CSV").Field("systemContains", "lọc hệ").Field("spoolParameter", "tham số spool").Field("stockLengthMm", "chiều dài cây")
                .Words("bom", "bảng khối lượng", "spool", "khối lượng ống", "bill of material"),
            new CommandDescriptor("AutoRoute", Revit, "Routing mức C: A* né chướng ngại giữa 2 điểm → model line → (tuỳ chọn) dựng duct/pipe", true, "RouteC", "PathFind")
                .Field("startMm", "{x,y,z}").Field("endMm", "{x,y,z}").Field("searchMarginMm", "biên hộp tìm").Field("obstacleCategories", "chướng ngại").Field("lineStyleName", "line style").Field("buildRoute", "dựng luôn").Field("dryRun", "xem trước")
                .Words("tự động tìm tuyến", "auto route", "pathfinding", "né va chạm", "routing tự động"),
            new CommandDescriptor("ScheduleExport", Revit, "Xuất schedule ra CSV đúng cột/hàng đang hiển thị", false, "ExportSchedules")
                .Field("outputFolder", "thư mục").Field("nameContains", "lọc tên").Field("names", "danh sách tên")
                .Words("xuất schedule", "schedule csv", "export schedule", "bảng thống kê ra excel"),
            new CommandDescriptor("ViewportCopy", Revit, "Copy legend/schedule từ một sheet sang nhiều sheet, cùng vị trí, ghim lại", true, "CopyViewports")
                .Field("sourceSheetNumber", "sheet nguồn").Field("targetSheetNumbers", "sheet đích").Field("targetSheetContains", "lọc đích").Field("pinAfterCopy", "ghim").Field("dryRun", "xem trước")
                .Words("copy viewport", "copy legend", "chép legend sang sheet", "copy schedule sang sheet"),

            // ── Revit — kiểm tra (cấp 2) ────────────────────────────────────
            new CommandDescriptor("ParameterRuleCheck", Revit, "Kiểm tra tham số thiếu / sai quy tắc đặt tên → HTML", false, "RuleCheck")
                .Field("rulesPath", "file JSON quy tắc").Field("outputPath", "file HTML").Field("create3dView", "true = GHI một 3D view isolate phần tử vi phạm (chỉ khi dryRun=false)").Field("dryRun", "xem trước: không tạo view")
                .Words("kiểm tra tham số", "rule check", "kiểm tra đặt tên"),
            new CommandDescriptor("ClashDetection", Revit, "Va chạm nội bộ giữa hai nhóm category → HTML + 3D view", false, "Clash")
                .Field("categoriesA", "nhóm A").Field("categoriesB", "nhóm B").Field("outputPath", "file HTML").Field("acceptedPath", "clash-accepted.json")
                .Field("includeLinkedModels", "xét cả model liên kết cho nhóm B (mặc định bật)", FieldKind.Bool).Field("create3dView", "true = GHI một 3D view isolate phần tử va chạm (chỉ khi dryRun=false)").Field("dryRun", "xem trước: không tạo view")
                .Words("clash", "va chạm", "kiểm tra va chạm"),

            // ── Revit — AI (offline) ────────────────────────────────────────
            new CommandDescriptor("CadLayerMap", Revit, "AI offline: gợi ý map layer CAD → Revit type, ghi CSV để duyệt", false, "LayerMap")
                .Field("layersCsvPath", "CSV từ LayerExport").Field("outputPath", "CSV mapping").Field("useOllama", "dùng model local nếu có")
                .Words("map layer", "ánh xạ layer", "layer sang type"),
            new CommandDescriptor("DictionaryLearn", Revit, "AI offline: soi tên tham số thật của dự án, đề xuất/ghi dictionary.json", false, "HocTuDien")
                .Field("categories", "category cần soi (rỗng = bộ mặc định)").Field("sampleSize", "số phần tử lấy mẫu mỗi category")
                .Field("outputPath", "file dictionary.json").Field("reportPath", "CSV để duyệt")
                .Field("acceptLowConfidence", "nhận cả dòng cần xem").Field("dryRun", "xem trước")
                .Words("học từ điển", "tên tham số dự án", "map tham số", "dictionary"),
            new CommandDescriptor("SpecToConfig", Revit, "AI offline: trích tầng/cao độ/hệ từ file thuyết minh → config ProjectInit", false)
                .Field("inputPath", "file .txt/.md thuyết minh").Field("outputPath", "JSON config")
                .Words("đọc thuyết minh", "spec sang config", "trích cao độ"),

            // ── Revit — công cụ nội bộ (không lên Ribbon, không chào ra /tools) ──
            new CommandDescriptor("UsageReport", Revit, "Đọc log của máy này thành số liệu: lệnh nào dùng thật, lệnh nào bấm rồi bỏ", false)
                .Field("logFolder", "thư mục log").Field("outputPath", "báo cáo Markdown").Field("csvPath", "CSV để gộp nhiều máy")
                .Field("days", "chỉ tính N ngày gần nhất").Field("app", "Revit / AutoCAD")
                .Words("số liệu sử dụng", "lệnh nào hay dùng", "usage")
                .Tooling(),
            new CommandDescriptor("RunTests", Revit, "Chạy bộ kiểm thử bên trong Revit trên model mẫu, ghi TRX + Markdown", false)
                .Field("suitePath", "file JSON mô tả bộ ca kiểm").Field("outputFolder", "nơi ghi báo cáo")
                .Field("onlyCommands", "chỉ chạy các lệnh này").Field("allowWrites", "cho phép ca allowWrite ghi thật")
                .Tooling(),

            // ── AutoCAD ─────────────────────────────────────────────────────
            new CommandDescriptor("LayerExport", AutoCad, "Xuất layer ra CSV", false)
                .Field("outputPath", "file CSV").Field("filterNameContains", "lọc tên")
                .Words("xuất layer", "export layer"),
            new CommandDescriptor("LayerImport", AutoCad, "Nhập layer từ CSV", true)
                .Field("inputPath", "file CSV").Field("createMissing", "tạo layer thiếu").Field("dryRun", "xem trước")
                .Words("nhập layer", "import layer"),
            new CommandDescriptor("DrawingCleanup", AutoCad, "Dọn layer rỗng, block/linetype/textstyle/dimstyle/regapp không dùng", true, "Cleanup", "Purge")
                .Field("purgeUnusedTextStyles", "text style").Field("purgeUnusedDimStyles", "dim style").Field("purgeRegApps", "regapp").Field("dryRun", "xem trước")
                .Words("dọn bản vẽ", "purge", "cleanup drawing", "dọn layer"),
            new CommandDescriptor("AutoNumbering", AutoCad, "Đánh số Block Reference theo attribute", true, "AutoNumber")
                .Field("blockName", "tên block").Field("attributeTag", "tag").Field("prefix", "tiền tố").Field("dryRun", "xem trước")
                .Words("đánh số block", "numbering block"),
            new CommandDescriptor("AttributeExport", AutoCad, "Xuất attribute của block ra CSV", false)
                .Field("blockName", "tên block (rỗng = mọi block có attribute)").Field("outputPath", "file CSV")
                .Words("xuất attribute", "export attribute", "xuất thuộc tính block"),
            new CommandDescriptor("AttributeImport", AutoCad, "Nhập CSV ghi ngược attribute vào block", true)
                .Field("inputPath", "file CSV").Field("dryRun", "xem trước")
                .Words("nhập attribute", "import attribute"),
            new CommandDescriptor("TextReplace", AutoCad, "Tìm/thay văn bản trong Text, MText, Attribute (regex)", true, "FindReplace")
                .Field("find", "chuỗi/regex").Field("replace", "thay bằng").Field("useRegex", "regex").Field("dryRun", "xem trước")
                .Words("thay text", "find replace", "đổi chữ", "sửa text hàng loạt"),
            new CommandDescriptor("LayerStandardCheck", AutoCad, "Kiểm tra layer theo bộ quy tắc đặt tên → HTML", false, "LayerCheck")
                .Field("rulesPath", "file JSON quy tắc").Field("outputPath", "file HTML")
                .Words("kiểm tra layer", "chuẩn layer", "layer standard"),
            new CommandDescriptor("GridExtract", AutoCad, "Trích trục từ layer AXIS ra CSV cho Revit GridFromCsv", false, "ExtractGrids")
                .Field("gridLayer", "layer trục (mặc định AXIS)").Field("outputPath", "file CSV")
                .Words("trích trục", "lấy trục từ cad", "extract grid"),
            new CommandDescriptor("XrefAudit", AutoCad, "Liệt kê xref, đường dẫn thiếu, xref chưa load", false)
                .Field("outputPath", "file CSV (tuỳ chọn)")
                .Words("xref", "kiểm tra xref", "xref thiếu"),
            new CommandDescriptor("LayerTranslate", AutoCad, "Map layer cũ → layer chuẩn theo CSV (đổi entity, merge, đặt thuộc tính) — như LAYTRANS", true, "LayTrans")
                .Field("mapCsvPath", "Source,Target,Color,Linetype,Lineweight,Plottable").Field("deleteEmptySource", "xoá layer nguồn rỗng").Field("dryRun", "xem trước")
                .Words("layer translate", "laytrans", "đổi layer theo chuẩn", "map layer chuẩn"),
            new CommandDescriptor("DrawingCompare", AutoCad, "So bản vẽ hiện tại với DWG khác ở mức layer (đếm entity theo layer) → CSV/HTML", false, "Compare")
                .Field("otherPath", "DWG so sánh").Field("outputPath", "CSV hoặc HTML").Field("moveToleranceMm", "dung sai dời")
                .Words("so sánh bản vẽ", "drawing compare", "khác nhau giữa hai bản"),
            new CommandDescriptor("BlockQuantity", AutoCad, "Đếm block theo tên (và nhóm theo attribute) → CSV BOM", false, "BlockCount", "Bom")
                .Field("outputPath", "file CSV").Field("groupByAttribute", "tag attribute để nhóm").Field("blockNameContains", "lọc")
                .Words("đếm block", "thống kê block", "bom block", "block quantity"),
            new CommandDescriptor("AttributeIncrement", AutoCad, "Gán attribute tăng dần theo mẫu {n:000} theo thứ tự vị trí (Lee Mac BATTE)", true, "BatchAttribute")
                .Field("blockName", "tên block").Field("attributeTag", "tag").Field("pattern", "mẫu, ví dụ P-{n:000}").Field("startNumber", "bắt đầu").Field("dryRun", "xem trước")
                .Words("attribute tăng dần", "đánh số attribute", "batte", "increment attribute"),
            new CommandDescriptor("CadLayerMap", AutoCad, "AI offline: gợi ý map layer → Revit type từ danh sách type", false, "LayerMap")
                .Field("revitTypesPath", "file .txt danh sách type").Field("outputPath", "CSV mapping").Field("useOllama", "dùng model local nếu có")
                .Words("map layer", "ánh xạ layer"),

            // ── AutoCAD — công cụ nội bộ (không lên lệnh người dùng, không chào ra /tools) ──
            new CommandDescriptor("RunTests", AutoCad, "Chạy bộ kiểm thử bên trong AutoCAD trên bản vẽ mẫu, ghi TRX + Markdown", false)
                .Field("suitePath", "file JSON mô tả bộ ca kiểm").Field("outputFolder", "nơi ghi báo cáo")
                .Field("onlyCommands", "chỉ chạy các lệnh này").Field("allowWrites", "cho phép ca allowWrite ghi thật")
                .Tooling(),
        };

        /// <summary>Lệnh dùng được của một nền tảng — chỉ những lệnh đã có mã nguồn trong Core.</summary>
        public static IEnumerable<CommandDescriptor> For(string app) =>
            AllFor(app).Where(c => c.Implemented && !c.Internal);

        /// <summary>Cả lệnh đã có lẫn lệnh mới chỉ có đặc tả (<see cref="CommandDescriptor.Implemented"/> = false).</summary>
        public static IEnumerable<CommandDescriptor> AllFor(string app) =>
            All.Where(c => string.Equals(c.App, app, StringComparison.OrdinalIgnoreCase));

        /// <summary>Lệnh đã chốt đặc tả nhưng chưa viết — dùng cho báo cáo hiện trạng.</summary>
        public static IReadOnlyList<string> PendingNames(string app) =>
            AllFor(app).Where(c => !c.Implemented).Select(c => c.Name).Distinct().ToList();

        /// <summary>
        /// Tra theo tên hoặc bí danh, kể cả lệnh chưa triển khai — để bảng dispatch trả về
        /// "lệnh không xác định" kèm danh sách hợp lệ thay vì im lặng bỏ qua.
        /// </summary>
        public static CommandDescriptor? Find(string app, string commandOrAlias) => AllFor(app).FirstOrDefault(c => c.Matches(commandOrAlias));

        /// <summary>
        /// Trường bool mà lớp Config THẬT mặc định <c>true</c> — form động dùng để tick sẵn checkbox khi
        /// người dùng chưa lưu giá trị. Mọi trường bool khác mặc định false. (Danh sách này đối chiếu tay
        /// với các <c>= true</c> trong Core; <c>dryRun</c> do form tự điều khiển nên không cần.)
        /// </summary>
        private static readonly HashSet<string> BoolTrueByDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dryRun",
            "keepIfUncertain",
            "includeLinkedModels",
            "pinAfterCopy",
            "fillSurfaces",
            "findIsRegex",
            "includeHeader",
            "skipExisting",
            "createFloorPlan",
            "createCentral",
            "closeAfterSave",
            "removeUnplacedViews",
            "removeEmptySheets",
            "allowVertical",
            "keepDuctHeight",
            "onlyDefaultNames",
            "depthFirst",
            "skipFittings",
            "checkWarnings",
            "checkUnplacedViews",
            "checkOpenConnectors",
            "checkInPlaceFamilies",
            "checkFileSizeMb",
            "removeEmptyLayers",
            "purgeUnusedBlocks",
            "purgeUnusedLinetypes",
        };

        /// <summary>Giá trị mặc định của một trường bool theo lớp Config thật (false nếu không biết).</summary>
        public static bool DefaultBool(string fieldName) => BoolTrueByDefault.Contains(fieldName ?? string.Empty);

        /// <summary>Danh sách tên chuẩn (phân biệt hoa thường theo Core) của một nền tảng.</summary>
        public static IReadOnlyList<string> Names(string app) => For(app).Select(c => c.Name).Distinct().ToList();

        /// <summary>Payload cho <c>GET /tools</c> và cho MCP <c>tools/list</c>.</summary>
        public static object Describe(string app)
        {
            return new
            {
                app,
                tools = For(app).Select(c => new
                {
                    name = c.Name,
                    aliases = c.Aliases,
                    description = c.Description,
                    writesModel = c.WritesModel,
                    inputSchema = new
                    {
                        type = "object",
                        properties = c.Fields.ToDictionary(
                            f => f.Name,
                            f => (object)new { type = JsonTypeOf(f.Kind), description = f.Description }),
                    },
                }).ToList(),
            };
        }

        /// <summary>Kiểu JSON Schema tương ứng — model local bám schema tốt hơn khi biết đâu là số/bool/mảng.</summary>
        private static string JsonTypeOf(FieldKind kind)
        {
            switch (kind)
            {
                case FieldKind.Number: return "number";
                case FieldKind.Bool: return "boolean";
                case FieldKind.TextList:
                case FieldKind.Category: return "array";
                default: return "string";
            }
        }
    }
}
