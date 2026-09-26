using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class InputGalleryView : View
{
    private InputController _search;
    private bool _invalid;
    private SliderController _volume;
    private bool _customSliderStyle = true;
    private string _value = "Type in the first field";
    private string _lastEvent = "No input event yet";
    private ulong _lastInputRevision;
    private ulong _writeRequest;
    private string _lastWrite = "Edit or focus the first field, then try a conditional write.";
    private string _lastSliderEvent = "No slider event yet";
    private float _volumeValue = 40;

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var search = Field(
            ref ui,
            "Interactive + UTF-8 events",
            ui.Input(ref _search, new Utf8InputOptions(placeholder: "Search or enter 日本語…"u8))
                .AccessibleName("Search")
                .AccessibleDescription(
                    _invalid ? "Enter at most 24 characters." : "Search using any language."
                )
                .Style(SampleStyles.Input(theme, _invalid))
                .OnChanged(
                    this,
                    (view, input) =>
                    {
                        view._value = input.Value;
                        view._lastInputRevision = input.Revision;
                        view._invalid = input.Value.Length > 24;
                        view._lastEvent =
                            $"Changed · revision {input.Revision} · {input.Utf8Value.Length} UTF-8 bytes";
                        view.Invalidate();
                    }
                )
                .OnSubmitted(
                    this,
                    (view, input) =>
                    {
                        view._lastEvent = $"Submitted “{input.Value}” · revision {input.Revision}";
                        view._lastInputRevision = input.Revision;
                        view.Invalidate();
                    }
                )
                .OnFocusChanged(
                    this,
                    (view, input) =>
                    {
                        view._lastEvent = input.IsFocused ? "Focused" : "Blurred";
                        view._lastInputRevision = input.Revision;
                        view.Invalidate();
                    }
                )
                .OnWriteCompleted(
                    this,
                    static (view, result) =>
                    {
                        view._lastWrite =
                            $"Write #{result.RequestId}: {result.Outcome} · revision {result.Revision}";
                        view.Invalidate();
                    }
                )
                .Width(Percent(100)),
            _invalid
                ? "Use at most 24 characters."
                : "Select text to preview the custom selection color.",
            _invalid
        );
        var password = Field(
            ref ui,
            "Password masking",
            ui.Input(
                    "password"u8,
                    new Utf8InputOptions(placeholder: "Native password field"u8, password: true)
                )
                .Width(Percent(100))
        );
        var readOnly = Field(
            ref ui,
            "Read only",
            ui.Input(
                    "read-only"u8,
                    new Utf8InputOptions("Selectable native text"u8, readOnly: true)
                )
                .Width(Percent(100))
        );
        var disabled = Field(
            ref ui,
            "Disabled",
            ui.Input("disabled"u8, new Utf8InputOptions("Cannot focus or edit"u8, disabled: true))
                .Width(Percent(100))
        );
        var volumeInput = ui.Slider(
            ref _volume,
            new SliderOptions(min: 0, max: 100, step: 5, value: _volumeValue)
        );
        if (_customSliderStyle)
        {
            volumeInput = volumeInput.Style(SampleStyles.Slider(theme));
        }
        var volume = Field(
            ref ui,
            "Slider + native Change/Release events",
            volumeInput
                .OnChanged(
                    this,
                    (view, slider) =>
                    {
                        view._volumeValue = slider.End;
                        view._lastSliderEvent = $"Changed · {slider.End:0}";
                        view.Invalidate();
                    }
                )
                .OnReleased(
                    this,
                    (view, slider) =>
                    {
                        view._lastSliderEvent = $"Released · {slider.End:0}";
                        view.Invalidate();
                    }
                )
                .Width(Percent(100)),
            _customSliderStyle ? "Custom track and thumb colors" : "Theme track and thumb colors"
        );

        var controls = ui.HStack(
                ui.Button("focus-search", "Focus")
                    .OnClick(this, (view, _) => view._search.Focus())
                    .Padding(Px(8)),
                ui.Button("select-search", "Select all")
                    .OnClick(this, (view, _) => view._search.SelectAll())
                    .Padding(Px(8)),
                ui.Button("set-japanese", "Set UTF-8")
                    .OnClick(this, (view, _) => view._search.SetValue("東京からこんにちは"u8))
                    .Padding(Px(8)),
                ui.Button("clear-search", "Clear")
                    .OnClick(this, (view, _) => view._search.SetValue(ReadOnlySpan<byte>.Empty))
                    .Padding(Px(8)),
                ui.Button("conditional-write", "Write at last event revision")
                    .Disabled(_lastInputRevision == 0)
                    .OnClick(
                        this,
                        static (view, _) =>
                            view._search.SetValueIfCurrentWithResult(
                                "Conditional write"u8,
                                view._lastInputRevision,
                                checked(++view._writeRequest)
                            )
                    )
                    .Padding(Px(8))
            )
            .Gap(Px(8));

        return ui.VStack(
                ui.Text(_lastWrite).TextColor(theme.Colors.TextMuted),
                ui.Text(
                        "Editing and pointer/keyboard state stay in Rust. Managed callbacks are opt-in."u8
                    )
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text(
                        "Word keys: Option+Arrow/Backspace on macOS, Ctrl+Arrow/Backspace on Windows and Linux. Add Shift to select."u8
                    )
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text(
                        "Undo/redo: Cmd+Z / Cmd+Shift+Z on macOS; Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) on Windows and Linux."u8
                    )
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text(
                        "Double-click selects a word; drag beyond the field to extend. Triple-click selects all."u8
                    )
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(search, password).Gap(Px(14)),
                ui.HStack(readOnly, disabled).Gap(Px(14)),
                ui.HStack(
                        volume,
                        ui.Button(
                                "toggle-slider-style",
                                _customSliderStyle ? "Use theme colors" : "Use custom colors"
                            )
                            .Style(SampleStyles.Button(theme))
                            .OnClick(
                                this,
                                (view, _) =>
                                {
                                    view._customSliderStyle = !view._customSliderStyle;
                                    view.Invalidate();
                                }
                            )
                    )
                    .Gap(Px(14)),
                controls,
                ui.VStack(
                        ui.Text($"Value: {_value}").TextColor(theme.Colors.Text),
                        ui.Text(_lastEvent)
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.TextAccent),
                        ui.Text(_lastSliderEvent)
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.Success)
                    )
                    .Gap(Px(5))
                    .Padding(Px(12))
                    .Background(theme.Colors.InfoBackground)
                    .Radius(Px(8))
            )
            .Gap(Px(14))
            .Grow();
    }

    private static Element Field(
        ref RenderContext ui,
        ReadOnlySpan<char> label,
        Element input,
        string? help = null,
        bool invalid = false
    )
    {
        var theme = ui.Theme;
        var field = ui.VStack(
            ui.Text(label).FontSize(Px(theme.Typography.Detail)).TextColor(theme.Colors.TextMuted),
            input
        );
        if (help is not null)
        {
            field.Child(
                ui.Text(help)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(invalid ? theme.Colors.Error : theme.Colors.TextMuted)
            );
        }

        return field
            .Gap(Px(6))
            .Padding(Px(12))
            .Width(Percent(50))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(10));
    }
}
