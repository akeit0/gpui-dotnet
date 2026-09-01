using static Gpui.Colors;
using static Gpui.Units;

namespace Gpui;

/// <summary>Rendering choices for the library title bar.</summary>
public readonly struct GpuiTitleBarOptions
{
    /// <summary>
    /// Creates title-bar options. The default follows the native platform menu policy.
    /// </summary>
    public GpuiTitleBarOptions(bool forceManagedMenuOnMac = false)
    {
        ForceManagedMenuOnMac = forceManagedMenuOnMac;
    }

    /// <summary>
    /// On macOS, render the title and app-side popover menus in the custom title bar instead of
    /// leaving the menu to AppKit's global menu bar.
    /// </summary>
    public bool ForceManagedMenuOnMac { get; }
}

/// <summary>
/// Minimal managed title-bar composition for custom windows.
/// </summary>
/// <remarks>
/// On Windows and Linux this renders the supplied menus as app-side popovers and adds native
/// caption hit-test regions. On macOS the default is to let the application menu belong to AppKit,
/// so this reserves the traffic-light area and leaves menu rendering to
/// <see cref="GpuiApplication.SetMenuBar"/>. Set <see cref="GpuiTitleBarOptions.ForceManagedMenuOnMac"/>
/// when a custom macOS title bar must render the menus itself.
/// </remarks>
public static class GpuiTitleBar
{
    private const float HeightPx = 38;
    private const float MacTrafficLightWidthPx = 64;
    private const float MenuWidthPx = 224;

    /// <summary>Renders a title bar from a UTF-16 title and shared menu definitions.</summary>
    public static Element Render(
        ref RenderContext ui,
        ReadOnlySpan<char> title,
        IReadOnlyList<GpuiMenu> menus,
        GpuiTitleBarOptions options = default
    )
    {
        ValidateMenus(menus);
        Element titleElement = default;
        if (!OperatingSystem.IsMacOS() || options.ForceManagedMenuOnMac)
        {
            titleElement = ui.Text(title).FontSize(Px(13)).TextColor(Rgb(226, 232, 240));
        }

        return RenderCore(ref ui, titleElement, menus, options);
    }

    /// <summary>Renders a title bar from an already encoded UTF-8 title.</summary>
    public static Element Render(
        ref RenderContext ui,
        ReadOnlySpan<byte> utf8Title,
        IReadOnlyList<GpuiMenu> menus,
        GpuiTitleBarOptions options = default
    )
    {
        ValidateMenus(menus);
        Element titleElement = default;
        if (!OperatingSystem.IsMacOS() || options.ForceManagedMenuOnMac)
        {
            titleElement = ui.Text(utf8Title).FontSize(Px(13)).TextColor(Rgb(226, 232, 240));
        }

        return RenderCore(ref ui, titleElement, menus, options);
    }

    /// <summary>
    /// Wraps window content with the library title bar. On macOS, the default system title-bar
    /// path returns <paramref name="content"/> unchanged; other platforms receive managed chrome.
    /// </summary>
    public static Element RenderWindow(
        ref RenderContext ui,
        ReadOnlySpan<char> title,
        IReadOnlyList<GpuiMenu> menus,
        Element content,
        GpuiTitleBarOptions options = default
    )
    {
        ValidateMenus(menus);
        if (OperatingSystem.IsMacOS() && !options.ForceManagedMenuOnMac)
        {
            return content;
        }

        return ui
            .VStack(Render(ref ui, title, menus, options), content)
            .Width(Percent(100))
            .Height(Percent(100));
    }

    /// <summary>Wraps window content with a UTF-8 title-bar title.</summary>
    public static Element RenderWindow(
        ref RenderContext ui,
        ReadOnlySpan<byte> utf8Title,
        IReadOnlyList<GpuiMenu> menus,
        Element content,
        GpuiTitleBarOptions options = default
    )
    {
        ValidateMenus(menus);
        if (OperatingSystem.IsMacOS() && !options.ForceManagedMenuOnMac)
        {
            return content;
        }

        return ui
            .VStack(Render(ref ui, utf8Title, menus, options), content)
            .Width(Percent(100))
            .Height(Percent(100));
    }

    private static Element RenderCore(
        ref RenderContext ui,
        Element title,
        IReadOnlyList<GpuiMenu> menus,
        GpuiTitleBarOptions options
    )
    {
        var managedMenu = !OperatingSystem.IsMacOS() || options.ForceManagedMenuOnMac;
        Element<DivTag> titleRegion;
        if (OperatingSystem.IsMacOS())
        {
            var trafficLightRegion = ui
                .Div()
                .Width(Px(MacTrafficLightWidthPx))
                .Height(Percent(100))
                .WindowControlArea(WindowControlArea.Drag);
            titleRegion = managedMenu
                ? ui
                    .HStack(trafficLightRegion, title)
                    .ItemsCenter()
                    .Padding(Px(9))
                    .Height(Percent(100))
                    .WindowControlArea(WindowControlArea.Drag)
                : trafficLightRegion;
        }
        else
        {
            titleRegion = ui
                .HStack(title)
                .ItemsCenter()
                .Padding(Px(9))
                .Height(Percent(100))
                .WindowControlArea(WindowControlArea.Drag);
        }

        Element<DivTag> trailingDragRegion = ui
            .Div()
            .Grow()
            .Height(Percent(100))
            .WindowControlArea(WindowControlArea.Drag);

        Element<DivTag> dragAndMenus;
        if (!managedMenu || menus.Count == 0)
        {
            dragAndMenus = ui.HStack(titleRegion, trailingDragRegion).Grow();
        }
        else
        {
            var menuBar = RenderMenuBar(ref ui, menus);
            dragAndMenus = ui.HStack(titleRegion, menuBar, trailingDragRegion).Grow();
        }

        Element<DivTag> titleBar;
        if (OperatingSystem.IsMacOS())
        {
            titleBar = ui.HStack(dragAndMenus);
        }
        else
        {
            var windowsCaptionGlyphs = OperatingSystem.IsWindows();
            ReadOnlySpan<byte> minimizeGlyph = windowsCaptionGlyphs ? "\uE921"u8 : "−"u8;
            ReadOnlySpan<byte> maximizeGlyph = windowsCaptionGlyphs ? "\uE922"u8 : "□"u8;
            ReadOnlySpan<byte> closeGlyph = windowsCaptionGlyphs ? "\uE8BB"u8 : "×"u8;
            var glyphSize = windowsCaptionGlyphs ? 10 : 15;

            titleBar = ui.HStack(
                dragAndMenus,
                CaptionButton(
                    ref ui,
                    "gpui-titlebar-minimize"u8,
                    minimizeGlyph,
                    WindowControlArea.Minimize,
                    glyphSize,
                    Rgb(30, 41, 59)
                ),
                CaptionButton(
                    ref ui,
                    "gpui-titlebar-maximize"u8,
                    maximizeGlyph,
                    WindowControlArea.Maximize,
                    glyphSize,
                    Rgb(30, 41, 59)
                ),
                CaptionButton(
                    ref ui,
                    "gpui-titlebar-close"u8,
                    closeGlyph,
                    WindowControlArea.Close,
                    glyphSize,
                    Rgb(232, 17, 32)
                )
            );
        }

        return titleBar
            .Height(Px(HeightPx))
            .Width(Percent(100))
            .Background(Rgb(15, 23, 42));
    }

    private static Element<DivTag> RenderMenuBar(
        ref RenderContext ui,
        IReadOnlyList<GpuiMenu> menus
    )
    {
        var owner = ui.EventBindingOwner;
        var menuElements = new List<Element>(menus.Count);
        for (var index = 0; index < menus.Count; index++)
        {
            var menu = menus[index];
            var key = $"gpui-titlebar-menu-{index}";
            var trigger = ui
                .Button($"{key}-trigger", menu.Title)
                .Height(Percent(100))
                .Padding(Px(9))
                .FontSize(Px(12))
                .Background(Rgba(15, 23, 42, 0))
                .HoverBackground(Rgb(30, 41, 59))
                .BorderWidth(Px(0))
                .Radius(Px(0))
                .TextColor(Rgb(226, 232, 240));
            var content = RenderMenuSurface(ref ui, menu, owner, key);
            menuElements.Add(ui.PopoverMenu($"{key}-popover", trigger, content));
        }

        return ui.HStack(menuElements.ToArray()).Height(Percent(100)).ItemsCenter();
    }

    private static Element<DivTag> RenderMenuSurface(
        ref RenderContext ui,
        GpuiMenu menu,
        View owner,
        string key
    )
    {
        var items = new List<Element>(menu.Items.Count);
        for (var index = 0; index < menu.Items.Count; index++)
        {
            var item = menu.Items[index];
            if (item.IsSeparator)
            {
                items.Add(
                    ui
                        .Divider()
                        .Width(Percent(100))
                        .Height(Px(1))
                        .Background(Rgb(226, 232, 240))
                );
            }
            else if (item.NestedMenu is not null)
            {
                var submenuKey = $"{key}-submenu-{index}";
                var submenuTrigger = MenuItemButton(
                    ref ui,
                    $"{submenuKey}-trigger",
                    ui.HStack(
                            ui.Text(item.Title),
                            ui.Spacer().Grow(),
                            ui.Text("›"u8)
                        )
                        .ItemsCenter()
                );
                var submenuContent = RenderMenuSurface(
                    ref ui,
                    item.NestedMenu,
                    owner,
                    submenuKey
                );
                items.Add(
                    ui.PopoverMenu(
                        $"{submenuKey}-popover",
                        submenuTrigger,
                        submenuContent
                    )
                );
            }
            else
            {
                items.Add(
                    MenuItemButton(ref ui, $"{key}-item-{index}", item.Title).OnClick(
                        owner,
                        item.EventCallback
                    )
                );
            }
        }

        return ui
            .VStack(items.ToArray())
            .Gap(Px(2))
            .Padding(Px(6))
            .Width(Px(MenuWidthPx))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(203, 213, 225))
            .Radius(Px(7));
    }

    private static Element<ButtonTag> MenuItemButton(
        ref RenderContext ui,
        string id,
        ReadOnlySpan<char> title
    ) =>
        ui
            .Button(id, title)
            .Width(Percent(100))
            .Padding(Px(8))
            .FontSize(Px(12))
            .Background(Rgb(255, 255, 255))
            .HoverBackground(Rgb(241, 245, 249))
            .BorderWidth(Px(0))
            .Radius(Px(4))
            .TextColor(Rgb(15, 23, 42));

    private static Element<ButtonTag> MenuItemButton(
        ref RenderContext ui,
        string id,
        Element label
    ) =>
        ui
            .Button(id, label)
            .Width(Percent(100))
            .Padding(Px(8))
            .FontSize(Px(12))
            .Background(Rgb(255, 255, 255))
            .HoverBackground(Rgb(241, 245, 249))
            .BorderWidth(Px(0))
            .Radius(Px(4))
            .TextColor(Rgb(15, 23, 42));

    private static Element<ButtonTag> CaptionButton(
        ref RenderContext ui,
        ReadOnlySpan<byte> id,
        ReadOnlySpan<byte> label,
        WindowControlArea area,
        float glyphSize,
        Color hoverBackground
    ) =>
        ui
            .Button(id, label)
            .WindowControlArea(area)
            .Width(Px(36))
            .Height(Percent(100))
            .ItemsCenter()
            .JustifyCenter()
            .FontSize(Px(glyphSize))
            .Background(Rgb(15, 23, 42))
            .HoverBackground(hoverBackground)
            .BorderWidth(Px(0))
            .Radius(Px(0))
            .TextColor(Rgb(248, 250, 252));

    private static void ValidateMenus(IReadOnlyList<GpuiMenu> menus)
    {
        ArgumentNullException.ThrowIfNull(menus);
        for (var index = 0; index < menus.Count; index++)
        {
            var menu = menus[index];
            if (menu is null)
            {
                throw new ArgumentException(
                    "Menu definitions cannot contain null entries.",
                    nameof(menus)
                );
            }

            GpuiMenu.Validate(menu, nameof(menus));
        }
    }
}
