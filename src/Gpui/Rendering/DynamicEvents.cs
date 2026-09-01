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
}
