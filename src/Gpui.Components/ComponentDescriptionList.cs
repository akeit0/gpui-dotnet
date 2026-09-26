namespace Gpui.Components;

/// <summary>Layout of the native description list.</summary>
public sealed record ComponentDescriptionListOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentOrientation Orientation { get; init; } = ComponentOrientation.Horizontal;
    public float LabelWidthPixels { get; init; } = 120;
    public bool Bordered { get; init; } = true;
    public uint Columns { get; init; } = 3;
}

/// <summary>One label/value pair or a full-width separator in a description list.</summary>
public readonly struct ComponentDescriptionEntry
{
    private readonly byte _kind;

    private ComponentDescriptionEntry(byte kind, Element label, Element value, uint span)
    {
        _kind = kind;
        Label = label;
        Value = value;
        Span = span;
    }

    public Element Label { get; }
    public Element Value { get; }
    public uint Span { get; }

    internal bool IsItem => _kind == 1;
    internal bool IsSeparator => _kind == 2;

    public static ComponentDescriptionEntry Item(Element label, Element value, uint span = 1)
    {
        if (label.IsDefault)
            throw new ArgumentException("A description label needs an element.", nameof(label));
        if (value.IsDefault)
            throw new ArgumentException("A description value needs an element.", nameof(value));
        if (span is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(span), "Span must be between 1 and 10.");
        return new ComponentDescriptionEntry(1, label, value, span);
    }

    public static ComponentDescriptionEntry Separator() => new(2, default, default, 0);
}

public static partial class ComponentElements
{
    /// <summary>
    /// Batches rich label/value children and their column spans into one native description list.
    /// A separator occupies a complete row.
    /// </summary>
    public static Element<NativeExtensionTag> DescriptionList(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentDescriptionListOptions? options = null,
        params ReadOnlySpan<ComponentDescriptionEntry> entries
    )
    {
        options ??= new();
        if (options.Columns is < 1 or > 10)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Columns must be between 1 and 10."
            );
        if (!float.IsFinite(options.LabelWidthPixels) || options.LabelWidthPixels <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Label width must be positive and finite."
            );

        var spans = new uint[entries.Length];
        var childCount = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry.IsItem)
            {
                if (entry.Span > options.Columns)
                    throw new ArgumentOutOfRangeException(
                        nameof(entries),
                        "An entry span exceeds the column count."
                    );
                spans[index] = entry.Span;
                childCount = checked(childCount + 2);
            }
            else if (!entry.IsSeparator)
            {
                throw new ArgumentException(
                    "A description entry must be an item or separator.",
                    nameof(entries)
                );
            }
        }

        var children = new Element[childCount];
        var childIndex = 0;
        foreach (var entry in entries)
        {
            if (!entry.IsItem)
                continue;
            children[childIndex++] = entry.Label;
            children[childIndex++] = entry.Value;
        }

        return ui.NativeExtension(
            ComponentsExtension.DescriptionList,
            key,
            ComponentSchema.DescriptionList.EncodeConfiguration(
                (ComponentSchema.DescriptionList.Size)(int)options.Size,
                (ComponentSchema.DescriptionList.Axis)(int)options.Orientation,
                options.LabelWidthPixels,
                options.Bordered,
                options.Columns,
                spans
            ),
            children
        );
    }
}
