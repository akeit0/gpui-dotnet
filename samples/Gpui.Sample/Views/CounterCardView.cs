using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class CounterCardView : View
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
                ui.Text($"Counter: {_count:N0}"),
                ui.Button("increment", "Async increment")
                    .OnClick(this, (view, _) => view.Increment())
                    .Style(
                        SampleStyles.Button(ui.Theme, SampleButtonVariant.Primary)
                    )
            )
            .Gap(Px(10));
}
