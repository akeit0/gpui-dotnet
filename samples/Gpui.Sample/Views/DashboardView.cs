using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class DashboardView : View
{
    private ScrollController _scroll;
    private int _propsRevision;
    private bool _reverseCounters;

    protected override void OnMounted(ref ViewContext context)
    {
        _scroll = context.CreateScrollController("overview-scroll");
    }

    protected override Element Render(ref RenderContext ui)
    {
        var alphaProps = new CounterCardProps("Alpha slot", _propsRevision);
        var betaProps = new CounterCardProps("Beta slot", _propsRevision);
        Element firstCounter;
        Element secondCounter;
        if (_reverseCounters)
        {
            firstCounter = ui.Child<CounterCardView, CounterCardProps>("beta", in betaProps);
            secondCounter = ui.Child<CounterCardView, CounterCardProps>("alpha", in alphaProps);
        }
        else
        {
            firstCounter = ui.Child<CounterCardView, CounterCardProps>("alpha", in alphaProps);
            secondCounter = ui.Child<CounterCardView, CounterCardProps>("beta", in betaProps);
        }

        var controls = ui.HStack(
                ui.Button("scroll-top", "Top")
                    .OnClick(this, (view, _) => view._scroll.ScrollToTop())
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Button("scroll-bottom", "Bottom")
                    .OnClick(this, (view, _) => view._scroll.ScrollToBottom())
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Button("change-counter-props", "Change child props")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._propsRevision++;
                            view.Invalidate();
                        }
                    )
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Button("reverse-counter-order", "Reverse keyed children")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._reverseCounters = !view._reverseCounters;
                            view.Invalidate();
                        }
                    )
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Spacer(),
                ui.Badge(ui.Text("native ScrollHandle"u8))
                    .Background(ui.Theme.Colors.InfoBackground)
                    .TextColor(ui.Theme.Colors.Info)
            )
            .Gap(Px(8))
            .ItemsCenter();

        var retainedCounters = ui.VStack(
                ui.Text("Required props + keyed retention"u8)
                    .FontSize(Px(ui.Theme.Typography.Heading))
                    .TextColor(ui.Theme.Colors.Text),
                ui.Text(
                        "Change props or reverse the declarations; each local count stays with its key."u8
                    )
                    .FontSize(Px(ui.Theme.Typography.Detail))
                    .TextColor(ui.Theme.Colors.TextMuted),
                ui.HStack(firstCounter, secondCounter).Gap(Px(10))
            )
            .Gap(Px(8));

        Span<Element> cards = stackalloc Element[36];
        for (var index = 0; index < cards.Length; index++)
        {
            cards[index] = ui.VStack(
                    ui.Text($"Scrollable card {index + 1:D2}")
                        .FontSize(Px(ui.Theme.Typography.Title))
                        .TextColor(ui.Theme.Colors.Text),
                    ui.Text(
                            "Wheel/trackpad updates the Rust-owned offset without a managed callback."u8
                        )
                        .FontSize(Px(ui.Theme.Typography.Detail))
                        .TextColor(ui.Theme.Colors.TextMuted)
                )
                .Gap(Px(6))
                .Padding(Px(16))
                .Background(ui.Theme.Colors.SurfaceBackground)
                .BorderWidth(Px(1))
                .BorderColor(ui.Theme.Colors.BorderVariant)
                .Radius(Px(10));
        }

        var body = ui.VStack(cards).Gap(Px(10)).Padding(Px(4));
        var scroll = ui.Scroll(
                "overview-scroll",
                ScrollAxis.Vertical,
                new ScrollOptions(
                    smoothScrolling: true,
                    showScrollbar: true,
                    scrollbarGutter: true
                ),
                body
            )
            .Grow()
            .Width(Percent(100));

        return ui.VStack(controls, retainedCounters, scroll).Gap(Px(10)).Grow();
    }
}
