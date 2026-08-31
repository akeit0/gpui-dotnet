using Gpui;
using static Gpui.Units;

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
                ui.Button("increment", "Async increment").OnClick(Events.Increment).Padding(Px(10))
            )
            .Gap(Px(10));
}
