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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnClick, view.BindClick(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnClick, view.BindClick(callback), payload);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnClick<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ClickEvent, ValueTask> callback
    )
        where TTag : unmanaged, IInteractiveElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnClick, view.BindClick(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnClick<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ClickEvent, ValueTask> callback,
        ulong payload
    )
        where TTag : unmanaged, IInteractiveElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnClick, view.BindClick(callback), payload);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnClick<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ClickEvent, Task> callback
    )
        where TTag : unmanaged, IInteractiveElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnClick, view.BindClick(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnClick<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ClickEvent, Task> callback,
        ulong payload
    )
        where TTag : unmanaged, IInteractiveElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnClick, view.BindClick(callback), payload);
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
        ArenaWriter.AddCallback(element.Inner, OpCode.InputOnChanged, view.BindInput(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.InputOnSubmitted, view.BindInput(callback));
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
            view.BindInput(callback)
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
        ArenaWriter.AddCallback(element.Inner, OpCode.SliderOnChanged, view.BindSlider(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, SliderEvent, ValueTask> callback
    )
        where TTag : unmanaged, ISliderElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.SliderOnChanged, view.BindSlider(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, SliderEvent, Task> callback
    )
        where TTag : unmanaged, ISliderElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.SliderOnChanged, view.BindSlider(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.SliderOnReleased, view.BindSlider(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnReleased<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, SliderEvent, ValueTask> callback
    )
        where TTag : unmanaged, ISliderElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.SliderOnReleased, view.BindSlider(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnReleased<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, SliderEvent, Task> callback
    )
        where TTag : unmanaged, ISliderElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.SliderOnReleased, view.BindSlider(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.DockOnLayout, view.BindDock(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnDockLayoutChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, DockEvent, ValueTask> callback
    )
        where TTag : unmanaged, IDockAreaElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.DockOnLayout, view.BindDock(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnDockLayoutChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, DockEvent, Task> callback
    )
        where TTag : unmanaged, IDockAreaElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.DockOnLayout, view.BindDock(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.DockOnClosed, view.BindDock(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnDockPanelClosed<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, DockEvent, ValueTask> callback
    )
        where TTag : unmanaged, IDockAreaElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.DockOnClosed, view.BindDock(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnDockPanelClosed<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, DockEvent, Task> callback
    )
        where TTag : unmanaged, IDockAreaElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.DockOnClosed, view.BindDock(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OverlayOnDismiss, view.BindClick(callback));
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
            view.BindClick(callback),
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnKeyDown, view.BindKey(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnKeyDown<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, KeyEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnKeyDown, view.BindKey(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnKeyDown<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, KeyEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnKeyDown, view.BindKey(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnKeyUp, view.BindKey(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnKeyUp<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, KeyEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnKeyUp, view.BindKey(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnKeyUp<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, KeyEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnKeyUp, view.BindKey(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseDown, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseDown<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseDown, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseDown<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseDown, view.BindMouse(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseUp, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseUp<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseUp, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseUp<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseUp, view.BindMouse(callback));
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
            view.BindModifiers(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnModifiersChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ModifiersEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnModifiersChanged,
            view.BindModifiers(callback)
        );
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnModifiersChanged<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ModifiersEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(
            element.Inner,
            OpCode.OnModifiersChanged,
            view.BindModifiers(callback)
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnHover, view.BindHover(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnHover<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, HoverEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnHover, view.BindHover(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnHover<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, HoverEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnHover, view.BindHover(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseDownOut, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseDownOut<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseDownOut, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseDownOut<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseDownOut, view.BindMouse(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseUpOut, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseUpOut<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseUpOut, view.BindMouse(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseUpOut<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseUpOut, view.BindMouse(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseMove, view.BindMouseMove(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseMove<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseMoveEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseMove, view.BindMouseMove(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnMouseMove<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, MouseMoveEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnMouseMove, view.BindMouseMove(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnScrollWheel, view.BindScrollWheel(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnScrollWheel<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ScrollWheelEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnScrollWheel, view.BindScrollWheel(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnScrollWheel<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, ScrollWheelEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnScrollWheel, view.BindScrollWheel(callback));
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
        ArenaWriter.AddCallback(element.Inner, OpCode.OnFileDrop, view.BindFileDrop(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnFileDrop<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, FileDropEvent, ValueTask> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnFileDrop, view.BindFileDrop(callback));
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> OnFileDrop<TTag, TView>(
        this Element<TTag> element,
        TView view,
        Func<TView, FileDropEvent, Task> callback
    )
        where TTag : unmanaged, IKeyMouseElementTag
        where TView : ViewBase
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(callback);
        ArenaWriter.AddCallback(element.Inner, OpCode.OnFileDrop, view.BindFileDrop(callback));
        return element;
    }
}
