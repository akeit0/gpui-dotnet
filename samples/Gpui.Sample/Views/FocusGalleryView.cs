using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class FocusGalleryView : View
{
    private FocusController _preview;
    private InputController _notes;
    private bool _tabStop = true;
    private int _index;

    private void Step(int delta)
    {
        _index = (_index + delta + 3) % 3;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var title = _index switch { 0 => "Morning light", 1 => "Quiet afternoon", _ => "Evening glow" };
        var color = _index switch
        {
            0 => theme.Colors.InfoBackground,
            1 => theme.Colors.SuccessBackground,
            _ => theme.Colors.WarningBackground,
        };
        var preview = ui.FocusTarget(ref _preview,
            ui.VStack(
                ui.Text($"Preview {_index + 1} of 3").TextColor(theme.Colors.TextMuted),
                ui.Text(title).FontSize(Px(theme.Typography.Heading)),
                ui.Text("← / → change preview · Escape returns to notes").TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(18))
            .Padding(Px(24))
            .Height(Px(180))
            .Width(Percent(100))
            .Background(color)
            .TextColor(theme.Colors.Text)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(12)),
            tabStop: _tabStop)
            .OnShortcut(this, new(ShortcutKey.Left), static view => view.Step(-1))
            .OnShortcut(this, new(ShortcutKey.Right), static view => view.Step(1))
            .OnShortcut(this, new(ShortcutKey.Escape), static view => view._notes.Focus());

        return ui.VStack(
            ui.Text("Click the preview, use Tab, or press Ctrl/⌘+1 to focus it.")
                .TextColor(theme.Colors.TextMuted),
            ui.HStack(
                ui.Button("focus-preview", "Focus preview")
                    .OnClick(this, static (view, _) => view._preview.Focus())
                    .Style(SampleStyles.Button(theme)),
                ui.Button("toggle-tab-stop", _tabStop ? "Skip preview in Tab order" : "Include preview in Tab order")
                    .OnClick(this, static (view, _) => { view._tabStop = !view._tabStop; view.Invalidate(); })
                    .Style(SampleStyles.Button(theme))
            ).Gap(Px(10)),
            preview,
            ui.Text("Notes").TextColor(theme.Colors.TextMuted),
            ui.Input(ref _notes, new InputOptions(placeholder: "Arrow keys edit here; the preview stays unchanged."))
                .Width(Percent(100)),
            ui.Text(_tabStop
                ? "The preview participates in Tab order. Its focus remains when the preview changes."
                : "Tab skips the preview. Clicking it and Ctrl/⌘+1 still work.")
                .TextColor(theme.Colors.TextMuted)
        )
        .Gap(Px(16))
        .Grow()
        .OnShortcut(this, new(ShortcutKey.D1, ShortcutModifiers.Primary), static view => view._preview.Focus());
    }
}
