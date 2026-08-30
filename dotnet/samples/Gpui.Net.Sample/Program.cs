using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

var stressGrowth = args.Contains("--stress-growth", StringComparer.Ordinal);
GpuiApplication.Run(
    stressGrowth ? new ArenaGrowthView() : new SampleShellView(),
    new GpuiApplicationOptions
    {
        Title = stressGrowth ? "GPUI.NET Arena Growth" : "GPUI.NET Component Gallery",
        Width = 920,
        Height = 620,
    }
);

internal sealed class SampleShellView : View
{
    private readonly SidebarView _sidebar = new();
    private readonly DashboardView _dashboard = new();

    protected override Element Render(ref RenderContext ui)
    {
        var sidebar = RenderView(_sidebar, ref ui);
        var dashboard = RenderView(_dashboard, ref ui);

        var topBar = ui.HStack(
                ui.VStack(
                        ui.Text("GPUI.NET COMPONENT GALLERY"u8)
                            .FontSize(Px(18))
                            .TextColor(Rgb(15, 23, 42)),
                        ui.Text("generated events · retained native snapshot · managed state"u8)
                            .FontSize(Px(12))
                            .TextColor(Rgb(100, 116, 139))
                    )
                    .Gap(Px(3)),
                ui.Spacer(),
                ui.Badge(ui.Text("ABI"u8))
                    .Background(Rgb(224, 231, 255))
                    .TextColor(Rgb(67, 56, 202))
                    .Padding(Px(7))
            )
            .ItemsCenter();

        var content = ui.VStack(topBar, ui.Divider(), dashboard).Gap(Px(14)).Padding(Px(22)).Grow();

        return ui.HStack(sidebar, content)
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(Rgb(241, 245, 249));
    }
}

[GpuiView]
internal sealed partial class SidebarView : View
{
    private bool _activitySelected;

    [GpuiEvent]
    private void ShowOverview() => SelectActivity(false);

    [GpuiEvent]
    private void ShowActivity() => SelectActivity(true);

    protected override Element Render(ref RenderContext ui)
    {
        var selected = _activitySelected ? "Activity" : "Overview";

        return ui.VStack(
                ui.Text("SIDEBAR VIEW"u8).FontSize(Px(11)).TextColor(Rgb(148, 163, 184)),
                ui.Divider().Background(Rgb(51, 65, 85)),
                ui.Button("show-overview", "Overview")
                    .OnClick(Events.ShowOverview)
                    .Padding(Px(10))
                    .Background(_activitySelected ? Rgb(15, 23, 42) : Rgb(51, 65, 85))
                    .BorderColor(_activitySelected ? Rgb(51, 65, 85) : Rgb(99, 102, 241))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Button("show-activity", "Activity")
                    .OnClick(Events.ShowActivity)
                    .Padding(Px(10))
                    .Background(_activitySelected ? Rgb(79, 70, 229) : Rgb(15, 23, 42))
                    .BorderColor(_activitySelected ? Rgb(129, 140, 248) : Rgb(51, 65, 85))
                    .TextColor(Rgb(255, 255, 255)),
                ui.Spacer(),
                ui.Text($"Selected: {selected}").FontSize(Px(12)).TextColor(Rgb(148, 163, 184)),
                ui.Text("Callbacks are generated; no constructor registration exists."u8)
                    .FontSize(Px(11))
                    .TextColor(Rgb(100, 116, 139))
            )
            .Gap(Px(10))
            .Padding(Px(18))
            .Width(Px(220))
            .Height(Percent(100))
            .Background(Rgb(15, 23, 42));
    }

    private void SelectActivity(bool selected)
    {
        _activitySelected = selected;
        Invalidate();
    }
}

internal sealed class DashboardView : View
{
    private readonly CounterCardView _counter = new();
    private readonly PreferencesView _preferences = new();

    protected override Element Render(ref RenderContext ui)
    {
        var architecture = ui.VStack(
                ui.Badge(ui.Text("RUNTIME"u8))
                    .Background(Rgb(219, 234, 254))
                    .TextColor(Rgb(30, 41, 59)),
                ui.Text("C# state → render IR → Rust GPUI"u8)
                    .FontSize(Px(16))
                    .TextColor(Rgb(15, 23, 42))
            )
            .Gap(Px(8))
            .Padding(Px(14))
            .Grow()
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(226, 232, 240))
            .Radius(Px(10));

        return ui.VStack(
                architecture,
                ui.HStack(RenderView(_counter, ref ui), RenderView(_preferences, ref ui))
                    .Gap(Px(14))
            )
            .Gap(Px(14))
            .Grow();
    }
}

[GpuiView]
internal sealed partial class CounterCardView : View
{
    private int _count;

    protected override void OnMounted(ref ViewContext context) =>
        Console.WriteLine("counter mounted on GPUI UI thread");

    protected override void OnUnmounted(ref ViewContext context) =>
        Console.WriteLine("counter unmounted");

    [GpuiEvent]
    private async ValueTask Increment(ClickEvent _)
    {
        await Task.Delay(100);
        _count++;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui)
    {
        var button = ui.Button("increment", "Async increment")
            .OnClick(Events.Increment)
            .Padding(Px(10))
            .Background(Rgb(37, 99, 235))
            .BorderColor(Rgb(96, 165, 250))
            .TextColor(Rgb(255, 255, 255));

        return ui.VStack(
                ui.Text("COUNTER CARD VIEW"u8).FontSize(Px(11)).TextColor(Rgb(100, 116, 139)),
                ui.Text($"{_count:N0}").FontSize(Px(42)).TextColor(Rgb(15, 23, 42)),
                ui.Text("ValueTask completion and exceptions are observed by the runtime."u8)
                    .FontSize(Px(12))
                    .TextColor(Rgb(100, 116, 139)),
                ui.Spacer(),
                button
            )
            .Gap(Px(12))
            .Padding(Px(18))
            .Width(Px(300))
            .Height(Px(300))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(226, 232, 240))
            .Radius(Px(12));
    }
}

[GpuiView]
internal sealed partial class PreferencesView : View
{
    private bool _telemetry = true;
    private bool _compact;

    [GpuiEvent]
    private void ToggleTelemetry()
    {
        _telemetry = !_telemetry;
        Invalidate();
    }

    [GpuiEvent]
    private void SelectComfortable()
    {
        _compact = false;
        Invalidate();
    }

    [GpuiEvent]
    private void SelectCompact()
    {
        _compact = true;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text("PREFERENCES VIEW"u8).FontSize(Px(11)).TextColor(Rgb(100, 116, 139)),
                ui.Divider(),
                ui.HStack(
                        ui.Text("Telemetry"u8).TextColor(Rgb(30, 41, 59)),
                        ui.Spacer(),
                        ui.Badge(ui.Text(_telemetry ? "ON"u8 : "OFF"u8))
                            .Background(_telemetry ? Rgb(220, 252, 231) : Rgb(241, 245, 249))
                            .TextColor(_telemetry ? Rgb(21, 128, 61) : Rgb(100, 116, 139))
                    )
                    .ItemsCenter(),
                ui.Checkbox("telemetry", "Share anonymous performance counters")
                    .Checked(_telemetry)
                    .OnClick(Events.ToggleTelemetry)
                    .Padding(Px(8))
                    .Radius(Px(8))
                    .Background(Rgb(248, 250, 252)),
                ui.Text("Density"u8).TextColor(Rgb(30, 41, 59)),
                ui.Radio("comfortable", "Comfortable")
                    .Checked(!_compact)
                    .OnClick(Events.SelectComfortable)
                    .Padding(Px(8)),
                ui.Radio("compact", "Compact")
                    .Checked(_compact)
                    .OnClick(Events.SelectCompact)
                    .Padding(Px(8)),
                ui.Spacer(),
                ui.Text("Render() only describes UI; it does not mutate managed state."u8)
                    .FontSize(Px(11))
                    .TextColor(Rgb(100, 116, 139))
            )
            .Gap(Px(10))
            .Padding(Px(18))
            .Grow()
            .Height(Px(300))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(226, 232, 240))
            .Radius(Px(12));
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
