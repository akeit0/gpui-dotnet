using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a retained native Dock area. The center declaration seeds the initial layout and
    /// is reapplied only when its structure changes, so ordinary managed renders do not undo
    /// native tab moves or split resizing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockAreaTag> DockArea(
        ReadOnlySpan<char> key,
        Element center,
        DockOptions options = default
    ) => DockArea(key, center, ReadOnlySpan<Element>.Empty, options);

    /// <summary>
    /// Declares a retained native Dock area with optional left, bottom, or right regions.
    /// Each side may occur at most once.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockAreaTag> DockArea(
        ReadOnlySpan<char> key,
        Element center,
        ReadOnlySpan<Element> regions,
        DockOptions options = default
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A Dock area key cannot be empty.", nameof(key));
        }
        ResourceKeys.ValidateExplicitChars(key, nameof(key));

        var element = ArenaWriter.AddNode<DockAreaTag>(_arena, ComponentId.DockArea, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        if (options.Locked)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockLocked, 1);
        }
        ArenaWriter.AddChild(element.Inner, center);
        ArenaWriter.AddChildren(element.Inner, regions);
        return element;
    }

    /// <summary>
    /// Declares a retained native Dock area bound to a controller sharing the area key.
    /// An unbound controller is bound to <paramref name="key"/> on first use.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockAreaTag> DockArea(
        ref DockController controller,
        ReadOnlySpan<char> key,
        Element center,
        DockOptions options = default
    ) => DockArea(ref controller, key, center, ReadOnlySpan<Element>.Empty, options);

    /// <summary>
    /// Declares a retained native Dock area with optional regions, bound to a controller
    /// sharing the area key. An unbound controller is bound to <paramref name="key"/> on
    /// first use.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockAreaTag> DockArea(
        ref DockController controller,
        ReadOnlySpan<char> key,
        Element center,
        ReadOnlySpan<Element> regions,
        DockOptions options = default
    )
    {
        if (controller.IsBound && !KeyEquals(controller.Utf8KeySpan, key))
        {
            throw new ArgumentException(
                "A Dock controller is already bound to a different area key.",
                nameof(controller)
            );
        }
        if (!controller.IsBound)
        {
            var byteCount = System.Text.Encoding.UTF8.GetByteCount(key);
            var utf8Key = new byte[byteCount];
            System.Text.Encoding.UTF8.GetBytes(key, utf8Key);
            controller = new DockController(OwnerView, utf8Key);
        }
        return DockArea(key, center, regions, options);
    }

    private static bool KeyEquals(ReadOnlySpan<byte> utf8Key, ReadOnlySpan<char> key)
    {
        if (key.IsEmpty)
        {
            return false;
        }
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(key);
        if (byteCount != utf8Key.Length)
        {
            return false;
        }
        Span<byte> encoded = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
        System.Text.Encoding.UTF8.GetBytes(key, encoded);
        return utf8Key.SequenceEqual(encoded);
    }

    /// <summary>Declares a collapsible layout region on one edge of a Dock area.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockRegionTag> DockRegion(
        DockSide side,
        Element content,
        DockRegionOptions options = default
    )
    {
        if ((uint)side > (uint)DockSide.Right)
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        var element = ArenaWriter.AddNode<DockRegionTag>(_arena, ComponentId.DockRegion);
        if (side != DockSide.Left)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockRegionSide, (uint)side);
        }
        if (!options.EffectiveInitiallyOpen)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockRegionOpen, 0);
        }
        if (!options.EffectiveCollapsible)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockRegionCollapsible, 0);
        }
        ArenaWriter.AddChild(element.Inner, content);
        return element;
    }

    /// <summary>Declares a horizontal or vertical container in a Dock layout.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockSplitTag> DockSplit(
        DockAxis axis,
        params ReadOnlySpan<Element> children
    )
    {
        if ((uint)axis > (uint)DockAxis.Vertical)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }
        if (children.IsEmpty)
        {
            throw new ArgumentException("A Dock split requires at least one child.", nameof(children));
        }

        var element = ArenaWriter.AddNode<DockSplitTag>(_arena, ComponentId.DockSplit);
        if (axis != DockAxis.Horizontal)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockAxis, (uint)axis);
        }
        ArenaWriter.AddChildren(element.Inner, children);
        return element;
    }

    /// <summary>Declares a tab group in a Dock layout.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockTabsTag> DockTabs(
        int activeIndex = 0,
        params ReadOnlySpan<Element> panels
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeIndex);
        if (panels.IsEmpty)
        {
            throw new ArgumentException("Dock tabs require at least one panel.", nameof(panels));
        }
        if (activeIndex >= panels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(activeIndex));
        }

        var element = ArenaWriter.AddNode<DockTabsTag>(_arena, ComponentId.DockTabs);
        if (activeIndex != 0)
        {
            ArenaWriter.AddU32(
                element.Inner,
                OpCode.DockActiveIndex,
                checked((uint)activeIndex)
            );
        }
        ArenaWriter.AddChildren(element.Inner, panels);
        return element;
    }

    /// <summary>
    /// Declares stable Dock panel content. The content may be an ordinary element tree or a
    /// framework-owned managed child View.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DockPanelTag> DockPanel(
        ReadOnlySpan<char> id,
        ReadOnlySpan<char> title,
        Element content,
        DockPanelOptions options = default
    )
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A Dock panel ID cannot be empty.", nameof(id));
        }
        if (id.Contains('\0') || title.Contains('\0'))
        {
            throw new ArgumentException("Dock panel IDs and titles cannot contain NUL characters.");
        }

        var element = ArenaWriter.AddCompositeNode<DockPanelTag>(
            _arena,
            ComponentId.DockPanel,
            id,
            title,
            ReadOnlySpan<char>.Empty
        );
        if (!options.EffectiveClosable)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockPanelClosable, 0);
        }
        if (!options.EffectiveZoomable)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockPanelZoomable, 0);
        }
        if (options.EffectiveInnerPadding)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.DockPanelInnerPadding, 1);
        }
        ArenaWriter.AddChild(element.Inner, content);
        return element;
    }
}
