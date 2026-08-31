using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

internal readonly record struct RouteHeaderProps(string Title, string Detail);

[GpuiView]
internal sealed partial class RouteHeaderView : View<RouteHeaderProps>
{
    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text(Props.Title).FontSize(Px(18)).TextColor(Rgb(15, 23, 42)),
                ui.Text(Props.Detail).FontSize(Px(12)).TextColor(Rgb(100, 116, 139))
            )
            .Gap(Px(3));
}
