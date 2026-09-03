using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ObserverView : View
{
    private InputController _probe;
    private int _saveCount;
    private string _lastKey = "Press Ctrl+S, then release it.";
    private string _lastMouse = "Click inside the mouse panel.";
    private string _modifiers = "Hold Ctrl, Alt, Shift, or Cmd alone.";
    private string _lastMove = "Move over the mouse panel.";
    private string _lastWheel = "Scroll over the mouse panel.";
    private string _lastDrop = "Drop OS files onto the drop zone.";
    private bool _hovering;

    private void Save()
    {
        _saveCount++;
        _lastKey = $"Saved {_saveCount}x via Ctrl+S.";
        Invalidate();
    }

    private static string DescribeModifiers(uint modifiers)
    {
        var parts = new List<string>(5);
        if ((modifiers & 1u) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((modifiers & 2u) != 0)
        {
            parts.Add("Alt");
        }
        if ((modifiers & 4u) != 0)
        {
            parts.Add("Shift");
        }
        if ((modifiers & 8u) != 0)
        {
            parts.Add("Cmd/Super");
        }
        if ((modifiers & 16u) != 0)
        {
            parts.Add("Fn");
        }
        return parts.Count == 0 ? "none" : string.Join("+", parts);
    }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var inputCard = ui.VStack(
                ui.Text("Typing stays in the field"u8)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Input(ref _probe, new Utf8InputOptions(placeholder: "Type here — hot keys observe only"u8))
                    .Width(Percent(100))
            )
            .Gap(Px(6))
            .Padding(Px(12))
            .Width(Percent(50))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(10));
        var mouseCard = ui.VStack(
                ui.Text("Mouse observers (hover/move/down/up/wheel)"u8)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text(_lastMouse)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.Success),
                ui.Text(_lastMove)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text(_lastWheel)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(6))
            .Padding(Px(12))
            .Width(Percent(50))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(_hovering ? theme.Colors.Accent : theme.Colors.BorderVariant)
            .Radius(Px(10))
            .OnHover(
                this,
                (view, hover) =>
                {
                    view._hovering = hover.IsHovering;
                    view.Invalidate();
                }
            )
            .OnMouseMove(
                this,
                (view, move) =>
                {
                    view._lastMove =
                        $"Move · ({move.X:0}, {move.Y:0}) · pressed {move.PressedButton?.ToString() ?? "none"}";
                    view.Invalidate();
                }
            )
            .OnScrollWheel(
                this,
                (view, wheel) =>
                {
                    view._lastWheel =
                        $"Wheel · ({wheel.DeltaX:0.0}, {wheel.DeltaY:0.0}) {wheel.Units}";
                    view.Invalidate();
                }
            )
            .OnMouseDownOut(
                this,
                (view, mouse) =>
                {
                    view._lastMouse = $"Down outside · {mouse.Button}";
                    view.Invalidate();
                }
            )
            .OnMouseDown(
                this,
                (view, mouse) =>
                {
                    view._lastMouse =
                        $"Down · {mouse.Button} · ({mouse.X:0}, {mouse.Y:0}) · x{mouse.ClickCount} · {DescribeModifiers(mouse.Modifiers)}";
                    view.Invalidate();
                }
            )
            .OnMouseUp(
                this,
                (view, mouse) =>
                {
                    view._lastMouse =
                        $"Up · {mouse.Button} · ({mouse.X:0}, {mouse.Y:0}) · x{mouse.ClickCount} · {DescribeModifiers(mouse.Modifiers)}";
                    view.Invalidate();
                }
            );
        var status = ui.VStack(
                ui.Text($"Saves: {_saveCount}").TextColor(theme.Colors.Text),
                ui.Text(_lastKey)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextAccent),
                ui.Text($"Modifiers: {_modifiers}")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(5))
            .Padding(Px(12))
            .Background(theme.Colors.InfoBackground)
            .Radius(Px(8));
        var dropZone = ui.VStack(
                ui.Text("File drop zone"u8)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text(_lastDrop)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.Success)
            )
            .Gap(Px(6))
            .Padding(Px(12))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(10))
            .OnFileDrop(
                this,
                (view, drop) =>
                {
                    var names = drop.Paths.Select(System.IO.Path.GetFileName).Take(3);
                    view._lastDrop =
                        $"{drop.Paths.Count} file(s) at ({drop.X:0}, {drop.Y:0}): {string.Join(", ", names)}";
                    view.Invalidate();
                }
            );

        return ui.VStack(
                ui.Text("Observers never stop propagation: focused editing wins, hot keys see the rest."u8)
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(inputCard, mouseCard).Gap(Px(14)),
                ui.Button("save-button", "Save (Ctrl+S)")
                    .OnClick(this, (view, _) => view.Save())
                    .Padding(Px(8)),
                dropZone,
                status
            )
            .Gap(Px(14))
            .Grow()
            .OnKeyDown(
                this,
                (view, key) =>
                {
                    if (!key.IsHeld && key.Matches("s", control: true))
                    {
                        view.Save();
                        return;
                    }
                    view._lastKey =
                        $"Down · “{key.Key}” · {DescribeModifiers(key.Modifiers)}{(key.IsHeld ? " · held" : string.Empty)}";
                    view.Invalidate();
                }
            )
            .OnKeyUp(
                this,
                (view, key) =>
                {
                    view._lastKey = $"Up · “{key.Key}” · {DescribeModifiers(key.Modifiers)}";
                    view.Invalidate();
                }
            )
            .OnModifiersChanged(
                this,
                (view, modifiers) =>
                {
                    view._modifiers = modifiers.IsEmpty
                        ? "none"
                        : DescribeModifiers(modifiers.Modifiers);
                    view.Invalidate();
                }
            );
    }
}
