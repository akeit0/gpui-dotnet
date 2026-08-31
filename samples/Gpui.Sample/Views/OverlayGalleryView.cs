using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class OverlayGalleryView : View
{
    private OverlayDemo _open;
    private int _backgroundClicks;
    private int _contextMenuActions;

    protected override Element Render(ref RenderContext ui)
    {
        var page = ui.VStack(
                ui.Text("Overlays are window-relative and paint after normal content."u8)
                    .FontSize(Px(13))
                    .TextColor(Rgb(71, 85, 105)),
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
                            .Padding(Px(10))
                            .Background(Rgb(79, 70, 229))
                            .BorderColor(Rgb(99, 102, 241))
                            .TextColor(Rgb(255, 255, 255)),
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
                                        .FontSize(Px(13))
                                        .TextColor(Rgb(255, 255, 255)),
                                    ui.Text(
                                            "Shows after 500 ms and stays open while you cross the gap."u8
                                        )
                                        .FontSize(Px(11))
                                        .TextColor(Rgb(203, 213, 225))
                                )
                                .Gap(Px(5))
                                .Padding(Px(10))
                                .Width(Px(260))
                                .Background(Rgb(15, 23, 42))
                                .Radius(Px(8))
                        ),
                        ui.Tooltip(
                            "right-tooltip"u8,
                            ui.Button("hover-right", "Prefer right side").Padding(Px(10)),
                            ui.Text("Flips and clamps when the preferred side does not fit."u8)
                                .FontSize(Px(11))
                                .TextColor(Rgb(255, 255, 255))
                                .Padding(Px(9))
                                .Width(Px(220))
                                .Background(Rgb(79, 70, 229))
                                .Radius(Px(8)),
                            new TooltipOptions(placement: TooltipPlacement.Right)
                        )
                    )
                    .Gap(Px(10)),
                ui.ContextMenu(
                        "content-context-menu"u8,
                        ui.VStack(
                                ui.Text("Application context menu"u8)
                                    .FontSize(Px(14))
                                    .TextColor(Rgb(30, 41, 59)),
                                ui.Text("Right-click this card; title bars keep the OS menu."u8)
                                    .FontSize(Px(12))
                                    .TextColor(Rgb(100, 116, 139))
                            )
                            .Gap(Px(6))
                            .Padding(Px(16))
                            .Width(Px(360))
                            .Background(Rgb(255, 255, 255))
                            .BorderWidth(Px(1))
                            .BorderColor(Rgb(203, 213, 225))
                            .Radius(Px(10)),
                        ui.VStack(
                                ui.Text("Content actions"u8)
                                    .FontSize(Px(11))
                                    .TextColor(Rgb(100, 116, 139))
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
                                    .Background(Rgb(255, 255, 255))
                                    .BorderColor(Rgb(255, 255, 255))
                            )
                            .Width(Px(220))
                            .Gap(Px(2))
                            .Padding(Px(6))
                            .Background(Rgb(255, 255, 255))
                            .BorderWidth(Px(1))
                            .BorderColor(Rgb(203, 213, 225))
                            .Radius(Px(8))
                    )
                    .Width(Px(360)),
                ui.VStack(
                        ui.Text("Interaction checks"u8).FontSize(Px(14)).TextColor(Rgb(30, 41, 59)),
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
                    .Background(Rgb(255, 255, 255))
                    .BorderWidth(Px(1))
                    .BorderColor(Rgb(226, 232, 240))
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
                        ui.Text(title).FontSize(Px(18)).TextColor(Rgb(15, 23, 42)),
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
                    .FontSize(Px(12))
                    .TextColor(Rgb(100, 116, 139)),
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
                    .FontSize(Px(12))
                    .TextColor(Rgb(79, 70, 229))
            )
            .Gap(Px(14))
            .Padding(Px(20))
            .Width(Px(isSheet ? 380 : 440))
            .Height(isSheet ? Percent(100) : Px(290))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(203, 213, 225))
            .Radius(Px(14));

        var options = new OverlayOptions(
            margin: isSheet ? 12 : 24,
            backdrop: Rgba(15, 23, 42, 150)
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
