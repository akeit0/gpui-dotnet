using Gpui.Interop;

namespace Gpui;

[Flags]
public enum ShortcutModifiers : uint
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Platform = 8,
    Function = 16,

    /// <summary>Command on macOS; Control on Windows and Linux.</summary>
    Primary = 32,
}

/// <summary>
/// A single key and exact modifiers, matched natively in the focused element's ancestry.
/// Text-producing keys require Control, Platform, or Primary; bare-character modes are not exposed.
/// </summary>
public readonly record struct Shortcut(
    ShortcutKey Key,
    ShortcutModifiers Modifiers = ShortcutModifiers.None
);

/// <summary>Disabled bindings reserve their gesture; repeats are consumed without invoking by default.</summary>
public readonly struct ShortcutOptions
{
    private readonly uint _flags;

    public ShortcutOptions(bool enabled = true, bool consume = true, bool allowRepeat = false)
    {
        _flags = (enabled ? 0u : 2u) | (consume ? 0u : 1u) | (allowRepeat ? 4u : 0u);
    }

    public bool Enabled => (_flags & 2) == 0;
    public bool Consume => (_flags & 1) == 0;
    public bool AllowRepeat => (_flags & 4) != 0;

    internal ulong Pack(Shortcut shortcut)
    {
        if (
            (uint)shortcut.Key is < 1 or > (uint)ShortcutKey.Backtick
            || (uint)shortcut.Modifiers > 63
        )
            throw new ArgumentException("Invalid shortcut key or modifiers.", nameof(shortcut));
        var packed =
            (ulong)shortcut.Key | ((ulong)shortcut.Modifiers << 16) | ((ulong)_flags << 24);
        if (SemanticRegistry.PayloadError(OpCode.OnShortcut, 1, packed) != 0)
            throw new ArgumentException(
                "Text keys require Control, Platform, or Primary. Primary cannot be combined with Control or Platform.",
                nameof(shortcut)
            );
        return packed;
    }
}

public static partial class ElementExtensions
{
    /// <summary>
    /// Declares a native shortcut for this element's focus scope. Descendants win; the last matching
    /// declaration wins within an element. Only matched, enabled commands invoke managed code.
    /// </summary>
    public static Element<TTag> OnShortcut<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Shortcut shortcut,
        Action<TView> callback,
        ShortcutOptions options = default
    )
        where TTag : unmanaged, IShortcutScopeElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        var packed = options.Pack(shortcut);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnShortcut,
            view.Runtime.Events.BindShortcut(callback),
            packed
        );
        return element;
    }
}
