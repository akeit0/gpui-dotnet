using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ReactivityView : View
{
    private readonly Signal<int> _count = new(0);
    private readonly Signal<bool> _showSecondReader = new(true);

    protected override Element Render(ref RenderContext ui)
    {
        // Pass the shared identity, not its value: only the readers subscribe to the count.
        var controlsProps = new SharedCountProps(_count);
        var countProps = new SignalReaderProps(_count, "Live count", 1, false);
        var doubledProps = new SignalReaderProps(_count, "Doubled count", 2, true);
        var showSecondReader = _showSecondReader.Value;
        var secondReader = showSecondReader
            ? ui.Child("doubled", SignalReaderView.Spec(doubledProps))
            : ui.VStack(
                    ui.Text("Doubled reader removed"u8),
                    ui.Text("Change the count, then show a fresh reader."u8)
                        .TextColor(ui.Theme.Colors.TextMuted)
                )
                .Gap(Px(8))
                .Padding(Px(16))
                .Grow();

        return ui.VStack(
                ui.Text("One shared value, independent sibling views"u8)
                    .FontSize(Px(ui.Theme.Typography.Heading)),
                ui.Text("Use the controls, pause the doubled display, or remove and recreate it."u8)
                    .TextColor(ui.Theme.Colors.TextMuted),
                ui.Child("controls", SignalControlsView.Spec(controlsProps)),
                ui.HStack(ui.Child("count", SignalReaderView.Spec(countProps)), secondReader)
                    .Gap(Px(12)),
                ui.Button(
                        "toggle-reader",
                        showSecondReader ? "Remove doubled reader" : "Show doubled reader"
                    )
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view._showSecondReader.Value = !view._showSecondReader.Value;
                        }
                    )
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Text(
                        "The live count keeps updating while the doubled reader is paused or absent."u8
                    )
                    .TextColor(ui.Theme.Colors.TextMuted)
            )
            .Gap(Px(14))
            .Grow();
    }
}
