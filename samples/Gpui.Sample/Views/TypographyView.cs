using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class TypographyView : View
{
    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        return ui.VStack(
                ui.Text("Bold italic with an accent underline")
                    .FontWeight(700)
                    .FontStyle(FontStyle.Italic)
                    .Underline()
                    .TextDecorationColor(theme.Colors.Accent)
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                ui.Text("Wavy warning underline, like a spellchecker")
                    .Underline()
                    .TextDecorationWavy()
                    .TextDecorationColor(theme.Colors.Warning)
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                ui.Text("Struck through")
                    .LineThrough()
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text("Georgia serif (system fallback if unavailable)")
                    .FontFamily("Georgia")
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                ui.Text("Courier New monospace")
                    .FontFamily("Courier New")
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                ui.Div(
                        ui.Text("A long line cut short with a custom ellipsis indicator")
                            .WhiteSpace(WhiteSpace.Nowrap)
                            .TextTruncate("…")
                            .FontSize(Px(theme.Typography.Body))
                            .TextColor(theme.Colors.TextMuted)
                    )
                    .Width(Px(240))
                    .MaxWidth(Percent(100)),
                ui.Text("Highlighted with a taller line height")
                    .TextBackground(theme.Colors.WarningBackground)
                    .LineHeight(Percent(175))
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text)
            )
            .Gap(Px(8))
            .Padding(Px(12))
            .Grow()
            .Width(Percent(100))
            .MinWidth(Px(0))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(12));
    }
}
