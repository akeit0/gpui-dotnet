using System.Text;

namespace Gpui;

/// <summary>Initial configuration for a retained native single-line input.</summary>
public readonly struct InputOptions
{
    public InputOptions(
        string? initialValue = null,
        string? placeholder = null,
        bool disabled = false,
        bool readOnly = false,
        bool password = false
    )
    {
        InitialValue = initialValue ?? string.Empty;
        Placeholder = placeholder ?? string.Empty;
        Disabled = disabled;
        ReadOnly = readOnly;
        Password = password;
    }

    /// <summary>
    /// Value used only when the native resource is first created. Use <see cref="InputController.SetValue"/>
    /// for subsequent programmatic changes.
    /// </summary>
    public string? InitialValue { get; }

    public string? Placeholder { get; }
    public bool Disabled { get; }
    public bool ReadOnly { get; }
    public bool Password { get; }

    internal ReadOnlySpan<char> EffectiveInitialValue => InitialValue ?? string.Empty;
    internal ReadOnlySpan<char> EffectivePlaceholder => Placeholder ?? string.Empty;
}

/// <summary>
/// Allocation-free UTF-8 initial configuration for a retained native single-line input.
/// The supplied spans are copied directly into the current render arena.
/// </summary>
public readonly ref struct Utf8InputOptions
{
    public Utf8InputOptions(
        ReadOnlySpan<byte> initialValue = default,
        ReadOnlySpan<byte> placeholder = default,
        bool disabled = false,
        bool readOnly = false,
        bool password = false
    )
    {
        InitialValue = initialValue;
        Placeholder = placeholder;
        Disabled = disabled;
        ReadOnly = readOnly;
        Password = password;
    }

    public ReadOnlySpan<byte> InitialValue { get; }
    public ReadOnlySpan<byte> Placeholder { get; }
    public bool Disabled { get; }
    public bool ReadOnly { get; }
    public bool Password { get; }
}

/// <summary>Kind of event emitted by a retained native input.</summary>
public enum InputEventKind : ushort
{
    Changed = 1,
    Submitted = 2,
    FocusChanged = 3,
}

/// <summary>
/// Snapshot of an input's native state when an opt-in event is emitted. The UTF-8 value is owned
/// by this event and remains valid across asynchronous handlers. UTF-16 decoding is lazy.
/// </summary>
public sealed class InputEvent
{
    private readonly byte[] _utf8Value;
    private string? _value;

    internal InputEvent(InputEventKind kind, byte[] utf8Value, bool isFocused, ulong revision)
    {
        Kind = kind;
        _utf8Value = utf8Value;
        IsFocused = isFocused;
        Revision = revision;
    }

    public InputEventKind Kind { get; }

    /// <summary>The native value without a UTF-8 to UTF-16 conversion.</summary>
    public ReadOnlyMemory<byte> Utf8Value => _utf8Value;

    /// <summary>The value decoded to UTF-16 on first access.</summary>
    public string Value => _value ??= Encoding.UTF8.GetString(_utf8Value);

    public bool IsFocused { get; }
    public ulong Revision { get; }
}
