using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class CounterCardView : View<CounterCardProps>
{
    private int _count;

    private async ValueTask Increment()
    {
        await Task.Delay(100, Lifetime);
        _count++;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text(Props.Title)
                    .FontSize(Px(ui.Theme.Typography.Title))
                    .TextColor(ui.Theme.Colors.Text),
                ui.Text($"Parent props revision: {Props.Revision:N0}")
                    .FontSize(Px(ui.Theme.Typography.Detail))
                    .TextColor(ui.Theme.Colors.TextMuted),
                ui.Text($"Retained local count: {_count:N0}").TextColor(ui.Theme.Colors.Text),
                ui.Button("increment", "Async increment")
                    .OnClick(this, (view, _) => view.Increment())
                    .Style(SampleStyles.Button(ui.Theme, SampleButtonVariant.Primary))
            )
            .Gap(Px(8))
            .Padding(Px(14))
            .Grow()
            .Background(ui.Theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(ui.Theme.Colors.BorderVariant)
            .Radius(Px(10));
}
