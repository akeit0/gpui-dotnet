namespace Gpui.Components;

public enum ComponentFormLabelPlacement
{
    Above,
    Beside,
}

/// <summary>Layout of an optional native form. Validation and values remain application-owned.</summary>
public sealed record ComponentFormOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
    public ComponentFormLabelPlacement LabelPlacement { get; init; } =
        ComponentFormLabelPlacement.Above;
    public uint Columns { get; init; } = 1;
    public float LabelWidthPixels { get; init; } = 140;
}

/// <summary>One labeled control in a form's batched child list.</summary>
public readonly struct ComponentFormField
{
    private readonly bool _valid;

    private ComponentFormField(
        string label,
        Element control,
        string helpText,
        string errorText,
        bool required,
        uint columnSpan
    )
    {
        _valid = true;
        Label = label;
        Control = control;
        HelpText = helpText;
        ErrorText = errorText;
        Required = required;
        ColumnSpan = columnSpan;
    }

    public string Label { get; }
    public Element Control { get; }
    public string HelpText { get; }
    public string ErrorText { get; }
    public bool Required { get; }
    public uint ColumnSpan { get; }
    internal bool IsValid => _valid;

    /// <summary>
    /// Names a GPUI.NET core control and attaches the active help or error text to it.
    /// An error replaces help in both the visible field and accessible description.
    /// </summary>
    public static ComponentFormField For<TTag>(
        string label,
        Element<TTag> control,
        string? helpText = null,
        string? errorText = null,
        bool required = false,
        uint columnSpan = 1
    )
        where TTag : unmanaged, IAccessibleElementTag
    {
        Validate(label, control, helpText, errorText, columnSpan);
        var named = control.AccessibleName(label);
        var description = Description(helpText, errorText, required);
        if (description.Length != 0)
            named = named.AccessibleDescription(description);
        return new ComponentFormField(
            label,
            named,
            helpText ?? string.Empty,
            errorText ?? string.Empty,
            required,
            columnSpan
        );
    }

    /// <summary>
    /// Adds a control that already declares its own accessible name. Use this for optional
    /// extension controls such as Textarea, whose accessible label is set in their options.
    /// </summary>
    public static ComponentFormField NamedControl(
        string label,
        Element control,
        string? helpText = null,
        string? errorText = null,
        bool required = false,
        uint columnSpan = 1
    )
    {
        Validate(label, control, helpText, errorText, columnSpan);
        return new ComponentFormField(
            label,
            control,
            helpText ?? string.Empty,
            errorText ?? string.Empty,
            required,
            columnSpan
        );
    }

    private static string Description(string? helpText, string? errorText, bool required)
    {
        var detail = !string.IsNullOrEmpty(errorText) ? $"Error: {errorText}" : helpText ?? "";
        if (!required)
            return detail;
        return detail.Length == 0 ? "Required" : $"Required. {detail}";
    }

    private static void Validate(
        string label,
        Element control,
        string? helpText,
        string? errorText,
        uint columnSpan
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (control.IsDefault)
            throw new ArgumentException("A form field needs a control.", nameof(control));
        if (columnSpan is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(columnSpan));
        if (
            label.Contains('\0')
            || helpText?.Contains('\0') == true
            || errorText?.Contains('\0') == true
        )
            throw new ArgumentException("Form field text cannot contain NUL.");
    }
}

public static partial class ComponentElements
{
    /// <summary>Batches form fields and an optional full-width footer into one native layout.</summary>
    public static Element<NativeExtensionTag> Form(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ReadOnlySpan<ComponentFormField> fields,
        ComponentFormOptions? options = null,
        Element footer = default
    )
    {
        options ??= new();
        if (options.Columns is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(options), "Columns must be 1 through 4.");
        if (!float.IsFinite(options.LabelWidthPixels) || options.LabelWidthPixels <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Label width must be positive and finite."
            );
        if (fields.Length > 256)
            throw new ArgumentOutOfRangeException(
                nameof(fields),
                "A form can contain at most 256 fields."
            );

        var labels = new string[fields.Length];
        var helpTexts = new string[fields.Length];
        var errorTexts = new string[fields.Length];
        var required = new uint[fields.Length];
        var spans = new uint[fields.Length];
        var children = new Element[fields.Length + (footer.IsDefault ? 0 : 1)];
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (!field.IsValid)
                throw new ArgumentException(
                    "A form field must be constructed with a factory.",
                    nameof(fields)
                );
            if (field.ColumnSpan > options.Columns)
                throw new ArgumentOutOfRangeException(
                    nameof(fields),
                    "A field span exceeds the column count."
                );
            labels[index] = field.Label;
            helpTexts[index] = field.HelpText;
            errorTexts[index] = field.ErrorText;
            required[index] = field.Required ? 1u : 0u;
            spans[index] = field.ColumnSpan;
            children[index] = field.Control;
        }
        if (!footer.IsDefault)
            children[^1] = footer;

        return ui.NativeExtension(
            ComponentsExtension.Form,
            key,
            ComponentSchema.Form.EncodeConfiguration(
                (ComponentSchema.Form.Size)(int)options.Size,
                options.LabelPlacement switch
                {
                    ComponentFormLabelPlacement.Above => ComponentSchema.Form.LabelAxis.Vertical,
                    ComponentFormLabelPlacement.Beside => ComponentSchema.Form.LabelAxis.Horizontal,
                    _ => throw new ArgumentOutOfRangeException(nameof(options)),
                },
                options.Columns,
                options.LabelWidthPixels,
                labels,
                helpTexts,
                errorTexts,
                required,
                spans,
                !footer.IsDefault
            ),
            children
        );
    }
}
