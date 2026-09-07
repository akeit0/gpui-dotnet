using System.Diagnostics;
using static Gpui.Units;

namespace Gpui;

internal readonly record struct StatsProps(TravelStore Store, ulong Revision);

internal readonly record struct StatsInput(TravelStore Store, ulong Revision);

internal readonly record struct StatsData(int TotalLikes, int DoneDays, float AvgRating, int Trips);

/// <summary>Stats tab: memo summary, week bars, animated vector chart.</summary>
[GpuiView]
internal sealed partial class StatsView : View<StatsProps>
{
    private const double AnimationSeconds = 0.8;
    private readonly Memo<StatsInput, StatsData> _summary;
    private long _animationStart;

    public StatsView(ViewConstruction construction, StatsProps initialProps)
        : base(construction)
    {
        _summary = construction.Memo<StatsInput, StatsData>();
        _animationStart = Stopwatch.GetTimestamp();
    }


    private static StatsData Summarize(TravelStore store)
    {
        var likes = 0;
        foreach (var dest in store.Destinations)
        {
            likes += dest.Likes;
        }
        foreach (var entry in store.Entries)
        {
            likes += entry.Likes;
        }
        var days = 0;
        var rating = 0;
        foreach (var trip in store.Trips)
        {
            foreach (var done in trip.Days)
            {
                if (done)
                {
                    days++;
                }
            }
            rating += trip.Rating;
        }
        var average = store.Trips.Count == 0 ? 0 : (float)rating / store.Trips.Count;
        return new StatsData(likes, days, average, store.Trips.Count);
    }

    private void Replay()
    {
        _animationStart = Stopwatch.GetTimestamp();
        Invalidate();
    }

    protected override Element Render(in StatsProps props, ref RenderContext ui)
    {
        var theme = ui.Theme;
        var stats = _summary.Get(
            new StatsInput(props.Store, props.Revision),
            static input => Summarize(input.Store)
        );

        var progress = AnimationProgress();
        return ui.VStack(
                ui.HStack(
                        ui.Text("Your season")
                            .FontSize(Px(theme.Typography.Heading))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Button("replay-chart", "Replay")
                            .OnClick(this, static (view, _) => view.Replay())
                            .Style(WanderStyles.Button(theme, WanderButtonVariant.Primary))
                    )
                    .ItemsCenter(),
                WeekBars(ref ui, stats),
                ui.Dynamic(
                    progress.Active,
                    Chart(ref ui, stats, progress.Value)
                ),
                GoalBar(ref ui, props.Store)
            )
            .Gap(Px(12))
            .Grow();
    }

    private (float Value, bool Active) AnimationProgress()
    {
        if (_animationStart == 0)
        {
            return (1, false);
        }
        var linear = Math.Clamp(
            Stopwatch.GetElapsedTime(_animationStart).TotalSeconds / AnimationSeconds,
            0,
            1
        );
        return ((float)(1 - Math.Pow(1 - linear, 3)), linear < 1);
    }

    private static Element WeekBars(ref RenderContext ui, StatsData stats)
    {
        var theme = ui.Theme;
        Span<Element> bars =
        [
            DayBar(ref ui, "M", stats.DoneDays * 11 % 100),
            DayBar(ref ui, "T", stats.DoneDays * 23 % 100),
            DayBar(ref ui, "W", stats.DoneDays * 37 % 100),
            DayBar(ref ui, "T", stats.DoneDays * 41 % 100),
            DayBar(ref ui, "F", stats.DoneDays * 53 % 100),
            DayBar(ref ui, "S", stats.DoneDays * 67 % 100),
            DayBar(ref ui, "S", stats.DoneDays * 79 % 100),
        ];
        return ui.VStack(
                ui.Text($"Likes {stats.TotalLikes:N0} · days out {stats.DoneDays:N0} · avg ★ {stats.AvgRating:0.0}")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(bars).Gap(Px(8)).Height(Px(110)).ItemsEnd()
            )
            .Gap(Px(8))
            .Padding(Px(14))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(16));
    }

    private static Element DayBar(ref RenderContext ui, string label, int fill)
    {
        var theme = ui.Theme;
        return ui.VStack(
                ui.Div()
                    .Width(Percent(100))
                    .Height(Percent(fill))
                    .Background(theme.Colors.Accent)
                    .Radius(Px(6)),
                ui.Text(label)
                    .FontSize(Px(theme.Typography.Caption))
                    .TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(4))
            .Width(Percent(14))
            .Height(Percent(100))
            .ItemsCenter()
            .JustifyEnd();
    }

    private static Element Chart(ref RenderContext ui, StatsData stats, float progress)
    {
        var theme = ui.Theme;
        var top = 100 - 70 * progress;
        var mid = 100 - (20 + stats.Trips * 9) * progress;
        var area = ui.Path()
            .MoveTo(0, 100)
            .LineTo(0, mid).LineTo(33, top).LineTo(66, mid).LineTo(100, 100 - 78 * progress)
            .LineTo(100, 100)
            .Close()
            .Fill(theme.Colors.Accent.WithAlpha(52));
        var line = ui.Path()
            .MoveTo(0, mid)
            .LineTo(33, top).LineTo(66, mid).LineTo(100, 100 - 78 * progress)
            .Stroke(theme.Colors.Accent, Px(3));
        var dot = ui.Circle(100, 100 - 78 * progress, 2.5f)
            .Fill(theme.Colors.SurfaceBackground)
            .Stroke(theme.Colors.Accent, Px(2));
        return ui.Drawing(area, line, dot)
            .ViewBox(0, 0, 100, 110)
            .Width(Percent(100))
            .Height(Px(180))
            .Padding(Px(12))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(16));
    }

    private static Element GoalBar(ref RenderContext ui, TravelStore store)
    {
        var theme = ui.Theme;
        var percent = store.GoalKm <= 0 ? 0 : Math.Clamp(store.WalkedKm / store.GoalKm * 100, 0, 100);
        return ui.VStack(
                ui.HStack(
                        ui.Text("Season goal")
                            .FontSize(Px(theme.Typography.BodySmall))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Text($"{store.WalkedKm:0}/{store.GoalKm:0} km")
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.TextMuted)
                    )
                    .ItemsCenter(),
                ui.Div(
                        ui.Div()
                            .Width(Percent(percent))
                            .Height(Percent(100))
                            .Background(theme.Colors.Success)
                            .Radius(Px(6))
                    )
                    .Height(Px(12))
                    .Width(Percent(100))
                    .Background(theme.Colors.ElementActive)
                    .Radius(Px(6))
            )
            .Gap(Px(8))
            .Padding(Px(14))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(16));
    }
}
