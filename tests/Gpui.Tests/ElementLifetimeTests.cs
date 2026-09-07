using System.Runtime.CompilerServices;
using static Gpui.Units;

namespace Gpui.Tests;

public sealed class ElementLifetimeTests
{
    [Fact]
    public void DisposedElementsRejectNumericAndDataWritesAndComposition()
    {
        var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var escaped = ui.Div();
        arena.Dispose();
        using var replacement = new RenderArenaOwner();
        var current = replacement.BeginRender().Div();

        Assert.Throws<ObjectDisposedException>(() => escaped.Padding(Px(8)));
        Assert.Throws<ObjectDisposedException>(() => escaped.FontFamily("serif"));
        Assert.Throws<ObjectDisposedException>(() => escaped.FontFallbacks(["serif"]));
        Assert.Throws<ObjectDisposedException>(() => escaped.FontFeatures([("liga", 1u)]));
        Assert.Throws<ObjectDisposedException>(() => current.Child(escaped));
        Assert.Equal(0, replacement.GetStats().Children);
    }

    [Fact]
    public void ReusedArenaRejectsOldElementsBeforeWritingData()
    {
        using var arena = new RenderArenaOwner();
        var escaped = arena.BeginRender().Div();
        var current = arena.BeginRender().Div();
        var before = arena.GetStats();
        Assert.Throws<InvalidOperationException>(() => escaped.FontFamily("stale"));
        Assert.Throws<InvalidOperationException>(() => escaped.Padding(Px(8)));
        Assert.Equal(before.Utf8Bytes, arena.GetStats().Utf8Bytes);
        Assert.Equal(before.Ops, arena.GetStats().Ops);
        arena.Validate(current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextAndInterpolationRejectResetOrDisposedStorage(bool dispose)
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var handler = new Utf8InterpolatedStringHandler(1, 0, ui);
        handler.AppendLiteral("a");
        if (dispose) arena.Dispose();
        else arena.BeginRender();

        Exception? contextError = null;
        Exception? handlerError = null;
        try { _ = ui.Text("stale"); }
        catch (Exception error) { contextError = error; }
        try { handler.AppendLiteral("stale"); }
        catch (Exception error) { handlerError = error; }
        Assert.IsAssignableFrom<InvalidOperationException>(contextError);
        Assert.IsAssignableFrom<InvalidOperationException>(handlerError);
    }

    [Fact]
    public void ElementsKeepStorageAliveWithoutAnExplicitOwnerReference()
    {
        var element = CreateElement(out var weak);
        Collect();
        Assert.True(weak.IsAlive);
        element.Padding(Px(8));
        element.Inner.Owner!.Validate(element);
        element.Inner.Owner.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Element<DivTag> CreateElement(out WeakReference weak)
    {
        var owner = new RenderArenaOwner();
        weak = new WeakReference(owner);
        return owner.BeginRender().Div();
    }

    [Fact]
    public void ContextKeepsStorageAliveBeforeItsFirstElement()
    {
        var ui = CreateContext(out var weak);
        Collect();
        Assert.True(weak.IsAlive);
        var element = ui.Text("alive");
        element.Inner.Owner!.Validate(element);
        element.Inner.Owner.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RenderContext CreateContext(out WeakReference weak)
    {
        var owner = new RenderArenaOwner();
        weak = new WeakReference(owner);
        return owner.BeginRender();
    }

    [Fact]
    public unsafe void ExhaustedGenerationDoesNotWrapOrResetOutput()
    {
        using var arena = new RenderArenaOwner();
        arena.BeginRender().Text("preserved");
        arena.NativeArena->Generation = uint.MaxValue;
        Assert.Throws<OverflowException>(() => arena.BeginRender());
        Assert.Equal(uint.MaxValue, arena.GetStats().Generation);
        Assert.Equal(1, arena.GetStats().Nodes);
        Assert.Equal(9, arena.GetStats().Utf8Bytes);
    }

    [Fact]
    public void CrossThreadWritesResetAndDisposalFailWithoutChangingStorage()
    {
        using var arena = new RenderArenaOwner();
        var element = arena.BeginRender().Div();
        Exception? writeError = null;
        Exception? resetError = null;
        Exception? disposeError = null;
        var thread = new Thread(() =>
        {
            try { element.Padding(Px(8)); }
            catch (Exception error) { writeError = error; }
            try { arena.BeginRender(); }
            catch (Exception error) { resetError = error; }
            try { arena.Dispose(); }
            catch (Exception error) { disposeError = error; }
        });
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(writeError);
        Assert.IsType<InvalidOperationException>(resetError);
        Assert.IsType<InvalidOperationException>(disposeError);
        Assert.Equal(0, arena.GetStats().Ops);
        arena.Validate(element);
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("reset")]
    [InlineData("write")]
    public void FormatterCannotInvalidateBorrowedMemory(string operation)
    {
        using var arena = new RenderArenaOwner(1, 1, 1, 1);
        var ui = arena.BeginRender();
        var formatter = new ReentrantFormatter(arena, operation);
        Exception? failure = null;
        try { _ = ui.Text($"{formatter}"); }
        catch (Exception error) { failure = error; }
        Assert.IsType<InvalidOperationException>(failure);
        var fresh = arena.BeginRender().Text("recovered");
        arena.Validate(fresh);
    }

    private sealed class ReentrantFormatter(RenderArenaOwner arena, string operation)
    {
        public override string ToString()
        {
            Collect();
            switch (operation)
            {
                case "dispose": arena.Dispose(); break;
                case "reset": arena.BeginRender(); break;
                default: new RenderContext(arena).Text(new string('x', 8192)); break;
            }
            return "unsafe";
        }
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
