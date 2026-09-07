extern alias TaskBoard;

using Board = TaskBoard::Gpui;

namespace Gpui.Tests;

public sealed class TaskBoardStyleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NavigationContentRemainsReadableAcrossInteractionStates(bool dark, bool selected)
    {
        var theme = dark ? Board.TaskBoardThemes.Dark : Board.TaskBoardThemes.Light;
        var style = Board.BoardStyles.Button(theme, Board.BoardButtonVariant.Navigation, selected);
        foreach (var background in new[] { style.Background, style.HoverBackground, style.ActiveBackground })
        {
            foreach (var foreground in new[] { style.Text, style.SecondaryText })
            {
                var ratio = Contrast(foreground, background);
                Assert.True(ratio >= 4.5, $"{theme.Name}, selected={selected}: {foreground} on {background} has contrast {ratio:F2}.");
            }
        }
    }

    private static double Contrast(Color foreground, Color background)
    {
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color color)
    {
        Assert.Equal(255u, color.Rgba & 255);
        static double Linear(uint value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.Rgba >> 24)
            + 0.7152 * Linear((color.Rgba >> 16) & 255)
            + 0.0722 * Linear((color.Rgba >> 8) & 255);
    }
}
