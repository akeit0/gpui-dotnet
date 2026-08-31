using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class DashboardView : View
{
    private ScrollController _scroll;

    protected override void OnMounted(ref ViewContext context)
    {
        _scroll = context.CreateScrollController("overview-scroll");
    }

    protected override Element Render(ref RenderContext ui)
    {
        var controls = ui.HStack(
                ui.Button("scroll-top", "Top")
                    .OnClick(this, (view, _) => view._scroll.ScrollToTop()),
                ui.Button("scroll-bottom", "Bottom")
                    .OnClick(this, (view, _) => view._scroll.ScrollToBottom()),
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
                    ui.Text(
                            "Wheel/trackpad updates the Rust-owned offset without a managed callback."u8
                        )
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
                new ScrollOptions(
                    smoothScrolling: true,
                    showScrollbar: true,
                    scrollbarGutter: true
                ),
                body
            )
            .Grow()
            .Width(Percent(100));

        return ui.VStack(controls, scroll).Gap(Px(10)).Grow();
    }
}
