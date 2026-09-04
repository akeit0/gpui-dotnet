using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class GridView : View
{
    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        return ui.VStack(
                ui.Text("Grid auto-placement; the wide tile spans two columns.")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Div(
                        GridTile(ref ui, "One", theme.Colors.InfoBackground, theme.Colors.Info),
                        GridTile(
                            ref ui,
                            "Two",
                            theme.Colors.SuccessBackground,
                            theme.Colors.Success
                        ),
                        GridTile(
                                ref ui,
                                "Three spans two",
                                theme.Colors.WarningBackground,
                                theme.Colors.Warning
                            )
                            .ColSpan(2),
                        GridTile(ref ui, "Four", theme.Colors.InfoBackground, theme.Colors.Info),
                        GridTile(
                            ref ui,
                            "Five",
                            theme.Colors.SuccessBackground,
                            theme.Colors.Success
                        )
                    )
                    .Grid()
                    .GridCols(3)
                    .Gap(Px(10))
                    .Width(Percent(100))
                    .MinWidth(Px(0))
            )
            .Gap(Px(8))
            .Grow()
            .Width(Percent(100))
            .MinWidth(Px(0));
    }

    private static Element<DivTag> GridTile(
        ref RenderContext ui,
        ReadOnlySpan<char> label,
        Color background,
        Color text
    )
    {
        var theme = ui.Theme;
        return ui.Div(ui.Text(label).FontSize(Px(ui.Theme.Typography.Body)).TextColor(text))
            .Padding(Px(12))
            .Height(Px(56))
            .Background(background)
            .BorderWidth(Px(2))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(8));
    }
}
