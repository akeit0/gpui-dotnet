using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class CompanionWindowView : View<string>
{
    private int _localCount;

    private readonly GpuiWindow Window;

    public CompanionWindowView(ViewConstruction construction, string initialProps)
        : base(construction) => Window = construction.Window;

    private void CloseWindow()
    {
        Window.Close();
    }

    protected override Element Render(in string props, ref RenderContext ui)
    {
        var theme = ui.Theme;
        var windowId = Window.Id;
        return ui.VStack(
                ui.HStack(
                        ui.VStack(
                                ui.Text("Companion workspace"u8)
                                    .FontSize(Px(theme.Typography.Large))
                                    .TextColor(theme.Colors.Text),
                                ui.Text(props)
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextMuted)
                            )
                            .Gap(Px(4)),
                        ui.Spacer(),
                        ui.Badge(ui.Text($"Window {windowId}"))
                            .Background(theme.Colors.SuccessBackground)
                            .TextColor(theme.Colors.Success)
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
                                    .FontSize(Px(theme.Typography.Title))
                                    .TextColor(theme.Colors.Text),
                                ui.Text($"Companion count: {_localCount:N0}")
                                    .FontSize(Px(theme.Typography.Metric))
                                    .TextColor(theme.Colors.TextAccent),
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
                                    .Background(theme.Colors.ElementSelected)
                                    .BorderColor(theme.Colors.BorderFocused)
                            )
                            .Gap(Px(14))
                            .Padding(Px(22))
                            .Grow()
                            .Background(theme.Colors.SurfaceBackground)
                            .BorderWidth(Px(1))
                            .BorderColor(theme.Colors.BorderVariant)
                            .Radius(Px(12)),
                        ui.VStack(
                                ui.Text("Per-window ownership"u8)
                                    .FontSize(Px(theme.Typography.Title))
                                    .TextColor(theme.Colors.Text),
                                ui.Text("• separate root View"u8),
                                ui.Text("• separate native resources"u8),
                                ui.Text("• isolated close lifecycle"u8),
                                ui.Text("• shared application event loop"u8)
                            )
                            .Gap(Px(10))
                            .Padding(Px(22))
                            .Grow()
                            .Background(theme.Colors.SurfaceBackground)
                            .BorderWidth(Px(1))
                            .BorderColor(theme.Colors.BorderVariant)
                            .Radius(Px(12))
                    )
                    .Gap(Px(16))
                    .Grow()
            )
            .Gap(Px(18))
            .Padding(Px(24))
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(theme.Colors.Background);
    }
}
