namespace Gpui.Components;

public enum ComponentEmptyMediaVariant
{
    Default,
    Icon,
}

/// <summary>Semantic text and media treatment for an application-owned empty state.</summary>
public sealed record ComponentEmptyOptions
{
    public ComponentEmptyMediaVariant MediaVariant { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public static partial class ComponentElements
{
    /// <summary>
    /// Declares a themed empty state. The optional content slot hosts application controls;
    /// the footer follows the named media, text, and content slots.
    /// </summary>
    public static Element<NativeExtensionTag> Empty(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentEmptyOptions? options = null,
        Element? media = null,
        Element? content = null,
        Element? footer = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Empty,
            key,
            ComponentSchema.Empty.EncodeConfiguration(
                (ComponentSchema.Empty.MediaVariant)(int)options.MediaVariant,
                options.Title,
                options.Description,
                media.HasValue,
                content.HasValue,
                footer.HasValue
            ),
            ComponentSlots.Pack(media, content, footer)
        );
    }
}
