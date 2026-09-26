using System.Text;

namespace Gpui.Components;

/// <summary>Initial value and current presentation for an ordinary multiline text field.</summary>
public sealed record ComponentTextareaOptions
{
    /// <summary>Consumed only when the keyed native resource is first created.</summary>
    public string InitialValue { get; init; } = string.Empty;

    public string Placeholder { get; init; } = string.Empty;
    public uint Rows { get; init; } = 3;
    public bool Disabled { get; init; }
    public bool ReadOnly { get; init; }
    public string AccessibilityLabel { get; init; } = string.Empty;
}

/// <summary>A copied UTF-8 value from a native Textarea edit.</summary>
public sealed class ComponentTextareaChangedEvent
    : INativeExtensionEvent<ComponentTextareaChangedEvent>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ReadOnlyMemory<byte> _utf8Value;
    private string? _value;

    private ComponentTextareaChangedEvent(ReadOnlyMemory<byte> utf8Value, ulong revision)
    {
        _utf8Value = utf8Value;
        Revision = revision;
    }

    public ReadOnlyMemory<byte> Utf8Value => _utf8Value;
    public string Value => _value ??= Encoding.UTF8.GetString(_utf8Value.Span);
    public ulong Revision { get; }

    public static ComponentTextareaChangedEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        if (
            nativeEvent.Kind != ComponentSchema.Textarea.EventChanged
            || nativeEvent.Flags != 0
            || nativeEvent.Revision == 0
        )
        {
            throw new InvalidOperationException("The Textarea change event has invalid metadata.");
        }
        try
        {
            _ = StrictUtf8.GetCharCount(nativeEvent.Payload.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException(
                "The Textarea change event contains invalid UTF-8.",
                exception
            );
        }
        return new ComponentTextareaChangedEvent(nativeEvent.Payload, nativeEvent.Revision);
    }
}

/// <summary>Coarse commands for one retained native Textarea.</summary>
public readonly struct ComponentTextareaController
{
    private readonly NativeExtensionController _native;

    internal ComponentTextareaController(NativeExtensionController native) => _native = native;

    public bool IsBound => _native.IsBound;

    public void Focus() => _native.Dispatch(ComponentSchema.Textarea.CommandFocus);

    /// <summary>
    /// Authoritatively replaces the native value without emitting a change event. A changed
    /// replacement resets selection, scroll, and undo history; an identical value preserves them.
    /// </summary>
    public void SetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0'))
            throw new ArgumentException("Textarea values cannot contain NUL.", nameof(value));
        _native.Dispatch(ComponentSchema.Textarea.CommandSetValue, Encoding.UTF8.GetBytes(value));
    }

    /// <summary>UTF-8 replacement. The native host validates and copies the payload.</summary>
    public void SetValue(ReadOnlySpan<byte> utf8Value) =>
        _native.Dispatch(ComponentSchema.Textarea.CommandSetValue, utf8Value);

    internal NativeExtensionController Native => _native;
}

public static partial class ComponentElements
{
    public static ComponentTextareaController CreateTextareaController(
        this ViewConstruction context,
        string key
    ) => new(context.CreateNativeExtensionController(ComponentsExtension.Textarea, key));

    public static ComponentTextareaController CreateTextareaController(
        this ViewConstruction context,
        ReadOnlySpan<byte> utf8Key
    ) => new(context.CreateNativeExtensionController(ComponentsExtension.Textarea, utf8Key));

    public static Element<NativeExtensionTag> Textarea(
        this RenderContext ui,
        ComponentTextareaController controller,
        ComponentTextareaOptions? options = null
    ) => ui.NativeExtension(controller.Native, TextareaConfiguration(options, 0));

    public static Element<NativeExtensionTag> Textarea<TView>(
        this RenderContext ui,
        ComponentTextareaController controller,
        TView view,
        Action<TView, ComponentTextareaChangedEvent> onChanged,
        ComponentTextareaOptions? options = null
    )
        where TView : ViewBase =>
        ui.NativeExtension(
            controller.Native,
            TextareaConfiguration(options, ui.BindNativeExtensionEvent(view, onChanged).Token)
        );

    public static Element<NativeExtensionTag> Textarea(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentTextareaOptions? options = null
    ) => ui.NativeExtension(ComponentsExtension.Textarea, key, TextareaConfiguration(options, 0));

    public static Element<NativeExtensionTag> Textarea<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentTextareaChangedEvent> onChanged,
        ComponentTextareaOptions? options = null
    )
        where TView : ViewBase =>
        ui.NativeExtension(
            ComponentsExtension.Textarea,
            key,
            TextareaConfiguration(options, ui.BindNativeExtensionEvent(view, onChanged).Token)
        );

    internal static string TextareaConfiguration(
        ComponentTextareaOptions? options,
        ulong changedEvent
    )
    {
        options ??= new();
        if (options.Rows is 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options), "Rows must be 1 through 1000.");
        return ComponentSchema.Textarea.EncodeConfiguration(
            options.InitialValue,
            options.Placeholder,
            options.Rows,
            options.Disabled,
            options.ReadOnly,
            options.AccessibilityLabel,
            changedEvent
        );
    }
}
