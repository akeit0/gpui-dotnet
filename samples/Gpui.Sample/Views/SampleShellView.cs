using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class SampleShellView : View
{
    private SamplePage _page;

    private readonly GpuiApplication Application;
    private GpuiMenu[] MenuBar = [];
    private readonly GpuiWindow Window;
    private readonly Effect<NoProps> _menus;

    public SampleShellView(ViewConstruction construction)
        : base(construction)
    {
        Application = construction.Application;
        Window = construction.Window;
        _menus = construction.Effect<NoProps>(InstallMenus);
    }

    private void InstallMenus(EffectScope scope, NoProps input)
    {
        MenuBar = CreateMenuBar(scope);
        Application.SetMenuBar(MenuBar);
        Invalidate();
    }

    private void OpenNewWindow()
    {
        var application = Application;
        var window = application.OpenWindow(
            CompanionWindowView.Spec("Opened from the Windows gallery"),
            new GpuiWindowOptions
            {
                Title = "GPUI.NET Components — Opening",
                Width = 800,
                Height = 580,
                Activate = false,
            }
        );
        window.SetTitle($"GPUI.NET Companion — Window {window.Id}");
        window.Resize(860, 620);
        window.Activate();
    }

    private void OpenCustomTitleBarWindow()
    {
        var application = Application;
        var window = application.OpenWindow(
            CustomTitleBarWindowView.Spec(),
            new GpuiWindowOptions
            {
                Title = "GPUI.NET Custom Title Bar",
                Width = 820,
                Height = 560,
                TitleBarStyle = WindowTitleBarStyle.Custom,
            }
        );
        window.SetTitle($"GPUI.NET Custom Title Bar — Window {window.Id}");
    }

    private void CloseWindow()
    {
        Window.Close();
    }

    private void ToggleTheme()
    {
        var application = Application;
        application.SetTheme(
            application.Theme.Appearance == GpuiThemeAppearance.Dark
                ? SampleThemes.Light
                : SampleThemes.Dark
        );
    }

    protected override Element Render(ref RenderContext ui)
    {
        ui.Effect(_menus, default);
        var theme = ui.Theme;
        var sidebar = RenderSidebar(ref ui);
        var page = _page switch
        {
            SamplePage.Overview => ui.Child("content", DashboardView.Spec()),
            SamplePage.Reactivity => ui.Child("content", ReactivityView.Spec()),
            SamplePage.Analysis => ui.Child("content", AnalysisGalleryView.Spec()),
            SamplePage.Activity => ui.Child("content", ActivityView.Spec()),
            SamplePage.Tables => ui.Child("content", TableView.Spec()),
            SamplePage.Dock => ui.Child("content", DockView.Spec()),
            SamplePage.Images => ui.Child("content", ImageGalleryView.Spec()),
            SamplePage.Text => ui.Child("content", TypographyView.Spec()),
            SamplePage.Grid => ui.Child("content", GridView.Spec()),
            SamplePage.Inputs => ui.Child("content", InputGalleryView.Spec()),
            SamplePage.Observers => ui.Child("content", ObserverView.Spec()),
            SamplePage.Focus => ui.Child("content", FocusGalleryView.Spec()),
            SamplePage.Overlays => ui.Child("content", OverlayGalleryView.Spec()),
            SamplePage.Windows => RenderWindowGallery(ref ui),
            _ => throw new InvalidOperationException("Unknown sample page."),
        };
        var pageSlot = ui.VStack(page).Grow().Height(Percent(100));

        var routeProps = new RouteHeaderProps(
            _page switch
            {
                SamplePage.Overview => "Overview",
                SamplePage.Reactivity => "Reactivity",
                SamplePage.Analysis => "Document analysis",
                SamplePage.Activity => "Activity",
                SamplePage.Tables => "Tables",
                SamplePage.Dock => "Dock",
                SamplePage.Images => "Images",
                SamplePage.Text => "Text",
                SamplePage.Grid => "Grid",
                SamplePage.Inputs => "Inputs",
                SamplePage.Observers => "Observers",
                SamplePage.Focus => "Keyboard focus",
                SamplePage.Overlays => "Overlays",
                SamplePage.Windows => "Windows",
                _ => throw new InvalidOperationException("Unknown sample page."),
            },
            _page switch
            {
                SamplePage.Overview => "retained ScrollHandle; wheel scrolling stays native",
                SamplePage.Reactivity =>
                    "shared Signals across sibling views, conditional reads, and teardown",
                SamplePage.Analysis =>
                    "document subscriptions, local queries, cached matches, and background analysis",
                SamplePage.Activity =>
                    "20,000 variable-height rows; managed rendering is range-batched",
                SamplePage.Tables =>
                    "declared columns drive the native header and row cell reconciliation",
                SamplePage.Dock =>
                    "native tab activation, dragging, resizing, focus, and retained managed panels",
                SamplePage.Images => "GPUI-native decoding, caching, fitting, and grayscale",
                SamplePage.Text => "weight, style, decorations, and line height without a web view",
                SamplePage.Grid => "grid containers, templates, spans, and line placement",
                SamplePage.Focus =>
                    "focus a custom preview with the keyboard, pointer, or a command",
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
        var routeHeader = ui.Child("route-header", RouteHeaderView.Spec(routeProps));

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
                ui.Badge(ui.Text("ABI v7"u8))
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

    private GpuiMenu[] CreateMenuBar(EffectScope scope) =>
        [
            new GpuiMenu(
                "GPUI.NET",
                GpuiMenuItem.Command(
                    "About GPUI.NET",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Overview))
                ),
                GpuiMenuItem.Command(
                    "Toggle light/dark theme",
                    scope.Bind(this, static view => view.ToggleTheme())
                ),
                GpuiMenuItem.Separator(),
                GpuiMenuItem.Command(
                    "Close window",
                    scope.Bind(this, static view => view.CloseWindow())
                )
            ),
            new GpuiMenu(
                "File",
                GpuiMenuItem.Command(
                    "New window",
                    scope.Bind(this, static view => view.OpenNewWindow())
                ),
                GpuiMenuItem.Command(
                    "New custom-title-bar window",
                    scope.Bind(this, static view => view.OpenCustomTitleBarWindow())
                ),
                GpuiMenuItem.Separator(),
                GpuiMenuItem.Command(
                    "Close window",
                    scope.Bind(this, static view => view.CloseWindow())
                )
            ),
            new GpuiMenu(
                "View",
                GpuiMenuItem.Command(
                    "Scroll view",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Overview))
                ),
                GpuiMenuItem.Command(
                    "Reactivity",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Reactivity))
                ),
                GpuiMenuItem.Command(
                    "Virtual list",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Activity))
                ),
                GpuiMenuItem.Command(
                    "Virtual table",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Tables))
                ),
                GpuiMenuItem.Command(
                    "Dock",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Dock))
                ),
                GpuiMenuItem.Command(
                    "Images",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Images))
                ),
                GpuiMenuItem.Command(
                    "Text",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Text))
                ),
                GpuiMenuItem.Command(
                    "Grid",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Grid))
                ),
                GpuiMenuItem.Command(
                    "Inputs",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Inputs))
                ),
                GpuiMenuItem.Command(
                    "Observers",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Observers))
                ),
                GpuiMenuItem.Command(
                    "Keyboard focus",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Focus))
                ),
                GpuiMenuItem.Command(
                    "Overlays + tooltips",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Overlays))
                ),
                GpuiMenuItem.Command(
                    "Windows",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Windows))
                )
            ),
            new GpuiMenu(
                "Help",
                GpuiMenuItem.Command(
                    "About GPUI.NET",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Overview))
                ),
                GpuiMenuItem.Command(
                    "Open overlay gallery",
                    scope.Bind(this, static view => view.ShowPage(SamplePage.Overlays))
                )
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
                NavigationButton(ref ui, "show-reactivity", "Reactivity", SamplePage.Reactivity),
                NavigationButton(ref ui, "show-analysis", "Analysis", SamplePage.Analysis),
                NavigationButton(ref ui, "show-activity", "Virtual list", SamplePage.Activity),
                NavigationButton(ref ui, "show-tables", "Virtual table", SamplePage.Tables),
                NavigationButton(ref ui, "show-dock", "Dock", SamplePage.Dock),
                NavigationButton(ref ui, "show-images", "Images", SamplePage.Images),
                NavigationButton(ref ui, "show-text", "Text", SamplePage.Text),
                NavigationButton(ref ui, "show-grid", "Grid", SamplePage.Grid),
                NavigationButton(ref ui, "show-inputs", "Inputs", SamplePage.Inputs),
                NavigationButton(ref ui, "show-observers", "Observers", SamplePage.Observers),
                NavigationButton(ref ui, "show-focus", "Keyboard focus", SamplePage.Focus),
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
                                ui.Badge(ui.Text($"Gallery window ID: {Window.Id}"))
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
