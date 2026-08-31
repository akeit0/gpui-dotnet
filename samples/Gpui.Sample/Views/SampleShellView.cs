using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class SampleShellView : View
{
    private SamplePage _page;

    internal GpuiApplication? Application { get; init; }
    internal GpuiWindow? Window { get; set; }

    [GpuiEvent]
    private void OpenNewWindow()
    {
        var application =
            Application
            ?? throw new InvalidOperationException(
                "The sample root is not associated with its application."
            );
        var root = new CompanionWindowView { Origin = "Opened from the Windows gallery" };
        var window = application.OpenWindow(
            root,
            new GpuiWindowOptions
            {
                Title = "GPUI.NET Components — Opening",
                Width = 800,
                Height = 580,
                Activate = false,
            }
        );
        root.Window = window;
        window.SetTitle($"GPUI.NET Companion — Window {window.Id}");
        window.Resize(860, 620);
        window.Activate();
    }

    [GpuiEvent]
    private void OpenCustomTitleBarWindow()
    {
        var application =
            Application
            ?? throw new InvalidOperationException(
                "The sample root is not associated with its application."
            );
        var root = new CustomTitleBarWindowView();
        var window = application.OpenWindow(
            root,
            new GpuiWindowOptions
            {
                Title = "GPUI.NET Custom Title Bar",
                Width = 820,
                Height = 560,
                TitleBarStyle = WindowTitleBarStyle.Custom,
            }
        );
        root.Window = window;
        window.SetTitle($"GPUI.NET Custom Title Bar — Window {window.Id}");
    }

    [GpuiEvent]
    private void CloseWindow()
    {
        (
            Window ?? throw new InvalidOperationException("The sample root has no window handle.")
        ).Close();
    }

    [GpuiEvent]
    private void ShowOverview()
    {
        _page = SamplePage.Overview;
        Invalidate();
    }

    [GpuiEvent]
    private void ShowActivity()
    {
        _page = SamplePage.Activity;
        Invalidate();
    }

    [GpuiEvent]
    private void ShowTables()
    {
        _page = SamplePage.Tables;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui)
    {
        var sidebar = RenderSidebar(ref ui);
        var page = _page switch
        {
            SamplePage.Overview => ui.Child<DashboardView>("content"),
            SamplePage.Activity => ui.Child<ActivityView>("content"),
            SamplePage.Tables => ui.Child<TableView>("content"),
            SamplePage.Images => ui.Child<ImageGalleryView>("content"),
            SamplePage.Inputs => ui.Child<InputGalleryView>("content"),
            SamplePage.Overlays => ui.Child<OverlayGalleryView>("content"),
            SamplePage.Windows => RenderWindowGallery(ref ui),
            _ => throw new InvalidOperationException("Unknown sample page."),
        };
        var pageSlot = ui.VStack(page).Grow().Height(Percent(100));

        var routeProps = new RouteHeaderProps(
            _page switch
            {
                SamplePage.Overview => "Overview",
                SamplePage.Activity => "Activity",
                SamplePage.Tables => "Tables",
                SamplePage.Images => "Images",
                SamplePage.Inputs => "Inputs",
                SamplePage.Overlays => "Overlays",
                SamplePage.Windows => "Windows",
                _ => throw new InvalidOperationException("Unknown sample page."),
            },
            _page switch
            {
                SamplePage.Overview => "retained ScrollHandle; wheel scrolling stays native",
                SamplePage.Activity =>
                    "20,000 variable-height rows; managed rendering is range-batched",
                SamplePage.Tables =>
                    "declared columns drive the native header and row cell reconciliation",
                SamplePage.Images => "GPUI-native decoding, caching, fitting, and grayscale",
                SamplePage.Inputs =>
                    "retained native editing, IME, selection, focus, and UTF-8 events",
                SamplePage.Overlays =>
                    "deferred overlays, native tooltip timing, flipping, and dismissal",
                SamplePage.Windows =>
                    "one application, independent roots, stable handles, isolated state",
                _ => throw new InvalidOperationException("Unknown sample page."),
            }
        );
        var routeHeader = ui.Child<RouteHeaderView, RouteHeaderProps>(
            "route-header",
            in routeProps
        );

        var topBar = ui.HStack(
                routeHeader,
                ui.Spacer(),
                ui.Button("close-window", "Close")
                    .OnClick(Events.CloseWindow)
                    .Padding(Px(7))
                    .Background(Rgb(255, 255, 255))
                    .BorderColor(Rgb(203, 213, 225)),
                ui.Badge(ui.Text("ABI V5"u8))
                    .Background(Rgb(224, 231, 255))
                    .TextColor(Rgb(67, 56, 202))
                    .Padding(Px(7))
            )
            .ItemsCenter();

        var content = ui.VStack(topBar, ui.Divider(), pageSlot)
            .Gap(Px(14))
            .Padding(Px(22))
            .Grow()
            .Height(Percent(100));

        var gallery = ui.HStack(sidebar, content)
            .Width(Percent(100))
            .Grow()
            .Background(Rgb(241, 245, 249));
        var menuBar = RenderMenuBar(ref ui);
        return ui.VStack(
                SampleTitleBar.Render(ref ui, "GPUI.NET  /  Components"u8, menuBar),
                gallery
            )
            .Width(Percent(100))
            .Height(Percent(100));
    }

    private Element RenderMenuBar(ref RenderContext ui)
    {
        var fileMenu = ui.PopoverMenu(
            "titlebar-file-menu"u8,
            TitleBarMenuTrigger(ref ui, "titlebar-file"u8, "File"u8),
            MenuSurface(
                ref ui,
                MenuItem(ref ui, "menu-new-window"u8, "New window"u8, Events.OpenNewWindow),
                MenuItem(
                    ref ui,
                    "menu-custom-window"u8,
                    "New custom-title-bar window"u8,
                    Events.OpenCustomTitleBarWindow
                ),
                ui.Divider(),
                MenuItem(ref ui, "menu-close-window"u8, "Close window"u8, Events.CloseWindow)
            )
        );
        var viewMenu = ui.PopoverMenu(
            "titlebar-view-menu"u8,
            TitleBarMenuTrigger(ref ui, "titlebar-view"u8, "View"u8),
            MenuSurface(
                ref ui,
                MenuItem(ref ui, "menu-overview"u8, "Scroll view"u8, Events.ShowOverview),
                MenuItem(ref ui, "menu-activity"u8, "Virtual list"u8, Events.ShowActivity),
                MenuItem(ref ui, "menu-tables"u8, "Virtual table"u8, Events.ShowTables),
                MenuItem(ref ui, "menu-images"u8, "Images"u8, Events.ShowImages),
                MenuItem(ref ui, "menu-inputs"u8, "Inputs"u8, Events.ShowInputs),
                MenuItem(ref ui, "menu-overlays"u8, "Overlays + tooltips"u8, Events.ShowOverlays),
                MenuItem(ref ui, "menu-windows"u8, "Windows"u8, Events.ShowWindows)
            )
        );
        var helpMenu = ui.PopoverMenu(
            "titlebar-help-menu"u8,
            TitleBarMenuTrigger(ref ui, "titlebar-help"u8, "Help"u8),
            MenuSurface(
                ref ui,
                MenuItem(ref ui, "menu-about"u8, "About GPUI.NET"u8, Events.ShowOverview),
                MenuItem(
                    ref ui,
                    "menu-overlay-guide"u8,
                    "Open overlay gallery"u8,
                    Events.ShowOverlays
                )
            )
        );

        return ui.HStack(fileMenu, viewMenu, helpMenu).Height(Percent(100)).ItemsCenter();
    }

    private static Element TitleBarMenuTrigger(
        ref RenderContext ui,
        ReadOnlySpan<byte> id,
        ReadOnlySpan<byte> label
    ) =>
        ui.Button(id, label)
            .Height(Percent(100))
            .Padding(Px(9))
            .FontSize(Px(12))
            .Background(Rgb(15, 23, 42))
            .HoverBackground(Rgb(30, 41, 59))
            .BorderWidth(Px(0))
            .Radius(Px(0))
            .TextColor(Rgb(226, 232, 240));

    private static Element MenuSurface(ref RenderContext ui, params ReadOnlySpan<Element> items) =>
        ui.VStack(items)
            .Gap(Px(2))
            .Padding(Px(6))
            .Width(Px(224))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(203, 213, 225))
            .Radius(Px(7));

    private static Element MenuItem(
        ref RenderContext ui,
        ReadOnlySpan<byte> id,
        ReadOnlySpan<byte> label,
        Callback<ClickEvent> callback
    ) =>
        ui.Button(id, label)
            .OnClick(callback)
            .Width(Percent(100))
            .Padding(Px(8))
            .FontSize(Px(12))
            .Background(Rgb(255, 255, 255))
            .HoverBackground(Rgb(241, 245, 249))
            .BorderWidth(Px(0))
            .Radius(Px(4))
            .TextColor(Rgb(15, 23, 42));

    private Element RenderSidebar(ref RenderContext ui)
    {
        var overviewSelected = _page == SamplePage.Overview;
        var activitySelected = _page == SamplePage.Activity;
        var tablesSelected = _page == SamplePage.Tables;
        var imagesSelected = _page == SamplePage.Images;
        var inputsSelected = _page == SamplePage.Inputs;
        var overlaysSelected = _page == SamplePage.Overlays;
        var windowsSelected = _page == SamplePage.Windows;
        return ui.VStack(
                ui.Text("SIDEBAR"u8).FontSize(Px(11)).TextColor(Rgb(148, 163, 184)),
                ui.Divider().Background(Rgb(51, 65, 85)),
                ui.Button("show-overview", "Scroll view")
                    .OnClick(Events.ShowOverview)
                    .Padding(Px(10))
                    .Background(overviewSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(overviewSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-activity", "Virtual list")
                    .OnClick(Events.ShowActivity)
                    .Padding(Px(10))
                    .Background(activitySelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(activitySelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-tables", "Virtual table")
                    .OnClick(Events.ShowTables)
                    .Padding(Px(10))
                    .Background(tablesSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(tablesSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-images", "Images")
                    .OnClick(Events.ShowImages)
                    .Padding(Px(10))
                    .Background(imagesSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(imagesSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-inputs", "Inputs")
                    .OnClick(Events.ShowInputs)
                    .Padding(Px(10))
                    .Background(inputsSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(inputsSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-overlays", "Overlays + tooltips")
                    .OnClick(Events.ShowOverlays)
                    .Padding(Px(10))
                    .Background(overlaysSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(overlaysSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-windows", "Windows")
                    .OnClick(Events.ShowWindows)
                    .Padding(Px(10))
                    .Background(windowsSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(windowsSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Spacer(),
                ui.Text("The keyed content slot replaces its View type when the route changes."u8)
                    .FontSize(Px(11))
                    .TextColor(Rgb(100, 116, 139))
            )
            .Gap(Px(10))
            .Padding(Px(18))
            .Width(Px(220))
            .Height(Percent(100))
            .Background(Rgb(15, 23, 42));
    }

    [GpuiEvent]
    private void ShowImages()
    {
        _page = SamplePage.Images;
        Invalidate();
    }

    [GpuiEvent]
    private void ShowInputs()
    {
        _page = SamplePage.Inputs;
        Invalidate();
    }

    [GpuiEvent]
    private void ShowOverlays()
    {
        _page = SamplePage.Overlays;
        Invalidate();
    }

    [GpuiEvent]
    private void ShowWindows()
    {
        _page = SamplePage.Windows;
        Invalidate();
    }

    private Element RenderWindowGallery(ref RenderContext ui) =>
        ui.VStack(
                ui.VStack(
                        ui.Text(
                                "Open a genuinely different root view in the same GPUI application."u8
                            )
                            .FontSize(Px(18))
                            .TextColor(Rgb(15, 23, 42)),
                        ui.Text(
                                "Each window owns its render tree, resources, controls, and failure boundary. Closing the companion leaves this gallery running."u8
                            )
                            .FontSize(Px(13))
                            .TextColor(Rgb(71, 85, 105)),
                        ui.HStack(
                                ui.Button("open-companion-window", "System title bar")
                                    .OnClick(Events.OpenNewWindow)
                                    .Padding(Px(11))
                                    .Background(Rgb(79, 70, 229))
                                    .BorderColor(Rgb(67, 56, 202))
                                    .TextColor(Rgb(255, 255, 255)),
                                ui.Button("open-custom-titlebar", "Custom title bar")
                                    .OnClick(Events.OpenCustomTitleBarWindow)
                                    .Padding(Px(11))
                                    .Background(Rgb(15, 23, 42))
                                    .BorderColor(Rgb(51, 65, 85))
                                    .TextColor(Rgb(255, 255, 255)),
                                ui.Badge(ui.Text($"Gallery window ID: {Window?.Id ?? 0}"))
                                    .Background(Rgb(224, 231, 255))
                                    .TextColor(Rgb(67, 56, 202))
                                    .Padding(Px(8))
                            )
                            .Gap(Px(10))
                            .ItemsCenter()
                    )
                    .Gap(Px(14))
                    .Padding(Px(22))
                    .Background(Rgb(255, 255, 255))
                    .BorderWidth(Px(1))
                    .BorderColor(Rgb(226, 232, 240))
                    .Radius(Px(12)),
                ui.Text(
                        "The custom variant uses native drag/minimize/maximize/close hit-test regions while its visuals remain managed."u8
                    )
                    .FontSize(Px(12))
                    .TextColor(Rgb(100, 116, 139))
            )
            .Gap(Px(14))
            .Grow();
}
