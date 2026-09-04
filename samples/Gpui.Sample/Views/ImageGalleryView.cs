using System.Diagnostics;
using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ImageGalleryView : View
{
    private const double ChartAnimationSeconds = 0.7;
    private long _chartAnimationStart;
    private ScrollController _scroll;

    private static readonly string ImagePath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "gpui.svg"
    );

    protected override void OnMounted(ref ViewContext context)
    {
        _scroll = context.CreateScrollController("images-scroll");
    }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var contain = ImageCard(ref ui, "Contain", ImageFit.Contain, false);
        var cover = ImageCard(ref ui, "Cover", ImageFit.Cover, false);
        var grayscale = ImageCard(ref ui, "Grayscale", ImageFit.Cover, true);
        var (chartProgress, chartAnimating) = ChartAnimation();
        var vectorChart = ui.Dynamic(chartAnimating, VectorChart(ref ui, chartProgress));
        var body = ui.VStack(
                ui.HStack(
                        ui.Text(
                                "Native vector paths and one cached SVG rendered without a web view."u8
                            )
                            .FontSize(Px(theme.Typography.BodySmall))
                            .TextColor(theme.Colors.TextMuted),
                        ui.Spacer(),
                        ui.Button("animate-vector-chart", "Animate chart")
                            .OnClick(
                                this,
                                static (view, _) =>
                                {
                                    view._chartAnimationStart = Stopwatch.GetTimestamp();
                                    view.Invalidate();
                                }
                            )
                            .Style(SampleStyles.Button(theme, SampleButtonVariant.Primary))
                    )
                    .Gap(Px(10))
                    .ItemsCenter(),
                vectorChart,
                ui.HStack(contain, cover, grayscale).Gap(Px(14)).JustifyCenter().Wrap(FlexWrap.Wrap)
            )
            .Gap(Px(14));
        return ui.Scroll(
                "images-scroll",
                ScrollAxis.Vertical,
                new ScrollOptions(
                    smoothScrolling: true,
                    showScrollbar: true,
                    scrollbarGutter: true
                ),
                body
            )
            .Grow()
            .Width(Percent(100));
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
            .AspectRatio(4f / 3f)
            .Radius(Px(12))
            .Background(theme.Colors.ElementActive);

        return ui.VStack(
                ui.Text(label).FontSize(Px(theme.Typography.Body)).TextColor(theme.Colors.Text),
                image
            )
            .Gap(Px(8))
            .Padding(Px(12))
            .Width(Px(240))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(12))
            .ShadowColor(Colors.Rgba(0, 0, 0, 90))
            .ShadowOffset(0, 6)
            .ShadowBlur(16)
            .ShadowSpread(0);
    }

    private static Element VectorChart(ref RenderContext ui, float progress)
    {
        var theme = ui.Theme;
        var firstY = Lerp(100, 48, progress);
        var secondY = Lerp(100, 42, progress);
        var finalY = Lerp(100, 18, progress);
        var area = ui.Path()
            .MoveTo(0, 100)
            .CubicTo(18, Lerp(100, 82, progress), 24, Lerp(100, 42, progress), 38, firstY)
            .CubicTo(52, Lerp(100, 54, progress), 61, Lerp(100, 78, progress), 72, secondY)
            .CubicTo(82, Lerp(100, 10, progress), 91, Lerp(100, 24, progress), 100, finalY)
            .LineTo(100, 100)
            .Close()
            .Fill(theme.Colors.Accent.WithAlpha(44));
        var line = ui.Path()
            .MoveTo(0, 100)
            .CubicTo(18, Lerp(100, 82, progress), 24, Lerp(100, 42, progress), 38, firstY)
            .CubicTo(52, Lerp(100, 54, progress), 61, Lerp(100, 78, progress), 72, secondY)
            .CubicTo(82, Lerp(100, 10, progress), 91, Lerp(100, 24, progress), 100, finalY)
            .Stroke(theme.Colors.Accent, Px(3));
        var baseline = ui.Line(0, 100, 100, 100)
            .Stroke(theme.Colors.BorderVariant, Px(1))
            .Dash(Px(3), Px(3));
        var firstDot = ui.Circle(38, firstY, 2.5f)
            .Fill(theme.Colors.SurfaceBackground)
            .Stroke(theme.Colors.Accent, Px(2));
        var secondDot = ui.Circle(72, secondY, 2.5f)
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

    private (float Progress, bool Active) ChartAnimation()
    {
        if (_chartAnimationStart == 0)
        {
            return (1, false);
        }

        var linear = Math.Clamp(
            Stopwatch.GetElapsedTime(_chartAnimationStart).TotalSeconds / ChartAnimationSeconds,
            0,
            1
        );
        var eased = 1 - Math.Pow(1 - linear, 3);
        return ((float)eased, linear < 1);
    }

    private static float Lerp(float start, float end, float progress) =>
        start + (end - start) * progress;
}
