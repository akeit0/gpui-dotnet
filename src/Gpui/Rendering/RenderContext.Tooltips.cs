using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares window-owned content for a delayed List/Table tooltip request, outside row renderers.
    /// Timing and placement come from the collection's OnTooltipRequested options.
    /// Movement, eviction, dismissal, or declaration removal expires the native anchor.
    /// </summary>
    public Element<TooltipTag> RowTooltip(
        ReadOnlySpan<char> key, ListTooltipEvent request, Element content)
    {
        if (request.AnchorId == 0)
            throw new ArgumentException("A native row tooltip request is required.", nameof(request));
        if (key.IsEmpty)
            throw new ArgumentException("A tooltip key cannot be empty.", nameof(key));
        var element = ArenaWriter.AddNode<TooltipTag>(_arena, ComponentId.Tooltip, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU64(element.Inner, OpCode.TooltipRowAnchor, request.AnchorId);
        ArenaWriter.AddChild(element.Inner, Div());
        ArenaWriter.AddChild(element.Inner, content);
        return element;
    }

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
        options.WriteTo(element.Inner);
        ArenaWriter.AddChild(element.Inner, trigger);
        ArenaWriter.AddChild(element.Inner, content);
    }
}
