using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares native hover behavior around a trigger and paints the content in a deferred,
    /// viewport-aware layer. Tooltip content can be any semantic element.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TooltipTag> Tooltip(
        ReadOnlySpan<char> key,
        Element trigger,
        Element content,
        TooltipOptions options = default
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A tooltip key cannot be empty.", nameof(key));
        }

        var element = ArenaWriter.AddNode<TooltipTag>(_arena, ComponentId.Tooltip, key);
        ConfigureTooltip(element, trigger, content, options);
        return element;
    }

    /// <summary>Declares a tooltip with an already encoded UTF-8 key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TooltipTag> Tooltip(
        ReadOnlySpan<byte> utf8Key,
        Element trigger,
        Element content,
        TooltipOptions options = default
    )
    {
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("A tooltip key cannot be empty.", nameof(utf8Key));
        }

        var element = ArenaWriter.AddNode<TooltipTag>(_arena, ComponentId.Tooltip, utf8Key);
        ConfigureTooltip(element, trigger, content, options);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConfigureTooltip(
        Element<TooltipTag> element,
        Element trigger,
        Element content,
        TooltipOptions options
    )
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.TooltipPlacement,
            (uint)options.EffectivePlacement
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.TooltipAlignment,
            (uint)options.EffectiveAlignment
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.TooltipShowDelayMs,
            options.EffectiveShowDelayMilliseconds
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.TooltipHideDelayMs,
            options.EffectiveHideDelayMilliseconds
        );
        ArenaWriter.AddF32(element.Inner, OpCode.TooltipGapPx, options.EffectiveGap);
        ArenaWriter.AddF32(element.Inner, OpCode.TooltipMarginPx, options.EffectiveMargin);
        ArenaWriter.AddChild(element.Inner, trigger);
        ArenaWriter.AddChild(element.Inner, content);
    }
}
