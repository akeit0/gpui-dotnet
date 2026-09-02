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
    ) => ui.NativeExtension(controller.Native, Configuration(options));

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
        return ui.NativeExtension(EditorExtension.Component, key, Configuration(options));
    }

    private static string Configuration(EditorOptions? options)
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
        return string.Concat(flags.ToString(CultureInfo.InvariantCulture), "\n", options.Language);
    }
}
