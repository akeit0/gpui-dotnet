using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Wraps a trigger with native right-click handling and paints managed menu content in a
    /// pointer-anchored deferred layer. Outside click, Escape, and menu selection dismiss natively.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ContextMenuTag> ContextMenu(
        ReadOnlySpan<char> key,
        Element trigger,
        Element content,
        ContextMenuOptions options = default
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A context-menu key cannot be empty.", nameof(key));
        }

        var element = ArenaWriter.AddNode<ContextMenuTag>(_arena, ComponentId.ContextMenu, key);
        ConfigureContextMenu(element, trigger, content, options);
        return element;
    }

    /// <summary>Declares a context menu with an already encoded UTF-8 key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ContextMenuTag> ContextMenu(
        ReadOnlySpan<byte> utf8Key,
        Element trigger,
        Element content,
        ContextMenuOptions options = default
    )
    {
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("A context-menu key cannot be empty.", nameof(utf8Key));
        }

        var element = ArenaWriter.AddNode<ContextMenuTag>(_arena, ComponentId.ContextMenu, utf8Key);
        ConfigureContextMenu(element, trigger, content, options);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConfigureContextMenu(
        Element<ContextMenuTag> element,
        Element trigger,
        Element content,
        ContextMenuOptions options
    )
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ContextMenuPriority, options.EffectivePriority);
        ArenaWriter.AddF32(element.Inner, OpCode.ContextMenuMarginPx, options.EffectiveMargin);
        ArenaWriter.AddChild(element.Inner, trigger);
        ArenaWriter.AddChild(element.Inner, content);
    }
}
