namespace Gpui.Components;

/// <summary>The application-owned lifecycle state of a file or media attachment.</summary>
public enum ComponentAttachmentStatus
{
    Pending,
    Uploading,
    Processing,
    Failed,
    Complete,
}

/// <summary>Presentation for one file or media attachment. The application owns its file model.</summary>
public sealed record ComponentAttachmentOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentOrientation Orientation { get; init; }
    public ComponentAttachmentStatus Status { get; init; } = ComponentAttachmentStatus.Complete;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string PreviewSource { get; init; } = string.Empty;
}

/// <summary>A request to open an attachment, emitted by its native card surface.</summary>
public sealed class ComponentAttachmentClickedEvent
    : INativeExtensionEvent<ComponentAttachmentClickedEvent>
{
    private ComponentAttachmentClickedEvent() { }

    public static ComponentAttachmentClickedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ComponentEvents.ValidateEmpty(nativeEvent, ComponentSchema.Attachment.EventClicked);
        return new ComponentAttachmentClickedEvent();
    }
}

public static partial class ComponentElements
{
    /// <summary>
    /// Declares one attachment. Media, extra content, and actions are independent optional slots;
    /// controls placed in actions retain their own events and do not activate the card.
    /// </summary>
    public static Element<NativeExtensionTag> Attachment(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentAttachmentOptions? options = null,
        Element? media = null,
        Element? content = null,
        Element? actions = null
    ) => Attachment(ui, key, 0, options, media, content, actions);

    public static Element<NativeExtensionTag> Attachment<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentAttachmentClickedEvent> onClick,
        ComponentAttachmentOptions? options = null,
        Element? media = null,
        Element? content = null,
        Element? actions = null
    )
        where TView : ViewBase =>
        Attachment(
            ui,
            key,
            ui.BindNativeExtensionEvent(view, onClick).Token,
            options,
            media,
            content,
            actions
        );

    private static Element<NativeExtensionTag> Attachment(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentAttachmentOptions? options,
        Element? media,
        Element? content,
        Element? actions
    )
    {
        options ??= new();
        var count =
            (media.HasValue ? 1 : 0) + (content.HasValue ? 1 : 0) + (actions.HasValue ? 1 : 0);
        var children = new Element[count];
        var index = 0;
        if (media.HasValue)
            children[index++] = media.Value;
        if (content.HasValue)
            children[index++] = content.Value;
        if (actions.HasValue)
            children[index] = actions.Value;

        return ui.NativeExtension(
            ComponentsExtension.Attachment,
            key,
            ComponentSchema.Attachment.EncodeConfiguration(
                (ComponentSchema.Attachment.Size)(int)options.Size,
                (ComponentSchema.Attachment.Axis)(int)options.Orientation,
                (ComponentSchema.Attachment.Status)(int)options.Status,
                options.Title,
                options.Description,
                options.PreviewSource,
                media.HasValue,
                content.HasValue,
                actions.HasValue,
                token
            ),
            children
        );
    }
}
