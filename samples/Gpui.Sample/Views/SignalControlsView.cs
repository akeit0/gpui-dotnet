using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class SignalControlsView : View<SharedCountProps>
{
    protected override Element Render(in SharedCountProps props, ref RenderContext ui) =>
        ui.HStack(
                ui.Button("decrement", "−1")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view.CommittedProps.Count.Value--;
                        }
                    )
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Button("increment", "+1")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view.CommittedProps.Count.Value++;
                        }
                    )
                    .Style(SampleStyles.Button(ui.Theme, SampleButtonVariant.Primary)),
                ui.Button("reset", "Reset")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view.CommittedProps.Count.Value = 0;
                        }
                    )
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Button("set-equal", "Set same value")
                    .OnClick(
                        this,
                        (view, _) =>
                        {
                            view.CommittedProps.Count.Set(view.CommittedProps.Count.Value);
                        }
                    )
                    .Style(SampleStyles.Button(ui.Theme))
            )
            .Gap(Px(8));
}
