using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class RouteHeaderView : View<RouteHeaderProps>
{
    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text(Props.Title)
                    .FontSize(Px(ui.Theme.Typography.Heading))
                    .TextColor(ui.Theme.Colors.Text),
                ui.Text(Props.Detail)
                    .Width(Percent(100))
                    .FontSize(Px(ui.Theme.Typography.Detail))
                    .TextColor(ui.Theme.Colors.TextMuted)
            )
            .Gap(Px(3))
            .MinWidth(Px(100));
}
