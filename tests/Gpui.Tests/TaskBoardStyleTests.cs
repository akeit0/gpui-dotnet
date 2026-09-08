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
        foreach (
            var surface in new[] { style.Colors.Normal, style.Colors.Hover, style.Colors.Pressed }
        )
        {
            foreach (var foreground in new[] { surface.Foreground, style.SecondaryText })
            {
                ContrastAssert.OpaqueText(
                    foreground,
                    surface.Background,
                    $"{theme.Name}, selected={selected}"
                );
            }
        }
    }
}
