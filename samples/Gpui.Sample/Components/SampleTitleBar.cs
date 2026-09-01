using Gpui;
using static Gpui.Units;

internal static class SampleTitleBar
{
    internal static Element Render(ref RenderContext ui, ReadOnlySpan<byte> utf8Title) =>
        Render(
            ref ui,
            ui.Text(utf8Title)
                .FontSize(Px(ui.Theme.Typography.TitleBar))
                .TextColor(ui.Theme.Colors.TitleBarText),
            null
        );

    internal static Element Render(
        ref RenderContext ui,
        ReadOnlySpan<byte> utf8Title,
        Element? menuBar
    ) =>
        Render(
            ref ui,
            ui.Text(utf8Title)
                .FontSize(Px(ui.Theme.Typography.TitleBar))
                .TextColor(ui.Theme.Colors.TitleBarText),
            menuBar
        );

    internal static Element Render(ref RenderContext ui, ReadOnlySpan<char> title) =>
        Render(
            ref ui,
            ui.Text(title)
                .FontSize(Px(ui.Theme.Typography.TitleBar))
                .TextColor(ui.Theme.Colors.TitleBarText),
            null
        );

    private static Element Render(ref RenderContext ui, Element title, Element? menuBar)
    {
        var windowsCaptionGlyphs = OperatingSystem.IsWindows();
        ReadOnlySpan<byte> minimizeGlyph = windowsCaptionGlyphs ? "\uE921"u8 : "−"u8;
        ReadOnlySpan<byte> maximizeGlyph = windowsCaptionGlyphs ? "\uE922"u8 : "□"u8;
        ReadOnlySpan<byte> closeGlyph = windowsCaptionGlyphs ? "\uE8BB"u8 : "×"u8;
        var glyphSize = windowsCaptionGlyphs ? 10 : 15;
        var titleRegion = OperatingSystem.IsMacOS()
            ? ui.Div().Width(Px(64)).Height(Percent(100)).WindowControlArea(WindowControlArea.Drag)
            : ui.HStack(title)
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
                    ui.Theme.Colors.TitleBarHover
                ),
                Button(
                    ref ui,
                    "titlebar-maximize"u8,
                    maximizeGlyph,
                    WindowControlArea.Maximize,
                    36,
                    glyphSize,
                    ui.Theme.Colors.TitleBarHover
                ),
                Button(
                    ref ui,
                    "titlebar-close"u8,
                    closeGlyph,
                    WindowControlArea.Close,
                    36,
                    glyphSize,
                    ui.Theme.Colors.TitleBarCloseHover
                )
            );
        return titleBar
            .Height(Px(38))
            .Width(Percent(100))
            .Background(ui.Theme.Colors.TitleBarBackground);
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
            .Background(ui.Theme.Colors.TitleBarBackground)
            .HoverBackground(hoverBackground)
            .BorderWidth(Px(0))
            .Radius(Px(0))
            .TextColor(ui.Theme.Colors.TitleBarText);
}
