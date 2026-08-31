using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

internal static class SampleTitleBar
{
    internal static Element Render(ref RenderContext ui, ReadOnlySpan<byte> utf8Title) =>
        Render(ref ui, ui.Text(utf8Title).FontSize(Px(13)).TextColor(Rgb(226, 232, 240)), null);

    internal static Element Render(
        ref RenderContext ui,
        ReadOnlySpan<byte> utf8Title,
        Element menuBar
    ) => Render(ref ui, ui.Text(utf8Title).FontSize(Px(13)).TextColor(Rgb(226, 232, 240)), menuBar);

    internal static Element Render(ref RenderContext ui, ReadOnlySpan<char> title) =>
        Render(ref ui, ui.Text(title).FontSize(Px(13)).TextColor(Rgb(226, 232, 240)), null);

    private static Element Render(ref RenderContext ui, Element title, Element? menuBar)
    {
        var windowsCaptionGlyphs = OperatingSystem.IsWindows();
        ReadOnlySpan<byte> minimizeGlyph = windowsCaptionGlyphs ? "\uE921"u8 : "−"u8;
        ReadOnlySpan<byte> maximizeGlyph = windowsCaptionGlyphs ? "\uE922"u8 : "□"u8;
        ReadOnlySpan<byte> closeGlyph = windowsCaptionGlyphs ? "\uE8BB"u8 : "×"u8;
        var glyphSize = windowsCaptionGlyphs ? 10 : 15;
        var titleRegion = ui.HStack(ui.Div().Width(Px(OperatingSystem.IsMacOS() ? 64 : 0)), title)
            .ItemsCenter()
            .Padding(Px(9))
            .Height(Percent(100))
            .WindowControlArea(WindowControlArea.Drag);
        var trailingDragRegion = ui.Div()
            .Grow()
            .Height(Percent(100))
            .WindowControlArea(WindowControlArea.Drag);
        var dragAndMenus = menuBar.HasValue
            ? ui.HStack(titleRegion, menuBar.Value, trailingDragRegion).Grow()
            : ui.HStack(titleRegion, trailingDragRegion).Grow();

        var titleBar = OperatingSystem.IsMacOS()
            ? ui.HStack(dragAndMenus)
            : ui.HStack(
                dragAndMenus,
                Button(
                    ref ui,
                    "titlebar-minimize"u8,
                    minimizeGlyph,
                    WindowControlArea.Minimize,
                    36,
                    glyphSize,
                    Rgb(30, 41, 59)
                ),
                Button(
                    ref ui,
                    "titlebar-maximize"u8,
                    maximizeGlyph,
                    WindowControlArea.Maximize,
                    36,
                    glyphSize,
                    Rgb(30, 41, 59)
                ),
                Button(
                    ref ui,
                    "titlebar-close"u8,
                    closeGlyph,
                    WindowControlArea.Close,
                    36,
                    glyphSize,
                    Rgb(232, 17, 32)
                )
            );
        return titleBar.Height(Px(38)).Width(Percent(100)).Background(Rgb(15, 23, 42));
    }

    private static Element Button(
        ref RenderContext ui,
        ReadOnlySpan<byte> id,
        ReadOnlySpan<byte> label,
        WindowControlArea area,
        float width,
        float fontSize,
        Color hoverBackground
    ) =>
        ui.Button(id, label)
            .WindowControlArea(area)
            .Width(Px(width))
            .Height(Percent(100))
            .ItemsCenter()
            .JustifyCenter()
            .FontSize(Px(fontSize))
            .Background(Rgb(15, 23, 42))
            .HoverBackground(hoverBackground)
            .BorderWidth(Px(0))
            .Radius(Px(0))
            .TextColor(Rgb(248, 250, 252));
}
