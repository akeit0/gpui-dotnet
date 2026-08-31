using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Attaches managed menu content below a trigger. Left-click opens the menu; selection,
    /// outside pointer-down, or Escape dismisses it natively and restores prior focus.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<PopoverMenuTag> PopoverMenu(
        ReadOnlySpan<char> key,
        Element trigger,
        Element content,
        PopoverMenuOptions options = default
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A popover-menu key cannot be empty.", nameof(key));
        }

        var element = ArenaWriter.AddNode<PopoverMenuTag>(_arena, ComponentId.PopoverMenu, key);
        ConfigurePopoverMenu(element, trigger, content, options);
        return element;
    }

    /// <summary>Declares a popover menu with an already encoded UTF-8 key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<PopoverMenuTag> PopoverMenu(
        ReadOnlySpan<byte> utf8Key,
        Element trigger,
        Element content,
        PopoverMenuOptions options = default
    )
    {
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("A popover-menu key cannot be empty.", nameof(utf8Key));
        }

        var element = ArenaWriter.AddNode<PopoverMenuTag>(_arena, ComponentId.PopoverMenu, utf8Key);
        ConfigurePopoverMenu(element, trigger, content, options);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConfigurePopoverMenu(
        Element<PopoverMenuTag> element,
        Element trigger,
        Element content,
        PopoverMenuOptions options
    )
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.PopoverMenuPriority, options.EffectivePriority);
        ArenaWriter.AddF32(element.Inner, OpCode.PopoverMenuMarginPx, options.EffectiveMargin);
        ArenaWriter.AddChild(element.Inner, trigger);
        ArenaWriter.AddChild(element.Inner, content);
    }
}
