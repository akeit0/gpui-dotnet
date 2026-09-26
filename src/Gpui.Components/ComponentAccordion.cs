using System.Buffers.Binary;
using System.Text;

namespace Gpui.Components;

/// <summary>A disclosure section with a stable ID and one managed content element.</summary>
public readonly record struct ComponentAccordionItem(
    uint Id,
    string Title,
    Element Content,
    bool Disabled = false
);

public sealed record ComponentAccordionOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public IReadOnlyList<uint> OpenIds { get; init; } = Array.Empty<uint>();
    public bool Multiple { get; init; }
    public bool Bordered { get; init; } = true;
    public bool Disabled { get; init; }
}

/// <summary>The full set of open IDs requested by an Accordion interaction.</summary>
public sealed class ComponentAccordionChangedEvent
    : INativeExtensionEvent<ComponentAccordionChangedEvent>
{
    private ComponentAccordionChangedEvent(uint[] openIds) => OpenIds = openIds;

    public IReadOnlyList<uint> OpenIds { get; }

    public static ComponentAccordionChangedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Accordion.EventChanged
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length % sizeof(uint) != 0
            || nativeEvent.Payload.Length > 256 * sizeof(uint)
        )
            throw new InvalidOperationException("The Accordion event is invalid.");
        var ids = new uint[nativeEvent.Payload.Length / sizeof(uint)];
        var seen = new HashSet<uint>();
        for (var index = 0; index < ids.Length; index++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(
                nativeEvent.Payload.Span.Slice(index * sizeof(uint), sizeof(uint))
            );
            if (id == 0 || !seen.Add(id))
                throw new InvalidOperationException("The Accordion event has invalid IDs.");
            ids[index] = id;
        }
        return new(ids);
    }
}

public static partial class ComponentElements
{
    public static Element<NativeExtensionTag> Accordion(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentAccordionOptions? options = null,
        params ReadOnlySpan<ComponentAccordionItem> items
    ) => Accordion(ui, key, 0, options, items);

    public static Element<NativeExtensionTag> Accordion<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentAccordionChangedEvent> onChanged,
        ComponentAccordionOptions? options = null,
        params ReadOnlySpan<ComponentAccordionItem> items
    )
        where TView : ViewBase =>
        Accordion(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options, items);

    private static Element<NativeExtensionTag> Accordion(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentAccordionOptions? options,
        ReadOnlySpan<ComponentAccordionItem> items
    )
    {
        options ??= new();
        var batch = AccordionBatch.Create(items, options.OpenIds, options.Multiple);
        return ui.NativeExtension(
            ComponentsExtension.Accordion,
            key,
            ComponentSchema.Accordion.EncodeConfiguration(
                (ComponentSchema.Accordion.Size)(int)options.Size,
                batch.Ids,
                batch.Titles,
                batch.Disabled,
                batch.OpenIds,
                options.Multiple,
                options.Bordered,
                options.Disabled,
                token
            ),
            batch.Content
        );
    }
}

internal sealed class AccordionBatch
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private AccordionBatch(
        uint[] ids,
        string[] titles,
        uint[] disabled,
        uint[] openIds,
        Element[] content
    )
    {
        Ids = ids;
        Titles = titles;
        Disabled = disabled;
        OpenIds = openIds;
        Content = content;
    }

    public uint[] Ids { get; }
    public string[] Titles { get; }
    public uint[] Disabled { get; }
    public uint[] OpenIds { get; }
    public Element[] Content { get; }

    public static AccordionBatch Create(
        ReadOnlySpan<ComponentAccordionItem> items,
        IReadOnlyList<uint> openIds,
        bool multiple
    )
    {
        ArgumentNullException.ThrowIfNull(openIds);
        if (items.Length > 256 || openIds.Count > items.Length || (!multiple && openIds.Count > 1))
            throw new ArgumentException(
                "The Accordion has an invalid item or open-ID count.",
                nameof(items)
            );
        var ids = new uint[items.Length];
        var titles = new string[items.Length];
        var disabled = new uint[items.Length];
        var content = new Element[items.Length];
        var known = new HashSet<uint>();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (string.IsNullOrWhiteSpace(item.Title))
                throw new ArgumentException("Accordion items need titles.", nameof(items));
            int titleBytes;
            try
            {
                titleBytes = StrictUtf8.GetByteCount(item.Title);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Accordion titles must be valid Unicode.",
                    nameof(items),
                    exception
                );
            }
            if (
                item.Id == 0
                || !known.Add(item.Id)
                || item.Title.Any(char.IsControl)
                || titleBytes > 1024
            )
                throw new ArgumentException(
                    "Accordion items need distinct nonzero IDs and titles.",
                    nameof(items)
                );
            ids[index] = item.Id;
            titles[index] = item.Title;
            disabled[index] = item.Disabled ? 1u : 0u;
            content[index] = item.Content;
        }
        var open = new uint[openIds.Count];
        var seen = new HashSet<uint>();
        for (var index = 0; index < open.Length; index++)
        {
            var id = openIds[index];
            if (!known.Contains(id) || !seen.Add(id))
                throw new ArgumentException(
                    "Open Accordion IDs must be distinct items.",
                    nameof(openIds)
                );
            open[index] = id;
        }
        return new(ids, titles, disabled, open, content);
    }
}
