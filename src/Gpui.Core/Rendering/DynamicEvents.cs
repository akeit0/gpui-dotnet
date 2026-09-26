using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

/// <summary>
/// View-bound event helpers. Click callbacks always receive the native <see cref="ClickEvent"/>
/// so payload, pointer coordinates, buttons, and modifiers remain available at the call site.
/// The delegate is retained by the target View and the render IR stores only a compact token.
/// </summary>
public static partial class ElementExtensions
{
    /// <summary>
    /// Requests tooltip content after hovering a marked item element (500 ms by default).
    /// Options configure the complete native tooltip behavior; ItemTooltip supplies only its content.
    /// Use ItemTooltipTarget in items and keep ListDataSource.ContentRevision stable while opening.
    /// </summary>
    public static Element<TTag> OnTooltipRequested<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ListTooltipEvent> callback,
        TooltipOptions options = default
    )
        where TTag : unmanaged, IVirtualizedElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.ListOnTooltipRequested,
            view.Runtime.Events.BindListTooltip(callback)
        );
        options.WriteTo(element.Inner);
        return element;
    }

    /// <summary>Marks an element inside a virtual item as the hover target for its collection's tooltip.</summary>
    public static Element<TTag> ItemTooltipTarget<TTag>(
        this Element<TTag> element,
        bool enabled = true
    )
        where TTag : unmanaged, IStyledElementTag
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ItemTooltipTarget, enabled ? 1u : 0u);
        return element;
    }

    /// <summary>
    /// Requests a window-owned context menu on right-click of an item with ItemId.
    /// Keep ListDataSource.ContentRevision stable while rendering the requested menu.
    /// </summary>
    public static Element<TTag> OnContextMenuRequested<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ListContextMenuEvent> callback
    )
        where TTag : unmanaged, IVirtualizedElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.ListOnContextMenuRequested,
            view.Runtime.Events.BindListContextMenu(callback)
        );
        return element;
    }

    /// <summary>
    /// Requests single-item selection on unmodified Space or an unconsumed primary single press.
    /// The application owns selection state and item styling; navigation alone does not select.
    /// </summary>
    public static Element<TTag> OnSelectionRequested<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ListSelectionEvent> callback
    )
        where TTag : unmanaged, IVirtualizedElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.ListOnSelectionRequested,
            view.Runtime.Events.BindListSelection(callback)
        );
        return element;
    }

    /// <summary>
    /// Activates a List/Table item on unmodified Enter or an unconsumed primary double press.
    /// Does not change selection or synthesize item/child click events.
    /// </summary>
    public static Element<TTag> OnActivated<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ListActivationEvent> callback
    )
        where TTag : unmanaged, IVirtualizedElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.ListOnActivated,
            view.Runtime.Events.BindListActivation(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnClick<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ClickEvent> callback
    )
        where TTag : unmanaged, IInteractiveElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnClick,
            view.Runtime.Events.BindClick(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnClick<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ClickEvent> callback,
        ulong payload
    )
        where TTag : unmanaged, IInteractiveElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnClick,
            view.Runtime.Events.BindClick(callback),
            payload
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, InputEvent> callback
    )
        where TTag : unmanaged, IInputElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.InputOnChanged,
            view.Runtime.Events.BindInput(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnSubmitted<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, InputEvent> callback
    )
        where TTag : unmanaged, IInputElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.InputOnSubmitted,
            view.Runtime.Events.BindInput(callback)
        );
        return element;
    }

    /// <summary>Observes opt-in conditional-write decisions while this binding remains live.</summary>
    public static Element<TTag> OnWriteCompleted<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, InputWriteResult> callback
    )
        where TTag : unmanaged, IInputElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.InputOnWriteCompleted,
            view.Runtime.Events.BindInputWrite(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnFocusChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, InputEvent> callback
    )
        where TTag : unmanaged, IInputElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.InputOnFocusChanged,
            view.Runtime.Events.BindInput(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, SliderEvent> callback
    )
        where TTag : unmanaged, ISliderElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.SliderOnChanged,
            view.Runtime.Events.BindSlider(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnReleased<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, SliderEvent> callback
    )
        where TTag : unmanaged, ISliderElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.SliderOnReleased,
            view.Runtime.Events.BindSlider(callback)
        );
        return element;
    }

    /// <summary>
    /// Binds coarse Dock layout notifications (structural changes and requested exports) on a
    /// Dock area. The event is coarse by design: debounce with <see cref="DockEvent.Revision"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnDockLayoutChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, DockEvent> callback
    )
        where TTag : unmanaged, IDockAreaElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.DockOnLayout,
            view.Runtime.Events.BindDock(callback)
        );
        return element;
    }

    /// <summary>
    /// Binds native panel-close notifications on a Dock area. Panels removed by declaration or
    /// pruned by layout import do not fire this event; only native closes (chrome or
    /// <see cref="DockController.ClosePanel"/>) do. The closed panel stays closed until the
    /// declaration drops its id.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnDockPanelClosed<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, DockEvent> callback
    )
        where TTag : unmanaged, IDockAreaElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.DockOnClosed,
            view.Runtime.Events.BindDock(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<OverlayTag> OnDismiss<TView>(
        this Element<OverlayTag> element,
        TView view,
        Action<TView, ClickEvent> callback
    )
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OverlayOnDismiss,
            view.Runtime.Events.BindClick(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<OverlayTag> OnDismiss<TView>(
        this Element<OverlayTag> element,
        TView view,
        Action<TView, ClickEvent> callback,
        ulong payload
    )
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OverlayOnDismiss,
            view.Runtime.Events.BindClick(callback),
            payload
        );
        return element;
    }

    /// <summary>
    /// Observes key presses bubbling through this element without consuming them.
    /// Attach to the root container for window-wide hot keys; focused controls that
    /// handle the key stop propagation first, so text input keeps working.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnKeyDown<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, KeyEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnKeyDown,
            view.Runtime.Events.BindKey(callback)
        );
        return element;
    }

    /// <summary>Observes key releases bubbling through this element without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnKeyUp<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, KeyEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnKeyUp,
            view.Runtime.Events.BindKey(callback)
        );
        return element;
    }

    /// <summary>Observes mouse presses over this element without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseDown<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, MouseEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnMouseDown,
            view.Runtime.Events.BindMouse(callback)
        );
        return element;
    }

    /// <summary>Observes mouse releases over this element without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseUp<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, MouseEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnMouseUp,
            view.Runtime.Events.BindMouse(callback)
        );
        return element;
    }

    /// <summary>
    /// Observes modifier-key changes bubbling through this element without consuming them.
    /// This is the only event for modifier-only presses (e.g. holding Ctrl alone), which never
    /// produce key down/up events in GPUI. Attach to the root container for window-wide tracking.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnModifiersChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ModifiersEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnModifiersChanged,
            view.Runtime.Events.BindModifiers(callback)
        );
        return element;
    }

    /// <summary>
    /// Observes hover enter/exit transitions over this element without consuming them.
    /// Fires on transitions only, never per mouse move.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnHover<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, HoverEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnHover,
            view.Runtime.Events.BindHover(callback)
        );
        return element;
    }

    /// <summary>Observes mouse presses outside this element without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseDownOut<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, MouseEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnMouseDownOut,
            view.Runtime.Events.BindMouse(callback)
        );
        return element;
    }

    /// <summary>Observes mouse releases outside this element without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseUpOut<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, MouseEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnMouseUpOut,
            view.Runtime.Events.BindMouse(callback)
        );
        return element;
    }

    /// <summary>
    /// Observes mouse movement over this element without consuming it.
    /// Only published while bound; keep handlers cheap because this fires at pointer frequency.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseMove<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, MouseMoveEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnMouseMove,
            view.Runtime.Events.BindMouseMove(callback)
        );
        return element;
    }

    /// <summary>
    /// Observes scroll-wheel movement over this element without consuming it.
    /// Only published while bound; keep handlers cheap. This does not replace
    /// retained Scroll resources, which keep owning wheel/trackpad deltas natively.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnScrollWheel<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, ScrollWheelEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnScrollWheel,
            view.Runtime.Events.BindScrollWheel(callback)
        );
        return element;
    }

    /// <summary>
    /// Observes OS files dropped onto this element without consuming the drop.
    /// GPUI translates the platform drop into its internal drag system, so this fires on the
    /// element under the cursor. Only published while bound.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnFileDrop<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Action<TView, FileDropEvent> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnFileDrop,
            view.Runtime.Events.BindFileDrop(callback)
        );
        return element;
    }
}
