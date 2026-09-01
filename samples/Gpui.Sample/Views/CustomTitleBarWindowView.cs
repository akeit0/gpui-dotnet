using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class CustomTitleBarWindowView : View
{
    private int _localCount;

    internal GpuiWindow? Window { get; set; }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var titleBar = SampleTitleBar.Render(
            ref ui,
            $"GPUI.NET  /  Custom window {Window?.Id ?? 0}"
        );

        var content = ui.VStack(
                ui.Text("Managed visuals, native window behavior"u8)
                    .FontSize(Px(theme.Typography.Large))
                    .TextColor(theme.Colors.Text),
                ui.Text("Drag or double-click the dark header. Pointer motion stays in GPUI."u8)
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.VStack(
                        ui.Text($"Local content state: {_localCount:N0}")
                            .FontSize(Px(theme.Typography.Title))
                            .TextColor(theme.Colors.TextAccent),
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
                            .Background(theme.Colors.ElementSelected)
                            .BorderColor(theme.Colors.BorderFocused)
                    )
                    .Gap(Px(12))
                    .Padding(Px(20))
                    .Background(theme.Colors.SurfaceBackground)
                    .BorderWidth(Px(1))
                    .BorderColor(theme.Colors.BorderVariant)
                    .Radius(Px(12))
            )
            .Gap(Px(18))
            .Padding(Px(28))
            .Grow();

        return ui.VStack(titleBar, content)
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(theme.Colors.Background);
    }
}
