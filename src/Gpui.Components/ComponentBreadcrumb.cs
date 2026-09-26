using System.Buffers.Binary;

namespace Gpui.Components;

/// <summary>A stable application ID and label for one native breadcrumb item.</summary>
public readonly record struct ComponentBreadcrumbItem(uint Id, string Label, bool Disabled = false);

/// <summary>The stable ID of the activated breadcrumb item.</summary>
public sealed class ComponentBreadcrumbClickedEvent
    : INativeExtensionEvent<ComponentBreadcrumbClickedEvent>
{
    private ComponentBreadcrumbClickedEvent(uint itemId) => ItemId = itemId;

    public uint ItemId { get; }

    public static ComponentBreadcrumbClickedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Breadcrumb.EventClicked
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length != sizeof(uint)
        )
        {
            throw new InvalidOperationException("The breadcrumb click event is invalid.");
        }
        return new ComponentBreadcrumbClickedEvent(
            BinaryPrimitives.ReadUInt32LittleEndian(nativeEvent.Payload.Span)
        );
    }
}

public static partial class ComponentElements
{
    /// <summary>Declares one native breadcrumb from a batch of application-owned items.</summary>
    public static Element<NativeExtensionTag> Breadcrumb(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        params ReadOnlySpan<ComponentBreadcrumbItem> items
    ) => Breadcrumb(ui, key, 0, items);

    /// <summary>Declares a breadcrumb and routes item activations by stable ID.</summary>
    public static Element<NativeExtensionTag> Breadcrumb<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentBreadcrumbClickedEvent> onClick,
        params ReadOnlySpan<ComponentBreadcrumbItem> items
    )
        where TView : ViewBase =>
        Breadcrumb(ui, key, ui.BindNativeExtensionEvent(view, onClick).Token, items);

    private static Element<NativeExtensionTag> Breadcrumb(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ReadOnlySpan<ComponentBreadcrumbItem> items
    )
    {
        var labels = new string[items.Length];
        var ids = new uint[items.Length];
        var disabled = new uint[items.Length];
        var seen = new HashSet<uint>();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Breadcrumb labels cannot be empty.", nameof(items));
            if (!seen.Add(item.Id))
                throw new ArgumentException("Breadcrumb IDs must be unique.", nameof(items));
            labels[index] = item.Label;
            ids[index] = item.Id;
            disabled[index] = item.Disabled ? 1u : 0u;
        }

        return ui.NativeExtension(
            ComponentsExtension.Breadcrumb,
            key,
            ComponentSchema.Breadcrumb.EncodeConfiguration(labels, ids, disabled, token)
        );
    }
}
