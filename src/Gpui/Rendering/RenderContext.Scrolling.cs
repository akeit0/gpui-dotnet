using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a retained native scroll container. Wheel/trackpad scrolling stays entirely in
    /// Rust; the resource is scoped by the current managed View handle plus <paramref name="key"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollAxis axis = ScrollAxis.Vertical,
        params ReadOnlySpan<Element> children
    ) => Scroll(key, axis, default, children);

    /// <summary>
    /// Declares a vertical retained native scroll container with explicit scrolling behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollOptions options,
        params ReadOnlySpan<Element> children
    ) => Scroll(key, ScrollAxis.Vertical, options, children);

    /// <summary>
    /// Declares a retained native scroll container with explicit scrolling behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollAxis axis,
        ScrollOptions options,
        params ReadOnlySpan<Element> children
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A scroll resource key cannot be empty.", nameof(key));
        }
        if ((uint)axis > (uint)ScrollAxis.Both)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        var element = ArenaWriter.AddNode<ScrollTag>(_arena, ComponentId.Scroll, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        // Every scroll option has a native default, so options equal to their default are not
        // written at all: the common scroll container emits a single op.
        if (axis != ScrollAxis.Vertical)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.ScrollAxis, (uint)axis);
        }
        if (!options.EffectiveSmoothScrolling)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.SmoothScroll, 0);
        }
        if (!options.EffectiveShowScrollbar)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.ShowScrollbar, 0);
        }
        if (options.EffectiveScrollbarGutter)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.ScrollbarGutter, 1);
        }
        if (options.EffectiveScrollbarWidth != 8)
        {
            ArenaWriter.AddF32(
                element.Inner,
                OpCode.ScrollbarWidth,
                options.EffectiveScrollbarWidth
            );
        }
        ArenaWriter.AddChildren(element.Inner, children);
        return element;
    }
}
