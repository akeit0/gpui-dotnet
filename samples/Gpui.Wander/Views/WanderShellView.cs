using static Gpui.Units;

namespace Gpui;

internal enum WanderTab
{
    Explore,
    Trips,
    Stats,
    Profile,
}

/// <summary>
/// Root View: a mobile-style phone column (status spoof, tab content, bottom tab bar)
/// centered on the desktop window. Tabs share one keyed child slot, so switching
/// replaces the child type; each tab keeps its own controllers, memos, and effects.
/// Render is heap-free: static handlers with payloads and arena-direct text.
/// </summary>
[GpuiView]
internal sealed partial class WanderShellView : View
{
    private readonly TravelStore _store = new();
    private readonly GpuiApplication _application;
    private readonly GpuiWindow _window;
    private readonly Effect<NoProps> _storeWatch;
    private readonly Effect<NoProps> _menus;
    private WanderTab _tab = WanderTab.Explore;
    private GpuiMenu[] _menuBar = [];

    public WanderShellView(ViewConstruction construction)
        : base(construction)
    {
        _application = construction.Application;
        _window = construction.Window;
        _storeWatch = construction.Effect<NoProps>(WatchStore);
        _menus = construction.Effect<NoProps>(InstallMenus);
    }

    private void WatchStore(EffectScope scope, NoProps input) =>
        scope.Own(_store.Subscribe(scope.Bind(this, static view => view.Invalidate())));

    private void InstallMenus(EffectScope scope, NoProps input)
    {
        _menuBar = CreateMenuBar(scope);
        _application.SetMenuBar(_menuBar);
        Invalidate();
    }

    private GpuiMenu[] CreateMenuBar(EffectScope scope) =>
    [
        new GpuiMenu(
            "Wander",
            GpuiMenuItem.Command("Reset demo data", scope.Bind(this, static view => view.ResetDemo())),
            GpuiMenuItem.Command("Toggle light/dark theme", scope.Bind(this, static view => view.ToggleTheme())),
            GpuiMenuItem.Separator(),
            GpuiMenuItem.Command("Close window", scope.Bind(this, static view => view.CloseWindow()))
        ),
        new GpuiMenu(
            "Go",
            GpuiMenuItem.Command("Explore", scope.Bind(this, static view => view.ShowTab(WanderTab.Explore))),
            GpuiMenuItem.Command("Trips", scope.Bind(this, static view => view.ShowTab(WanderTab.Trips))),
            GpuiMenuItem.Command("Stats", scope.Bind(this, static view => view.ShowTab(WanderTab.Stats))),
            GpuiMenuItem.Command("Profile", scope.Bind(this, static view => view.ShowTab(WanderTab.Profile)))
        ),
    ];

    private void ResetDemo() => _store.Reset();

    private void CloseWindow() => _window.Close();

    private void ToggleTheme() =>
        _application.SetTheme(
            _application.Theme.Appearance == GpuiThemeAppearance.Dark
                ? WanderThemes.Light
                : WanderThemes.Dark
        );

    private static readonly string[] TabIds = ["tab-0", "tab-1", "tab-2", "tab-3"];

    private void ShowTab(WanderTab tab)
    {
        _tab = tab;
        Invalidate();
    }

    private void SelectTab(ulong payload) => ShowTab((WanderTab)payload);

    private void OnHotKey(KeyEvent key)
    {
        if (key.IsHeld)
        {
            return;
        }
        if (key.Matches("1", control: true))
        {
            ShowTab(WanderTab.Explore);
        }
        else if (key.Matches("2", control: true))
        {
            ShowTab(WanderTab.Trips);
        }
        else if (key.Matches("3", control: true))
        {
            ShowTab(WanderTab.Stats);
        }
        else if (key.Matches("4", control: true))
        {
            ShowTab(WanderTab.Profile);
        }
    }

    private static string TabLabel(WanderTab tab) =>
        tab switch
        {
            WanderTab.Explore => "Explore",
            WanderTab.Trips => "Trips",
            WanderTab.Stats => "Stats",
            _ => "Profile",
        };

    private static string TabGlyph(WanderTab tab) =>
        tab switch
        {
            WanderTab.Explore => "✈",
            WanderTab.Trips => "◉",
            WanderTab.Stats => "▲",
            _ => "●",
        };

    protected override Element Render(ref RenderContext ui)
    {
        ui.Effect(_storeWatch, default);
        ui.Effect(_menus, default);
        var theme = ui.Theme;

        Element page = _tab switch
        {
            WanderTab.Explore => ui.Child("tab", ExploreView.Spec(new ExploreProps(_store))),
            WanderTab.Trips => ui.Child("tab", TripsView.Spec(new TripsProps(_store))),
            WanderTab.Stats => ui.Child("tab", StatsView.Spec(new StatsProps(_store, _store.Revision))),
            _ => ui.Child("tab", ProfileView.Spec(new ProfileProps(_store))),
        };

        var likes = 0;
        foreach (var entry in _store.Entries)
        {
            likes += entry.Likes;
        }

        var phone = ui.VStack(
                ui.HStack(
                        ui.Text("9:41")
                            .FontSize(Px(theme.Typography.Caption))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Text("●●●")
                            .FontSize(Px(theme.Typography.Caption))
                            .TextColor(theme.Colors.TextMuted)
                    )
                    .ItemsCenter()
                    .Padding(Px(10)),
                // Flex column, not Div: Grow is inert inside a block parent, and the
                // page's Grow (then the list's) needs a flex ancestor to fill against.
                ui.VStack(page)
                    .Grow()
                    .Width(Percent(100))
                    .Height(Percent(100))
                    .PaddingX(Px(14)),
                TabBar(ref ui, likes)
            )
            .Width(Px(430))
            .Height(Percent(100))
            .Background(theme.Colors.Background)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant);

        var content = ui.HStack(ui.Spacer(), phone, ui.Spacer())
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(theme.Colors.ElementActive)
            .OnKeyDown(this, static (view, key) => view.OnHotKey(key));
        return GpuiTitleBar.RenderWindow(ref ui, "Wander — Travel Journal", _menuBar, content);
    }

    private Element TabBar(ref RenderContext ui, int likes)
    {
        var theme = ui.Theme;
        Span<Element> tabs =
        [
            TabButton(ref ui, this, theme, WanderTab.Explore, "♥", likes, true),
            TabButton(ref ui, this, theme, WanderTab.Trips, string.Empty, _store.Trips.Count, true),
            TabButton(ref ui, this, theme, WanderTab.Stats, string.Empty, 0, false),
            TabButton(ref ui, this, theme, WanderTab.Profile, string.Empty, 0, false),
        ];
        return ui.VStack(
                ui.Divider(),
                ui.HStack(tabs).Gap(Px(4))
            )
            .Gap(Px(6))
            .Padding(Px(10))
            .Background(theme.Colors.SurfaceBackground);
    }

    private static Element TabButton(
        ref RenderContext ui,
        WanderShellView view,
        GpuiTheme theme,
        WanderTab tab,
        string badgePrefix,
        long badgeNumber,
        bool showBadge
    )
    {
        var selected = view._tab == tab;
        // The badge interpolation lands directly in ui.Text's arena handler: no string.
        var badge = showBadge
            ? ui.Text($"{badgePrefix}{badgeNumber:N0}")
            : ui.Text(string.Empty);
        return ui.Button(
                TabIds[(int)tab],
                ui.VStack(
                        ui.Text(TabGlyph(tab))
                            .FontSize(Px(18))
                            .TextColor(selected ? theme.Colors.Accent : theme.Colors.TextMuted),
                        ui.Text(TabLabel(tab))
                            .FontSize(Px(theme.Typography.Caption))
                            .TextColor(selected ? theme.Colors.Text : theme.Colors.TextMuted),
                        badge
                            .FontSize(Px(theme.Typography.Caption))
                            .TextColor(theme.Colors.TextAccent)
                    )
                    .Gap(Px(1))
                    .ItemsCenter()
            )
            .OnClick(view, static (v, e) => v.SelectTab(e.Payload), (ulong)tab)
            .Style(WanderStyles.Button(theme, WanderButtonVariant.Chip, selected))
            .Grow()
            .Width(Percent(25));
    }
}
