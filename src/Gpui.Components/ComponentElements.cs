using System.Buffers.Binary;

namespace Gpui.Components;

/// <summary>Schema identity for the optional gpui-component catalog host.</summary>
public static class ComponentsExtension
{
    public const ulong SchemaHash = ComponentSchema.SchemaHash;

    public static NativeExtensionRequirement Requirement { get; } =
        new(ComponentSchema.ExtensionId, ComponentSchema.SchemaVersion, SchemaHash);

    internal static NativeExtensionComponent Spinner { get; } =
        Component(ComponentSchema.Spinner.Kind);
    internal static NativeExtensionComponent Skeleton { get; } =
        Component(ComponentSchema.Skeleton.Kind);
    internal static NativeExtensionComponent Separator { get; } =
        Component(ComponentSchema.Separator.Kind);
    internal static NativeExtensionComponent Badge { get; } = Component(ComponentSchema.Badge.Kind);
    internal static NativeExtensionComponent Tag { get; } = Component(ComponentSchema.Tag.Kind);
    internal static NativeExtensionComponent Progress { get; } =
        Component(ComponentSchema.Progress.Kind);
    internal static NativeExtensionComponent ProgressCircle { get; } =
        Component(ComponentSchema.ProgressCircle.Kind);
    internal static NativeExtensionComponent Rating { get; } =
        Component(ComponentSchema.Rating.Kind);
    internal static NativeExtensionComponent Button { get; } =
        Component(ComponentSchema.Button.Kind);
    internal static NativeExtensionComponent Alert { get; } = Component(ComponentSchema.Alert.Kind);
    internal static NativeExtensionComponent GroupBox { get; } =
        Component(ComponentSchema.GroupBox.Kind);
    internal static NativeExtensionComponent Label { get; } = Component(ComponentSchema.Label.Kind);
    internal static NativeExtensionComponent Kbd { get; } = Component(ComponentSchema.Kbd.Kind);
    internal static NativeExtensionComponent Link { get; } = Component(ComponentSchema.Link.Kind);
    internal static NativeExtensionComponent Avatar { get; } =
        Component(ComponentSchema.Avatar.Kind);
    internal static NativeExtensionComponent ShimmerText { get; } =
        Component(ComponentSchema.ShimmerText.Kind);
    internal static NativeExtensionComponent Switch { get; } =
        Component(ComponentSchema.Switch.Kind);
    internal static NativeExtensionComponent Checkbox { get; } =
        Component(ComponentSchema.Checkbox.Kind);
    internal static NativeExtensionComponent Radio { get; } = Component(ComponentSchema.Radio.Kind);
    internal static NativeExtensionComponent Toggle { get; } =
        Component(ComponentSchema.Toggle.Kind);
    internal static NativeExtensionComponent Pagination { get; } =
        Component(ComponentSchema.Pagination.Kind);
    internal static NativeExtensionComponent Collapsible { get; } =
        Component(ComponentSchema.Collapsible.Kind);
    internal static NativeExtensionComponent Attachment { get; } =
        Component(ComponentSchema.Attachment.Kind);
    internal static NativeExtensionComponent Empty { get; } = Component(ComponentSchema.Empty.Kind);
    internal static NativeExtensionComponent Toolbar { get; } =
        Component(ComponentSchema.Toolbar.Kind);
    internal static NativeExtensionComponent ToolbarGroup { get; } =
        Component(ComponentSchema.ToolbarGroup.Kind);

    private static NativeExtensionComponent Component(string kind) => new(Requirement, kind);
}

public enum ComponentSize
{
    XSmall,
    Small,
    Medium,
    Large,
}

public enum ComponentOrientation
{
    Horizontal,
    Vertical,
}

public enum ComponentTagVariant
{
    Primary,
    Secondary,
    Danger,
    Success,
    Warning,
    Info,
}

public enum ComponentButtonVariant
{
    Default,
    Primary,
    Secondary,
    Danger,
    Success,
    Warning,
    Info,
    Ghost,
    Link,
    Text,
}

public enum ComponentAlertVariant
{
    Default,
    Info,
    Success,
    Warning,
    Error,
}

public enum ComponentGroupBoxVariant
{
    Normal,
    Fill,
    Outline,
}

public sealed record ComponentSpinnerOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public bool Circular { get; init; }
    public bool EaseInOut { get; init; }
    public string Color { get; init; } = string.Empty;
}

public sealed record ComponentSkeletonOptions
{
    public bool Secondary { get; init; }
}

public sealed record ComponentSeparatorOptions
{
    public ComponentOrientation Orientation { get; init; }
    public bool Dashed { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
}

public sealed record ComponentBadgeOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public bool Dot { get; init; }
    public uint Count { get; init; }
    public uint Max { get; init; } = 99;
    public string Color { get; init; } = string.Empty;
}

public sealed record ComponentTagOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentTagVariant Variant { get; init; } = ComponentTagVariant.Secondary;
    public bool Outline { get; init; }
    public bool RoundedFull { get; init; }
}

public sealed record ComponentProgressOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public float Value { get; init; }
    public bool Loading { get; init; }
    public string Color { get; init; } = string.Empty;
    public string AccessibilityLabel { get; init; } = string.Empty;
}

public sealed record ComponentProgressCircleOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;

    /// <summary>
    /// Optional fixed diameter. Zero keeps the selected semantic size.
    /// </summary>
    public float Diameter { get; init; }
    public float Value { get; init; }
    public bool Loading { get; init; }
    public string Color { get; init; } = string.Empty;
    public string AccessibilityLabel { get; init; } = string.Empty;
}

public sealed record ComponentRatingOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public uint Value { get; init; }
    public uint Max { get; init; } = 5;
    public bool Disabled { get; init; }
    public string Color { get; init; } = string.Empty;
}

public sealed record ComponentButtonOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentButtonVariant Variant { get; init; }
    public string Label { get; init; } = string.Empty;
    public string AccessibilityLabel { get; init; } = string.Empty;
    public bool Disabled { get; init; }
    public bool Selected { get; init; }
    public bool Loading { get; init; }
    public bool Outline { get; init; }
    public bool Compact { get; init; }
}

public sealed record ComponentAlertOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentAlertVariant Variant { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool Banner { get; init; }
}

public sealed record ComponentGroupBoxOptions
{
    public ComponentGroupBoxVariant Variant { get; init; }
    public string Title { get; init; } = string.Empty;
}

/// <summary>A button activation emitted by the native component.</summary>
public sealed class ComponentClickedEvent : INativeExtensionEvent<ComponentClickedEvent>
{
    private ComponentClickedEvent() { }

    public static ComponentClickedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ComponentEvents.ValidateEmpty(nativeEvent, ComponentSchema.Button.EventClicked);
        return new ComponentClickedEvent();
    }
}

/// <summary>An alert close activation emitted by the native component.</summary>
public sealed class ComponentAlertClosedEvent : INativeExtensionEvent<ComponentAlertClosedEvent>
{
    private ComponentAlertClosedEvent() { }

    public static ComponentAlertClosedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ComponentEvents.ValidateEmpty(nativeEvent, ComponentSchema.Alert.EventClosed);
        return new ComponentAlertClosedEvent();
    }
}

/// <summary>A rating selected by the user.</summary>
public sealed class ComponentRatingChangedEvent : INativeExtensionEvent<ComponentRatingChangedEvent>
{
    private ComponentRatingChangedEvent(uint value) => Value = value;

    public uint Value { get; }

    public static ComponentRatingChangedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        if (
            nativeEvent.Kind != ComponentSchema.Rating.EventChanged
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length != 4
        )
        {
            throw new InvalidOperationException("The rating event is invalid.");
        }
        return new ComponentRatingChangedEvent(
            BinaryPrimitives.ReadUInt32LittleEndian(nativeEvent.Payload.Span)
        );
    }
}

internal static class ComponentEvents
{
    internal static void ValidateEmpty(NativeExtensionEvent nativeEvent, ushort kind)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != kind
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || !nativeEvent.Payload.IsEmpty
        )
        {
            throw new InvalidOperationException("The component event is invalid.");
        }
    }

    internal static bool DecodeBoolean(NativeExtensionEvent nativeEvent, ushort kind)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != kind
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length != 1
            || nativeEvent.Payload.Span[0] > 1
        )
        {
            throw new InvalidOperationException("The checked-state event is invalid.");
        }
        return nativeEvent.Payload.Span[0] != 0;
    }
}

public static partial class ComponentElements
{
    public static Element<NativeExtensionTag> Spinner(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentSpinnerOptions? options = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Spinner,
            key,
            ComponentSchema.Spinner.EncodeConfiguration(
                Size(options.Size),
                options.Circular
                    ? ComponentSchema.Spinner.Icon.LoaderCircle
                    : ComponentSchema.Spinner.Icon.Loader,
                options.EaseInOut
                    ? ComponentSchema.Spinner.Ease.EaseInOut
                    : ComponentSchema.Spinner.Ease.Linear,
                options.Color
            )
        );
    }

    public static Element<NativeExtensionTag> Skeleton(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentSkeletonOptions? options = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Skeleton,
            key,
            ComponentSchema.Skeleton.EncodeConfiguration(options.Secondary)
        );
    }

    public static Element<NativeExtensionTag> Separator(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentSeparatorOptions? options = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Separator,
            key,
            ComponentSchema.Separator.EncodeConfiguration(
                options.Orientation == ComponentOrientation.Vertical
                    ? ComponentSchema.Separator.Orientation.Vertical
                    : ComponentSchema.Separator.Orientation.Horizontal,
                options.Dashed,
                options.Label,
                options.Color
            )
        );
    }

    public static Element<NativeExtensionTag> Badge(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentBadgeOptions? options = null,
        params ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Badge,
            key,
            ComponentSchema.Badge.EncodeConfiguration(
                SizeBadge(options.Size),
                options.Dot,
                options.Count,
                options.Max,
                options.Color
            ),
            children
        );
    }

    public static Element<NativeExtensionTag> Tag(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentTagOptions? options = null,
        params ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Tag,
            key,
            ComponentSchema.Tag.EncodeConfiguration(
                SizeTag(options.Size),
                TagVariant(options.Variant),
                options.Outline,
                options.RoundedFull
            ),
            children
        );
    }

    public static Element<NativeExtensionTag> Progress(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentProgressOptions? options = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Progress,
            key,
            ComponentSchema.Progress.EncodeConfiguration(
                SizeProgress(options.Size),
                options.Value,
                options.Loading,
                options.Color,
                options.AccessibilityLabel
            )
        );
    }

    public static Element<NativeExtensionTag> ProgressCircle(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentProgressCircleOptions? options = null,
        params ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        ArgumentOutOfRangeException.ThrowIfNegative(options.Diameter);
        return ui.NativeExtension(
            ComponentsExtension.ProgressCircle,
            key,
            ComponentSchema.ProgressCircle.EncodeConfiguration(
                SizeCircle(options.Size),
                options.Diameter,
                options.Value,
                options.Loading,
                options.Color,
                options.AccessibilityLabel
            ),
            children
        );
    }

    public static Element<NativeExtensionTag> Rating(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentRatingOptions? options = null
    ) => Rating(ui, key, 0, options);

    public static Element<NativeExtensionTag> Rating<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentRatingChangedEvent> onChanged,
        ComponentRatingOptions? options = null
    )
        where TView : ViewBase =>
        Rating(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options);

    public static Element<NativeExtensionTag> Button(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentButtonOptions? options = null,
        params ReadOnlySpan<Element> children
    ) => Button(ui, key, 0, options, children);

    public static Element<NativeExtensionTag> Button<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentClickedEvent> onClick,
        ComponentButtonOptions? options = null,
        params ReadOnlySpan<Element> children
    )
        where TView : ViewBase =>
        Button(ui, key, ui.BindNativeExtensionEvent(view, onClick).Token, options, children);

    public static Element<NativeExtensionTag> Alert(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentAlertOptions? options = null
    ) => Alert(ui, key, 0, options);

    public static Element<NativeExtensionTag> Alert<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentAlertClosedEvent> onClosed,
        ComponentAlertOptions? options = null
    )
        where TView : ViewBase =>
        Alert(ui, key, ui.BindNativeExtensionEvent(view, onClosed).Token, options);

    public static Element<NativeExtensionTag> GroupBox(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentGroupBoxOptions? options = null,
        params ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.GroupBox,
            key,
            ComponentSchema.GroupBox.EncodeConfiguration(
                options.Variant switch
                {
                    ComponentGroupBoxVariant.Fill => ComponentSchema.GroupBox.Variant.Fill,
                    ComponentGroupBoxVariant.Outline => ComponentSchema.GroupBox.Variant.Outline,
                    _ => ComponentSchema.GroupBox.Variant.Normal,
                },
                options.Title
            ),
            children
        );
    }

    private static Element<NativeExtensionTag> Rating(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentRatingOptions? options
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Rating,
            key,
            ComponentSchema.Rating.EncodeConfiguration(
                SizeRating(options.Size),
                options.Value,
                options.Max,
                options.Disabled,
                options.Color,
                token
            )
        );
    }

    private static Element<NativeExtensionTag> Button(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentButtonOptions? options,
        ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Button,
            key,
            ComponentSchema.Button.EncodeConfiguration(
                SizeButton(options.Size),
                ButtonVariant(options.Variant),
                options.Label,
                options.AccessibilityLabel,
                options.Disabled,
                options.Selected,
                options.Loading,
                options.Outline,
                options.Compact,
                token
            ),
            children
        );
    }

    private static Element<NativeExtensionTag> Alert(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentAlertOptions? options
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Alert,
            key,
            ComponentSchema.Alert.EncodeConfiguration(
                SizeAlert(options.Size),
                AlertVariant(options.Variant),
                options.Title,
                options.Message,
                options.Banner,
                token
            )
        );
    }

    private static ComponentSchema.Spinner.Size Size(ComponentSize value) =>
        (ComponentSchema.Spinner.Size)(int)value;

    private static ComponentSchema.Badge.Size SizeBadge(ComponentSize value) =>
        (ComponentSchema.Badge.Size)(int)value;

    private static ComponentSchema.Tag.Size SizeTag(ComponentSize value) =>
        (ComponentSchema.Tag.Size)(int)value;

    private static ComponentSchema.Progress.Size SizeProgress(ComponentSize value) =>
        (ComponentSchema.Progress.Size)(int)value;

    private static ComponentSchema.ProgressCircle.Size SizeCircle(ComponentSize value) =>
        (ComponentSchema.ProgressCircle.Size)(int)value;

    private static ComponentSchema.Rating.Size SizeRating(ComponentSize value) =>
        (ComponentSchema.Rating.Size)(int)value;

    private static ComponentSchema.Button.Size SizeButton(ComponentSize value) =>
        (ComponentSchema.Button.Size)(int)value;

    private static ComponentSchema.Alert.Size SizeAlert(ComponentSize value) =>
        (ComponentSchema.Alert.Size)(int)value;

    private static ComponentSchema.Tag.Variant TagVariant(ComponentTagVariant value) =>
        (ComponentSchema.Tag.Variant)(int)value;

    private static ComponentSchema.Button.Variant ButtonVariant(ComponentButtonVariant value) =>
        (ComponentSchema.Button.Variant)(int)value;

    private static ComponentSchema.Alert.Variant AlertVariant(ComponentAlertVariant value) =>
        (ComponentSchema.Alert.Variant)(int)value;
}
