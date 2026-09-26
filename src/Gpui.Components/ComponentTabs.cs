using System.Buffers.Binary;

namespace Gpui.Components;

public enum ComponentTabVariant
{
    Tab,
    Outline,
    Pill,
    Segmented,
    Underline,
}

/// <summary>One application-owned tab with a stable ID.</summary>
public readonly record struct ComponentTabItem(uint Id, string Label, bool Disabled = false);

/// <summary>Presentation for a controlled tab bar.</summary>
public sealed record ComponentTabsOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentTabVariant Variant { get; init; } = ComponentTabVariant.Tab;
    public uint? SelectedId { get; init; }
    public bool OverflowMenu { get; init; }
}

/// <summary>The stable ID of an activated tab. The application updates its selection.</summary>
public sealed class ComponentTabSelectedEvent : INativeExtensionEvent<ComponentTabSelectedEvent>
{
    private ComponentTabSelectedEvent(uint itemId) => ItemId = itemId;

    public uint ItemId { get; }

    public static ComponentTabSelectedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Tabs.EventSelected
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length != sizeof(uint)
        )
        {
            throw new InvalidOperationException("The tab selection event is invalid.");
        }
        return new ComponentTabSelectedEvent(
            BinaryPrimitives.ReadUInt32LittleEndian(nativeEvent.Payload.Span)
        );
    }
}

public static partial class ComponentElements
{
    /// <summary>Declares a tab bar with application-owned selection.</summary>
    public static Element<NativeExtensionTag> Tabs(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentTabsOptions? options = null,
        params ReadOnlySpan<ComponentTabItem> items
    ) => Tabs(ui, key, 0, options, items);

    /// <summary>Routes tab activation to the owning View by stable ID.</summary>
    public static Element<NativeExtensionTag> Tabs<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentTabSelectedEvent> onSelected,
        ComponentTabsOptions? options = null,
        params ReadOnlySpan<ComponentTabItem> items
    )
        where TView : ViewBase =>
        Tabs(ui, key, ui.BindNativeExtensionEvent(view, onSelected).Token, options, items);

    private static Element<NativeExtensionTag> Tabs(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentTabsOptions? options,
        ReadOnlySpan<ComponentTabItem> items
    )
    {
        options ??= new();
        var labels = new string[items.Length];
        var ids = new uint[items.Length];
        var disabled = new uint[items.Length];
        var seen = new HashSet<uint>();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Tab labels cannot be empty.", nameof(items));
            if (!seen.Add(item.Id))
                throw new ArgumentException("Tab IDs must be unique.", nameof(items));
            labels[index] = item.Label;
            ids[index] = item.Id;
            disabled[index] = item.Disabled ? 1u : 0u;
        }
        if (options.SelectedId is uint selectedId && !seen.Contains(selectedId))
            throw new ArgumentException(
                "The selected tab ID is absent from the items.",
                nameof(options)
            );

        return ui.NativeExtension(
            ComponentsExtension.Tabs,
            key,
            ComponentSchema.Tabs.EncodeConfiguration(
                (ComponentSchema.Tabs.Size)(int)options.Size,
                (ComponentSchema.Tabs.Variant)(int)options.Variant,
                labels,
                ids,
                disabled,
                options.SelectedId.GetValueOrDefault(),
                options.SelectedId.HasValue,
                options.OverflowMenu,
                token
            )
        );
    }
}
