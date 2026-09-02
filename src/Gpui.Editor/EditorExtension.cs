using System.Globalization;

namespace Gpui.Editor;

/// <summary>Schema identity shared by the managed editor package and its custom native host.</summary>
public static class EditorExtension
{
    public const ulong SchemaHash = 0x556347593588921FUL;

    public static NativeExtensionRequirement Requirement { get; } =
        new("gpui.net.editor", 1, SchemaHash);

    internal static NativeExtensionComponent Component { get; } =
        new(Requirement, "editor");
}

/// <summary>Initial and declarative presentation options for the native editor.</summary>
public sealed record EditorOptions
{
    /// <summary>Text consumed only when this retained editor key is first created.</summary>
    public string InitialValue { get; init; } = string.Empty;

    /// <summary>Optional highlighter language name understood by the native editor.</summary>
    public string Language { get; init; } = string.Empty;

    public bool Disabled { get; init; }
    public bool ReadOnly { get; init; }
    public bool LineNumbers { get; init; } = true;
    public bool Folding { get; init; } = true;
    public bool ShowWhitespace { get; init; }
}

public static class EditorElements
{
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
        options ??= new EditorOptions();
        ArgumentNullException.ThrowIfNull(options.InitialValue);
        ArgumentNullException.ThrowIfNull(options.Language);
        if (options.Language.Contains('\0') || options.Language.Contains('\n'))
        {
            throw new ArgumentException(
                "An editor language identifier cannot contain NUL or newline characters.",
                nameof(options)
            );
        }
        if (options.InitialValue.Contains('\0'))
        {
            throw new ArgumentException(
                "Initial editor text cannot contain NUL characters.",
                nameof(options)
            );
        }

        var flags = 0u;
        flags |= options.Disabled ? 1u << 0 : 0;
        flags |= options.ReadOnly ? 1u << 1 : 0;
        flags |= options.LineNumbers ? 1u << 2 : 0;
        flags |= options.Folding ? 1u << 3 : 0;
        flags |= options.ShowWhitespace ? 1u << 4 : 0;
        var configuration = string.Concat(
            flags.ToString(CultureInfo.InvariantCulture),
            "\n",
            options.Language,
            "\n",
            options.InitialValue
        );
        return ui.NativeExtension(EditorExtension.Component, key, configuration);
    }
}
