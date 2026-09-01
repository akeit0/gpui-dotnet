using System.Diagnostics;
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

/// <summary>
/// Imperative handle for a retained native input declared by the same View with ui.Input().
/// Native user edits do not require controller calls.
/// A default controller is assigned a stable key when passed by reference to ui.Input().
/// </summary>
[DebuggerDisplay("{DebuggerView,nq}")]
public readonly struct InputController
{
    private readonly ViewBase? _owner;
    private readonly byte[]? _utf8Key;

    internal InputController(ViewBase owner, string key)
    {
        _owner = owner;
        _utf8Key = Encoding.UTF8.GetBytes(key);
    }

    internal InputController(ViewBase owner, ReadOnlySpan<byte> utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key.ToArray();
    }

    /// <summary>Internal constructor that takes ownership of an already-encoded key array.</summary>
    internal InputController(ViewBase owner, byte[] utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key;
    }

    /// <summary>True once this controller has been bound to a resource.</summary>
    public bool IsBound => _utf8Key is not null;

    internal ReadOnlySpan<byte> Utf8KeySpan => _utf8Key;

    public bool IsDefault => _owner is null;

    private string DebuggerView
    {
        get
        {
            if (_utf8Key is null)
            {
                return "unbound";
            }
            return ResourceKeys.TryDecodeAutoKey(_utf8Key, out var id) ? $"auto:{id}" : "explicit";
        }
    }

    public void Focus() => Dispatch(ResourceCommandKind.InputFocus);

    public void Blur() => Dispatch(ResourceCommandKind.InputBlur);

    public void SelectAll() => Dispatch(ResourceCommandKind.InputSelectAll);

    /// <summary>Replaces the native value and moves the caret to its end.</summary>
    public void SetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Input,
                ResourceCommandKind.InputSetValue,
                null,
                0,
                0,
                value,
                Utf8KeyArray
            )
        );
    }

    /// <summary>
    /// Replaces the native value from UTF-8 and moves the caret to its end. The bytes are
    /// trusted: they must contain valid UTF-8 with no interior NUL, and are copied so the
    /// command remains safe after this call returns.
    /// </summary>
    public void SetValue(ReadOnlySpan<byte> utf8Value)
    {
        Owner.DispatchUtf8InputValue(Utf8KeyArray, utf8Value);
    }

    private void Dispatch(ResourceCommandKind command) =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(ResourceKind.Input, command, null, 0, 0, null, Utf8KeyArray)
        );

    private ViewBase Owner =>
        _owner ?? throw new InvalidOperationException("Default InputController cannot be used.");
    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default InputController cannot be used.");
}
