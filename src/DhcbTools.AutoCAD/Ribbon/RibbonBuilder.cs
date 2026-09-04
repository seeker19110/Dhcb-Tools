using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Windows;

namespace DhcbTools.AutoCAD.Ribbon;

/// <summary>
/// Dựng Ribbon tab "DHCB Tools" cho AutoCAD — tương đương <c>DhcbTools.Revit.App</c>, gọi lại đúng
/// các lệnh <c>[CommandMethod]</c> đã có trong <see cref="Commands.DhcbCommands"/> và
/// <see cref="Commands.BridgeCommands"/> qua macro, không thêm logic mới.
///
/// <c>ComponentManager.Ribbon</c> có thể vẫn là null lúc <c>Initialize()</c> chạy (Ribbon workspace
/// chưa dựng xong khi add-in nạp qua bundle) — nên phải chờ sự kiện <see cref="ComponentManager.ItemInitialized"/>
/// hoặc dựng ngay nếu Ribbon đã sẵn có.
///
/// Bố cục: panel chia theo ĐỐI TƯỢNG người dùng đang làm việc (layer → block/attribute → nội dung
/// bản vẽ → trao đổi với Revit), trong mỗi panel xếp theo trình tự dùng thật (xuất → nhập → kiểm →
/// chuyển đổi). Mọi nút cùng một cỡ và cùng một cỡ icon, xếp ba hàng mỗi cột: mắt quét theo cột
/// nhanh hơn hẳn so với trộn nút to nút nhỏ, và panel không phình ngang.
/// </summary>
internal static class RibbonBuilder
{
    private const string TabId = "DHCB_TOOLS_TAB";
    private const string TabTitle = "DHCB Tools";

    public static void EnsureBuilt()
    {
        if (ComponentManager.Ribbon is not null)
        {
            Build();
            return;
        }

        ComponentManager.ItemInitialized += OnItemInitialized;
    }

    private static void OnItemInitialized(object? sender, RibbonItemEventArgs e)
    {
        if (ComponentManager.Ribbon is null)
        {
            return;
        }

        ComponentManager.ItemInitialized -= OnItemInitialized;

        // Handler này chạy NGOÀI try/catch của App.Initialize; ném ra ở đây là AutoCAD tắt ngay với
        // "FATAL ERROR: Unhandled e0434352h" (đã gặp thật). Ribbon hỏng thì mất nút, không được mất
        // cả phiên làm việc — lệnh gõ tay vẫn dùng được.
        try
        {
            Build();
        }
        catch (System.Exception ex)
        {
            Shared.Hosting.DhcbLog.Error("AutoCAD", "dựng Ribbon", ex);
        }
    }

    private static void Build()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon is null || ribbon.Tabs.Any(t => t.Id == TabId))
        {
            return;
        }

        var tab = new RibbonTab { Id = TabId, Title = TabTitle, Name = TabTitle };
        ribbon.Tabs.Add(tab);

        // ── Layer: xuất → nhập → kiểm chuẩn → đổi theo bảng → gợi ý map sang Revit ──
        Panel(tab, "Layer",
            ("DHCB_LAYER_EXPORT", "Xuất layer ra CSV", "LX", RibbonIcons.Layer,
                "Xuất bảng layer của bản vẽ ra file CSV để chỉnh trong Excel."),
            ("DHCB_LAYER_IMPORT", "Nhập layer từ CSV", "LN", RibbonIcons.Layer,
                "Đọc lại CSV đã chỉnh và ghi thuộc tính vào layer."),
            ("DHCB_LAYER_CHECK", "Kiểm layer theo chuẩn", "KL", RibbonIcons.Layer,
                "Đối chiếu layer với bộ quy tắc trong layer-rules.json, ra báo cáo HTML."),
            ("DHCB_LAYER_TRANSLATE", "Đổi layer theo bảng", "ĐL", RibbonIcons.Layer,
                "Đổi layer của entity theo bảng chuẩn (kiểu LAYTRANS)."),
            ("DHCB_LAYER_MAP", "Gợi ý map layer → type", "AI", RibbonIcons.Layer,
                "Gợi ý map layer CAD sang type Revit bằng lớp AI chạy offline."));

        // ── Block & attribute: xuất → nhập → đánh số → tăng số → đếm ──
        Panel(tab, "Block & attribute",
            ("DHCB_ATTR_EXPORT", "Xuất attribute ra CSV", "AX", RibbonIcons.Block,
                "Xuất attribute của block ra file CSV."),
            ("DHCB_ATTR_IMPORT", "Nhập attribute từ CSV", "AN", RibbonIcons.Block,
                "Đọc lại CSV đã chỉnh và ghi vào attribute của block."),
            ("DHCB_AUTONUMBER", "Đánh số tự động", "ĐS", RibbonIcons.Block,
                "Đánh số attribute của block hàng loạt theo vị trí."),
            ("DHCB_ATTR_INCREMENT", "Tăng số attribute", "+1", RibbonIcons.Block,
                "Tăng dần giá trị một attribute cho các block đã chọn."),
            ("DHCB_BLOCK_QUANTITY", "Đếm block ra CSV", "ĐB", RibbonIcons.Block,
                "Đếm số lượng block theo nhóm, xuất CSV."));

        // ── Nội dung bản vẽ: dọn → sửa text → soi xref → so bản vẽ ──
        Panel(tab, "Bản vẽ",
            ("DHCB_CLEANUP", "Dọn bản vẽ (purge sâu)", "DD", RibbonIcons.Drawing,
                "Xoá layer, block, style thừa — an toàn với CLAYER, linetype của layer và xref."),
            ("DHCB_TEXT_REPLACE", "Thay văn bản", "TT", RibbonIcons.Drawing,
                "Tìm và thay text/mtext hàng loạt trong bản vẽ."),
            ("DHCB_XREF_AUDIT", "Kiểm kê Xref", "XR", RibbonIcons.Drawing,
                "Liệt kê toàn bộ Xref của bản vẽ và tình trạng liên kết."),
            ("DHCB_DRAWING_COMPARE", "So sánh bản vẽ", "SS", RibbonIcons.Drawing,
                "So layer giữa bản vẽ hiện tại và một file khác."));

        // ── Nối sang Revit / agent ──
        Panel(tab, "Revit & agent",
            ("DHCB_GRID_EXTRACT", "Trích trục ra CSV", "TR", RibbonIcons.Bridge,
                "Trích trục từ layer AXIS ra CSV — nạp sang Revit bằng lệnh GridFromCsv."),
            ("DHCB_BRIDGE", "Trạng thái HTTP Bridge", "BR", RibbonIcons.Bridge,
                "Xem cổng và đường dẫn token của HTTP Bridge dùng cho agent AI."));
    }

    /// <summary>Xếp các lệnh thành cột ba hàng — hết ba nút thì tự sang cột mới.</summary>
    private static void Panel(
        RibbonTab tab, string title,
        params (string Command, string Text, string Glyph, Color Color, string Tip)[] items)
    {
        const int rowsPerColumn = 3;

        var source = new RibbonPanelSource { Title = title };
        tab.Panels.Add(new RibbonPanel { Source = source });

        // Mỗi cột là một RibbonRowPanel riêng, các nút trong cột ngăn nhau bằng RibbonRowBreak.
        // (RibbonPanelBreak KHÔNG hợp lệ bên trong RibbonRowPanel — nhét vào là AutoCAD sập.)
        for (var start = 0; start < items.Length; start += rowsPerColumn)
        {
            var column = new RibbonRowPanel();
            var end = System.Math.Min(start + rowsPerColumn, items.Length);

            for (var i = start; i < end; i++)
            {
                if (i > start)
                {
                    column.Items.Add(new RibbonRowBreak());
                }

                var it = items[i];
                column.Items.Add(Button(it.Command, it.Text, it.Glyph, it.Color, it.Tip));
            }

            source.Items.Add(column);
        }
    }

    private static RibbonButton Button(string command, string text, string glyph, Color color, string tip)
        => new()
        {
            Text = text,
            ShowText = true,
            ShowImage = true,
            Image = RibbonIcons.Create(glyph, color, RibbonIcons.Small),
            Orientation = Orientation.Horizontal,
            Size = RibbonItemSize.Standard,
            ToolTip = tip,
            CommandHandler = new AcadCommandHandler(),
            // "^C^C" là quy ước macro của menu CUI; SendStringToExecute gửi ký tự thô nên phải dùng
            // đúng ký tự ETX (Ctrl+C) để huỷ lệnh đang dở, nếu không AutoCAD nhận nguyên chữ "^C^C".
            CommandParameter = $"\x03\x03_{command} ",
        };
}
