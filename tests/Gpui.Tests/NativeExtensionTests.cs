using System.Buffers.Binary;
using Gpui.Editor;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed class NativeExtensionTests
{
    [Fact]
    public async Task GenericExtensionEventBindingDecodesTypedEvent()
    {
        var view = new ExtensionProbeView();
        Attach(view, 41);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new ExtensionNoopRenderer(), view);
            var binding = ui.BindNativeExtensionEvent<ExtensionProbeView, ProbeExtensionEvent>(
                view,
                static (target, extensionEvent) => target.ExtensionEventValue = extensionEvent.Value
            );

            await view.DispatchNativeExtensionCore(
                unchecked((uint)binding.Token),
                new NativeExtensionEvent(7, 0, 3, [42])
            );

            Assert.Equal(42, view.ExtensionEventValue);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void EditorChangeDecodesRevisionedUtf8Replacement()
    {
        var payload = new byte[39];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, 7);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(20), 4);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(28), 3);
        "界"u8.CopyTo(payload.AsSpan(36));

        var changed = EditorChangedEvent.Decode(new NativeExtensionEvent(1, 0, 8, payload));

        Assert.Equal(7ul, changed.BaseRevision);
        Assert.Equal(8ul, changed.Revision);
        Assert.Equal(EditorChangeOrigin.User, changed.Origin);
        var edit = Assert.Single(changed.Edits);
        Assert.Equal(1ul, edit.Start);
        Assert.Equal(4ul, edit.DeletedLength);
        Assert.Equal("界"u8.ToArray(), edit.InsertedUtf8.ToArray());
    }

    [Fact]
    public void EditorCommandRejectionDecodesCurrentRevision()
    {
        Span<byte> payload = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload,
            (ushort)EditorCommandKind.ApplyEdit
        );
        BinaryPrimitives.WriteUInt64LittleEndian(payload[4..], 7);

        var rejected = EditorCommandRejectedEvent.Decode(
            new NativeExtensionEvent(
                2,
                (ushort)EditorCommandRejectedReason.StaleRevision,
                8,
                payload.ToArray()
            )
        );

        Assert.Equal(EditorCommandKind.ApplyEdit, rejected.Command);
        Assert.Equal(EditorCommandRejectedReason.StaleRevision, rejected.Reason);
        Assert.Equal(7ul, rejected.ExpectedRevision);
        Assert.Equal(8ul, rejected.CurrentRevision);
    }

    [Fact]
    public void EditorControllerEncodesTypedRevisionedCommands()
    {
        var commands = new List<CapturedEditorCommand>();
        var view = new ExtensionProbeView();
        Attach(
            view,
            41,
            (_, _, _, _, _, _, command, flags, revision, payload) =>
                commands.Add(new CapturedEditorCommand(command, flags, revision, payload.ToArray()))
        );
        try
        {
            view.Editor.Focus();
            view.Editor.SetSelection(7, 1, 4);
            view.Editor.ReplaceDocument(7, "界");
            view.Editor.ApplyEdit(8, new EditorEdit(1, 3, "z"u8.ToArray()));

            Assert.Collection(
                commands,
                command =>
                {
                    Assert.Equal((ushort)EditorCommandKind.Focus, command.Command);
                    Assert.Equal(0ul, command.ExpectedRevision);
                    Assert.Empty(command.Payload);
                },
                command =>
                {
                    Assert.Equal((ushort)EditorCommandKind.SetSelection, command.Command);
                    Assert.Equal(7ul, command.ExpectedRevision);
                    Assert.Equal(1ul, BinaryPrimitives.ReadUInt64LittleEndian(command.Payload));
                    Assert.Equal(
                        4ul,
                        BinaryPrimitives.ReadUInt64LittleEndian(command.Payload.AsSpan(8))
                    );
                },
                command =>
                {
                    Assert.Equal((ushort)EditorCommandKind.ReplaceDocument, command.Command);
                    Assert.Equal(7ul, command.ExpectedRevision);
                    Assert.Equal("界"u8.ToArray(), command.Payload);
                },
                command =>
                {
                    Assert.Equal((ushort)EditorCommandKind.ApplyEdit, command.Command);
                    Assert.Equal(8ul, command.ExpectedRevision);
                    Assert.Equal(1ul, BinaryPrimitives.ReadUInt64LittleEndian(command.Payload));
                    Assert.Equal(
                        3ul,
                        BinaryPrimitives.ReadUInt64LittleEndian(command.Payload.AsSpan(8))
                    );
                    Assert.Equal("z"u8.ToArray(), command.Payload[16..]);
                }
            );
            Assert.All(commands, command => Assert.Equal(0, command.Flags));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void EditorOptionsRejectNegativeLineNumberWidth()
    {
        var view = new ExtensionProbeView();
        Attach(view, 41);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderNegativeLineNumberWidth(view));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

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

    private static void RenderNegativeLineNumberWidth(ExtensionProbeView view)
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender(new ExtensionNoopRenderer(), view);
        ui.Editor(
            view.Editor,
            new EditorOptions
            {
                LineNumberWidth = new Pixels(-1),
            }
        );
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
        internal byte ExtensionEventValue { get; set; }

        protected override void OnMounted(ref ViewContext context) =>
            Editor = context.CreateEditorController("document");

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }

    private sealed record ProbeExtensionEvent(byte Value)
        : INativeExtensionEvent<ProbeExtensionEvent>
    {
        public static ProbeExtensionEvent Decode(NativeExtensionEvent nativeEvent) =>
            new(nativeEvent.Payload.Span[0]);
    }

    private sealed record CapturedEditorCommand(
        ushort Command,
        ushort Flags,
        ulong ExpectedRevision,
        byte[] Payload
    );

    private sealed unsafe class ExtensionNoopRenderer : IViewRenderer
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
