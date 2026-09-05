using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe class ValidatorReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisconnectedDockCyclesTerminateAndRestoreFlags(bool twoNodes)
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var panel = ui.DockPanel("p", "Panel", ui.Text("content"));
        var tabs = ui.DockTabs(0, panel);
        var split = ui.DockSplit(DockAxis.Horizontal, tabs);
        if (twoNodes)
        {
            var other = ui.DockSplit(DockAxis.Horizontal, split);
            split.Child(other);
        }
        else split.Child(split);
        Element root = ui.Div();
        Assert.Throws<InvalidOperationException>(() => arena.Validate(root));
        for (var index = 0; index < arena.NativeArena->NodeLength; index++)
            Assert.Equal(0, arena.NativeArena->Nodes[index].Flags);
        Assert.Throws<InvalidOperationException>(() => arena.Validate(root));
    }

}
