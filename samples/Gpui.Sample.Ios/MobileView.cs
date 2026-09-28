using Gpui;
using static Gpui.Units;

namespace Gpui.Sample.Ios;

[GpuiView]
internal sealed partial class MobileView : View
{
    private int _count;

    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text("GPUI.NET on iOS").FontSize(Px(28)),
                ui.Text("C# state and Views, rendered by gpui-mobile through Metal."),
                ui.Text($"Taps: {_count}").FontSize(Px(22)),
                ui.Button("increment", "Tap me")
                    .OnClick(this, (view, _) =>
                    {
                        view._count++;
                        view.Invalidate();
                    })
            )
            .Gap(Px(18))
            .Padding(Px(28))
            .Grow()
            .Background(ui.Theme.Colors.Background)
            .TextColor(ui.Theme.Colors.Text);
}
