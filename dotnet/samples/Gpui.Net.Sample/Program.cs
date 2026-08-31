using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

var stressGrowth = args.Contains("--stress-growth", StringComparer.Ordinal);
var multiWindow = args.Contains("--multi-window", StringComparer.Ordinal);
var application = new GpuiApplication();
View rootView = stressGrowth
    ? new ArenaGrowthView()
    : new SampleShellView { Application = application };
var mainWindow = application.OpenWindow(
    rootView,
    new GpuiWindowOptions
    {
        Title = stressGrowth ? "GPUI.NET Arena Growth" : "GPUI.NET Components",
        Width = 1040,
        Height = 700,
    }
);
if (rootView is SampleShellView mainShell)
{
    mainShell.Window = mainWindow;
}
if (multiWindow)
{
    var secondRoot = new CompanionWindowView { Origin = "Started with --multi-window" };
    var secondWindow = application.OpenWindow(
        secondRoot,
        new GpuiWindowOptions
        {
            Title = "GPUI.NET Companion — Window 2",
            Width = 860,
            Height = 620,
            Left = 80,
            Top = 80,
        }
    );
    secondRoot.Window = secondWindow;
}
application.Run();

internal enum SamplePage
{
    Overview,
    Activity,
    Images,
    Inputs,
    Overlays,
    Windows,
}

[GpuiView]
internal sealed partial class SampleShellView : View
{
    private SamplePage _page;

    internal GpuiApplication? Application { get; init; }
    internal GpuiWindow? Window { get; set; }

    [GpuiEvent]
    private void OpenNewWindow()
    {
        var application = Application ?? throw new InvalidOperationException(
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
    private void CloseWindow()
    {
        (Window ?? throw new InvalidOperationException("The sample root has no window handle."))
            .Close();
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

    protected override Element Render(ref RenderContext ui)
    {
        var sidebar = RenderSidebar(ref ui);
        var page = _page switch
        {
            SamplePage.Overview => ui.Child<DashboardView>("content"),
            SamplePage.Activity => ui.Child<ActivityView>("content"),
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
                SamplePage.Images => "Images",
                SamplePage.Inputs => "Inputs",
                SamplePage.Overlays => "Overlays",
                SamplePage.Windows => "Windows",
                _ => throw new InvalidOperationException("Unknown sample page."),
            },
            _page switch
            {
                SamplePage.Overview => "retained ScrollHandle; wheel scrolling stays native",
                SamplePage.Activity => "20,000 variable-height rows; managed rendering is range-batched",
                SamplePage.Images => "GPUI-native decoding, caching, fitting, and grayscale",
                SamplePage.Inputs => "retained native editing, IME, selection, focus, and UTF-8 events",
                SamplePage.Overlays => "deferred overlays, native tooltip timing, flipping, and dismissal",
                SamplePage.Windows => "one application, independent roots, stable handles, isolated state",
                _ => throw new InvalidOperationException("Unknown sample page."),
            }
        );
        var routeHeader = ui.Child<RouteHeaderView, RouteHeaderProps>("route-header", in routeProps);

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

        return ui.HStack(sidebar, content)
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(Rgb(241, 245, 249));
    }

    private Element RenderSidebar(ref RenderContext ui)
    {
        var overviewSelected = _page == SamplePage.Overview;
        var activitySelected = _page == SamplePage.Activity;
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
                        ui.Text("Open a genuinely different root view in the same GPUI application."u8)
                            .FontSize(Px(18))
                            .TextColor(Rgb(15, 23, 42)),
                        ui.Text(
                                "Each window owns its render tree, resources, controls, and failure boundary. Closing the companion leaves this gallery running."u8
                            )
                            .FontSize(Px(13))
                            .TextColor(Rgb(71, 85, 105)),
                        ui.HStack(
                                ui.Button("open-companion-window", "Launch companion window")
                                    .OnClick(Events.OpenNewWindow)
                                    .Padding(Px(11))
                                    .Background(Rgb(79, 70, 229))
                                    .BorderColor(Rgb(67, 56, 202))
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
                ui.Text("The companion includes local state so window isolation is easy to verify."u8)
                    .FontSize(Px(12))
                    .TextColor(Rgb(100, 116, 139))
            )
            .Gap(Px(14))
            .Grow();
}

[GpuiView]
internal sealed partial class CompanionWindowView : View
{
    private int _localCount;

    internal string Origin { get; init; } = "Opened by the sample";
    internal GpuiWindow? Window { get; set; }

    [GpuiEvent]
    private void Increment()
    {
        _localCount++;
        Invalidate();
    }

    [GpuiEvent]
    private void CloseWindow()
    {
        (Window ?? throw new InvalidOperationException("The companion has no window handle."))
            .Close();
    }

    protected override Element Render(ref RenderContext ui)
    {
        var windowId = Window?.Id ?? 0;
        return ui.VStack(
                ui.HStack(
                        ui.VStack(
                                ui.Text("Companion workspace"u8)
                                    .FontSize(Px(24))
                                    .TextColor(Rgb(15, 23, 42)),
                                ui.Text(Origin).FontSize(Px(12)).TextColor(Rgb(100, 116, 139))
                            )
                            .Gap(Px(4)),
                        ui.Spacer(),
                        ui.Badge(ui.Text($"Window {windowId}"))
                            .Background(Rgb(220, 252, 231))
                            .TextColor(Rgb(22, 101, 52))
                            .Padding(Px(8)),
                        ui.Button("close-companion", "Close this window")
                            .OnClick(Events.CloseWindow)
                            .Padding(Px(9))
                    )
                    .Gap(Px(10))
                    .ItemsCenter(),
                ui.HStack(
                        ui.VStack(
                                ui.Text("Independent local state"u8)
                                    .FontSize(Px(16))
                                    .TextColor(Rgb(30, 41, 59)),
                                ui.Text($"Companion count: {_localCount:N0}")
                                    .FontSize(Px(28))
                                    .TextColor(Rgb(79, 70, 229)),
                                ui.Button("increment-companion", "Increment only this window")
                                    .OnClick(Events.Increment)
                                    .Padding(Px(10))
                                    .Background(Rgb(238, 242, 255))
                                    .BorderColor(Rgb(165, 180, 252))
                            )
                            .Gap(Px(14))
                            .Padding(Px(22))
                            .Grow()
                            .Background(Rgb(255, 255, 255))
                            .BorderWidth(Px(1))
                            .BorderColor(Rgb(226, 232, 240))
                            .Radius(Px(12)),
                        ui.VStack(
                                ui.Text("Per-window ownership"u8)
                                    .FontSize(Px(16))
                                    .TextColor(Rgb(30, 41, 59)),
                                ui.Text("• separate root View"u8),
                                ui.Text("• separate native resources"u8),
                                ui.Text("• isolated close lifecycle"u8),
                                ui.Text("• shared application event loop"u8)
                            )
                            .Gap(Px(10))
                            .Padding(Px(22))
                            .Grow()
                            .Background(Rgb(255, 255, 255))
                            .BorderWidth(Px(1))
                            .BorderColor(Rgb(226, 232, 240))
                            .Radius(Px(12))
                    )
                    .Gap(Px(16))
                    .Grow()
            )
            .Gap(Px(18))
            .Padding(Px(24))
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(Rgb(241, 245, 249));
    }
}

internal readonly record struct RouteHeaderProps(string Title, string Detail);

[GpuiView]
internal sealed partial class RouteHeaderView : View<RouteHeaderProps>
{
    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text(Props.Title).FontSize(Px(18)).TextColor(Rgb(15, 23, 42)),
                ui.Text(Props.Detail).FontSize(Px(12)).TextColor(Rgb(100, 116, 139))
            )
            .Gap(Px(3));
}

[GpuiView]
internal sealed partial class DashboardView : View
{
    private ScrollController _scroll;

    protected override void OnMounted(ref ViewContext context)
    {
        _scroll = context.CreateScrollController("overview-scroll");
    }

    [GpuiEvent]
    private void ScrollTop() => _scroll.ScrollToTop();

    [GpuiEvent]
    private void ScrollBottom() => _scroll.ScrollToBottom();

    protected override Element Render(ref RenderContext ui)
    {
        var controls = ui.HStack(
                ui.Button("scroll-top", "Top").OnClick(Events.ScrollTop),
                ui.Button("scroll-bottom", "Bottom").OnClick(Events.ScrollBottom),
                ui.Spacer(),
                ui.Badge(ui.Text("native ScrollHandle"u8))
                    .Background(Rgb(219, 234, 254))
                    .TextColor(Rgb(30, 64, 175))
            )
            .Gap(Px(8))
            .ItemsCenter();

        Span<Element> cards = stackalloc Element[36];
        for (var index = 0; index < cards.Length; index++)
        {
            cards[index] = ui.VStack(
                    ui.Text($"Scrollable card {index + 1:D2}")
                        .FontSize(Px(16))
                        .TextColor(Rgb(15, 23, 42)),
                    ui.Text("Wheel/trackpad updates the Rust-owned offset without a managed callback."u8)
                        .FontSize(Px(12))
                        .TextColor(Rgb(100, 116, 139))
                )
                .Gap(Px(6))
                .Padding(Px(16))
                .Background(Rgb(255, 255, 255))
                .BorderWidth(Px(1))
                .BorderColor(Rgb(226, 232, 240))
                .Radius(Px(10));
        }

        var body = ui.VStack(cards).Gap(Px(10)).Padding(Px(4));
        var scroll = ui.Scroll(
                "overview-scroll",
                ScrollAxis.Vertical,
                new ScrollOptions(smoothScrolling: true, showScrollbar: true),
                body
            )
            .Grow()
            .Width(Percent(100));

        return ui.VStack(controls, scroll).Gap(Px(10)).Grow();
    }
}

[GpuiView]
internal sealed partial class ActivityView : View
{
    private const int ItemCount = 20_000;
    private ListController _list;
    private int _selected = -1;
    private ulong _contentRevision;

    protected override void OnMounted(ref ViewContext context)
    {
        _list = context.CreateListController("activity-list");
    }

    [GpuiEvent]
    private void JumpToMiddle() => _list.ScrollToItem(ItemCount / 2);

    [GpuiEvent]
    private void SelectRow(ClickEvent e)
    {
        var next = checked((int)e.Payload);
        var previous = _selected;
        _selected = next;
        _contentRevision++;

        // The item count and ordering did not change. Refresh only the affected measurements;
        // the controller also invalidates this View so the row content is regenerated.
        if (previous >= 0)
        {
            _list.Refresh(previous, 1);
        }
        _list.Refresh(next, 1);
    }

    [GpuiListItem]
    private Element ActivityRow(int index, ref RenderContext ui)
    {
        var selected = index == _selected;
        var heightHint = index % 7 == 0 ? "variable height" : "normal row";

        return ui.Button("activity-row", $"#{index:N0}  •  {heightHint}")
            .OnClick(Events.SelectRow, checked((ulong)index))
            .Padding(Px(selected ? 18 : index % 7 == 0 ? 14 : 9))
            .Background(selected ? Rgb(224, 231, 255) : Rgb(255, 255, 255))
            .BorderColor(selected ? Rgb(99, 102, 241) : Rgb(226, 232, 240))
            .TextColor(selected ? Rgb(55, 48, 163) : Rgb(30, 41, 59));
    }

    protected override Element Render(ref RenderContext ui)
    {
        var header = ui.HStack(
                ui.Text($"{ItemCount:N0} virtual rows")
                    .FontSize(Px(14))
                    .TextColor(Rgb(30, 41, 59)),
                ui.Spacer(),
                ui.Button("jump-middle", "Jump to 10,000")
                    .OnClick(Events.JumpToMiddle)
                    .Padding(Px(8))
            )
            .ItemsCenter();

        var list = ui.List(
                "activity-list",
                new ListDataSource(ItemCount, _contentRevision),
                Rows.ActivityRow,
                new ListOptions(
                    batchSize: 48,
                    overdraw: 320,
                    estimatedItemHeight: 40,
                    smoothScrolling: true,
                    showScrollbar: true
                )
            )
            .Grow()
            .Width(Percent(100));

        return ui.VStack(header, list).Gap(Px(10)).Grow();
    }
}

[GpuiView]
internal sealed partial class ImageGalleryView : View
{
    private static readonly string ImagePath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "gpui-net.svg"
    );

    protected override Element Render(ref RenderContext ui)
    {
        var contain = ImageCard(ref ui, "Contain", ImageFit.Contain, false);
        var cover = ImageCard(ref ui, "Cover", ImageFit.Cover, false);
        var grayscale = ImageCard(ref ui, "Grayscale", ImageFit.Cover, true);

        return ui.VStack(
                ui.Text("One cached SVG, rendered with three native presentation modes."u8)
                    .FontSize(Px(13))
                    .TextColor(Rgb(71, 85, 105)),
                ui.HStack(contain, cover, grayscale).Gap(Px(14)).JustifyCenter().Grow()
            )
            .Gap(Px(14))
            .Grow();
    }

    private static Element ImageCard(
        ref RenderContext ui,
        ReadOnlySpan<char> label,
        ImageFit fit,
        bool grayscale
    )
    {
        var image = ui.Image(ImagePath)
            .Fit(fit)
            .Grayscale(grayscale)
            .Width(Percent(100))
            .Height(Px(180))
            .Radius(Px(12))
            .Background(Rgb(226, 232, 240));

        return ui.VStack(
                ui.Text(label).FontSize(Px(14)).TextColor(Rgb(30, 41, 59)),
                image
            )
            .Gap(Px(8))
            .Padding(Px(12))
            .Width(Px(240))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(226, 232, 240))
            .Radius(Px(12));
    }
}

[GpuiView]
internal sealed partial class InputGalleryView : View
{
    private InputController _search;
    private string _value = "Type in the first field";
    private string _lastEvent = "No input event yet";

    protected override void OnMounted(ref ViewContext context)
    {
        _search = context.CreateInputController("search"u8);
    }

    [GpuiEvent]
    private void SearchChanged(InputEvent input)
    {
        _value = input.Value;
        _lastEvent = $"Changed · revision {input.Revision} · {input.Utf8Value.Length} UTF-8 bytes";
        Invalidate();
    }

    [GpuiEvent]
    private void SearchSubmitted(InputEvent input)
    {
        _lastEvent = $"Submitted “{input.Value}” · revision {input.Revision}";
        Invalidate();
    }

    [GpuiEvent]
    private void SearchFocusChanged(InputEvent input)
    {
        _lastEvent = input.IsFocused ? "Focused" : "Blurred";
        Invalidate();
    }

    [GpuiEvent]
    private void FocusSearch() => _search.Focus();

    [GpuiEvent]
    private void SelectSearch() => _search.SelectAll();

    [GpuiEvent]
    private void SetJapanese() => _search.SetValue("東京からこんにちは"u8);

    [GpuiEvent]
    private void ClearSearch() => _search.SetValue(ReadOnlySpan<byte>.Empty);

    protected override Element Render(ref RenderContext ui)
    {
        var search = Field(
            ref ui,
            "Interactive + UTF-8 events",
            ui.Input(
                    "search"u8,
                    new Utf8InputOptions(placeholder: "Search or enter 日本語…"u8)
                )
                .OnChanged(Events.SearchChanged)
                .OnSubmitted(Events.SearchSubmitted)
                .OnFocusChanged(Events.SearchFocusChanged)
                .Width(Percent(100))
        );
        var password = Field(
            ref ui,
            "Password masking",
            ui.Input(
                    "password"u8,
                    new Utf8InputOptions(placeholder: "Native password field"u8, password: true)
                )
                .Width(Percent(100))
        );
        var readOnly = Field(
            ref ui,
            "Read only",
            ui.Input(
                    "read-only"u8,
                    new Utf8InputOptions("Selectable native text"u8, readOnly: true)
                )
                .Width(Percent(100))
        );
        var disabled = Field(
            ref ui,
            "Disabled",
            ui.Input(
                    "disabled"u8,
                    new Utf8InputOptions("Cannot focus or edit"u8, disabled: true)
                )
                .Width(Percent(100))
        );

        var controls = ui.HStack(
                ui.Button("focus-search", "Focus").OnClick(Events.FocusSearch).Padding(Px(8)),
                ui.Button("select-search", "Select all").OnClick(Events.SelectSearch).Padding(Px(8)),
                ui.Button("set-japanese", "Set UTF-8").OnClick(Events.SetJapanese).Padding(Px(8)),
                ui.Button("clear-search", "Clear").OnClick(Events.ClearSearch).Padding(Px(8))
            )
            .Gap(Px(8));

        return ui.VStack(
                ui.Text("Editing and pointer/keyboard state stay in Rust. Managed callbacks are opt-in."u8)
                    .FontSize(Px(13))
                    .TextColor(Rgb(71, 85, 105)),
                ui.HStack(search, password).Gap(Px(14)),
                ui.HStack(readOnly, disabled).Gap(Px(14)),
                controls,
                ui.VStack(
                        ui.Text($"Value: {_value}").TextColor(Rgb(30, 41, 59)),
                        ui.Text(_lastEvent).FontSize(Px(12)).TextColor(Rgb(79, 70, 229))
                    )
                    .Gap(Px(5))
                    .Padding(Px(12))
                    .Background(Rgb(238, 242, 255))
                    .Radius(Px(8))
            )
            .Gap(Px(14))
            .Grow();
    }

    private static Element Field(
        ref RenderContext ui,
        ReadOnlySpan<char> label,
        Element input
    ) =>
        ui.VStack(ui.Text(label).FontSize(Px(12)).TextColor(Rgb(71, 85, 105)), input)
            .Gap(Px(6))
            .Padding(Px(12))
            .Width(Percent(50))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(226, 232, 240))
            .Radius(Px(10));
}

internal enum OverlayDemo
{
    None,
    Dialog,
    Sheet,
}

[GpuiView]
internal sealed partial class OverlayGalleryView : View
{
    private OverlayDemo _open;
    private int _backgroundClicks;

    [GpuiEvent]
    private void OpenDialog()
    {
        _open = OverlayDemo.Dialog;
        Invalidate();
    }

    [GpuiEvent]
    private void OpenSheet()
    {
        _open = OverlayDemo.Sheet;
        Invalidate();
    }

    [GpuiEvent]
    private void CloseOverlay()
    {
        _open = OverlayDemo.None;
        Invalidate();
    }

    [GpuiEvent]
    private void CountBackgroundClick()
    {
        _backgroundClicks++;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui)
    {
        var page = ui.VStack(
                ui.Text("Overlays are window-relative and paint after normal content."u8)
                    .FontSize(Px(13))
                    .TextColor(Rgb(71, 85, 105)),
                ui.HStack(
                        ui.Button("open-dialog", "Open centered dialog")
                            .OnClick(Events.OpenDialog)
                            .Padding(Px(10))
                            .Background(Rgb(79, 70, 229))
                            .BorderColor(Rgb(99, 102, 241))
                            .TextColor(Rgb(255, 255, 255)),
                        ui.Button("open-sheet", "Open right sheet")
                            .OnClick(Events.OpenSheet)
                            .Padding(Px(10)),
                        ui.Button("background-action", $"Background action · {_backgroundClicks}")
                            .OnClick(Events.CountBackgroundClick)
                            .Padding(Px(10))
                    )
                    .Gap(Px(10)),
                ui.HStack(
                        ui.Tooltip(
                            "default-tooltip"u8,
                            ui.Button("hover-default", "Hover for details").Padding(Px(10)),
                            ui.VStack(
                                    ui.Text("Native tooltip"u8)
                                        .FontSize(Px(13))
                                        .TextColor(Rgb(255, 255, 255)),
                                    ui.Text("Shows after 500 ms and stays open while you cross the gap."u8)
                                        .FontSize(Px(11))
                                        .TextColor(Rgb(203, 213, 225))
                                )
                                .Gap(Px(5))
                                .Padding(Px(10))
                                .Width(Px(260))
                                .Background(Rgb(15, 23, 42))
                                .Radius(Px(8))
                        ),
                        ui.Tooltip(
                            "right-tooltip"u8,
                            ui.Button("hover-right", "Prefer right side").Padding(Px(10)),
                            ui.Text("Flips and clamps when the preferred side does not fit."u8)
                                .FontSize(Px(11))
                                .TextColor(Rgb(255, 255, 255))
                                .Padding(Px(9))
                                .Width(Px(220))
                                .Background(Rgb(79, 70, 229))
                                .Radius(Px(8)),
                            new TooltipOptions(placement: TooltipPlacement.Right)
                        )
                    )
                    .Gap(Px(10)),
                ui.VStack(
                        ui.Text("Interaction checks"u8)
                            .FontSize(Px(14))
                            .TextColor(Rgb(30, 41, 59)),
                        ui.Text("• Tooltips use deferred content and disappear on pointer press."u8),
                        ui.Text("• Click the dim backdrop or press Escape to dismiss."u8),
                        ui.Text("• The background action must not fire through the backdrop."u8),
                        ui.Text("• Tab and Shift+Tab stay inside the modal; try IME composition."u8)
                    )
                    .Gap(Px(8))
                    .Padding(Px(18))
                    .Background(Rgb(255, 255, 255))
                    .BorderWidth(Px(1))
                    .BorderColor(Rgb(226, 232, 240))
                    .Radius(Px(12))
            )
            .Gap(Px(18))
            .Grow();

        if (_open == OverlayDemo.None)
        {
            return page;
        }

        var isSheet = _open == OverlayDemo.Sheet;
        var title = isSheet ? "Window-edge sheet" : "Centered dialog";
        var panel = ui.VStack(
                ui.HStack(
                        ui.Text(title).FontSize(Px(18)).TextColor(Rgb(15, 23, 42)),
                        ui.Spacer(),
                        ui.Button("close-overlay", "Close")
                            .OnClick(Events.CloseOverlay)
                            .Padding(Px(8))
                    )
                    .ItemsCenter(),
                ui.Text("This subtree is still ordinary semantic GPUI.NET content."u8)
                    .FontSize(Px(12))
                    .TextColor(Rgb(100, 116, 139)),
                ui.Input(
                        "overlay-first-input"u8,
                        new Utf8InputOptions(placeholder: "First modal tab stop…"u8)
                    )
                    .Width(Percent(100)),
                ui.Input(
                        "overlay-second-input"u8,
                        new Utf8InputOptions(placeholder: "Second modal tab stop; IME works here…"u8)
                    )
                    .Width(Percent(100)),
                ui.Text("Escape is handled after focused child controls get the key first."u8)
                    .FontSize(Px(12))
                    .TextColor(Rgb(79, 70, 229))
            )
            .Gap(Px(14))
            .Padding(Px(20))
            .Width(Px(isSheet ? 380 : 440))
            .Height(isSheet ? Percent(100) : Px(290))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(203, 213, 225))
            .Radius(Px(14));

        var options = new OverlayOptions(
            placement: isSheet ? OverlayPlacement.Right : OverlayPlacement.Center,
            margin: isSheet ? 12 : 24,
            backdrop: Rgba(15, 23, 42, 150)
        );
        var overlay = ui
            .Overlay("sample-overlay"u8, panel, options)
            .OnDismiss(Events.CloseOverlay);

        return ui.VStack(page, overlay).Grow().Height(Percent(100));
    }
}

[GpuiView]
internal sealed partial class CounterCardView : View
{
    private int _count;

    [GpuiEvent]
    private async ValueTask Increment(ClickEvent _)
    {
        await Task.Delay(100, Lifetime);
        _count++;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text($"Counter: {_count:N0}"),
                ui.Button("increment", "Async increment")
                    .OnClick(Events.Increment)
                    .Padding(Px(10))
            )
            .Gap(Px(10));
}

internal sealed class ArenaGrowthView : View
{
    private static readonly string LargePayload = new('x', 20 * 1024);

    protected override Element Render(ref RenderContext ui)
    {
        Span<Element> rows = stackalloc Element[600];
        rows[0] = ui.Text(LargePayload);
        for (var index = 1; index < rows.Length; index++)
        {
            rows[index] = ui.Text($"Growth row {index:D4}")
                .Padding(Px(1))
                .Width(Px(120))
                .Background(Rgb(246, 248, 251))
                .ItemsCenter();
        }

        return ui.VStack(rows).Gap(Px(1));
    }
}
