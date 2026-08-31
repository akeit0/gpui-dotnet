using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a retained native slider bound to <paramref name="controller"/>. The controller
    /// receives a stable per-View key on its first render; native value state survives subsequent
    /// renders and the initial value is consumed only when the resource is first created.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<SliderTag> Slider(
        ref SliderController controller,
        SliderOptions options = default
    )
    {
        BindAutoController(ref controller);
        return SliderCore(controller.Utf8KeySpan, options);
    }

    /// <summary>Declares a retained native slider with an explicit UTF-16 resource key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<SliderTag> Slider(ReadOnlySpan<char> key, SliderOptions options = default)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A slider resource key cannot be empty.", nameof(key));
        }
        if (key.Contains('\0'))
        {
            throw new ArgumentException("A slider resource key cannot contain NUL.", nameof(key));
        }
        ResourceKeys.ValidateExplicitChars(key, nameof(key));
        return SliderCore(key, options);
    }

    /// <summary>Declares a retained native slider with an explicit UTF-8 resource key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<SliderTag> Slider(ReadOnlySpan<byte> utf8Key, SliderOptions options = default)
    {
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("A slider resource key cannot be empty.", nameof(utf8Key));
        }
        if (utf8Key.Contains((byte)0))
        {
            throw new ArgumentException(
                "A slider resource key cannot contain NUL.",
                nameof(utf8Key)
            );
        }
        ResourceKeys.ValidateExplicitBytes(utf8Key, nameof(utf8Key));
        return SliderCore(utf8Key, options);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<SliderTag> SliderCore(ReadOnlySpan<char> key, SliderOptions options)
    {
        var element = ArenaWriter.AddNode<SliderTag>(_arena, ComponentId.Slider, key);
        return ConfigureSlider(element, options);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<SliderTag> SliderCore(ReadOnlySpan<byte> key, SliderOptions options)
    {
        var element = ArenaWriter.AddNode<SliderTag>(_arena, ComponentId.Slider, key);
        return ConfigureSlider(element, options);
    }

    private Element<SliderTag> ConfigureSlider(Element<SliderTag> element, SliderOptions options)
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        if (options.EffectiveMin != 0)
        {
            ArenaWriter.AddF32(element.Inner, OpCode.SliderMin, options.EffectiveMin);
        }
        if (options.EffectiveMax != 100)
        {
            ArenaWriter.AddF32(element.Inner, OpCode.SliderMax, options.EffectiveMax);
        }
        if (options.EffectiveStep != 1)
        {
            ArenaWriter.AddF32(element.Inner, OpCode.SliderStep, options.EffectiveStep);
        }
        if (options.EffectiveAxis != SliderAxis.Horizontal)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.SliderAxis, (uint)options.EffectiveAxis);
        }
        if (options.EffectiveDisabled)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.SliderDisabled, 1);
        }
        if (options.EffectiveScale != SliderScale.Linear)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.SliderScale, (uint)options.EffectiveScale);
        }
        if (options.HasInitialValue)
        {
            var value = options.InitialValue;
            if (value.IsRange)
            {
                ArenaWriter.AddF32(element.Inner, OpCode.SliderRangeStart, value.Start);
                ArenaWriter.AddF32(element.Inner, OpCode.SliderRangeEnd, value.End);
            }
            else
            {
                ArenaWriter.AddF32(element.Inner, OpCode.SliderValue, value.End);
            }
        }
        return element;
    }
}
