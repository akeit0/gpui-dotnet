using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a retained native input bound to <paramref name="controller"/>. The first render
    /// assigns the controller a per-view auto id; later renders reuse the retained id, so the
    /// binding is idempotent and the resource keeps its native state across renders. Text
    /// editing, selection, focus, clipboard, and IME composition remain native. InitialValue is
    /// consumed only when the resource is first created; use the controller for later
    /// programmatic value changes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<InputTag> Input(ref InputController controller, InputOptions options = default)
    {
        BindAutoController(ref controller);
        var element = ArenaWriter.AddCompositeNode<InputTag>(
            _arena,
            ComponentId.Input,
            controller.Utf8KeySpan,
            options.EffectiveInitialValue,
            options.EffectivePlaceholder
        );
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.InputDisabled, options.Disabled ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputReadOnly, options.ReadOnly ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputPassword, options.Password ? 1u : 0u);
        return element;
    }

    /// <summary>UTF-8 variant of <see cref="Input(ref InputController, InputOptions)"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<InputTag> Input(
        ref InputController controller,
        Utf8InputOptions options = default
    )
    {
        BindAutoController(ref controller);
        var element = ArenaWriter.AddCompositeNode<InputTag>(
            _arena,
            ComponentId.Input,
            controller.Utf8KeySpan,
            options.InitialValue,
            options.Placeholder
        );
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.InputDisabled, options.Disabled ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputReadOnly, options.ReadOnly ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.InputPassword, options.Password ? 1u : 0u);
        return element;
    }

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
        ResourceKeys.ValidateExplicitChars(key, nameof(key));

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
        ResourceKeys.ValidateExplicitBytes(utf8Key, nameof(utf8Key));

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
