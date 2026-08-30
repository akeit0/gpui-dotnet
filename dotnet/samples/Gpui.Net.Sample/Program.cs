using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

var stressGrowth = args.Contains("--stress-growth", StringComparer.Ordinal);
GpuiApplication.Run(
    stressGrowth ? new ArenaGrowthView() : new SampleShellView(),
    new GpuiApplicationOptions
    {
        Title = stressGrowth ? "GPUI.NET Arena Growth" : "GPUI.NET Scroll + Virtual List",
        Width = 1040,
        Height = 700,
    }
);

internal enum SamplePage
{
    Overview,
    Activity,
}

[GpuiView]
internal sealed partial class SampleShellView : View
{
    private SamplePage _page;

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
            _ => throw new InvalidOperationException("Unknown sample page."),
        };
        var pageSlot = ui.VStack(page).Grow().Height(Percent(100));

        var routeProps = new RouteHeaderProps(
            _page == SamplePage.Overview ? "Overview" : "Activity",
            _page == SamplePage.Overview
                ? "retained ScrollHandle; wheel scrolling stays native"
                : "20,000 variable-height rows; managed rendering is range-batched"
        );
        var routeHeader = ui.Child<RouteHeaderView, RouteHeaderProps>("route-header", in routeProps);

        var topBar = ui.HStack(
                routeHeader,
                ui.Spacer(),
                ui.Badge(ui.Text("ABI V3"u8))
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
        var activitySelected = _page == SamplePage.Activity;
        return ui.VStack(
                ui.Text("SIDEBAR"u8).FontSize(Px(11)).TextColor(Rgb(148, 163, 184)),
                ui.Divider().Background(Rgb(51, 65, 85)),
                ui.Button("show-overview", "Scroll view")
                    .OnClick(Events.ShowOverview)
                    .Padding(Px(10))
                    .Background(activitySelected ? Rgb(15, 23, 42) : Rgb(79, 70, 229))
                    .BorderColor(activitySelected ? Rgb(51, 65, 85) : Rgb(129, 140, 248))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-activity", "Virtual list")
                    .OnClick(Events.ShowActivity)
                    .Padding(Px(10))
                    .Background(activitySelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(activitySelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
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
        var scroll = ui.Scroll("overview-scroll", ScrollAxis.Vertical, body)
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
                ItemCount,
                Rows.ActivityRow,
                new ListOptions(batchSize: 48, overdraw: 320)
            )
            .Grow()
            .Width(Percent(100));

        return ui.VStack(header, list).Gap(Px(10)).Grow();
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
