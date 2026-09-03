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

/// <summary>One coarse event from a retained native Dock area.</summary>
public readonly struct DockEvent
{
    internal DockEvent(DockEventKind kind, string panelId, string layoutJson, ulong revision)
    {
        Kind = kind;
        PanelId = panelId;
        LayoutJson = layoutJson;
        Revision = revision;
    }

    public DockEventKind Kind { get; }

    /// <summary>Closed panel id for <see cref="DockEventKind.PanelClosed"/>; empty otherwise.</summary>
    public string PanelId { get; }

    /// <summary>Exported layout JSON for <see cref="DockEventKind.LayoutExported"/>; empty otherwise.</summary>
    public string LayoutJson { get; }

    /// <summary>Per-area monotonic sequence shared by every Dock event kind.</summary>
    public ulong Revision { get; }
}

/// <summary>
/// Optional imperative handle for a Dock area declared by the same View with ui.DockArea().
/// The controller shares the area key; commands queue until the next committed snapshot
/// materializes the resource. Tab activation stays declarative via DockTabs activeIndex:
/// the foundation offers no node-stable activation handle without a fork-side API.
/// </summary>
[System.Diagnostics.DebuggerDisplay("{DebuggerView,nq}")]
public readonly struct DockController
{
    private readonly ViewBase? _owner;
    private readonly byte[]? _utf8Key;

    internal DockController(ViewBase owner, string key)
    {
        _owner = owner;
        _utf8Key = System.Text.Encoding.UTF8.GetBytes(key);
    }

    /// <summary>Internal constructor that takes ownership of an already-encoded key array.</summary>
    internal DockController(ViewBase owner, byte[] utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key;
    }

    /// <summary>True once this controller has been bound to a resource.</summary>
    public bool IsBound => _utf8Key is not null;

    internal ReadOnlySpan<byte> Utf8KeySpan => _utf8Key;

    public bool IsDefault => _owner is null;

    private string DebuggerView
    {
        get
        {
            if (_utf8Key is null)
            {
                return "unbound";
            }
            return ResourceKeys.TryDecodeAutoKey(_utf8Key, out var id) ? $"auto:{id}" : "explicit";
        }
    }

    /// <summary>
    /// Removes a panel natively, as if closed through the tab chrome. Fires
    /// <see cref="DockEventKind.PanelClosed"/>; the panel stays closed until the declaration
    /// drops its id.
    /// </summary>
    public void ClosePanel(string panelId)
    {
        ValidatePanelId(panelId);
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Dock,
                ResourceCommandKind.DockClosePanel,
                null,
                0,
                0,
                Data: panelId,
                Utf8Key: Utf8KeyArray
            )
        );
        Owner.InvalidateFromController();
    }

    /// <summary>Opens or collapses a side region natively without changing the declaration.</summary>
    public void SetRegionOpen(DockSide side, bool open)
    {
        if ((uint)side > (uint)DockSide.Right)
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Dock,
                ResourceCommandKind.DockSetRegionOpen,
                null,
                (uint)side,
                open ? 1u : 0u,
                Utf8Key: Utf8KeyArray
            )
        );
        Owner.InvalidateFromController();
    }

    /// <summary>
    /// Replaces native layout structure from a previously exported layout document. Structure
    /// (splits, sizes, active tabs, region placement and open state) comes from the document;
    /// panel content, titles, and options come from the live declaration. Persisted panels
    /// unknown to the declaration are pruned; declared panels missing from the document are
    /// appended to the center. Lock state always comes from the declaration. Only documents
    /// produced by <see cref="ExportLayout"/> (the versioned GPUI.NET envelope) are accepted;
    /// anything else is consumed without effect.
    /// </summary>
    public void ImportLayout(string layoutJson)
    {
        ArgumentNullException.ThrowIfNull(layoutJson);
        if (layoutJson.Length == 0)
        {
            throw new ArgumentException("A Dock layout document cannot be empty.", nameof(layoutJson));
        }
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Dock,
                ResourceCommandKind.DockImportLayout,
                null,
                0,
                0,
                Data: layoutJson,
                Utf8Key: Utf8KeyArray
            )
        );
        Owner.InvalidateFromController();
    }

    /// <summary>
    /// Requests the authoritative native layout as JSON through the area's
    /// <c>OnDockLayoutChanged</c> binding as a <see cref="DockEventKind.LayoutExported"/>
    /// event. The document is a versioned GPUI.NET envelope wrapping the native layout;
    /// only such documents are accepted back by <see cref="ImportLayout"/>. Without that
    /// binding the export has nowhere to go and is dropped.
    /// </summary>
    public void ExportLayout()
    {
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Dock,
                ResourceCommandKind.DockExportLayout,
                null,
                0,
                0,
                Utf8Key: Utf8KeyArray
            )
        );
        Owner.InvalidateFromController();
    }

    private static void ValidatePanelId(string panelId)
    {
        ArgumentNullException.ThrowIfNull(panelId);
        if (panelId.Length == 0)
        {
            throw new ArgumentException("A Dock panel id cannot be empty.", nameof(panelId));
        }
        if (panelId.Contains('\0'))
        {
            throw new ArgumentException("A Dock panel id cannot contain NUL.", nameof(panelId));
        }
    }

    private ViewBase Owner =>
        _owner ?? throw new InvalidOperationException("Default DockController cannot be used.");
    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default DockController cannot be used.");
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
