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

        return ui.VStack(
                ui.Text("One cached SVG, rendered with three native presentation modes."u8)
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
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
}
