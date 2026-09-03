using System.Buffers.Binary;
using System.Buffers.Text;
using Gpui.Editor;
using Gpui.Interop;
using static Gpui.Units;

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
    public void EditorConfigurationEncodesDirectUtf8WithoutTempStrings()
    {
        // Golden vectors: byte-identical to the historical decimal/newline layout, so the
        // native parser and the schema hash are untouched.
        Assert.Equal(
            "12\n\n0\n0\n0"u8.ToArray(),
            EditorElements.Configuration(new EditorOptions(), 0, 0)
        );
        Assert.Equal(
            "12\nrust\n42\n43\n64"u8.ToArray(),
            EditorElements.Configuration(
                new EditorOptions { Language = "rust", LineNumberWidth = Px(64) },
                42,
                43
            )
        );
        Assert.Equal(
            "31\nrust\n18446744073709551615\n0\n0.5"u8.ToArray(),
            EditorElements.Configuration(
                new EditorOptions
                {
                    Language = "rust",
                    Disabled = true,
                    ReadOnly = true,
                    LineNumbers = true,
                    Folding = true,
                    ShowWhitespace = true,
                    LineNumberWidth = Px(0.5f),
                },
                ulong.MaxValue,
                0
            )
        );
        Assert.Equal(
            "12\n界\n0\n0\n100.25"u8.ToArray(),
            EditorElements.Configuration(
                new EditorOptions { Language = "界", LineNumberWidth = Px(100.25f) },
                0,
                0
            )
        );
    }

    [Fact]
    public void EditorConfigurationAllocatesOnlyItsResult()
    {
        var options = new EditorOptions { Language = "rust", LineNumberWidth = Px(64) };
        _ = EditorElements.Configuration(options, 42, 43);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var configuration = EditorElements.Configuration(options, 42, 43);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Exactly the returned array: object header plus length plus payload, no temp strings.
        Assert.Equal((ulong)(24 + configuration.Length), (ulong)allocated);
        Assert.Equal("12\nrust\n42\n43\n64"u8.ToArray(), configuration);
    }

    [Fact]
    public void EditorConfigurationRejectsInvalidOptions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EditorElements.Configuration(new EditorOptions { Language = null! }, 0, 0)
        );
        Assert.Throws<ArgumentException>(() =>
            EditorElements.Configuration(new EditorOptions { Language = "a\0b" }, 0, 0)
        );
        Assert.Throws<ArgumentException>(() =>
            EditorElements.Configuration(new EditorOptions { Language = "a\nb" }, 0, 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EditorElements.Configuration(new EditorOptions { LineNumberWidth = new Pixels(-1) }, 0, 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EditorElements.Configuration(
                new EditorOptions { LineNumberWidth = new Pixels(float.NaN) },
                0,
                0
            )
        );
    }

    [Fact]
    public unsafe void ByteConfigurationReachesNodeDataIntact()
    {
        var view = new ExtensionProbeView();
        Attach(view, 41);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new ExtensionNoopRenderer(), view);
            var expected = EditorElements.Configuration(
                new EditorOptions { Language = "rust", LineNumberWidth = Px(64) },
                0,
                0
            );
            var editor = ui.Editor(
                view.Editor,
                new EditorOptions { Language = "rust", LineNumberWidth = Px(64) }
            );
            arena.Validate(editor);

            // Node data is id\0kind\0key\0version\0hash\0config: every field must
            // round-trip, with the version/hash proving direct-UTF8 formatting
            // matches the historical char-then-UTF8 encoding.
            var native = arena.NativeArena;
            var found = false;
            for (var i = 0; i < native->NodeLength; i++)
            {
                var node = native->Nodes[i];
                if (node.Component != (ushort)ComponentId.NativeExtension)
                {
                    continue;
                }
                var payload = new ReadOnlySpan<byte>(
                    native->Utf8 + node.DataOffset,
                    checked((int)node.DataLength)
                );
                var fields = new Range[6];
                var field = 0;
                var start = 0;
                for (var index = 0; index < payload.Length && field < 5; index++)
                {
                    if (payload[index] != 0)
                    {
                        continue;
                    }
                    fields[field++] = new Range(start, index);
                    start = index + 1;
                }
                Assert.Equal(5, field);
                fields[5] = new Range(start, payload.Length);
                Assert.True(payload[fields[0]].SequenceEqual("gpui.net.editor"u8));
                Assert.True(payload[fields[1]].SequenceEqual("editor"u8));
                Assert.True(payload[fields[2]].SequenceEqual("document"u8));
                Assert.True(
                    Utf8Parser.TryParse(payload[fields[3]], out uint version, out var versionLength)
                    && versionLength == payload[fields[3]].Length
                    && version == EditorSchema.SchemaVersion
                );
                Assert.True(
                    Utf8Parser.TryParse(
                        payload[fields[4]],
                        out ulong hash,
                        out var hashLength,
                        'X'
                    )
                    && hashLength == payload[fields[4]].Length
                    && hash == EditorSchema.SchemaHash
                );
                Assert.True(payload[fields[5]].SequenceEqual(expected));
                found = true;
            }
            Assert.True(found);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public unsafe void ByteChannelRejectsNulConfiguration()
    {
        var view = new ExtensionProbeView();
        Attach(view, 41);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new ExtensionNoopRenderer(), view);
            try
            {
                ui.NativeExtension(view.Editor.Native, [(byte)'a', 0]);
                Assert.Fail("Expected a NUL-configuration ArgumentException.");
            }
            catch (ArgumentException)
            {
            }
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
