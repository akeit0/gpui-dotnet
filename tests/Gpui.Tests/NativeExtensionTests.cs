using Gpui.Editor;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe class NativeExtensionTests
{
    [Fact]
    public void OptionalEditorSchemaWritesGenericExtensionEnvelope()
    {
        var view = new ExtensionProbeView();
        uint ownerView = 0;
        uint schemaVersion = 0;
        ulong schemaHash = 0;
        string? extensionId = null;
        string? componentKind = null;
        string? key = null;
        ushort command = 0;
        byte[]? payload = null;
        Attach(
            view,
            41,
            (
                owner,
                version,
                hash,
                id,
                kind,
                commandKey,
                commandId,
                _,
                _,
                commandPayload
            ) =>
            {
                ownerView = owner;
                schemaVersion = version;
                schemaHash = hash;
                extensionId = System.Text.Encoding.UTF8.GetString(id);
                componentKind = System.Text.Encoding.UTF8.GetString(kind);
                key = System.Text.Encoding.UTF8.GetString(commandKey);
                command = commandId;
                payload = commandPayload.ToArray();
            }
        );
        try
        {
            view.Editor.Bootstrap("first\nsecond");

            Assert.Equal(41u, ownerView);
            Assert.Equal(EditorExtension.Requirement.Version, schemaVersion);
            Assert.Equal(EditorExtension.SchemaHash, schemaHash);
            Assert.Equal("gpui.net.editor", extensionId);
            Assert.Equal("editor", componentKind);
            Assert.Equal("document", key);
            Assert.Equal(1, command);
            Assert.Equal("first\nsecond", System.Text.Encoding.UTF8.GetString(payload!));

            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new ExtensionNoopRenderer(), view);
            var editor = ui.Editor(
                view.Editor,
                new EditorOptions
                {
                    Language = "rust",
                    ReadOnly = true,
                    ShowWhitespace = true,
                }
            );

            arena.Validate(editor);

            var dump = arena.Dump(editor);
            Assert.Contains("component=NativeExtension", dump, StringComparison.Ordinal);
            Assert.Contains("gpui.net.editor", dump, StringComparison.Ordinal);
            Assert.Contains($"{EditorExtension.SchemaHash:X16}", dump, StringComparison.Ordinal);
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

    private static void Attach(
        View view,
        uint handle,
        NativeExtensionCommandDispatcher? extensionCommand = null
    ) =>
        view.AttachRuntime(
            handle,
            static callback => callback(),
            static _ => { },
            static (_, _) => { },
            static (_, _, _) => { },
            extensionCommand ?? (static (_, _, _, _, _, _, _, _, _, _) => { })
        );

    private sealed class ExtensionProbeView : View
    {
        internal EditorController Editor { get; private set; }

        protected override void OnMounted(ref ViewContext context) =>
            Editor = context.CreateEditorController("document");

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
