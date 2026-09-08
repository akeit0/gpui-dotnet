using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ArenaGrowthView : View
{
    private static readonly string LargePayload = new('x', 20 * 1024);

    [System.Runtime.CompilerServices.InlineArray(600)]
    private struct RowBuffer
    {
        private Element _element;
    }

    protected override Element Render(ref RenderContext ui)
    {
        RowBuffer buffer = default;
        Span<Element> rows = buffer;
        rows[0] = ui.Text(LargePayload);
        for (var index = 1; index < rows.Length; index++)
        {
            rows[index] = ui.Text($"Growth row {index:D4}")
                .Padding(Px(1))
                .Width(Px(120))
                .Background(ui.Theme.Colors.Background)
                .ItemsCenter();
        }

        return ui.VStack(rows).Gap(Px(1));
    }
}
