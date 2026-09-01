using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class OverlayGalleryView : View
{
    private OverlayDemo _open;
    private int _backgroundClicks;
    private int _contextMenuActions;

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var page = ui.VStack(
                ui.Text("Overlays are window-relative and paint after normal content."u8)
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(
                        ui.Button("open-dialog", "Open centered dialog")
                            .OnClick(
                                this,
                                (view, _) =>
                                {
                                    view._open = OverlayDemo.Dialog;
                                    view.Invalidate();
                                }
                            )
                            .Style(SampleStyles.Button(theme, SampleButtonVariant.Primary)),
                        ui.Button("open-sheet", "Open right sheet")
                            .OnClick(
                                this,
                                (view, _) =>
                                {
                                    view._open = OverlayDemo.Sheet;
                                    view.Invalidate();
                                }
                            )
                            .Padding(Px(10)),
                        ui.Button("background-action", $"Background action · {_backgroundClicks}")
                            .OnClick(
                                this,
                                (view, _) =>
                                {
                                    view._backgroundClicks++;
                                    view.Invalidate();
                                }
                            )
                            .Padding(Px(10))
                    )
                    .Gap(Px(10)),
                ui.HStack(
                        ui.Tooltip(
                            "default-tooltip"u8,
                            ui.Button("hover-default", "Hover for details").Padding(Px(10)),
                            ui.VStack(
                                    ui.Text("Native tooltip"u8)
                                        .FontSize(Px(theme.Typography.BodySmall))
                                        .TextColor(theme.Colors.TitleBarText),
                                    ui.Text(
                                            "Shows after 500 ms and stays open while you cross the gap."u8
                                        )
                                        .FontSize(Px(theme.Typography.Caption))
                                        .TextColor(theme.Colors.TextMuted)
                                )
                                .Gap(Px(5))
                                .Padding(Px(10))
                                .Width(Px(260))
                                .Background(theme.Colors.TitleBarBackground)
                                .Radius(Px(8))
                        ),
                        ui.Tooltip(
                            "right-tooltip"u8,
                            ui.Button("hover-right", "Prefer right side").Padding(Px(10)),
                            ui.Text("Flips and clamps when the preferred side does not fit."u8)
                                .FontSize(Px(theme.Typography.Caption))
                                .TextColor(theme.Colors.TitleBarText)
                                .Padding(Px(9))
                                .Width(Px(220))
                                .Background(theme.Colors.Accent)
                                .Radius(Px(8)),
                            new TooltipOptions(placement: TooltipPlacement.Right)
                        )
                    )
                    .Gap(Px(10)),
                ui.ContextMenu(
                        "content-context-menu"u8,
                        ui.VStack(
                                ui.Text("Application context menu"u8)
                                    .FontSize(Px(theme.Typography.Body))
                                    .TextColor(theme.Colors.Text),
                                ui.Text("Right-click this card; title bars keep the OS menu."u8)
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextMuted)
                            )
                            .Gap(Px(6))
                            .Padding(Px(16))
                            .Width(Px(360))
                            .Background(theme.Colors.SurfaceBackground)
                            .BorderWidth(Px(1))
                            .BorderColor(theme.Colors.Border)
                            .Radius(Px(10)),
                        ui.VStack(
                                ui.Text("Content actions"u8)
                                    .FontSize(Px(theme.Typography.Caption))
                                    .TextColor(theme.Colors.TextMuted)
                                    .Padding(Px(7)),
                                ui.Button(
                                        "run-context-action"u8,
                                        $"Run action · {_contextMenuActions:N0}"
                                    )
                                    .OnClick(
                                        this,
                                        (view, _) =>
                                        {
                                            view._contextMenuActions++;
                                            view.Invalidate();
                                        }
                                    )
                                    .Width(Percent(100))
                                    .Padding(Px(9))
                                    .Background(theme.Colors.SurfaceBackground)
                                    .BorderColor(theme.Colors.SurfaceBackground)
                            )
                            .Width(Px(220))
                            .Gap(Px(2))
                            .Padding(Px(6))
                            .Background(theme.Colors.ElevatedSurfaceBackground)
                            .BorderWidth(Px(1))
                            .BorderColor(theme.Colors.Border)
                            .Radius(Px(8))
                    )
                    .Width(Px(360)),
                ui.VStack(
                        ui.Text("Interaction checks"u8)
                            .FontSize(Px(theme.Typography.Body))
                            .TextColor(theme.Colors.Text),
                        ui.Text(
                            "• Tooltips use deferred content and disappear on pointer press."u8
                        ),
                        ui.Text("• Application content can provide a managed right-click menu."u8),
                        ui.Text("• Custom title bars preserve the operating system window menu."u8),
                        ui.Text("• Click the dim backdrop or press Escape to dismiss."u8),
                        ui.Text("• The background action must not fire through the backdrop."u8),
                        ui.Text("• Tab and Shift+Tab stay inside the modal; try IME composition."u8)
                    )
                    .Gap(Px(8))
                    .Padding(Px(18))
                    .Background(theme.Colors.SurfaceBackground)
                    .BorderWidth(Px(1))
                    .BorderColor(theme.Colors.BorderVariant)
                    .Radius(Px(12))
            )
            .Gap(Px(18))
            .Grow();

        if (_open == OverlayDemo.None)
        {
            return page;
        }

        var isSheet = _open == OverlayDemo.Sheet;
        var title = isSheet ? "Window-edge sheet" : "Centered dialog";
        var panel = ui.VStack(
                ui.HStack(
                        ui.Text(title)
                            .FontSize(Px(theme.Typography.Heading))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Button("close-overlay", "Close")
                            .OnClick(
                                this,
                                (view, _) =>
                                {
                                    view._open = OverlayDemo.None;
                                    view.Invalidate();
                                }
                            )
                            .Padding(Px(8))
                    )
                    .ItemsCenter(),
                ui.Text("This subtree is still ordinary semantic GPUI.NET content."u8)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Input(
                        "overlay-first-input"u8,
                        new Utf8InputOptions(placeholder: "First modal tab stop…"u8)
                    )
                    .Width(Percent(100)),
                ui.Input(
                        "overlay-second-input"u8,
                        new Utf8InputOptions(
                            placeholder: "Second modal tab stop; IME works here…"u8
                        )
                    )
                    .Width(Percent(100)),
                ui.Text("Escape is handled after focused child controls get the key first."u8)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextAccent)
            )
            .Gap(Px(14))
            .Padding(Px(20))
            .Width(Px(isSheet ? 380 : 440))
            .Height(isSheet ? Percent(100) : Px(290))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(14));

        var options = new OverlayOptions(
            margin: isSheet ? 12 : 24,
            backdrop: theme.Colors.Background.WithAlpha(150)
        );
        var overlay = isSheet
            ? ui.Sheet("sample-overlay"u8, panel, SheetSide.Right, options)
            : ui.Dialog("sample-overlay"u8, panel, options);
        overlay = overlay.OnDismiss(
            this,
            (view, _) =>
            {
                view._open = OverlayDemo.None;
                view.Invalidate();
            }
        );

        return ui.VStack(page, overlay).Grow().Height(Percent(100));
    }
}
