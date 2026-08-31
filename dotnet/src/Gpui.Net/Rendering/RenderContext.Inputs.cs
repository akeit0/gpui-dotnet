using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a retained native single-line input. Text editing, selection, focus, clipboard,
    /// and IME composition remain native. InitialValue is consumed only when this resource key is
    /// first created; use an InputController for later programmatic value changes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<InputTag> Input(ReadOnlySpan<char> key, InputOptions options = default)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("An input resource key cannot be empty.", nameof(key));
        }
        if (key.Contains('\0'))
        {
            throw new ArgumentException("An input resource key cannot contain NUL.", nameof(key));
        }

        var element = ArenaWriter.AddCompositeNode<InputTag>(
            _arena,
            ComponentId.Input,
            key,
            options.EffectiveInitialValue,
            options.EffectivePlaceholder
        );
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.InputDisabled, options.Disabled ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputReadOnly, options.ReadOnly ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputPassword, options.Password ? 1u : 0u);
        return element;
    }

    /// <summary>
    /// Declares a retained native input from already-encoded UTF-8 data. The spans are copied
    /// directly into the render arena without intermediate strings or transcoding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<InputTag> Input(ReadOnlySpan<byte> utf8Key, Utf8InputOptions options = default)
    {
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("An input resource key cannot be empty.", nameof(utf8Key));
        }
        if (utf8Key.Contains((byte)0))
        {
            throw new ArgumentException(
                "An input resource key cannot contain NUL.",
                nameof(utf8Key)
            );
        }

        var element = ArenaWriter.AddCompositeNode<InputTag>(
            _arena,
            ComponentId.Input,
            utf8Key,
            options.InitialValue,
            options.Placeholder
        );
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.InputDisabled, options.Disabled ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputReadOnly, options.ReadOnly ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputPassword, options.Password ? 1u : 0u);
        return element;
    }
}

public static partial class ElementExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnChanged<TTag>(
        this Element<TTag> element,
        Callback<InputEvent> callback
    )
        where TTag : unmanaged, IInputElementTag
    {
        AddInputCallback(element.Inner, OpCode.InputOnChanged, callback);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnSubmitted<TTag>(
        this Element<TTag> element,
        Callback<InputEvent> callback
    )
        where TTag : unmanaged, IInputElementTag
    {
        AddInputCallback(element.Inner, OpCode.InputOnSubmitted, callback);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnFocusChanged<TTag>(
        this Element<TTag> element,
        Callback<InputEvent> callback
    )
        where TTag : unmanaged, IInputElementTag
    {
        AddInputCallback(element.Inner, OpCode.InputOnFocusChanged, callback);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddInputCallback(
        Element element,
        OpCode code,
        Callback<InputEvent> callback
    )
    {
        if (callback.IsDefault)
        {
            throw new ArgumentException(
                "A generated input callback is required.",
                nameof(callback)
            );
        }
        ArenaWriter.AddCallback(element, code, callback.Token);
    }
}
