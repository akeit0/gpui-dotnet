namespace Gpui.Components;

public enum ComponentMessageAlignment
{
    Start,
    End,
}

public enum ComponentBubbleVariant
{
    Filled,
    Secondary,
    Muted,
    Tinted,
    Outline,
    Ghost,
    Destructive,
}

public enum ComponentBubbleReactionSide
{
    Top,
    Bottom,
}

public sealed record ComponentBubbleOptions
{
    public ComponentBubbleVariant Variant { get; init; }
    public ComponentMessageAlignment? Alignment { get; init; }
    public ComponentBubbleReactionSide ReactionSide { get; init; } =
        ComponentBubbleReactionSide.Bottom;
    public ComponentMessageAlignment ReactionAlignment { get; init; } =
        ComponentMessageAlignment.End;
}

public sealed record ComponentMessageOptions
{
    public ComponentMessageAlignment Alignment { get; init; }
    public bool AccessibleListItem { get; init; }

    /// <summary>Removes the header/footer inset when the body uses a ghost bubble.</summary>
    public bool ContentHasGhostSurface { get; init; }
}

public enum ComponentMarkerVariant
{
    Plain,
    Separator,
    Border,
}

public enum ComponentMarkerAlignment
{
    Start,
    Center,
    End,
}

public enum ComponentMarkerLoadingStyle
{
    Spinner,
    Shimmer,
}

public sealed record ComponentMarkerOptions
{
    public ComponentMarkerVariant Variant { get; init; }
    public ComponentMarkerAlignment? Alignment { get; init; }
    public bool Loading { get; init; }
    public ComponentMarkerLoadingStyle LoadingStyle { get; init; }
    public bool StatusRole { get; init; }
    public string Text { get; init; } = string.Empty;
}

public static partial class ComponentElements
{
    /// <summary>Composes a themed bubble surface and an optional reaction region.</summary>
    public static Element<NativeExtensionTag> Bubble(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentBubbleOptions? options = null,
        Element? content = null,
        Element? reactions = null
    )
    {
        options ??= new();
        var alignment = options.Alignment switch
        {
            null => ComponentSchema.Bubble.Alignment.Inherit,
            ComponentMessageAlignment.Start => ComponentSchema.Bubble.Alignment.Start,
            ComponentMessageAlignment.End => ComponentSchema.Bubble.Alignment.End,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        return ui.NativeExtension(
            ComponentsExtension.Bubble,
            key,
            ComponentSchema.Bubble.EncodeConfiguration(
                (ComponentSchema.Bubble.Variant)(int)options.Variant,
                alignment,
                (ComponentSchema.Bubble.ReactionSide)(int)options.ReactionSide,
                (ComponentSchema.Bubble.ReactionAlignment)(int)options.ReactionAlignment,
                content.HasValue,
                reactions.HasValue
            ),
            ComponentSlots.Pack(content, reactions)
        );
    }

    public static Element<NativeExtensionTag> BubbleGroup(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        params ReadOnlySpan<Element> children
    ) =>
        ui.NativeExtension(
            ComponentsExtension.BubbleGroup,
            key,
            ComponentSchema.BubbleGroup.EncodeConfiguration(),
            children
        );

    /// <summary>Composes a message row from independent avatar, header, body, and footer slots.</summary>
    public static Element<NativeExtensionTag> Message(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentMessageOptions? options = null,
        Element? avatar = null,
        Element? header = null,
        Element? content = null,
        Element? footer = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Message,
            key,
            ComponentSchema.Message.EncodeConfiguration(
                (ComponentSchema.Message.Alignment)(int)options.Alignment,
                options.AccessibleListItem,
                options.ContentHasGhostSurface,
                avatar.HasValue,
                header.HasValue,
                content.HasValue,
                footer.HasValue
            ),
            ComponentSlots.Pack(avatar, header, content, footer)
        );
    }

    public static Element<NativeExtensionTag> MessageGroup(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        params ReadOnlySpan<Element> children
    ) =>
        ui.NativeExtension(
            ComponentsExtension.MessageGroup,
            key,
            ComponentSchema.MessageGroup.EncodeConfiguration(),
            children
        );

    /// <summary>Composes a conversation marker with an optional icon and extra content.</summary>
    public static Element<NativeExtensionTag> Marker(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentMarkerOptions? options = null,
        Element? icon = null,
        Element? extra = null
    )
    {
        options ??= new();
        var alignment = options.Alignment switch
        {
            null => ComponentSchema.Marker.Alignment.Inherit,
            ComponentMarkerAlignment.Start => ComponentSchema.Marker.Alignment.Start,
            ComponentMarkerAlignment.Center => ComponentSchema.Marker.Alignment.Center,
            ComponentMarkerAlignment.End => ComponentSchema.Marker.Alignment.End,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        return ui.NativeExtension(
            ComponentsExtension.Marker,
            key,
            ComponentSchema.Marker.EncodeConfiguration(
                (ComponentSchema.Marker.Variant)(int)options.Variant,
                alignment,
                options.Loading,
                (ComponentSchema.Marker.LoadingStyle)(int)options.LoadingStyle,
                options.StatusRole,
                options.Text,
                icon.HasValue,
                extra.HasValue
            ),
            ComponentSlots.Pack(icon, extra)
        );
    }
}
