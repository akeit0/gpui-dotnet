using Gpui;
using static Gpui.Units;

internal sealed class ArenaGrowthView : View
{
    private static readonly string LargePayload = new('x', 20 * 1024);

    protected override Element Render(ref RenderContext ui)
    {
        Span<Element> rows = stackalloc Element[600];
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
