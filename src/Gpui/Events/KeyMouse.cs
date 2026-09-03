namespace Gpui;

/// <summary>Mouse button identity for observer mouse events.</summary>
public enum MouseButton : uint
{
    Left = 0,
    Right = 1,
    Middle = 2,
    Back = 3,
    Forward = 4,
}

/// <summary>
/// Observer snapshot of a key press or release forwarded from native GPUI.
/// Handlers observe only: native dispatch never stops propagation for these events,
/// so focused controls (Input, Slider, List, Overlay) keep their behavior.
/// </summary>
public sealed class KeyEvent
{
    internal KeyEvent(KeyEventKind kind, string key, uint modifiers, bool isHeld)
    {
        Kind = kind;
        Key = key;
        Modifiers = modifiers;
        IsHeld = isHeld;
    }

    public KeyEventKind Kind { get; }
    public string Key { get; }
    public uint Modifiers { get; }
    public bool IsHeld { get; }

    public bool Control => (Modifiers & 1u) != 0;
    public bool Alt => (Modifiers & 2u) != 0;
    public bool Shift => (Modifiers & 4u) != 0;
    public bool Platform => (Modifiers & 8u) != 0;
    public bool Function => (Modifiers & 16u) != 0;

    /// <summary>
    /// Hot-key match helper. The key name compares ordinal-ignore-case;
    /// modifiers must match exactly so e.g. Ctrl+Shift+S does not match Ctrl+S.
    /// </summary>
    public bool Matches(
        string key,
        bool control = false,
        bool alt = false,
        bool shift = false,
        bool platform = false,
        bool function = false
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!string.Equals(Key, key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expected = (control ? 1u : 0u)
            | (alt ? 2u : 0u)
            | (shift ? 4u : 0u)
            | (platform ? 8u : 0u)
            | (function ? 16u : 0u);
        return Modifiers == expected;
    }
}

/// <summary>
/// Observer snapshot of modifier keys forwarded from native GPUI.
/// This is the only event for modifier-only presses (e.g. holding Ctrl alone),
/// which never produce key down/up events.
/// </summary>
public readonly struct ModifiersEvent
{
    internal ModifiersEvent(uint modifiers)
    {
        Modifiers = modifiers;
    }

    public uint Modifiers { get; }

    public bool Control => (Modifiers & 1u) != 0;
    public bool Alt => (Modifiers & 2u) != 0;
    public bool Shift => (Modifiers & 4u) != 0;
    public bool Platform => (Modifiers & 8u) != 0;
    public bool Function => (Modifiers & 16u) != 0;

    public bool IsEmpty => Modifiers == 0;
}

/// <summary>
/// Observer snapshot of a mouse press or release forwarded from native GPUI.
/// High-frequency mouse movement never crosses; only discrete down/up events do,
/// and only for elements that opt in. Handlers never stop propagation.
/// </summary>
public readonly struct MouseEvent
{
    internal MouseEvent(
        MouseEventKind kind,
        float x,
        float y,
        MouseButton button,
        uint clickCount,
        uint modifiers
    )
    {
        Kind = kind;
        X = x;
        Y = y;
        Button = button;
        ClickCount = clickCount;
        Modifiers = modifiers;
    }

    public MouseEventKind Kind { get; }
    public float X { get; }
    public float Y { get; }
    public MouseButton Button { get; }
    public uint ClickCount { get; }
    public uint Modifiers { get; }

    public bool Control => (Modifiers & 1u) != 0;
    public bool Alt => (Modifiers & 2u) != 0;
    public bool Shift => (Modifiers & 4u) != 0;
    public bool Platform => (Modifiers & 8u) != 0;
    public bool Function => (Modifiers & 16u) != 0;
}

/// <summary>
/// Observer snapshot of OS files dropped onto the bound element, forwarded from native GPUI.
/// Only published while a binding is registered. Paths are decoded lossy UTF-8 and owned by
/// this event, so they remain valid across asynchronous handlers.
/// </summary>
public sealed class FileDropEvent
{
    private readonly string[] _paths;

    internal FileDropEvent(float x, float y, string[] paths, uint modifiers)
    {
        X = x;
        Y = y;
        _paths = paths;
        Modifiers = modifiers;
    }

    public float X { get; }
    public float Y { get; }
    public IReadOnlyList<string> Paths => _paths;
    public uint Modifiers { get; }

    public bool Control => (Modifiers & 1u) != 0;
    public bool Alt => (Modifiers & 2u) != 0;
    public bool Shift => (Modifiers & 4u) != 0;
    public bool Platform => (Modifiers & 8u) != 0;
    public bool Function => (Modifiers & 16u) != 0;
}

/// <summary>
/// Observer snapshot of hover state forwarded from native GPUI.
/// Fires on enter/exit transitions only, never per mouse move.
/// </summary>
public readonly struct HoverEvent
{
    internal HoverEvent(bool isHovering)
    {
        IsHovering = isHovering;
    }

    public bool IsHovering { get; }
}

/// <summary>
/// Observer snapshot of mouse movement forwarded from native GPUI.
/// Only published while a binding is registered; handlers must stay cheap
/// because this fires at pointer frequency.
/// </summary>
public readonly struct MouseMoveEvent
{
    internal MouseMoveEvent(float x, float y, MouseButton? pressedButton, uint modifiers)
    {
        X = x;
        Y = y;
        PressedButton = pressedButton;
        Modifiers = modifiers;
    }

    public float X { get; }
    public float Y { get; }
    public MouseButton? PressedButton { get; }
    public uint Modifiers { get; }

    public bool Control => (Modifiers & 1u) != 0;
    public bool Alt => (Modifiers & 2u) != 0;
    public bool Shift => (Modifiers & 4u) != 0;
    public bool Platform => (Modifiers & 8u) != 0;
    public bool Function => (Modifiers & 16u) != 0;
}

/// <summary>Units of a scroll-wheel delta.</summary>
public enum ScrollDeltaUnits : uint
{
    Pixels = 0,
    Lines = 1,
}

/// <summary>
/// Observer snapshot of scroll-wheel movement forwarded from native GPUI.
/// Only published while a binding is registered; handlers must stay cheap.
/// This observes only and does not replace retained Scroll resources.
/// </summary>
public readonly struct ScrollWheelEvent
{
    internal ScrollWheelEvent(
        float x,
        float y,
        float deltaX,
        float deltaY,
        ScrollDeltaUnits units,
        uint modifiers
    )
    {
        X = x;
        Y = y;
        DeltaX = deltaX;
        DeltaY = deltaY;
        Units = units;
        Modifiers = modifiers;
    }

    public float X { get; }
    public float Y { get; }
    public float DeltaX { get; }
    public float DeltaY { get; }
    public ScrollDeltaUnits Units { get; }
    public uint Modifiers { get; }

    public bool Control => (Modifiers & 1u) != 0;
    public bool Alt => (Modifiers & 2u) != 0;
    public bool Shift => (Modifiers & 4u) != 0;
    public bool Platform => (Modifiers & 8u) != 0;
    public bool Function => (Modifiers & 16u) != 0;
}
