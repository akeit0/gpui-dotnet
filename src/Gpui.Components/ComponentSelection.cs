using System.Buffers.Binary;

namespace Gpui.Components;

/// <summary>An application-owned choice with a stable, nonzero ID.</summary>
public readonly record struct ComponentSelectionItem(uint Id, string Label, bool Disabled = false);

public record ComponentSelectionOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public string Placeholder { get; init; } = string.Empty;
    public string SearchPlaceholder { get; init; } = string.Empty;
    public bool Cleanable { get; init; }
    public bool Disabled { get; init; }
}

public sealed record ComponentSelectOptions : ComponentSelectionOptions
{
    public uint? SelectedId { get; init; }

    /// <summary>Fixed for the lifetime of one retained key.</summary>
    public bool Searchable { get; init; }
    public string AccessibilityLabel { get; init; } = string.Empty;
}

public sealed record ComponentComboboxOptions : ComponentSelectionOptions
{
    public IReadOnlyList<uint> SelectedIds { get; init; } = Array.Empty<uint>();

    /// <summary>Fixed for the lifetime of one retained key.</summary>
    public bool Multiple { get; init; }
}

/// <summary>A Select request. Null requests clearing the current selection.</summary>
public sealed class ComponentSelectSelectedEvent
    : INativeExtensionEvent<ComponentSelectSelectedEvent>
{
    private ComponentSelectSelectedEvent(uint? itemId) => ItemId = itemId;

    public uint? ItemId { get; }

    public static ComponentSelectSelectedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Select.EventSelected
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || (nativeEvent.Payload.Length != 0 && nativeEvent.Payload.Length != sizeof(uint))
        )
            throw new InvalidOperationException("The Select selection event is invalid.");
        if (nativeEvent.Payload.IsEmpty)
            return new(null);
        var id = BinaryPrimitives.ReadUInt32LittleEndian(nativeEvent.Payload.Span);
        if (id == 0)
            throw new InvalidOperationException("The Select item ID is invalid.");
        return new(id);
    }
}

/// <summary>The full selected-ID set requested by a Combobox interaction.</summary>
public sealed class ComponentComboboxChangedEvent
    : INativeExtensionEvent<ComponentComboboxChangedEvent>
{
    private ComponentComboboxChangedEvent(uint[] itemIds) => ItemIds = itemIds;

    public IReadOnlyList<uint> ItemIds { get; }

    public static ComponentComboboxChangedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Combobox.EventChanged
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length % sizeof(uint) != 0
            || nativeEvent.Payload.Length > 4096 * sizeof(uint)
        )
            throw new InvalidOperationException("The Combobox selection event is invalid.");
        var ids = new uint[nativeEvent.Payload.Length / sizeof(uint)];
        var seen = new HashSet<uint>();
        for (var index = 0; index < ids.Length; index++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(
                nativeEvent.Payload.Span.Slice(index * sizeof(uint), sizeof(uint))
            );
            if (id == 0 || !seen.Add(id))
                throw new InvalidOperationException(
                    "The Combobox selection event has invalid IDs."
                );
            ids[index] = id;
        }
        return new(ids);
    }
}

public static partial class ComponentElements
{
    public static Element<NativeExtensionTag> Select(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentSelectOptions? options = null,
        params ReadOnlySpan<ComponentSelectionItem> items
    ) => Select(ui, key, 0, options, items);

    public static Element<NativeExtensionTag> Select<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentSelectSelectedEvent> onSelected,
        ComponentSelectOptions? options = null,
        params ReadOnlySpan<ComponentSelectionItem> items
    )
        where TView : ViewBase =>
        Select(ui, key, ui.BindNativeExtensionEvent(view, onSelected).Token, options, items);

    private static Element<NativeExtensionTag> Select(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentSelectOptions? options,
        ReadOnlySpan<ComponentSelectionItem> items
    )
    {
        options ??= new();
        var batch = SelectionBatch.Create(items, options.SelectedId is uint id ? [id] : []);
        return ui.NativeExtension(
            ComponentsExtension.Select,
            key,
            ComponentSchema.Select.EncodeConfiguration(
                (ComponentSchema.Select.Size)(int)options.Size,
                batch.Labels,
                batch.Ids,
                batch.Disabled,
                batch.Selected,
                options.Placeholder,
                options.SearchPlaceholder,
                options.AccessibilityLabel,
                options.Searchable,
                options.Cleanable,
                options.Disabled,
                token
            )
        );
    }

    public static Element<NativeExtensionTag> Combobox(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentComboboxOptions? options = null,
        params ReadOnlySpan<ComponentSelectionItem> items
    ) => Combobox(ui, key, 0, options, items);

    public static Element<NativeExtensionTag> Combobox<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentComboboxChangedEvent> onChanged,
        ComponentComboboxOptions? options = null,
        params ReadOnlySpan<ComponentSelectionItem> items
    )
        where TView : ViewBase =>
        Combobox(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options, items);

    private static Element<NativeExtensionTag> Combobox(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentComboboxOptions? options,
        ReadOnlySpan<ComponentSelectionItem> items
    )
    {
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.SelectedIds);
        var batch = SelectionBatch.Create(items, options.SelectedIds);
        if (!options.Multiple && batch.Selected.Length > 1)
            throw new ArgumentException(
                "A single Combobox accepts one selected ID.",
                nameof(options)
            );
        return ui.NativeExtension(
            ComponentsExtension.Combobox,
            key,
            ComponentSchema.Combobox.EncodeConfiguration(
                (ComponentSchema.Combobox.Size)(int)options.Size,
                batch.Labels,
                batch.Ids,
                batch.Disabled,
                batch.Selected,
                options.Placeholder,
                options.SearchPlaceholder,
                options.Multiple,
                options.Cleanable,
                options.Disabled,
                token
            )
        );
    }
}

internal sealed class SelectionBatch
{
    private SelectionBatch(string[] labels, uint[] ids, uint[] disabled, uint[] selected)
    {
        Labels = labels;
        Ids = ids;
        Disabled = disabled;
        Selected = selected;
    }

    public string[] Labels { get; }
    public uint[] Ids { get; }
    public uint[] Disabled { get; }
    public uint[] Selected { get; }

    public static SelectionBatch Create(
        ReadOnlySpan<ComponentSelectionItem> items,
        IReadOnlyList<uint> selected
    )
    {
        if (items.Length > 4096)
            throw new ArgumentException(
                "Selection lists support at most 4096 items.",
                nameof(items)
            );
        var labels = new string[items.Length];
        var ids = new uint[items.Length];
        var disabled = new uint[items.Length];
        var known = new HashSet<uint>();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (item.Id == 0 || !known.Add(item.Id) || string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException(
                    "Selection items need unique nonzero IDs and labels.",
                    nameof(items)
                );
            labels[index] = item.Label;
            ids[index] = item.Id;
            disabled[index] = item.Disabled ? 1u : 0u;
        }
        var selectedIds = new uint[selected.Count];
        var seen = new HashSet<uint>();
        for (var index = 0; index < selected.Count; index++)
        {
            var id = selected[index];
            if (!known.Contains(id) || !seen.Add(id))
                throw new ArgumentException(
                    "Selected IDs must be distinct items in the batch.",
                    nameof(selected)
                );
            selectedIds[index] = id;
        }
        return new(labels, ids, disabled, selectedIds);
    }
}
