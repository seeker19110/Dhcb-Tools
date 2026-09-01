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

        /// <summary>Tên trường config → mô tả ngắn (dùng cho MCP inputSchema và cho intent parser).</summary>
        public Dictionary<string, string> ConfigFields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Từ khoá tiếng Việt/Anh để nhận dạng ý định.</summary>
        public List<string> Keywords { get; } = new List<string>();

        public CommandDescriptor Field(string name, string description)
        {
            ConfigFields[name] = description;
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
                .Field("outputFolder", "thư mục").Field("formats", "Pdf/Dwg/Ifc/Nwc").Field("sheetNumbers", "lọc sheet")
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
                .Field("familyPaths", "đường dẫn .rfa").Field("dryRun", "xem trước")
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
                .Field("create3dView", "tạo view khoanh vùng")
                .Words("connector hở", "open connector", "kiểm tra connector"),
            new CommandDescriptor("RouteFromLines", Revit, "Routing mức A: dựng duct/pipe/tray từ model line vẽ tay", true, "Routing", "RouteA")
                .Field("lineStyleName", "line style tuyến").Field("elementType", "Duct/Pipe/CableTray/Conduit").Field("typeName", "type").Field("systemType", "hệ")
                .Field("sizeMm", "{width,height} hoặc {diameter}").Field("offsetMm", "cao độ").Field("dryRun", "xem trước")
                .Words("routing", "dựng tuyến", "đi ống theo line", "dựng duct", "dựng ống"),
            new CommandDescriptor("DevicePlacement", Revit, "Routing mức B: rải thiết bị đầu cuối theo phòng", true, "RouteB", "PlaceDevices")
                .Field("deviceFamily", "family thiết bị").Field("roomFilter", "{levelName, nameContains}").Field("pattern", "{spacingXMm, spacingYMm, marginMm}").Field("dryRun", "xem trước")
                .Words("rải sprinkler", "rải miệng gió", "đặt thiết bị theo phòng", "sprinkler", "diffuser"),
            new CommandDescriptor("SizingProposal", Revit, "Đề xuất kích thước duct/pipe theo lưu lượng → CSV", false, "Sizing")
                .Field("outputPath", "file CSV").Field("maxPaPerM", "ma sát Pa/m").Field("maxVelocityMs", "vận tốc tối đa")
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

            // ── Revit — kiểm tra (cấp 2) ────────────────────────────────────
            new CommandDescriptor("ParameterRuleCheck", Revit, "Kiểm tra tham số thiếu / sai quy tắc đặt tên → HTML", false, "RuleCheck")
                .Field("rulesPath", "file JSON quy tắc").Field("outputPath", "file HTML")
                .Words("kiểm tra tham số", "rule check", "kiểm tra đặt tên"),
            new CommandDescriptor("ClashDetection", Revit, "Va chạm nội bộ giữa hai nhóm category → HTML + 3D view", false, "Clash")
                .Field("categoriesA", "nhóm A").Field("categoriesB", "nhóm B").Field("outputPath", "file HTML").Field("acceptedPath", "clash-accepted.json")
                .Words("clash", "va chạm", "kiểm tra va chạm"),

            // ── Revit — AI (offline) ────────────────────────────────────────
            new CommandDescriptor("CadLayerMap", Revit, "AI offline: gợi ý map layer CAD → Revit type, ghi CSV để duyệt", false, "LayerMap")
                .Field("layersCsvPath", "CSV từ LayerExport").Field("outputPath", "CSV mapping").Field("useOllama", "dùng model local nếu có")
                .Words("map layer", "ánh xạ layer", "layer sang type"),
            new CommandDescriptor("SpecToConfig", Revit, "AI offline: trích tầng/cao độ/hệ từ file thuyết minh → config ProjectInit", false)
                .Field("inputPath", "file .txt/.md thuyết minh").Field("outputPath", "JSON config")
                .Words("đọc thuyết minh", "spec sang config", "trích cao độ"),

            // ── AutoCAD ─────────────────────────────────────────────────────
            new CommandDescriptor("LayerExport", AutoCad, "Xuất layer ra CSV", false)
                .Field("outputPath", "file CSV").Field("filterNameContains", "lọc tên")
                .Words("xuất layer", "export layer"),
            new CommandDescriptor("LayerImport", AutoCad, "Nhập layer từ CSV", true)
                .Field("inputPath", "file CSV").Field("createMissing", "tạo layer thiếu").Field("dryRun", "xem trước")
                .Words("nhập layer", "import layer"),
            new CommandDescriptor("DrawingCleanup", AutoCad, "Dọn layer rỗng, block/linetype không dùng", true, "Cleanup")
                .Field("dryRun", "xem trước")
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
            new CommandDescriptor("CadLayerMap", AutoCad, "AI offline: gợi ý map layer → Revit type từ danh sách type", false, "LayerMap")
                .Field("revitTypesPath", "file .txt danh sách type").Field("outputPath", "CSV mapping").Field("useOllama", "dùng model local nếu có")
                .Words("map layer", "ánh xạ layer"),
        };

        public static IEnumerable<CommandDescriptor> For(string app) => All.Where(c => string.Equals(c.App, app, StringComparison.OrdinalIgnoreCase));

        public static CommandDescriptor? Find(string app, string commandOrAlias) => For(app).FirstOrDefault(c => c.Matches(commandOrAlias));

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
                        properties = c.ConfigFields.ToDictionary(k => k.Key, k => (object)new { description = k.Value }),
                    },
                }).ToList(),
            };
        }
    }
}
