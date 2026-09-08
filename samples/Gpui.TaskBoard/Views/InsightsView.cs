using static Gpui.Units;

namespace Gpui;

/// <summary>Props for the insights panel. The revision forces a rerender on every store change.</summary>
internal readonly record struct InsightsProps(TaskStore Store, ulong Revision);

internal readonly record struct InsightsInput(TaskStore Store, ulong Revision);

internal readonly record struct InsightsData(
    int Todo,
    int Doing,
    int Review,
    int Done,
    float AverageEstimate,
    int OpenEstimate
);

/// <summary>
/// Retained child View showing board statistics and a vector chart. Pure derived data comes
/// from an owned <see cref="Memo{TInput, TResult}"/>; the chart is a native Drawing.
/// Static memo calculation, stack-span composition, and arena-direct text reduce allocations;
/// this sample does not measure end-to-end render allocation costs.
/// </summary>
[GpuiView]
internal sealed partial class InsightsView : View<InsightsProps>
{
    private readonly Memo<InsightsInput, InsightsData> _stats;

    public InsightsView(ViewConstruction construction, InsightsProps initialProps)
        : base(construction) => _stats = construction.Memo<InsightsInput, InsightsData>();

    private static InsightsData Summarize(TaskStore store)
    {
        var todo = 0;
        var doing = 0;
        var review = 0;
        var done = 0;
        var estimate = 0f;
        var openEstimate = 0f;
        foreach (var task in store.Tasks)
        {
            switch (task.Status)
            {
                case TaskStatus.Todo:
                    todo++;
                    break;
                case TaskStatus.InProgress:
                    doing++;
                    break;
                case TaskStatus.Review:
                    review++;
                    break;
                case TaskStatus.Done:
                    done++;
                    break;
            }
            estimate += task.EstimateHours;
            if (!task.Completed)
            {
                openEstimate += task.EstimateHours;
            }
        }
        var average = store.Tasks.Count == 0 ? 0 : estimate / store.Tasks.Count;
        return new InsightsData(todo, doing, review, done, average, (int)openEstimate);
    }

    protected override Element Render(in InsightsProps props, ref RenderContext ui)
    {
        var theme = ui.Theme;
        var stats = _stats.Get(
            new InsightsInput(props.Store, props.Revision),
            static input => Summarize(input.Store)
        );

        // Fixed-size collection expressions compile to stack spans: no heap allocation.
        Span<Element> cards =
        [
            StatCard(ref ui, "To do", stats.Todo, theme.Colors.TextMuted),
            StatCard(ref ui, "Doing", stats.Doing, theme.Colors.Info),
            StatCard(ref ui, "Review", stats.Review, theme.Colors.Warning),
            StatCard(ref ui, "Done", stats.Done, theme.Colors.Success),
        ];

        var total = Math.Max(1, stats.Todo + stats.Doing + stats.Review + stats.Done);
        return ui.VStack(
                ui.HStack(cards).Gap(Px(10)),
                DistributionBar(ref ui, stats, total),
                VelocityChart(ref ui, stats),
                ui.Text(
                        $"Average estimate {stats.AverageEstimate:0.0}h · open work ≈ {stats.OpenEstimate}h"
                    )
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(14))
            .Padding(Px(16))
            .Grow();
    }

    private static Element StatCard(ref RenderContext ui, string label, int value, Color accent)
    {
        var theme = ui.Theme;
        return ui.VStack(
                ui.Text(label)
                    .FontSize(Px(theme.Typography.Caption))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text($"{value:N0}").FontSize(Px(theme.Typography.Heading)).TextColor(accent)
            )
            .Gap(Px(2))
            .Padding(Px(12))
            .Width(Percent(25))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(10));
    }

    private static Element DistributionBar(ref RenderContext ui, InsightsData stats, int total)
    {
        var theme = ui.Theme;
        Span<Element> segments =
        [
            Segment(ref ui, stats.Todo, total, theme.Colors.TextPlaceholder),
            Segment(ref ui, stats.Doing, total, theme.Colors.Info),
            Segment(ref ui, stats.Review, total, theme.Colors.Warning),
            Segment(ref ui, stats.Done, total, theme.Colors.Success),
        ];
        return ui.VStack(
                ui.Text("Status distribution")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(segments)
                    .Height(Px(14))
                    .Width(Percent(100))
                    .Background(theme.Colors.ElementActive)
                    .Radius(Px(7))
            )
            .Gap(Px(6));
    }

    private static Element Segment(ref RenderContext ui, int count, int total, Color color) =>
        ui.Div()
            .Width(Percent(total == 0 ? 0 : (float)count / total * 100))
            .Height(Percent(100))
            .Background(count == 0 ? Colors.Rgba(0, 0, 0, 0) : color);

    private static Element VelocityChart(ref RenderContext ui, InsightsData stats)
    {
        var theme = ui.Theme;
        // Deterministic pseudo-velocity derived from current counts: the chart shows how
        // vector Drawing maps stable coordinates through a ViewBox without managed painting.
        var p0 = 88 - ((stats.Done * 1 + stats.Doing * 7) % 70);
        var p1 = 88 - ((stats.Done * 2 + stats.Doing * 6) % 70);
        var p2 = 88 - ((stats.Done * 3 + stats.Doing * 5) % 70);
        var p3 = 88 - ((stats.Done * 4 + stats.Doing * 4) % 70);
        var p4 = 88 - ((stats.Done * 5 + stats.Doing * 3) % 70);
        var p5 = 88 - ((stats.Done * 6 + stats.Doing * 2) % 70);
        var p6 = 88 - ((stats.Done * 7 + stats.Doing * 1) % 70);
        var p7 = 88 - ((stats.Done * 8 + stats.Doing * 0) % 70);
        var area = ui.Path()
            .MoveTo(0, 100)
            .LineTo(0, p0)
            .LineTo(14.3f, p1)
            .LineTo(28.6f, p2)
            .LineTo(42.9f, p3)
            .LineTo(57.1f, p4)
            .LineTo(71.4f, p5)
            .LineTo(85.7f, p6)
            .LineTo(100, p7)
            .LineTo(100, 100)
            .Close()
            .Fill(theme.Colors.Success.WithAlpha(48));
        var line = ui.Path()
            .MoveTo(0, p0)
            .LineTo(14.3f, p1)
            .LineTo(28.6f, p2)
            .LineTo(42.9f, p3)
            .LineTo(57.1f, p4)
            .LineTo(71.4f, p5)
            .LineTo(85.7f, p6)
            .LineTo(100, p7)
            .Stroke(theme.Colors.Success, Px(2));
        var baseline = ui.Line(0, 100, 100, 100)
            .Stroke(theme.Colors.BorderVariant, Px(1))
            .Dash(Px(3), Px(3));
        var dot = ui.Circle(100, p7, 2.5f)
            .Fill(theme.Colors.SurfaceBackground)
            .Stroke(theme.Colors.Success, Px(2));
        return ui.VStack(
                ui.Text("Throughput trend")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Drawing(area, baseline, line, dot)
                    .ViewBox(0, 0, 100, 110)
                    .Width(Percent(100))
                    .Height(Px(170))
            )
            .Gap(Px(6))
            .Padding(Px(12))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(10));
    }
}
