using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class SampleShellView : View
{
    private SamplePage _page;

    internal GpuiApplication? Application { get; init; }
    internal GpuiMenu[] MenuBar { get; set; } = [];
    internal GpuiWindow? Window { get; set; }

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

    private void CloseWindow()
    {
        (
            Window ?? throw new InvalidOperationException("The sample root has no window handle.")
        ).Close();
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
                    .OnClick(this, (view, _) => view.CloseWindow())
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
        return GpuiTitleBar.RenderWindow(
            ref ui,
            "GPUI.NET  /  Components"u8,
            MenuBar,
            gallery
        );
    }

    internal GpuiMenu[] CreateMenuBar() =>
    [
        new GpuiMenu(
            "GPUI.NET",
            GpuiMenuItem.Command("About GPUI.NET", () => ShowPage(SamplePage.Overview)),
            GpuiMenuItem.Separator(),
            GpuiMenuItem.Command("Close window", CloseWindow)
        ),
        new GpuiMenu(
            "File",
            GpuiMenuItem.Command("New window", OpenNewWindow),
            GpuiMenuItem.Command("New custom-title-bar window", OpenCustomTitleBarWindow),
            GpuiMenuItem.Separator(),
            GpuiMenuItem.Command("Close window", CloseWindow)
        ),
        new GpuiMenu(
            "View",
            GpuiMenuItem.Command("Scroll view", () => ShowPage(SamplePage.Overview)),
            GpuiMenuItem.Command("Virtual list", () => ShowPage(SamplePage.Activity)),
            GpuiMenuItem.Command("Virtual table", () => ShowPage(SamplePage.Tables)),
            GpuiMenuItem.Command("Images", () => ShowPage(SamplePage.Images)),
            GpuiMenuItem.Command("Inputs", () => ShowPage(SamplePage.Inputs)),
            GpuiMenuItem.Command("Overlays + tooltips", () => ShowPage(SamplePage.Overlays)),
            GpuiMenuItem.Command("Windows", () => ShowPage(SamplePage.Windows))
        ),
        new GpuiMenu(
            "Help",
            GpuiMenuItem.Command("About GPUI.NET", () => ShowPage(SamplePage.Overview)),
            GpuiMenuItem.Command("Open overlay gallery", () => ShowPage(SamplePage.Overlays))
        )
    ];

    private void ShowPage(SamplePage page)
    {
        _page = page;
        Invalidate();
    }

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
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._page = SamplePage.Overview;
                            view.Invalidate();
                        }
                    )
                    .Padding(Px(10))
                    .Background(overviewSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(overviewSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-activity", "Virtual list")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._page = SamplePage.Activity;
                            view.Invalidate();
                        }
                    )
                    .Padding(Px(10))
                    .Background(activitySelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(activitySelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-tables", "Virtual table")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._page = SamplePage.Tables;
                            view.Invalidate();
                        }
                    )
                    .Padding(Px(10))
                    .Background(tablesSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(tablesSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-images", "Images")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._page = SamplePage.Images;
                            view.Invalidate();
                        }
                    )
                    .Padding(Px(10))
                    .Background(imagesSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(imagesSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-inputs", "Inputs")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._page = SamplePage.Inputs;
                            view.Invalidate();
                        }
                    )
                    .Padding(Px(10))
                    .Background(inputsSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(inputsSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-overlays", "Overlays + tooltips")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._page = SamplePage.Overlays;
                            view.Invalidate();
                        }
                    )
                    .Padding(Px(10))
                    .Background(overlaysSelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(overlaysSelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-windows", "Windows")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._page = SamplePage.Windows;
                            view.Invalidate();
                        }
                    )
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
                                    .OnClick(this, (view, _) => view.OpenNewWindow())
                                    .Padding(Px(11))
                                    .Background(Rgb(79, 70, 229))
                                    .BorderColor(Rgb(67, 56, 202))
                                    .TextColor(Rgb(255, 255, 255)),
                                ui.Button("open-custom-titlebar", "Custom title bar")
                                    .OnClick(this, (view, _) => view.OpenCustomTitleBarWindow())
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
