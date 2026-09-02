using System.Buffers.Binary;
using System.Globalization;
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
    public bool Folding { get; init; } = true;
    public bool ShowWhitespace { get; init; }
}

/// <summary>Identifies why the native document changed.</summary>
public enum EditorChangeOrigin : ushort
{
    /// <summary>The user changed the document through the native editor.</summary>
    User = 0,
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
        if (nativeEvent.Flags != (ushort)EditorChangeOrigin.User)
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
    ) => ui.NativeExtension(controller.Native, Configuration(options, 0));

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
        return ui.NativeExtension(controller.Native, Configuration(options, binding.Token));
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
        return ui.NativeExtension(EditorExtension.Component, key, Configuration(options, 0));
    }

    private static string Configuration(EditorOptions? options, ulong changedEventToken)
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
        var flags = 0u;
        flags |= options.Disabled ? EditorSchema.Editor.FlagDisabled : 0;
        flags |= options.ReadOnly ? EditorSchema.Editor.FlagReadOnly : 0;
        flags |= options.LineNumbers ? EditorSchema.Editor.FlagLineNumbers : 0;
        flags |= options.Folding ? EditorSchema.Editor.FlagFolding : 0;
        flags |= options.ShowWhitespace ? EditorSchema.Editor.FlagShowWhitespace : 0;
        return string.Concat(
            flags.ToString(CultureInfo.InvariantCulture),
            "\n",
            options.Language,
            "\n",
            changedEventToken.ToString(CultureInfo.InvariantCulture)
        );
    }
}
