using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class CompanionWindowView : View
{
    private int _localCount;

    internal string Origin { get; init; } = "Opened by the sample";
    internal GpuiWindow? Window { get; set; }

    private void CloseWindow()
    {
        (
            Window ?? throw new InvalidOperationException("The companion has no window handle.")
        ).Close();
    }

    protected override Element Render(ref RenderContext ui)
    {
        var windowId = Window?.Id ?? 0;
        return ui.VStack(
                ui.HStack(
                        ui.VStack(
                                ui.Text("Companion workspace"u8)
                                    .FontSize(Px(24))
                                    .TextColor(Rgb(15, 23, 42)),
                                ui.Text(Origin).FontSize(Px(12)).TextColor(Rgb(100, 116, 139))
                            )
                            .Gap(Px(4)),
                        ui.Spacer(),
                        ui.Badge(ui.Text($"Window {windowId}"))
                            .Background(Rgb(220, 252, 231))
                            .TextColor(Rgb(22, 101, 52))
                            .Padding(Px(8)),
                        ui.Button("close-companion", "Close this window")
                            .OnClick(this, (view, _) => view.CloseWindow())
                            .Padding(Px(9))
                    )
                    .Gap(Px(10))
                    .ItemsCenter(),
                ui.HStack(
                        ui.VStack(
                                ui.Text("Independent local state"u8)
                                    .FontSize(Px(16))
                                    .TextColor(Rgb(30, 41, 59)),
                                ui.Text($"Companion count: {_localCount:N0}")
                                    .FontSize(Px(28))
                                    .TextColor(Rgb(79, 70, 229)),
                                ui.Button("increment-companion", "Increment only this window")
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
                            .Gap(Px(14))
                            .Padding(Px(22))
                            .Grow()
                            .Background(Rgb(255, 255, 255))
                            .BorderWidth(Px(1))
                            .BorderColor(Rgb(226, 232, 240))
                            .Radius(Px(12)),
                        ui.VStack(
                                ui.Text("Per-window ownership"u8)
                                    .FontSize(Px(16))
                                    .TextColor(Rgb(30, 41, 59)),
                                ui.Text("• separate root View"u8),
                                ui.Text("• separate native resources"u8),
                                ui.Text("• isolated close lifecycle"u8),
                                ui.Text("• shared application event loop"u8)
                            )
                            .Gap(Px(10))
                            .Padding(Px(22))
                            .Grow()
                            .Background(Rgb(255, 255, 255))
                            .BorderWidth(Px(1))
                            .BorderColor(Rgb(226, 232, 240))
                            .Radius(Px(12))
                    )
                    .Gap(Px(16))
                    .Grow()
            )
            .Gap(Px(18))
            .Padding(Px(24))
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(Rgb(241, 245, 249));
    }
}
