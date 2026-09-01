using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ImageGalleryView : View
{
    private static readonly string ImagePath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "gpui.svg"
    );

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var contain = ImageCard(ref ui, "Contain", ImageFit.Contain, false);
        var cover = ImageCard(ref ui, "Cover", ImageFit.Cover, false);
        var grayscale = ImageCard(ref ui, "Grayscale", ImageFit.Cover, true);
        var vectorChart = VectorChart(ref ui);

        return ui.VStack(
                ui.Text("Native vector paths and one cached SVG rendered without a web view."u8)
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                vectorChart,
                ui.HStack(contain, cover, grayscale).Gap(Px(14)).JustifyCenter().Grow()
            )
            .Gap(Px(14))
            .Grow();
    }

    private static Element ImageCard(
        ref RenderContext ui,
        ReadOnlySpan<char> label,
        ImageFit fit,
        bool grayscale
    )
    {
        var theme = ui.Theme;
        var image = ui.Image(ImagePath)
            .Fit(fit)
            .Grayscale(grayscale)
            .Width(Percent(100))
            .Height(Px(180))
            .Radius(Px(12))
            .Background(theme.Colors.ElementActive);

        return ui.VStack(
                ui.Text(label)
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                image
            )
            .Gap(Px(8))
            .Padding(Px(12))
            .Width(Px(240))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(12));
    }

    private static Element VectorChart(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var area = ui.Path()
            .MoveTo(0, 100)
            .CubicTo(18, 82, 24, 42, 38, 48)
            .CubicTo(52, 54, 61, 78, 72, 42)
            .CubicTo(82, 10, 91, 24, 100, 18)
            .LineTo(100, 100)
            .Close()
            .Fill(theme.Colors.Accent.WithAlpha(44));
        var line = ui.Path()
            .MoveTo(0, 100)
            .CubicTo(18, 82, 24, 42, 38, 48)
            .CubicTo(52, 54, 61, 78, 72, 42)
            .CubicTo(82, 10, 91, 24, 100, 18)
            .Stroke(theme.Colors.Accent, Px(3));
        var baseline = ui.Line(0, 100, 100, 100)
            .Stroke(theme.Colors.BorderVariant, Px(1))
            .Dash(Px(3), Px(3));
        var firstDot = ui.Circle(38, 48, 2.5f)
            .Fill(theme.Colors.SurfaceBackground)
            .Stroke(theme.Colors.Accent, Px(2));
        var secondDot = ui.Circle(72, 42, 2.5f)
            .Fill(theme.Colors.SurfaceBackground)
            .Stroke(theme.Colors.Accent, Px(2));

        return ui.Drawing(area, baseline, line, firstDot, secondDot)
            .ViewBox(0, 0, 100, 110)
            .Width(Percent(100))
            .Height(Px(180))
            .Padding(Px(14))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(12));
    }
}
