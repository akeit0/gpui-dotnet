using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ImageGalleryView : View
{
    private static readonly string ImagePath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "gpui-net.svg"
    );

    protected override Element Render(ref RenderContext ui)
    {
        var contain = ImageCard(ref ui, "Contain", ImageFit.Contain, false);
        var cover = ImageCard(ref ui, "Cover", ImageFit.Cover, false);
        var grayscale = ImageCard(ref ui, "Grayscale", ImageFit.Cover, true);

        return ui.VStack(
                ui.Text("One cached SVG, rendered with three native presentation modes."u8)
                    .FontSize(Px(13))
                    .TextColor(Rgb(71, 85, 105)),
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
        var image = ui.Image(ImagePath)
            .Fit(fit)
            .Grayscale(grayscale)
            .Width(Percent(100))
            .Height(Px(180))
            .Radius(Px(12))
            .Background(Rgb(226, 232, 240));

        return ui.VStack(ui.Text(label).FontSize(Px(14)).TextColor(Rgb(30, 41, 59)), image)
            .Gap(Px(8))
            .Padding(Px(12))
            .Width(Px(240))
            .Background(Rgb(255, 255, 255))
            .BorderWidth(Px(1))
            .BorderColor(Rgb(226, 232, 240))
            .Radius(Px(12));
    }
}
