using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

/// <summary>Direction in which a Dock split arranges its child containers.</summary>
public enum DockAxis : uint
{
    Horizontal = 0,
    Vertical = 1,
}

/// <summary>Edge occupied by a collapsible Dock region.</summary>
public enum DockSide : uint
{
    Left = 0,
    Bottom = 1,
    Right = 2,
}

/// <summary>Behavior of one retained native Dock area.</summary>
public readonly struct DockOptions
{
    public DockOptions(bool locked = false)
    {
        Locked = locked;
    }

    /// <summary>Prevents tab rearrangement while leaving split resizing available.</summary>
    public bool Locked { get; }
}

/// <summary>Behavior and presentation options for one Dock panel.</summary>
public readonly struct DockPanelOptions
{
    private readonly bool _initialized;

    public DockPanelOptions(
        bool closable = true,
        bool zoomable = true,
        bool innerPadding = false
    )
    {
        Closable = closable;
        Zoomable = zoomable;
        InnerPadding = innerPadding;
        _initialized = true;
    }

    public bool Closable { get; }
    public bool Zoomable { get; }
    public bool InnerPadding { get; }

    internal bool EffectiveClosable => !_initialized || Closable;
    internal bool EffectiveZoomable => !_initialized || Zoomable;
    internal bool EffectiveInnerPadding => _initialized && InnerPadding;
}

/// <summary>Initial behavior of a retained side Dock region.</summary>
public readonly struct DockRegionOptions
{
    private readonly bool _initialized;

    public DockRegionOptions(bool initiallyOpen = true, bool collapsible = true)
    {
        InitiallyOpen = initiallyOpen;
        Collapsible = collapsible;
        _initialized = true;
    }

    /// <summary>Seeds visibility when the region is first added to the retained Dock.</summary>
    public bool InitiallyOpen { get; }

    /// <summary>Allows the native Dock chrome to collapse and reopen the region.</summary>
    public bool Collapsible { get; }

    internal bool EffectiveInitiallyOpen => !_initialized || InitiallyOpen;
    internal bool EffectiveCollapsible => !_initialized || Collapsible;
}

public static partial class ElementExtensions
{
    /// <summary>
    /// Seeds this container's extent when it is a direct child of a Dock split, or the initial
    /// extent of a side Dock region. Native resizing owns the live size after creation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> InitialSize<TTag>(this Element<TTag> element, float pixels)
        where TTag : unmanaged, IDockContainerElementTag
    {
        if (!float.IsFinite(pixels) || pixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels));
        }

        ArenaWriter.AddF32(element.Inner, OpCode.DockInitialSizePx, pixels);
        return element;
    }
}
