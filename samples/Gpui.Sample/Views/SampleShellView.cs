using Gpui;
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

    private void ToggleTheme()
    {
        var application =
            Application
            ?? throw new InvalidOperationException(
                "The sample root is not associated with its application."
            );
        application.SetTheme(
            application.Theme.Appearance == GpuiThemeAppearance.Dark
                ? SampleThemes.Light
                : SampleThemes.Dark
        );
    }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var sidebar = RenderSidebar(ref ui);
        var page = _page switch
        {
            SamplePage.Overview => ui.Child<DashboardView>("content"),
            SamplePage.Activity => ui.Child<ActivityView>("content"),
            SamplePage.Tables => ui.Child<TableView>("content"),
            SamplePage.Dock => ui.Child<DockView>("content"),
            SamplePage.Images => ui.Child<ImageGalleryView>("content"),
            SamplePage.Text => ui.Child<TypographyView>("content"),
            SamplePage.Inputs => ui.Child<InputGalleryView>("content"),
            SamplePage.Observers => ui.Child<ObserverView>("content"),
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
                SamplePage.Dock => "Dock",
                SamplePage.Images => "Images",
                SamplePage.Text => "Text",
                SamplePage.Inputs => "Inputs",
                SamplePage.Observers => "Observers",
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
                SamplePage.Dock =>
                    "native tab activation, dragging, resizing, focus, and retained managed panels",
                SamplePage.Images => "GPUI-native decoding, caching, fitting, and grayscale",
                SamplePage.Text => "weight, style, decorations, and line height without a web view",
                SamplePage.Inputs =>
                    "retained native editing, IME, selection, focus, and UTF-8 events",
                SamplePage.Observers =>
                    "observer key/mouse events; focused controls win, the rest bubbles",
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
                    .Style(SampleStyles.Button(theme)),
                ui.Button(
                        "toggle-theme",
                        theme.Appearance == GpuiThemeAppearance.Dark ? "Light theme" : "Dark theme"
                    )
                    .OnClick(this, (view, _) => view.ToggleTheme())
                    .Style(SampleStyles.Button(theme, SampleButtonVariant.Primary)),
                ui.Badge(ui.Text("ABI v1"u8))
                    .FontSize(Px(theme.Typography.Caption))
                    .Background(theme.Colors.InfoBackground)
                    .TextColor(theme.Colors.Info)
                    .Padding(Px(7))
            )
            .Gap(Px(6))
            .ItemsCenter();

        var content = ui.VStack(topBar, ui.Divider(), pageSlot)
            .Gap(Px(14))
            .Padding(Px(22))
            .Grow()
            .Height(Percent(100));

        var gallery = ui.HStack(sidebar, content)
            .Width(Percent(100))
            .Grow()
            .Background(theme.Colors.Background);
        return GpuiTitleBar.RenderWindow(ref ui, "GPUI.NET  /  Components"u8, MenuBar, gallery);
    }

    internal GpuiMenu[] CreateMenuBar() =>
        [
            new GpuiMenu(
                "GPUI.NET",
                GpuiMenuItem.Command("About GPUI.NET", () => ShowPage(SamplePage.Overview)),
                GpuiMenuItem.Command("Toggle light/dark theme", ToggleTheme),
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
                GpuiMenuItem.Command("Dock", () => ShowPage(SamplePage.Dock)),
                GpuiMenuItem.Command("Images", () => ShowPage(SamplePage.Images)),
                GpuiMenuItem.Command("Text", () => ShowPage(SamplePage.Text)),
                GpuiMenuItem.Command("Inputs", () => ShowPage(SamplePage.Inputs)),
                GpuiMenuItem.Command("Observers", () => ShowPage(SamplePage.Observers)),
                GpuiMenuItem.Command("Overlays + tooltips", () => ShowPage(SamplePage.Overlays)),
                GpuiMenuItem.Command("Windows", () => ShowPage(SamplePage.Windows))
            ),
            new GpuiMenu(
                "Help",
                GpuiMenuItem.Command("About GPUI.NET", () => ShowPage(SamplePage.Overview)),
                GpuiMenuItem.Command("Open overlay gallery", () => ShowPage(SamplePage.Overlays))
            ),
        ];

    private void ShowPage(SamplePage page)
    {
        _page = page;
        Invalidate();
    }

    private Element RenderSidebar(ref RenderContext ui)
    {
        var theme = ui.Theme;
        return ui.VStack(
                ui.Text("SIDEBAR"u8)
                    .FontSize(Px(theme.Typography.Caption))
                    .TextColor(theme.Colors.TextPlaceholder),
                ui.Divider().Background(theme.Colors.TitleBarHover),
                NavigationButton(ref ui, "show-overview", "Scroll view", SamplePage.Overview),
                NavigationButton(ref ui, "show-activity", "Virtual list", SamplePage.Activity),
                NavigationButton(ref ui, "show-tables", "Virtual table", SamplePage.Tables),
                NavigationButton(ref ui, "show-dock", "Dock", SamplePage.Dock),
                NavigationButton(ref ui, "show-images", "Images", SamplePage.Images),
                NavigationButton(ref ui, "show-text", "Text", SamplePage.Text),
                NavigationButton(ref ui, "show-inputs", "Inputs", SamplePage.Inputs),
                NavigationButton(ref ui, "show-observers", "Observers", SamplePage.Observers),
                NavigationButton(
                    ref ui,
                    "show-overlays",
                    "Overlays + tooltips",
                    SamplePage.Overlays
                ),
                NavigationButton(ref ui, "show-windows", "Windows", SamplePage.Windows),
                ui.Spacer(),
                ui.Text("The keyed content slot replaces its View type when the route changes."u8)
                    .FontSize(Px(theme.Typography.Caption))
                    .TextColor(theme.Colors.TextPlaceholder)
            )
            .Gap(Px(10))
            .Padding(Px(18))
            .Width(Px(220))
            .Height(Percent(100))
            .Background(theme.Colors.PanelBackground);
    }

    private Element NavigationButton(
        ref RenderContext ui,
        string id,
        string title,
        SamplePage page
    ) =>
        ui.Button(id, title)
            .OnClick(this, (view, _) => view.ShowPage(page))
            .Style(
                SampleStyles.Button(
                    ui.Theme,
                    SampleButtonVariant.Navigation,
                    selected: _page == page
                )
            );

    private Element RenderWindowGallery(ref RenderContext ui) =>
        ui.VStack(
                ui.VStack(
                        ui.Text(
                                "Open a genuinely different root view in the same GPUI application."u8
                            )
                            .FontSize(Px(ui.Theme.Typography.Heading))
                            .TextColor(ui.Theme.Colors.Text),
                        ui.Text(
                                "Each window owns its render tree, resources, controls, and failure boundary. Closing the companion leaves this gallery running."u8
                            )
                            .FontSize(Px(ui.Theme.Typography.BodySmall))
                            .TextColor(ui.Theme.Colors.TextMuted),
                        ui.HStack(
                                ui.Button("open-companion-window", "System title bar")
                                    .OnClick(this, (view, _) => view.OpenNewWindow())
                                    .Padding(Px(11))
                                    .FontSize(Px(ui.Theme.Typography.Button))
                                    .Background(ui.Theme.Colors.Accent)
                                    .HoverBackground(ui.Theme.Colors.BorderSelected)
                                    .BorderWidth(Px(1))
                                    .BorderColor(ui.Theme.Colors.BorderSelected)
                                    .TextColor(ui.Theme.Colors.TitleBarText),
                                ui.Button("open-custom-titlebar", "Custom title bar")
                                    .OnClick(this, (view, _) => view.OpenCustomTitleBarWindow())
                                    .Padding(Px(11))
                                    .FontSize(Px(ui.Theme.Typography.Button))
                                    .Background(ui.Theme.Colors.TitleBarBackground)
                                    .HoverBackground(ui.Theme.Colors.TitleBarHover)
                                    .BorderWidth(Px(1))
                                    .BorderColor(ui.Theme.Colors.TitleBarHover)
                                    .TextColor(ui.Theme.Colors.TitleBarText),
                                ui.Badge(ui.Text($"Gallery window ID: {Window?.Id ?? 0}"))
                                    .FontSize(Px(ui.Theme.Typography.Caption))
                                    .Background(ui.Theme.Colors.InfoBackground)
                                    .TextColor(ui.Theme.Colors.Info)
                                    .Padding(Px(8))
                            )
                            .Gap(Px(10))
                            .ItemsCenter()
                    )
                    .Gap(Px(14))
                    .Padding(Px(22))
                    .Background(ui.Theme.Colors.SurfaceBackground)
                    .BorderWidth(Px(1))
                    .BorderColor(ui.Theme.Colors.BorderVariant)
                    .Radius(Px(12)),
                ui.Text(
                        "The custom variant uses native drag/minimize/maximize/close hit-test regions while its visuals remain managed."u8
                    )
                    .FontSize(Px(ui.Theme.Typography.Detail))
                    .TextColor(ui.Theme.Colors.TextMuted)
            )
            .Gap(Px(14))
            .Grow();
}
