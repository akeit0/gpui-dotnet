using System.Buffers.Binary;

namespace Gpui.Components;

/// <summary>A committed single date or an ordered date range.</summary>
public readonly record struct ComponentDateValue(bool IsRange, DateOnly? Start, DateOnly? End)
{
    public static ComponentDateValue Single(DateOnly? date) => new(false, date, null);

    public static ComponentDateValue Range(DateOnly? start, DateOnly? end) => new(true, start, end);
}

public record ComponentDateOptions
{
    public ComponentDateValue Value { get; init; } = ComponentDateValue.Single(null);
    public DateOnly? MinimumDate { get; init; }
    public DateOnly? MaximumDate { get; init; }
    public IReadOnlyList<DayOfWeek> DisabledWeekdays { get; init; } = [];
    public DayOfWeek FirstDayOfWeek { get; init; } = DayOfWeek.Sunday;
    public uint NumberOfMonths { get; init; } = 1;
    public uint FirstYear { get; init; } = 1900;
    public uint LastYear { get; init; } = 2100;
    public ComponentSize Size { get; init; } = ComponentSize.Medium;
}

public sealed record ComponentCalendarOptions : ComponentDateOptions;

public sealed record ComponentDatePickerOptions : ComponentDateOptions
{
    public string Placeholder { get; init; } = string.Empty;
    public bool Cleanable { get; init; }
    public bool Disabled { get; init; }
}

/// <summary>A copied date selection requested by the native Calendar or DatePicker.</summary>
public sealed class ComponentDateChangedEvent : INativeExtensionEvent<ComponentDateChangedEvent>
{
    private ComponentDateChangedEvent(ComponentDateValue value) => Value = value;

    public ComponentDateValue Value { get; }

    public static ComponentDateChangedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Calendar.EventChanged
            || nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.Length != 9
        )
            throw new InvalidOperationException("The date event is invalid.");
        var payload = nativeEvent.Payload.Span;
        if (payload[0] > 1)
            throw new InvalidOperationException("The date mode is invalid.");
        var start = DecodeDay(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4)));
        var end = DecodeDay(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(5, 4)));
        var value = new ComponentDateValue(payload[0] != 0, start, end);
        try
        {
            DateBatch.ValidateValue(value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The date event range is invalid.", exception);
        }
        return new(value);
    }

    private static DateOnly? DecodeDay(uint day)
    {
        if (day == uint.MaxValue)
            return null;
        if (day > DateOnly.MaxValue.DayNumber)
            throw new InvalidOperationException("The date day number is invalid.");
        return DateOnly.FromDayNumber((int)day);
    }
}

public static partial class ComponentElements
{
    public static Element<NativeExtensionTag> Calendar(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentCalendarOptions? options = null
    ) => Calendar(ui, key, 0, options);

    public static Element<NativeExtensionTag> Calendar<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentDateChangedEvent> onChanged,
        ComponentCalendarOptions? options = null
    )
        where TView : ViewBase =>
        Calendar(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options);

    public static Element<NativeExtensionTag> DatePicker(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentDatePickerOptions? options = null
    ) => DatePicker(ui, key, 0, options);

    public static Element<NativeExtensionTag> DatePicker<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentDateChangedEvent> onChanged,
        ComponentDatePickerOptions? options = null
    )
        where TView : ViewBase =>
        DatePicker(ui, key, ui.BindNativeExtensionEvent(view, onChanged).Token, options);

    private static Element<NativeExtensionTag> Calendar(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentCalendarOptions? options
    )
    {
        options ??= new();
        var dates = DateBatch.Create(options);
        return ui.NativeExtension(
            ComponentsExtension.Calendar,
            key,
            ComponentSchema.Calendar.EncodeConfiguration(
                (ComponentSchema.Calendar.Size)(int)options.Size,
                dates.StartDay,
                dates.EndDay,
                options.Value.IsRange,
                dates.MinimumDay,
                dates.MaximumDay,
                dates.DisabledWeekdays,
                (uint)options.FirstDayOfWeek,
                options.NumberOfMonths,
                options.FirstYear,
                options.LastYear,
                token
            )
        );
    }

    private static Element<NativeExtensionTag> DatePicker(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentDatePickerOptions? options
    )
    {
        options ??= new();
        var dates = DateBatch.Create(options);
        return ui.NativeExtension(
            ComponentsExtension.DatePicker,
            key,
            ComponentSchema.DatePicker.EncodeConfiguration(
                (ComponentSchema.DatePicker.Size)(int)options.Size,
                dates.StartDay,
                dates.EndDay,
                options.Value.IsRange,
                dates.MinimumDay,
                dates.MaximumDay,
                dates.DisabledWeekdays,
                (uint)options.FirstDayOfWeek,
                options.NumberOfMonths,
                options.FirstYear,
                options.LastYear,
                options.Placeholder,
                options.Cleanable,
                options.Disabled,
                token
            )
        );
    }
}

internal readonly record struct DateBatch(
    uint StartDay,
    uint EndDay,
    uint MinimumDay,
    uint MaximumDay,
    uint DisabledWeekdays
)
{
    public static DateBatch Create(ComponentDateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateValue(options.Value);
        ArgumentNullException.ThrowIfNull(options.DisabledWeekdays);
        if (
            options.FirstYear < 1
            || options.LastYear > 9999
            || options.FirstYear > options.LastYear
            || options.NumberOfMonths is < 1 or > 2
            || (uint)options.FirstDayOfWeek > 6
            || options.MinimumDate > options.MaximumDate
        )
            throw new ArgumentException("The date options have invalid limits.", nameof(options));
        var mask = 0u;
        foreach (var day in options.DisabledWeekdays)
        {
            if ((uint)day > 6)
                throw new ArgumentException("A disabled weekday is invalid.", nameof(options));
            mask |= 1u << (int)day;
        }
        ValidateSelected(options.Value.Start, options, mask);
        ValidateSelected(options.Value.End, options, mask);
        return new(
            EncodeDay(options.Value.Start),
            EncodeDay(options.Value.End),
            EncodeDay(options.MinimumDate),
            EncodeDay(options.MaximumDate),
            mask
        );
    }

    internal static void ValidateValue(ComponentDateValue value)
    {
        if (
            (!value.IsRange && value.End is not null)
            || (value.End is not null && value.Start is null)
            || value.Start > value.End
        )
            throw new ArgumentException(
                "A date range needs ordered start and end dates.",
                nameof(value)
            );
    }

    private static void ValidateSelected(DateOnly? date, ComponentDateOptions options, uint mask)
    {
        if (date is not DateOnly selected)
            return;
        if (
            selected.Year < options.FirstYear
            || selected.Year > options.LastYear
            || selected < options.MinimumDate
            || selected > options.MaximumDate
            || (mask & (1u << (int)selected.DayOfWeek)) != 0
        )
            throw new ArgumentException(
                "The selected date is outside the allowed dates.",
                nameof(options)
            );
    }

    private static uint EncodeDay(DateOnly? date) =>
        date is DateOnly value ? (uint)value.DayNumber : uint.MaxValue;
}
