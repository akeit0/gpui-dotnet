using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class CounterCardView : View<CounterCardProps>
{
    private readonly Signal<int> _count = new(0);
    private readonly WorkScope _work;

    public CounterCardView(ViewConstruction context, CounterCardProps initialProps) : base(context) => _work = context.Work;

    private void Increment() =>
        _work.Start(
            this,
            100,
            static async (delay, lifetime) =>
            {
                await Task.Delay(delay, lifetime).ConfigureAwait(false);
                return 1;
            },
            static (view, increment) => view._count.Value += increment
        );

    protected override Element Render(in CounterCardProps props, ref RenderContext ui) =>
        ui.VStack(
                ui.Text(props.Title)
                    .FontSize(Px(ui.Theme.Typography.Title))
                    .TextColor(ui.Theme.Colors.Text),
                ui.Text($"Parent props revision: {props.Revision:N0}")
                    .FontSize(Px(ui.Theme.Typography.Detail))
                    .TextColor(ui.Theme.Colors.TextMuted),
                ui.Text($"Retained local count: {_count.Value:N0}").TextColor(ui.Theme.Colors.Text),
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
