using Gpui.Editor;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe class NativeExtensionTests
{
    [Fact]
    public void OptionalEditorSchemaWritesGenericExtensionEnvelope()
    {
        var view = new ExtensionProbeView();
        Attach(view, 41);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new ExtensionNoopRenderer(), view);
            var editor = ui.Editor(
                "document",
                new EditorOptions
                {
                    InitialValue = "first\nsecond",
                    Language = "rust",
                    ReadOnly = true,
                    ShowWhitespace = true,
                }
            );

            arena.Validate(editor);

            var dump = arena.Dump(editor);
            Assert.Contains("component=NativeExtension", dump, StringComparison.Ordinal);
            Assert.Contains("gpui.net.editor", dump, StringComparison.Ordinal);
            Assert.Contains("556347593588921F", dump, StringComparison.Ordinal);
            Assert.Equal(1, arena.GetStats().Nodes);
            Assert.Equal(1, arena.GetStats().Ops);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("bad/id")]
    [InlineData("")]
    public void ExtensionRequirementRejectsUnstableIdentifiers(string id)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new NativeExtensionRequirement(id, 1, 1)
        );
    }

    [Fact]
    public void DefaultExtensionRequirementIsRejectedByBuilder()
    {
        Assert.Throws<ArgumentException>(RenderDefaultExtension);
    }

    private static void RenderDefaultExtension()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        ui.NativeExtension(default, "resource", ReadOnlySpan<char>.Empty);
    }

    private static void Attach(View view, uint handle) =>
        view.AttachRuntime(
            handle,
            static callback => callback(),
            static _ => { },
            static (_, _) => { },
            static (_, _, _) => { }
        );

    private sealed class ExtensionProbeView : View
    {
        protected override Element Render(ref RenderContext ui) => ui.Div();
    }

    private sealed class ExtensionNoopRenderer : IViewRenderer
    {
        public Element RenderChild<TView>(ViewBase owner, ChildSlot slot, RenderArena* destination)
            where TView : View, IGeneratedViewFactory<TView> => throw new NotSupportedException();

        public Element RenderChild<TView, TProps>(
            ViewBase owner,
            ChildSlot slot,
            in TProps props,
            RenderArena* destination
        )
            where TProps : IEquatable<TProps>
            where TView : View<TProps>, IGeneratedViewFactory<TView> =>
            throw new NotSupportedException();
    }
}
