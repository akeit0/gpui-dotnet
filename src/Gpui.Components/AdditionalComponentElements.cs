using System.Buffers.Binary;

namespace Gpui.Components;

public enum ComponentToggleVariant
{
    Ghost,
    Outline,
}

public sealed record ComponentLabelOptions
{
    public string Text { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public bool Masked { get; init; }
    public string Highlight { get; init; } = string.Empty;
    public bool HighlightPrefix { get; init; }
}

public sealed record ComponentKbdOptions
{
    public string Keystroke { get; init; } = string.Empty;
    public bool Appearance { get; init; } = true;
    public bool Outline { get; init; }
}

public sealed record ComponentLinkOptions
{
    public string Href { get; init; } = string.Empty;
}

public sealed record ComponentAvatarOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public string Name { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

/// <summary>An SVG asset provided by the selected native component host.</summary>
public sealed record ComponentIconOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public string AssetPath { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
}

public sealed record ComponentShimmerTextOptions
{
    public string Text { get; init; } = string.Empty;
    public uint DurationMilliseconds { get; init; } = 2000;
    public bool Reverse { get; init; }
    public bool Once { get; init; }
    public string Color { get; init; } = string.Empty;
}

public sealed record ComponentSwitchOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public bool Checked { get; init; }
    public bool Disabled { get; init; }
    public string Label { get; init; } = string.Empty;
    public string AccessibilityLabel { get; init; } = string.Empty;
    public string Tooltip { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
}

public sealed record ComponentCheckboxOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public bool Checked { get; init; }
    public bool Disabled { get; init; }
    public string Label { get; init; } = string.Empty;
    public string AccessibilityLabel { get; init; } = string.Empty;
    public string Tooltip { get; init; } = string.Empty;
}

public sealed record ComponentRadioOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public bool Checked { get; init; }
    public bool Disabled { get; init; }
    public string Label { get; init; } = string.Empty;
    public string AccessibilityLabel { get; init; } = string.Empty;
    public string Tooltip { get; init; } = string.Empty;
}

public sealed record ComponentToggleOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentToggleVariant Variant { get; init; }
    public bool Checked { get; init; }
    public bool Disabled { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Tooltip { get; init; } = string.Empty;
}

public sealed record ComponentPaginationOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public uint CurrentPage { get; init; } = 1;
    public uint TotalPages { get; init; } = 1;
    public uint VisiblePages { get; init; } = 5;
    public bool Disabled { get; init; }
    public bool Compact { get; init; }
}

public sealed record ComponentCollapsibleOptions
{
    public bool Open { get; init; }
    public bool Animated { get; init; } = true;
}

/// <summary>A Link activation emitted by the native component.</summary>
public sealed class ComponentLinkClickedEvent : INativeExtensionEvent<ComponentLinkClickedEvent>
{
    private ComponentLinkClickedEvent() { }

    public static ComponentLinkClickedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ComponentEvents.ValidateEmpty(nativeEvent, ComponentSchema.Link.EventClicked);
        return new ComponentLinkClickedEvent();
    }
}

/// <summary>A requested checked state emitted by the native Switch component.</summary>
public sealed class ComponentSwitchChangedEvent : INativeExtensionEvent<ComponentSwitchChangedEvent>
{
    private ComponentSwitchChangedEvent(bool value) => Value = value;

    public bool Value { get; }

    public static ComponentSwitchChangedEvent Decode(NativeExtensionEvent nativeEvent) =>
        new(ComponentEvents.DecodeBoolean(nativeEvent, ComponentSchema.Switch.EventChanged));
}

/// <summary>A requested checked state emitted by the native Checkbox component.</summary>
public sealed class ComponentCheckboxChangedEvent
    : INativeExtensionEvent<ComponentCheckboxChangedEvent>
{
    private ComponentCheckboxChangedEvent(bool value) => Value = value;

    public bool Value { get; }

    public static ComponentCheckboxChangedEvent Decode(NativeExtensionEvent nativeEvent) =>
        new(ComponentEvents.DecodeBoolean(nativeEvent, ComponentSchema.Checkbox.EventChanged));
}

/// <summary>A requested checked state emitted by the native Radio component.</summary>
public sealed class ComponentRadioChangedEvent : INativeExtensionEvent<ComponentRadioChangedEvent>
{
    private ComponentRadioChangedEvent(bool value) => Value = value;

    public bool Value { get; }

    public static ComponentRadioChangedEvent Decode(NativeExtensionEvent nativeEvent) =>
        new(ComponentEvents.DecodeBoolean(nativeEvent, ComponentSchema.Radio.EventChanged));
}

/// <summary>A requested checked state emitted by the native Toggle component.</summary>
public sealed class ComponentToggleChangedEvent : INativeExtensionEvent<ComponentToggleChangedEvent>
{
    private ComponentToggleChangedEvent(bool value) => Value = value;

    public bool Value { get; }

    public static ComponentToggleChangedEvent Decode(NativeExtensionEvent nativeEvent) =>
        new(ComponentEvents.DecodeBoolean(nativeEvent, ComponentSchema.Toggle.EventChanged));
}

/// <summary>A requested page emitted by the native Pagination component.</summary>
public sealed class ComponentPageChangedEvent : INativeExtensionEvent<ComponentPageChangedEvent>
{
    private ComponentPageChangedEvent(uint page) => Page = page;

    public uint Page { get; }

    public static ComponentPageChangedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Pagination.EventChanged
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length != 4
        )
        {
            throw new InvalidOperationException("The pagination event is invalid.");
        }
        return new ComponentPageChangedEvent(
            BinaryPrimitives.ReadUInt32LittleEndian(nativeEvent.Payload.Span)
        );
    }
}

public static partial class ComponentElements
{
    public static Element<NativeExtensionTag> Label(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentLabelOptions? options = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Label,
            key,
            ComponentSchema.Label.EncodeConfiguration(
                options.Text,
                options.Secondary,
                options.Masked,
                options.Highlight,
                options.HighlightPrefix
            )
        );
    }

    public static Element<NativeExtensionTag> Kbd(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentKbdOptions options
    ) =>
        ui.NativeExtension(
            ComponentsExtension.Kbd,
            key,
            ComponentSchema.Kbd.EncodeConfiguration(
                options.Keystroke,
                options.Appearance,
                options.Outline
            )
        );

    public static Element<NativeExtensionTag> Link(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentLinkOptions? options = null,
        params ReadOnlySpan<Element> children
    ) => Link(ui, key, 0, options, children);

    public static Element<NativeExtensionTag> Link<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentLinkClickedEvent> onClick,
        ComponentLinkOptions? options = null,
        params ReadOnlySpan<Element> children
    )
        where TView : ViewBase =>
        Link(ui, key, ui.BindNativeExtensionEvent(view, onClick).Token, options, children);

    public static Element<NativeExtensionTag> Avatar(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentAvatarOptions? options = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Avatar,
            key,
            ComponentSchema.Avatar.EncodeConfiguration(
                (ComponentSchema.Avatar.Size)(int)options.Size,
                options.Name,
                options.Source
            )
        );
    }

    public static Element<NativeExtensionTag> Icon(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentIconOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.AssetPath);
        return ui.NativeExtension(
            ComponentsExtension.Icon,
            key,
            ComponentSchema.Icon.EncodeConfiguration(
                (ComponentSchema.Icon.Size)(int)options.Size,
                options.AssetPath,
                options.Color
            )
        );
    }

    public static Element<NativeExtensionTag> ShimmerText(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentShimmerTextOptions? options = null
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.ShimmerText,
            key,
            ComponentSchema.ShimmerText.EncodeConfiguration(
                options.Text,
                options.DurationMilliseconds,
                options.Reverse,
                options.Once,
                options.Color
            )
        );
    }

    public static Element<NativeExtensionTag> Switch(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentSwitchOptions? options = null
    ) => Switch(ui, key, 0, options);

    public static Element<NativeExtensionTag> Switch<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentSwitchChangedEvent> onChanged,
        ComponentSwitchOptions? options = null
    )
        where TView : ViewBase =>
        Switch(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options);

    public static Element<NativeExtensionTag> Checkbox(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentCheckboxOptions? options = null,
        params ReadOnlySpan<Element> children
    ) => Checkbox(ui, key, 0, options, children);

    public static Element<NativeExtensionTag> Checkbox<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentCheckboxChangedEvent> onChanged,
        ComponentCheckboxOptions? options = null,
        params ReadOnlySpan<Element> children
    )
        where TView : ViewBase =>
        Checkbox(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options, children);

    public static Element<NativeExtensionTag> Radio(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentRadioOptions? options = null,
        params ReadOnlySpan<Element> children
    ) => Radio(ui, key, 0, options, children);

    public static Element<NativeExtensionTag> Radio<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentRadioChangedEvent> onChanged,
        ComponentRadioOptions? options = null,
        params ReadOnlySpan<Element> children
    )
        where TView : ViewBase =>
        Radio(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options, children);

    public static Element<NativeExtensionTag> Toggle(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentToggleOptions? options = null,
        params ReadOnlySpan<Element> children
    ) => Toggle(ui, key, 0, options, children);

    public static Element<NativeExtensionTag> Toggle<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentToggleChangedEvent> onChanged,
        ComponentToggleOptions? options = null,
        params ReadOnlySpan<Element> children
    )
        where TView : ViewBase =>
        Toggle(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options, children);

    public static Element<NativeExtensionTag> Pagination(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentPaginationOptions? options = null
    ) => Pagination(ui, key, 0, options);

    public static Element<NativeExtensionTag> Pagination<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentPageChangedEvent> onChanged,
        ComponentPaginationOptions? options = null
    )
        where TView : ViewBase =>
        Pagination(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options);

    public static Element<NativeExtensionTag> Collapsible(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentCollapsibleOptions? options = null,
        params ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Collapsible,
            key,
            ComponentSchema.Collapsible.EncodeConfiguration(options.Open, options.Animated),
            children
        );
    }

    private static Element<NativeExtensionTag> Link(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentLinkOptions? options,
        ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Link,
            key,
            ComponentSchema.Link.EncodeConfiguration(options.Href, token),
            children
        );
    }

    private static Element<NativeExtensionTag> Switch(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentSwitchOptions? options
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Switch,
            key,
            ComponentSchema.Switch.EncodeConfiguration(
                (ComponentSchema.Switch.Size)(int)options.Size,
                options.Checked,
                options.Disabled,
                options.Label,
                options.AccessibilityLabel,
                options.Tooltip,
                options.Color,
                token
            )
        );
    }

    private static Element<NativeExtensionTag> Checkbox(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentCheckboxOptions? options,
        ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Checkbox,
            key,
            ComponentSchema.Checkbox.EncodeConfiguration(
                (ComponentSchema.Checkbox.Size)(int)options.Size,
                options.Checked,
                options.Disabled,
                options.Label,
                options.AccessibilityLabel,
                options.Tooltip,
                token
            ),
            children
        );
    }

    private static Element<NativeExtensionTag> Radio(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentRadioOptions? options,
        ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Radio,
            key,
            ComponentSchema.Radio.EncodeConfiguration(
                (ComponentSchema.Radio.Size)(int)options.Size,
                options.Checked,
                options.Disabled,
                options.Label,
                options.AccessibilityLabel,
                options.Tooltip,
                token
            ),
            children
        );
    }

    private static Element<NativeExtensionTag> Toggle(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentToggleOptions? options,
        ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Toggle,
            key,
            ComponentSchema.Toggle.EncodeConfiguration(
                (ComponentSchema.Toggle.Size)(int)options.Size,
                options.Variant == ComponentToggleVariant.Outline
                    ? ComponentSchema.Toggle.Variant.Outline
                    : ComponentSchema.Toggle.Variant.Ghost,
                options.Checked,
                options.Disabled,
                options.Label,
                options.Tooltip,
                token
            ),
            children
        );
    }

    private static Element<NativeExtensionTag> Pagination(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentPaginationOptions? options
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Pagination,
            key,
            ComponentSchema.Pagination.EncodeConfiguration(
                (ComponentSchema.Pagination.Size)(int)options.Size,
                options.CurrentPage,
                options.TotalPages,
                options.VisiblePages,
                options.Disabled,
                options.Compact,
                token
            )
        );
    }
}
