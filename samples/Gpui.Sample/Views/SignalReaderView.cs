using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class SignalReaderView : View<SignalReaderProps>
{
    private readonly Signal<bool> _following = new(true);

    protected override Element Render(ref RenderContext ui)
    {
        var following = !Props.CanPause || _following.Value;
        // A paused render deliberately omits the shared read, so acceptance detaches that edge.
        var value = following ? $"{(long)Props.Count.Value * Props.Multiplier:N0}" : "Paused";
        Element control = Props.CanPause
            ? ui.Button("toggle-following", following ? "Pause" : "Resume latest")
                .OnClick(this, (view, _) => { view._following.Value = !view._following.Value; })
                .Style(SampleStyles.Button(ui.Theme))
            : ui.Text("Always follows the shared count"u8).TextColor(ui.Theme.Colors.TextMuted);

        return ui.VStack(
                ui.Text(Props.Title).FontSize(Px(ui.Theme.Typography.Title)),
                ui.Text(value).FontSize(Px(32)),
                control
            )
            .Gap(Px(10))
            .Padding(Px(16))
            .Grow()
            .Background(ui.Theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(ui.Theme.Colors.BorderVariant)
            .Radius(Px(10));
    }
}
