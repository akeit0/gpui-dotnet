using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;

namespace Gpui.Editor;

/// <summary>Schema identity shared by the managed editor package and its custom native host.</summary>
public static class EditorExtension
{
    public const ulong SchemaHash = EditorSchema.SchemaHash;

    public static NativeExtensionRequirement Requirement { get; } =
        new(EditorSchema.ExtensionId, EditorSchema.SchemaVersion, SchemaHash);

    internal static NativeExtensionComponent Component { get; } =
        new(Requirement, EditorSchema.Editor.Kind);
}

/// <summary>Initial and declarative presentation options for the native editor.</summary>
public sealed record EditorOptions
{
    /// <summary>Optional highlighter language name understood by the native editor.</summary>
    public string Language { get; init; } = string.Empty;

    public bool Disabled { get; init; }
    public bool ReadOnly { get; init; }
    public bool LineNumbers { get; init; } = true;

    /// <summary>
    /// Optional fixed width of the line-number column. When omitted, the native editor sizes it
    /// from the document's line count. Folding controls reserve separate space.
    /// </summary>
    public Pixels? LineNumberWidth { get; init; }

    public bool Folding { get; init; } = true;
    public bool ShowWhitespace { get; init; }
}

/// <summary>Identifies why the native document changed.</summary>
public enum EditorChangeOrigin : ushort
{
    /// <summary>The user changed the document through the native editor.</summary>
    User = 0,

    /// <summary>The document changed through an <see cref="EditorController"/> command.</summary>
    Command = 1,
}

/// <summary>Identifies an imperative editor operation.</summary>
public enum EditorCommandKind : ushort
{
    Bootstrap = EditorSchema.Editor.CommandBootstrap,
    Focus = EditorSchema.Editor.CommandFocus,
    SetSelection = EditorSchema.Editor.CommandSetSelection,
    ReplaceDocument = EditorSchema.Editor.CommandReplaceDocument,
    ApplyEdit = EditorSchema.Editor.CommandApplyEdit,
}

/// <summary>Explains why a state-dependent editor command was not applied.</summary>
public enum EditorCommandRejectedReason : ushort
{
    StaleRevision = 1,
    InvalidRange = 2,
}

/// <summary>One contiguous UTF-8 replacement, expressed in bytes of the prior revision.</summary>
public readonly record struct EditorEdit(
    ulong Start,
    ulong DeletedLength,
    ReadOnlyMemory<byte> InsertedUtf8
);

/// <summary>A revisioned native editor transaction decoded from the extension event envelope.</summary>
public sealed class EditorChangedEvent : INativeExtensionEvent<EditorChangedEvent>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private EditorChangedEvent(
        ulong baseRevision,
        ulong revision,
        EditorChangeOrigin origin,
        IReadOnlyList<EditorEdit> edits
    )
    {
        BaseRevision = baseRevision;
        Revision = revision;
        Origin = origin;
        Edits = edits;
    }

    public ulong BaseRevision { get; }
    public ulong Revision { get; }
    public EditorChangeOrigin Origin { get; }
    public IReadOnlyList<EditorEdit> Edits { get; }

    public static EditorChangedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (nativeEvent.Kind != EditorSchema.Editor.EventChanged)
        {
            throw new InvalidOperationException($"Unknown editor event kind {nativeEvent.Kind}.");
        }
        if (!Enum.IsDefined((EditorChangeOrigin)nativeEvent.Flags))
        {
            throw new InvalidOperationException(
                $"Unknown editor change origin {nativeEvent.Flags}."
            );
        }

        var payload = nativeEvent.Payload;
        var span = payload.Span;
        if (span.Length < 12)
        {
            throw new InvalidOperationException("The editor change payload is truncated.");
        }
        var baseRevision = BinaryPrimitives.ReadUInt64LittleEndian(span);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        if (nativeEvent.Revision <= baseRevision || count == 0 || count > (span.Length - 12) / 24)
        {
            throw new InvalidOperationException("The editor change revision or edit count is invalid.");
        }

        var edits = new EditorEdit[checked((int)count)];
        var offset = 12;
        ulong previousStart = ulong.MaxValue;
        for (var index = 0; index < edits.Length; index++)
        {
            if (span.Length - offset < 24)
            {
                throw new InvalidOperationException("The editor change edit header is truncated.");
            }
            var start = BinaryPrimitives.ReadUInt64LittleEndian(span[offset..]);
            var deletedLength = BinaryPrimitives.ReadUInt64LittleEndian(span[(offset + 8)..]);
            var insertedLength = BinaryPrimitives.ReadUInt64LittleEndian(span[(offset + 16)..]);
            offset += 24;
            if (
                insertedLength > int.MaxValue
                || insertedLength > checked((ulong)(span.Length - offset))
                || ulong.MaxValue - start < deletedLength
                || (index != 0 && start + deletedLength > previousStart)
            )
            {
                throw new InvalidOperationException("The editor change edit range is invalid.");
            }

            var inserted = payload.Slice(offset, checked((int)insertedLength));
            try
            {
                _ = StrictUtf8.GetCharCount(inserted.Span);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException(
                    "The editor change contains invalid UTF-8.",
                    exception
                );
            }
            edits[index] = new EditorEdit(start, deletedLength, inserted);
            previousStart = start;
            offset += checked((int)insertedLength);
        }
        if (offset != span.Length)
        {
            throw new InvalidOperationException("The editor change payload has trailing data.");
        }

        return new EditorChangedEvent(
            baseRevision,
            nativeEvent.Revision,
            (EditorChangeOrigin)nativeEvent.Flags,
            edits
        );
    }
}

/// <summary>A revision-checked editor command that native state could not apply.</summary>
public sealed class EditorCommandRejectedEvent
    : INativeExtensionEvent<EditorCommandRejectedEvent>
{
    private EditorCommandRejectedEvent(
        EditorCommandKind command,
        EditorCommandRejectedReason reason,
        ulong expectedRevision,
        ulong currentRevision
    )
    {
        Command = command;
        Reason = reason;
        ExpectedRevision = expectedRevision;
        CurrentRevision = currentRevision;
    }

    public EditorCommandKind Command { get; }
    public EditorCommandRejectedReason Reason { get; }
    public ulong ExpectedRevision { get; }
    public ulong CurrentRevision { get; }

    public static EditorCommandRejectedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (nativeEvent.Kind != EditorSchema.Editor.EventCommandRejected)
        {
            throw new InvalidOperationException($"Unknown editor event kind {nativeEvent.Kind}.");
        }
        if (!Enum.IsDefined((EditorCommandRejectedReason)nativeEvent.Flags))
        {
            throw new InvalidOperationException(
                $"Unknown editor command rejection reason {nativeEvent.Flags}."
            );
        }

        var span = nativeEvent.Payload.Span;
        if (span.Length != 12 || BinaryPrimitives.ReadUInt16LittleEndian(span[2..]) != 0)
        {
            throw new InvalidOperationException("The editor command rejection payload is invalid.");
        }
        var command = (EditorCommandKind)BinaryPrimitives.ReadUInt16LittleEndian(span);
        if (!Enum.IsDefined(command) || command == EditorCommandKind.Bootstrap)
        {
            throw new InvalidOperationException($"Unknown rejected editor command {command}.");
        }

        return new EditorCommandRejectedEvent(
            command,
            (EditorCommandRejectedReason)nativeEvent.Flags,
            BinaryPrimitives.ReadUInt64LittleEndian(span[4..]),
            nativeEvent.Revision
        );
    }
}

/// <summary>Imperative handle for one retained native editor document.</summary>
public readonly struct EditorController
{
    private readonly NativeExtensionController _native;

    internal EditorController(NativeExtensionController native) => _native = native;

    public bool IsBound => _native.IsBound;

    /// <summary>
    /// Supplies the document once, independently from render snapshots. This command may be sent
    /// during <c>OnMounted</c>, before the matching editor declaration is materialized.
    /// </summary>
    public void Bootstrap(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _native.Dispatch(EditorSchema.Editor.CommandBootstrap, Encoding.UTF8.GetBytes(value));
    }

    /// <summary>UTF-8 bootstrap variant. Native code copies the bytes before returning.</summary>
    public void Bootstrap(ReadOnlySpan<byte> utf8Value) =>
        _native.Dispatch(EditorSchema.Editor.CommandBootstrap, utf8Value);

    /// <summary>Moves native keyboard focus to this editor.</summary>
    public void Focus() => _native.Dispatch(EditorSchema.Editor.CommandFocus);

    /// <summary>Sets the selection using UTF-8 byte offsets against a known document revision.</summary>
    public void SetSelection(ulong expectedRevision, ulong start, ulong end)
    {
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Selection end cannot precede start.");
        }
        Span<byte> payload = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, start);
        BinaryPrimitives.WriteUInt64LittleEndian(payload[8..], end);
        _native.Dispatch(
            EditorSchema.Editor.CommandSetSelection,
            payload,
            expectedRevision: expectedRevision
        );
    }

    /// <summary>Replaces the complete document if the native revision still matches.</summary>
    public void ReplaceDocument(ulong expectedRevision, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ReplaceDocument(expectedRevision, Encoding.UTF8.GetBytes(value));
    }

    /// <summary>UTF-8 document replacement variant.</summary>
    public void ReplaceDocument(ulong expectedRevision, ReadOnlySpan<byte> utf8Value) =>
        _native.Dispatch(
            EditorSchema.Editor.CommandReplaceDocument,
            utf8Value,
            expectedRevision: expectedRevision
        );

    /// <summary>Applies one contiguous UTF-8 replacement against a known document revision.</summary>
    public void ApplyEdit(ulong expectedRevision, in EditorEdit edit)
    {
        if (ulong.MaxValue - edit.Start < edit.DeletedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(edit), "The editor edit range overflows.");
        }
        var inserted = edit.InsertedUtf8.Span;
        var payload = new byte[checked(16 + inserted.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, edit.Start);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8), edit.DeletedLength);
        inserted.CopyTo(payload.AsSpan(16));
        _native.Dispatch(
            EditorSchema.Editor.CommandApplyEdit,
            payload,
            expectedRevision: expectedRevision
        );
    }

    internal NativeExtensionController Native => _native;
}

public static class EditorElements
{
    /// <summary>Creates a controller for an editor declared by the same View.</summary>
    public static EditorController CreateEditorController(this ViewContext context, string key) =>
        new(context.CreateNativeExtensionController(EditorExtension.Component, key));

    /// <summary>Creates a controller whose editor resource key is already UTF-8.</summary>
    public static EditorController CreateEditorController(
        this ViewContext context,
        ReadOnlySpan<byte> utf8Key
    ) => new(context.CreateNativeExtensionController(EditorExtension.Component, utf8Key));

    /// <summary>
    /// Declares the retained editor bound to <paramref name="controller"/>. Use this form when the
    /// document is supplied through <see cref="EditorController.Bootstrap(string)"/>.
    /// </summary>
    public static Element<NativeExtensionTag> Editor(
        this RenderContext ui,
        EditorController controller,
        EditorOptions? options = null
    ) => ui.NativeExtension(controller.Native, Configuration(options, 0, 0));

    /// <summary>Declares an editor and binds a typed callback for native document transactions.</summary>
    public static Element<NativeExtensionTag> Editor<TView>(
        this RenderContext ui,
        EditorController controller,
        TView view,
        Action<TView, EditorChangedEvent> onChanged,
        EditorOptions? options = null
    )
        where TView : ViewBase
    {
        var binding = ui.BindNativeExtensionEvent(view, onChanged);
        return ui.NativeExtension(controller.Native, Configuration(options, binding.Token, 0));
    }

    /// <summary>
    /// Declares an editor with document-change and state-dependent command-rejection callbacks.
    /// </summary>
    public static Element<NativeExtensionTag> Editor<TView>(
        this RenderContext ui,
        EditorController controller,
        TView view,
        Action<TView, EditorChangedEvent> onChanged,
        Action<TView, EditorCommandRejectedEvent> onCommandRejected,
        EditorOptions? options = null
    )
        where TView : ViewBase
    {
        var changed = ui.BindNativeExtensionEvent(view, onChanged);
        var rejected = ui.BindNativeExtensionEvent(view, onCommandRejected);
        return ui.NativeExtension(
            controller.Native,
            Configuration(options, changed.Token, rejected.Token)
        );
    }

    /// <summary>
    /// Declares a retained editor implemented by a host containing <see cref="EditorExtension"/>.
    /// Editing, selection, scrolling, highlighting, undo, and IME remain native.
    /// </summary>
    public static Element<NativeExtensionTag> Editor(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        EditorOptions? options = null
    )
    {
        return ui.NativeExtension(EditorExtension.Component, key, Configuration(options, 0, 0));
    }

    internal static byte[] Configuration(
        EditorOptions? options,
        ulong changedEventToken,
        ulong commandRejectedEventToken
    )
    {
        options ??= new EditorOptions();
        ArgumentNullException.ThrowIfNull(options.Language);
        if (options.Language.Contains('\0') || options.Language.Contains('\n'))
        {
            throw new ArgumentException(
                "An editor language identifier cannot contain NUL or newline characters.",
                nameof(options)
            );
        }
        uint flags = 0;
        flags |= options.Disabled ? EditorSchema.Editor.FlagDisabled : 0;
        flags |= options.ReadOnly ? EditorSchema.Editor.FlagReadOnly : 0;
        flags |= options.LineNumbers ? EditorSchema.Editor.FlagLineNumbers : 0;
        flags |= options.Folding ? EditorSchema.Editor.FlagFolding : 0;
        flags |= options.ShowWhitespace ? EditorSchema.Editor.FlagShowWhitespace : 0;
        var lineNumberWidth = options.LineNumberWidth?.Value ?? 0;
        if (!float.IsFinite(lineNumberWidth) || lineNumberWidth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The editor line-number width must be finite and non-negative."
            );
        }

        // Direct UTF-8 with no intermediate string: numbers format into
        // stack buffers, the language encodes in place, and the single
        // allocation is the returned configuration. The bytes match the
        // historical decimal/newline layout exactly, so the native parser
        // and the schema hash are untouched.
        Span<byte> flagsBytes = stackalloc byte[10];
        Span<byte> changedBytes = stackalloc byte[20];
        Span<byte> rejectedBytes = stackalloc byte[20];
        Span<byte> widthBytes = stackalloc byte[24];
        if (!Utf8Formatter.TryFormat(flags, flagsBytes, out var flagsLength)
            || !Utf8Formatter.TryFormat(
                changedEventToken,
                changedBytes,
                out var changedLength
            )
            || !Utf8Formatter.TryFormat(
                commandRejectedEventToken,
                rejectedBytes,
                out var rejectedLength
            )
            || !Utf8Formatter.TryFormat(
                lineNumberWidth,
                widthBytes,
                out var widthLength,
                new StandardFormat('R')
            ))
        {
            throw new InvalidOperationException("Failed to encode the editor configuration.");
        }
        var languageLength = Encoding.UTF8.GetByteCount(options.Language);
        var configuration = new byte[checked(
            flagsLength + 1 + languageLength + 1 + changedLength + 1 + rejectedLength + 1 + widthLength
        )];
        var destination = configuration.AsSpan();
        flagsBytes[..flagsLength].CopyTo(destination);
        var offset = flagsLength;
        destination[offset++] = (byte)'\n';
        offset += Encoding.UTF8.GetBytes(options.Language, destination[offset..]);
        destination[offset++] = (byte)'\n';
        changedBytes[..changedLength].CopyTo(destination[offset..]);
        offset += changedLength;
        destination[offset++] = (byte)'\n';
        rejectedBytes[..rejectedLength].CopyTo(destination[offset..]);
        offset += rejectedLength;
        destination[offset++] = (byte)'\n';
        widthBytes[..widthLength].CopyTo(destination[offset..]);
        offset += widthLength;
        Debug.Assert(offset == configuration.Length);
        return configuration;
    }
}
