using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe class ValidatorReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnattachedCyclesAreRejectedForOrdinaryElements(bool twoNodes)
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var first = ui.Div();
        if (twoNodes)
        {
            var second = ui.Div(first);
            first.Child(second);
        }
        else first.Child(first);
        Element root = ui.Div();
        Assert.Contains("cycle", Assert.Throws<InvalidOperationException>(() => arena.Validate(root)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanelIdsAreScopedToTheirNearestDockArea(bool nested)
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var inner = Area(arena, "inner").Child(ui.DockTabs(0, ui.DockPanel("same", "Inner", ui.Text("inner"))));
        var outer = Area(arena, "outer").Child(ui.DockTabs(0, ui.DockPanel("same", "Outer", nested ? inner : ui.Text("outer"))));
        Element root = nested ? outer : ui.Div(inner, outer);
        ReverseEdges(arena);
        arena.Validate(root);
    }

    [Theory]
    [InlineData("panel", "panel", true)]
    [InlineData("panel", "panel-long", false)]
    [InlineData("項目", "項目", true)]
    [InlineData("項目", "項目二", false)]
    public void PanelUniquenessUsesTheEntireUtf8Id(string first, string second, bool duplicate)
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var root = Area(arena, "area").Child(ui.DockTabs(0,
            ui.DockPanel(first, "One", ui.Text("one")),
            ui.DockPanel(second, "Two", ui.Text("two"))));
        ReverseEdges(arena);
        if (duplicate)
            Assert.Contains("duplicate panel ID", Assert.Throws<InvalidOperationException>(() => arena.Validate(root)).Message);
        else arena.Validate(root);
    }

    [Fact]
    public void FinalDockOperationsDetermineStructuralValidation()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var tabs = ui.DockTabs(0, ui.DockPanel("p", "Panel", ui.Text("content")));
        ArenaWriter.AddU32(tabs.Inner, OpCode.DockActiveIndex, 7);
        ArenaWriter.AddU32(tabs.Inner, OpCode.DockActiveIndex, 0);
        var root = Area(arena, "area").Child(tabs);
        arena.Validate(root);
        ArenaWriter.AddU32(tabs.Inner, OpCode.DockActiveIndex, 2);
        Assert.Contains("active index", Assert.Throws<InvalidOperationException>(() => arena.Validate(root)).Message);
    }

    [Fact]
    public void LargeValidationScratchIsResetAfterFailureAndReorderedEdges()
    {
        using var arena = new RenderArenaOwner();
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var ui = arena.BeginRender();
            var root = ui.Div();
            var last = ui.Div();
            root.Child(last);
            for (var index = 0; index < 1024; index++) root.Child(ui.Text("leaf"));
            if ((iteration & 1) == 0) root.Child(last);
            ReverseEdges(arena);
            if ((iteration & 1) == 0)
                Assert.Contains("attached more than once", Assert.Throws<InvalidOperationException>(() => arena.Validate(root)).Message);
            else arena.Validate(root);
            for (var index = 0; index < arena.NativeArena->NodeLength; index++)
                Assert.Equal(0, arena.NativeArena->Nodes[index].Flags);
        }
    }

    private static Element<DockAreaTag> Area(RenderArenaOwner arena, string key) =>
        ArenaWriter.AddNode<DockAreaTag>(arena.NativeArena, ComponentId.DockArea, key);

    private static void ReverseEdges(RenderArenaOwner arena) =>
        new Span<ChildRecord>(arena.NativeArena->Children, arena.NativeArena->ChildLength).Reverse();

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
