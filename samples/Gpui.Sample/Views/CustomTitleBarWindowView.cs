using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class CustomTitleBarWindowView : View
{
    private int _localCount;

    internal GpuiWindow? Window { get; set; }

    protected override Element Render(ref RenderContext ui)
    {
        var titleBar = SampleTitleBar.Render(
            ref ui,
            $"GPUI.NET  /  Custom window {Window?.Id ?? 0}"
        );

        var content = ui.VStack(
                ui.Text("Managed visuals, native window behavior"u8)
                    .FontSize(Px(24))
                    .TextColor(Rgb(15, 23, 42)),
                ui.Text("Drag or double-click the dark header. Pointer motion stays in GPUI."u8)
                    .FontSize(Px(13))
                    .TextColor(Rgb(71, 85, 105)),
                ui.VStack(
                        ui.Text($"Local content state: {_localCount:N0}")
                            .FontSize(Px(20))
                            .TextColor(Rgb(79, 70, 229)),
                        ui.Button("custom-titlebar-increment", "Increment content")
                            .OnClick(
                                this,
                                (view, _) =>
                                {
                                    view._localCount++;
                                    view.Invalidate();
                                }
                            )
                            .Padding(Px(10))
                            .Background(Rgb(238, 242, 255))
                            .BorderColor(Rgb(165, 180, 252))
                    )
                    .Gap(Px(12))
                    .Padding(Px(20))
                    .Background(Rgb(255, 255, 255))
                    .BorderWidth(Px(1))
                    .BorderColor(Rgb(226, 232, 240))
                    .Radius(Px(12))
            )
            .Gap(Px(18))
            .Padding(Px(28))
            .Grow();

        return ui.VStack(titleBar, content)
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(Rgb(241, 245, 249));
    }
}
