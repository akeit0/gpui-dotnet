namespace Gpui;

internal enum ViewEventBindingScope
{
    None,
    Render,
    ListRange,
}

public abstract partial class ViewBase
{
    private const uint DynamicEventBit = 0x8000_0000u;
    private const uint DynamicEventEntryMask = 0x7FFF_FFFFu;

    [ThreadStatic]
    private static ViewBase? _currentEventBindingOwner;

    internal static ViewBase? CurrentEventBindingOwner
    {
        get => _currentEventBindingOwner;
        set => _currentEventBindingOwner = value;
    }

    private delegate ValueTask EventBinder(
        object target,
        Delegate callback,
        in EventDispatch dispatch
    );

    private enum EventDispatchKind : byte
    {
        Click,
        Input,
        Slider,
        Dock,
        Key,
        Mouse,
        Modifiers,
        Hover,
        MouseMove,
        ScrollWheel,
        FileDrop,
        NativeExtension,
    }

    private readonly struct EventDispatch
    {
        internal EventDispatch(ClickEvent click)
        {
            Kind = EventDispatchKind.Click;
            Click = click;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            NativeExtension = null;
        }

        internal EventDispatch(InputEvent input)
        {
            Kind = EventDispatchKind.Input;
            Click = default;
            Input = input;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            NativeExtension = null;
        }

        internal EventDispatch(SliderEvent slider)
        {
            Kind = EventDispatchKind.Slider;
            Click = default;
            Input = null;
            Slider = slider;
            Dock = default;
            Key = null;
            Mouse = default;
            NativeExtension = null;
        }

        internal EventDispatch(DockEvent dock)
        {
            Kind = EventDispatchKind.Dock;
            Click = default;
            Input = null;
            Slider = default;
            Dock = dock;
            Key = null;
            Mouse = default;
            NativeExtension = null;
        }

        internal EventDispatch(KeyEvent key)
        {
            Kind = EventDispatchKind.Key;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = key;
            Mouse = default;
            NativeExtension = null;
        }

        internal EventDispatch(MouseEvent mouse)
        {
            Kind = EventDispatchKind.Mouse;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = mouse;
            Modifiers = default;
            NativeExtension = null;
        }

        internal EventDispatch(ModifiersEvent modifiers)
        {
            Kind = EventDispatchKind.Modifiers;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            Modifiers = modifiers;
            Hover = default;
            MouseMove = default;
            ScrollWheel = default;
            NativeExtension = null;
        }

        internal EventDispatch(HoverEvent hover)
        {
            Kind = EventDispatchKind.Hover;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            Modifiers = default;
            Hover = hover;
            MouseMove = default;
            ScrollWheel = default;
            NativeExtension = null;
        }

        internal EventDispatch(MouseMoveEvent mouseMove)
        {
            Kind = EventDispatchKind.MouseMove;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            Modifiers = default;
            Hover = default;
            MouseMove = mouseMove;
            ScrollWheel = default;
            NativeExtension = null;
        }

        internal EventDispatch(ScrollWheelEvent scrollWheel)
        {
            Kind = EventDispatchKind.ScrollWheel;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            Modifiers = default;
            Hover = default;
            MouseMove = default;
            ScrollWheel = scrollWheel;
            FileDrop = null;
            NativeExtension = null;
        }

        internal EventDispatch(FileDropEvent fileDrop)
        {
            Kind = EventDispatchKind.FileDrop;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            Modifiers = default;
            Hover = default;
            MouseMove = default;
            ScrollWheel = default;
            FileDrop = fileDrop;
            NativeExtension = null;
        }

        internal EventDispatch(NativeExtensionEvent nativeExtension)
        {
            Kind = EventDispatchKind.NativeExtension;
            Click = default;
            Input = null;
            Slider = default;
            Dock = default;
            Key = null;
            Mouse = default;
            Modifiers = default;
            NativeExtension = nativeExtension;
        }

        internal EventDispatchKind Kind { get; }
        internal ClickEvent Click { get; }
        internal InputEvent? Input { get; }
        internal SliderEvent Slider { get; }
        internal DockEvent Dock { get; }
        internal KeyEvent? Key { get; }
        internal MouseEvent Mouse { get; }
        internal ModifiersEvent Modifiers { get; }
        internal HoverEvent Hover { get; }
        internal MouseMoveEvent MouseMove { get; }
        internal ScrollWheelEvent ScrollWheel { get; }
        internal FileDropEvent? FileDrop { get; }
        internal NativeExtensionEvent? NativeExtension { get; }
    }

    private struct EventEntry
    {
        internal object? Target;
        internal Delegate? Callback;
        internal int BinderIndex;
        internal int LastPass;
    }

    /// <summary>
    /// Registers a typed click callback on this mounted View. Equivalent bindings reuse their
    /// recyclable per-View entry, so the native token remains compact and stable across renders.
    /// </summary>
    internal ulong BindClick<TView>(Action<TView, ClickEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, ClickEventBinder<TView>.Index);

    internal ulong BindClick<TView>(Func<TView, ClickEvent, ValueTask> callback)
        where TView : ViewBase =>
        BindDynamicEvent(this, callback, ClickEventAsyncBinder<TView>.Index);

    internal ulong BindClick<TView>(Func<TView, ClickEvent, Task> callback)
        where TView : ViewBase =>
        BindDynamicEvent(this, callback, ClickEventTaskBinder<TView>.Index);

    /// <summary>Registers a typed input callback on this mounted View.</summary>
    internal ulong BindInput<TView>(Action<TView, InputEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, InputBinder<TView>.Index);

    internal ulong BindInput<TView>(Func<TView, InputEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, InputAsyncBinder<TView>.Index);

    internal ulong BindInput<TView>(Func<TView, InputEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, InputTaskBinder<TView>.Index);

    internal ulong BindSlider<TView>(Action<TView, SliderEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, SliderBinder<TView>.Index);

    internal ulong BindSlider<TView>(Func<TView, SliderEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, SliderAsyncBinder<TView>.Index);

    internal ulong BindSlider<TView>(Func<TView, SliderEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, SliderTaskBinder<TView>.Index);

    /// <summary>Registers a typed Dock area callback on this mounted View.</summary>
    internal ulong BindDock<TView>(Action<TView, DockEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, DockBinder<TView>.Index);

    internal ulong BindDock<TView>(Func<TView, DockEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, DockAsyncBinder<TView>.Index);

    internal ulong BindDock<TView>(Func<TView, DockEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, DockTaskBinder<TView>.Index);

    /// <summary>Registers a typed key-event callback on this mounted View.</summary>
    internal ulong BindKey<TView>(Action<TView, KeyEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, KeyBinder<TView>.Index);

    internal ulong BindKey<TView>(Func<TView, KeyEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, KeyAsyncBinder<TView>.Index);

    internal ulong BindKey<TView>(Func<TView, KeyEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, KeyTaskBinder<TView>.Index);

    /// <summary>Registers a typed mouse-event callback on this mounted View.</summary>
    internal ulong BindMouse<TView>(Action<TView, MouseEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, MouseBinder<TView>.Index);

    internal ulong BindMouse<TView>(Func<TView, MouseEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, MouseAsyncBinder<TView>.Index);

    internal ulong BindMouse<TView>(Func<TView, MouseEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, MouseTaskBinder<TView>.Index);

    /// <summary>Registers a typed modifier-key callback on this mounted View.</summary>
    internal ulong BindModifiers<TView>(Action<TView, ModifiersEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, ModifiersBinder<TView>.Index);

    internal ulong BindModifiers<TView>(Func<TView, ModifiersEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, ModifiersAsyncBinder<TView>.Index);

    internal ulong BindModifiers<TView>(Func<TView, ModifiersEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, ModifiersTaskBinder<TView>.Index);

    /// <summary>Registers a typed hover-state callback on this mounted View.</summary>
    internal ulong BindHover<TView>(Action<TView, HoverEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, HoverBinder<TView>.Index);

    internal ulong BindHover<TView>(Func<TView, HoverEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, HoverAsyncBinder<TView>.Index);

    internal ulong BindHover<TView>(Func<TView, HoverEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, HoverTaskBinder<TView>.Index);

    /// <summary>Registers a typed mouse-move callback on this mounted View.</summary>
    internal ulong BindMouseMove<TView>(Action<TView, MouseMoveEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, MouseMoveBinder<TView>.Index);

    internal ulong BindMouseMove<TView>(Func<TView, MouseMoveEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, MouseMoveAsyncBinder<TView>.Index);

    internal ulong BindMouseMove<TView>(Func<TView, MouseMoveEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, MouseMoveTaskBinder<TView>.Index);

    /// <summary>Registers a typed scroll-wheel callback on this mounted View.</summary>
    internal ulong BindScrollWheel<TView>(Action<TView, ScrollWheelEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, ScrollWheelBinder<TView>.Index);

    internal ulong BindScrollWheel<TView>(Func<TView, ScrollWheelEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, ScrollWheelAsyncBinder<TView>.Index);

    internal ulong BindScrollWheel<TView>(Func<TView, ScrollWheelEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, ScrollWheelTaskBinder<TView>.Index);

    /// <summary>Registers a typed file-drop callback on this mounted View.</summary>
    internal ulong BindFileDrop<TView>(Action<TView, FileDropEvent> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, FileDropBinder<TView>.Index);

    internal ulong BindFileDrop<TView>(Func<TView, FileDropEvent, ValueTask> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, FileDropAsyncBinder<TView>.Index);

    internal ulong BindFileDrop<TView>(Func<TView, FileDropEvent, Task> callback)
        where TView : ViewBase => BindDynamicEvent(this, callback, FileDropTaskBinder<TView>.Index);

    internal ulong BindNativeExtensionEvent<TView, TEvent>(Action<TView, TEvent> callback)
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent> =>
        BindDynamicEvent(this, callback, NativeExtensionBinder<TView, TEvent>.Index);

    internal ulong BindNativeExtensionEvent<TView, TEvent>(
        Func<TView, TEvent, ValueTask> callback
    )
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent> =>
        BindDynamicEvent(this, callback, NativeExtensionAsyncBinder<TView, TEvent>.Index);

    internal ulong BindNativeExtensionEvent<TView, TEvent>(Func<TView, TEvent, Task> callback)
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent> =>
        BindDynamicEvent(this, callback, NativeExtensionTaskBinder<TView, TEvent>.Index);

    private ulong BindDynamicEvent(ViewBase target, Delegate callback, int binderIndex)
    {
        return (_currentEventBindingOwner ?? this).BindDynamicEventCore(
            target,
            callback,
            binderIndex
        );
    }

    private ulong BindDynamicEventCore(ViewBase target, Delegate callback, int binderIndex)
    {
        var attachment = RequireUiAttachment(
            "Event callbacks can only be bound while the View is mounted and rendering."
        );
        var entries = attachment.EventEntries ??= [];
        var scope = attachment.EventBindingScope;
        var pass = attachment.EventBindingPass;
        for (var index = 0; index < entries.Count; index++)
        {
            var current = entries[index];
            if (
                current.BinderIndex == binderIndex
                && IsEntryInScope(current.LastPass, scope)
                && ReferenceEquals(current.Target, target)
                && Equals(current.Callback, callback)
            )
            {
                current.Target = target;
                current.Callback = callback;
                current.LastPass = pass;
                entries[index] = current;
                return DynamicEventToken(attachment.ViewHandle, index);
            }
        }

        var freeEventIds = attachment.FreeEventIds;
        var entryIndex = freeEventIds is { Count: > 0 } ? freeEventIds.Pop() : entries.Count;
        if (entryIndex >= (int)DynamicEventEntryMask)
        {
            throw new InvalidOperationException(
                "The View has exhausted its dynamic event entries."
            );
        }

        var entry = new EventEntry
        {
            Target = target,
            Callback = callback,
            BinderIndex = binderIndex,
            LastPass = pass,
        };
        if (entryIndex == entries.Count)
        {
            entries.Add(entry);
        }
        else
        {
            entries[entryIndex] = entry;
        }
        return DynamicEventToken(attachment.ViewHandle, entryIndex);
    }

    internal void BeginEventBindingPass(ViewEventBindingScope scope)
    {
        var attachment = RequireUiAttachment();
        if (attachment.EventBindingScope != ViewEventBindingScope.None)
        {
            throw new InvalidOperationException(
                "Nested View event-binding passes are not supported."
            );
        }

        var pass = ++attachment.NextEventBindingPass;
        attachment.EventBindingScope = scope;
        attachment.EventBindingPass = scope == ViewEventBindingScope.ListRange ? -pass : pass;
    }

    internal void CompleteEventBindingPass(ViewEventBindingScope scope, bool completed)
    {
        var attachment = _uiAttachment;
        if (attachment is null)
        {
            return;
        }
        attachment.AssertAccess();
        if (attachment.EventBindingScope != scope)
        {
            return;
        }

        if (completed && attachment.EventEntries is { } entries)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (
                    entry.BinderIndex != 0
                    && IsEntryInScope(entry.LastPass, scope)
                    && entry.LastPass != attachment.EventBindingPass
                )
                {
                    entries[index] = default;
                    (attachment.FreeEventIds ??= new Stack<int>()).Push(index);
                }
            }
        }

        attachment.EventBindingScope = ViewEventBindingScope.None;
        attachment.EventBindingPass = 0;
    }

    private static ulong DynamicEventToken(uint viewHandle, int index) =>
        ((ulong)viewHandle << 32) | DynamicEventBit | checked((uint)(index + 1));

    private ValueTask DispatchDynamicClickAsync(uint eventId, ClickEvent clickEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "click");
        }

        var dispatch = new EventDispatch(clickEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicInputAsync(uint eventId, InputEvent inputEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "input");
        }

        var dispatch = new EventDispatch(inputEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicSliderAsync(uint eventId, SliderEvent sliderEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "slider");
        }

        var dispatch = new EventDispatch(sliderEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicDockAsync(uint eventId, DockEvent dockEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "dock");
        }

        var dispatch = new EventDispatch(dockEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicKeyAsync(uint eventId, KeyEvent keyEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "key");
        }

        var dispatch = new EventDispatch(keyEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicMouseAsync(uint eventId, MouseEvent mouseEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "mouse");
        }

        var dispatch = new EventDispatch(mouseEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicModifiersAsync(uint eventId, ModifiersEvent modifiersEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "modifiers");
        }

        var dispatch = new EventDispatch(modifiersEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicHoverAsync(uint eventId, HoverEvent hoverEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "hover");
        }

        var dispatch = new EventDispatch(hoverEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicMouseMoveAsync(uint eventId, MouseMoveEvent mouseMoveEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "mouse move");
        }

        var dispatch = new EventDispatch(mouseMoveEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicScrollWheelAsync(uint eventId, ScrollWheelEvent scrollWheelEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "scroll wheel");
        }

        var dispatch = new EventDispatch(scrollWheelEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private ValueTask DispatchDynamicFileDropAsync(uint eventId, FileDropEvent fileDropEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "file drop");
        }

        var dispatch = new EventDispatch(fileDropEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private bool TryGetDynamicEvent(uint eventId, out EventEntry entry)
    {
        if ((eventId & DynamicEventBit) == 0)
        {
            entry = default;
            return false;
        }

        var entryId = eventId & DynamicEventEntryMask;
        if (entryId == 0)
        {
            entry = default;
            return false;
        }

        var attachment = _uiAttachment;
        if (attachment is null)
        {
            entry = default;
            return false;
        }
        attachment.AssertAccess();
        var entries = attachment.EventEntries;
        if (entries is null)
        {
            entry = default;
            return false;
        }

        var index = checked((int)entryId - 1);
        if (index >= entries.Count)
        {
            entry = default;
            return false;
        }

        entry = entries[index];
        return entry.BinderIndex != 0;
    }

    private static ValueTask MissingDynamicEvent(uint eventId, string eventType) =>
        ValueTask.FromException(
            new InvalidOperationException(
                $"Dynamic {eventType} event entry {eventId & DynamicEventEntryMask} has no callback."
            )
        );

    private static bool IsEntryInScope(int pass, ViewEventBindingScope scope) =>
        scope switch
        {
            ViewEventBindingScope.None => pass == 0,
            ViewEventBindingScope.Render => pass > 0,
            ViewEventBindingScope.ListRange => pass < 0,
            _ => false,
        };

    internal ValueTask DispatchClickCore(uint eventId, ClickEvent clickEvent) =>
        DispatchDynamicClickAsync(eventId, clickEvent);

    internal ValueTask DispatchInputCore(uint eventId, InputEvent inputEvent) =>
        DispatchDynamicInputAsync(eventId, inputEvent);

    internal ValueTask DispatchSliderCore(uint eventId, SliderEvent sliderEvent) =>
        DispatchDynamicSliderAsync(eventId, sliderEvent);

    internal ValueTask DispatchDockCore(uint eventId, DockEvent dockEvent) =>
        DispatchDynamicDockAsync(eventId, dockEvent);

    internal ValueTask DispatchKeyCore(uint eventId, KeyEvent keyEvent) =>
        DispatchDynamicKeyAsync(eventId, keyEvent);

    internal ValueTask DispatchMouseCore(uint eventId, MouseEvent mouseEvent) =>
        DispatchDynamicMouseAsync(eventId, mouseEvent);

    internal ValueTask DispatchModifiersCore(uint eventId, ModifiersEvent modifiersEvent) =>
        DispatchDynamicModifiersAsync(eventId, modifiersEvent);

    internal ValueTask DispatchHoverCore(uint eventId, HoverEvent hoverEvent) =>
        DispatchDynamicHoverAsync(eventId, hoverEvent);

    internal ValueTask DispatchMouseMoveCore(uint eventId, MouseMoveEvent mouseMoveEvent) =>
        DispatchDynamicMouseMoveAsync(eventId, mouseMoveEvent);

    internal ValueTask DispatchScrollWheelCore(uint eventId, ScrollWheelEvent scrollWheelEvent) =>
        DispatchDynamicScrollWheelAsync(eventId, scrollWheelEvent);

    internal ValueTask DispatchFileDropCore(uint eventId, FileDropEvent fileDropEvent) =>
        DispatchDynamicFileDropAsync(eventId, fileDropEvent);

    internal ValueTask DispatchNativeExtensionCore(
        uint eventId,
        NativeExtensionEvent nativeExtensionEvent
    )
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            return MissingDynamicEvent(eventId, "native extension");
        }

        var dispatch = new EventDispatch(nativeExtensionEvent);
        return EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private static class EventBinderRegistry
    {
        private const int SegmentShift = 8;
        private const int SegmentSize = 1 << SegmentShift;
        private const int SegmentMask = SegmentSize - 1;
        private const int MaxSegments = 256;
        private static readonly EventBinder[]?[] Segments = new EventBinder[]?[MaxSegments];
        private static int _count;

        internal static int Add(EventBinder binder)
        {
            var index = Interlocked.Increment(ref _count);
            var segmentIndex = index >> SegmentShift;
            if ((uint)segmentIndex >= MaxSegments)
            {
                throw new InvalidOperationException("The event binder registry is exhausted.");
            }

            var segment = Volatile.Read(ref Segments[segmentIndex]);
            if (segment is null)
            {
                var created = new EventBinder[SegmentSize];
                segment =
                    Interlocked.CompareExchange(ref Segments[segmentIndex], created, null)
                    ?? created;
            }

            Volatile.Write(ref segment[index & SegmentMask], binder);
            return index;
        }

        internal static EventBinder Get(int index)
        {
            if (index <= 0)
            {
                throw new InvalidOperationException("The event binder index is invalid.");
            }

            var segmentIndex = index >> SegmentShift;
            if ((uint)segmentIndex >= MaxSegments)
            {
                throw new InvalidOperationException("The event binder index is invalid.");
            }

            var segment = Volatile.Read(ref Segments[segmentIndex]);
            var binder = segment is null ? null : Volatile.Read(ref segment[index & SegmentMask]);
            return binder
                ?? throw new InvalidOperationException("The event binder is not registered.");
        }
    }

    private static class ClickEventBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Click)
            {
                return WrongDispatchKind("click");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "click");
            }
            if (callback is not Action<TView, ClickEvent> typedCallback)
            {
                return WrongCallback("Action<TView, ClickEvent>", "click");
            }

            typedCallback(typedTarget, dispatch.Click);
            return ValueTask.CompletedTask;
        }
    }

    private static class ClickEventAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Click)
            {
                return WrongDispatchKind("click");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "click");
            }
            if (callback is not Func<TView, ClickEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, ClickEvent, ValueTask>", "click");
            }

            return typedCallback(typedTarget, dispatch.Click);
        }
    }

    private static class ClickEventTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Click)
            {
                return WrongDispatchKind("click");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "click");
            }
            if (callback is not Func<TView, ClickEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, ClickEvent, Task>", "click");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.Click));
        }
    }

    private static class InputBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Input || dispatch.Input is not { } input)
            {
                return WrongDispatchKind("input");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "input");
            }
            if (callback is not Action<TView, InputEvent> typedCallback)
            {
                return WrongCallback("Action<TView, InputEvent>", "input");
            }

            typedCallback(typedTarget, input);
            return ValueTask.CompletedTask;
        }
    }

    private static class InputAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Input || dispatch.Input is not { } input)
            {
                return WrongDispatchKind("input");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "input");
            }
            if (callback is not Func<TView, InputEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, InputEvent, ValueTask>", "input");
            }

            return typedCallback(typedTarget, input);
        }
    }

    private static class InputTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Input || dispatch.Input is not { } input)
            {
                return WrongDispatchKind("input");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "input");
            }
            if (callback is not Func<TView, InputEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, InputEvent, Task>", "input");
            }

            return new ValueTask(typedCallback(typedTarget, input));
        }
    }

    private static class SliderBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Slider)
            {
                return WrongDispatchKind("slider");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "slider");
            }
            if (callback is not Action<TView, SliderEvent> typedCallback)
            {
                return WrongCallback("Action<TView, SliderEvent>", "slider");
            }

            typedCallback(typedTarget, dispatch.Slider);
            return ValueTask.CompletedTask;
        }
    }

    private static class SliderAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Slider)
            {
                return WrongDispatchKind("slider");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "slider");
            }
            if (callback is not Func<TView, SliderEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, SliderEvent, ValueTask>", "slider");
            }

            return typedCallback(typedTarget, dispatch.Slider);
        }
    }

    private static class SliderTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Slider)
            {
                return WrongDispatchKind("slider");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "slider");
            }
            if (callback is not Func<TView, SliderEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, SliderEvent, Task>", "slider");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.Slider));
        }
    }

    private static class DockBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Dock)
            {
                return WrongDispatchKind("dock");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "dock");
            }
            if (callback is not Action<TView, DockEvent> typedCallback)
            {
                return WrongCallback("Action<TView, DockEvent>", "dock");
            }

            typedCallback(typedTarget, dispatch.Dock);
            return ValueTask.CompletedTask;
        }
    }

    private static class DockAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Dock)
            {
                return WrongDispatchKind("dock");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "dock");
            }
            if (callback is not Func<TView, DockEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, DockEvent, ValueTask>", "dock");
            }

            return typedCallback(typedTarget, dispatch.Dock);
        }
    }

    private static class DockTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Dock)
            {
                return WrongDispatchKind("dock");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "dock");
            }
            if (callback is not Func<TView, DockEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, DockEvent, Task>", "dock");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.Dock));
        }
    }

    private static class KeyBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Key || dispatch.Key is not { } key)
            {
                return WrongDispatchKind("key");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "key");
            }
            if (callback is not Action<TView, KeyEvent> typedCallback)
            {
                return WrongCallback("Action<TView, KeyEvent>", "key");
            }

            typedCallback(typedTarget, key);
            return ValueTask.CompletedTask;
        }
    }

    private static class KeyAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Key || dispatch.Key is not { } key)
            {
                return WrongDispatchKind("key");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "key");
            }
            if (callback is not Func<TView, KeyEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, KeyEvent, ValueTask>", "key");
            }

            return typedCallback(typedTarget, key);
        }
    }

    private static class KeyTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Key || dispatch.Key is not { } key)
            {
                return WrongDispatchKind("key");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "key");
            }
            if (callback is not Func<TView, KeyEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, KeyEvent, Task>", "key");
            }

            return new ValueTask(typedCallback(typedTarget, key));
        }
    }

    private static class MouseBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Mouse)
            {
                return WrongDispatchKind("mouse");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "mouse");
            }
            if (callback is not Action<TView, MouseEvent> typedCallback)
            {
                return WrongCallback("Action<TView, MouseEvent>", "mouse");
            }

            typedCallback(typedTarget, dispatch.Mouse);
            return ValueTask.CompletedTask;
        }
    }

    private static class MouseAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Mouse)
            {
                return WrongDispatchKind("mouse");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "mouse");
            }
            if (callback is not Func<TView, MouseEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, MouseEvent, ValueTask>", "mouse");
            }

            return typedCallback(typedTarget, dispatch.Mouse);
        }
    }

    private static class MouseTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Mouse)
            {
                return WrongDispatchKind("mouse");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "mouse");
            }
            if (callback is not Func<TView, MouseEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, MouseEvent, Task>", "mouse");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.Mouse));
        }
    }

    private static class ModifiersBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Modifiers)
            {
                return WrongDispatchKind("modifiers");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "modifiers");
            }
            if (callback is not Action<TView, ModifiersEvent> typedCallback)
            {
                return WrongCallback("Action<TView, ModifiersEvent>", "modifiers");
            }

            typedCallback(typedTarget, dispatch.Modifiers);
            return ValueTask.CompletedTask;
        }
    }

    private static class ModifiersAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Modifiers)
            {
                return WrongDispatchKind("modifiers");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "modifiers");
            }
            if (callback is not Func<TView, ModifiersEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, ModifiersEvent, ValueTask>", "modifiers");
            }

            return typedCallback(typedTarget, dispatch.Modifiers);
        }
    }

    private static class ModifiersTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Modifiers)
            {
                return WrongDispatchKind("modifiers");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "modifiers");
            }
            if (callback is not Func<TView, ModifiersEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, ModifiersEvent, Task>", "modifiers");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.Modifiers));
        }
    }

    private static class HoverBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Hover)
            {
                return WrongDispatchKind("hover");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "hover");
            }
            if (callback is not Action<TView, HoverEvent> typedCallback)
            {
                return WrongCallback("Action<TView, HoverEvent>", "hover");
            }

            typedCallback(typedTarget, dispatch.Hover);
            return ValueTask.CompletedTask;
        }
    }

    private static class HoverAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Hover)
            {
                return WrongDispatchKind("hover");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "hover");
            }
            if (callback is not Func<TView, HoverEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, HoverEvent, ValueTask>", "hover");
            }

            return typedCallback(typedTarget, dispatch.Hover);
        }
    }

    private static class HoverTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Hover)
            {
                return WrongDispatchKind("hover");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "hover");
            }
            if (callback is not Func<TView, HoverEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, HoverEvent, Task>", "hover");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.Hover));
        }
    }

    private static class MouseMoveBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.MouseMove)
            {
                return WrongDispatchKind("mouse move");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "mouse move");
            }
            if (callback is not Action<TView, MouseMoveEvent> typedCallback)
            {
                return WrongCallback("Action<TView, MouseMoveEvent>", "mouse move");
            }

            typedCallback(typedTarget, dispatch.MouseMove);
            return ValueTask.CompletedTask;
        }
    }

    private static class MouseMoveAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.MouseMove)
            {
                return WrongDispatchKind("mouse move");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "mouse move");
            }
            if (callback is not Func<TView, MouseMoveEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, MouseMoveEvent, ValueTask>", "mouse move");
            }

            return typedCallback(typedTarget, dispatch.MouseMove);
        }
    }

    private static class MouseMoveTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.MouseMove)
            {
                return WrongDispatchKind("mouse move");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "mouse move");
            }
            if (callback is not Func<TView, MouseMoveEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, MouseMoveEvent, Task>", "mouse move");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.MouseMove));
        }
    }

    private static class ScrollWheelBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.ScrollWheel)
            {
                return WrongDispatchKind("scroll wheel");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "scroll wheel");
            }
            if (callback is not Action<TView, ScrollWheelEvent> typedCallback)
            {
                return WrongCallback("Action<TView, ScrollWheelEvent>", "scroll wheel");
            }

            typedCallback(typedTarget, dispatch.ScrollWheel);
            return ValueTask.CompletedTask;
        }
    }

    private static class ScrollWheelAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.ScrollWheel)
            {
                return WrongDispatchKind("scroll wheel");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "scroll wheel");
            }
            if (callback is not Func<TView, ScrollWheelEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, ScrollWheelEvent, ValueTask>", "scroll wheel");
            }

            return typedCallback(typedTarget, dispatch.ScrollWheel);
        }
    }

    private static class ScrollWheelTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.ScrollWheel)
            {
                return WrongDispatchKind("scroll wheel");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "scroll wheel");
            }
            if (callback is not Func<TView, ScrollWheelEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, ScrollWheelEvent, Task>", "scroll wheel");
            }

            return new ValueTask(typedCallback(typedTarget, dispatch.ScrollWheel));
        }
    }

    private static class FileDropBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.FileDrop || dispatch.FileDrop is not { } fileDrop)
            {
                return WrongDispatchKind("file drop");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "file drop");
            }
            if (callback is not Action<TView, FileDropEvent> typedCallback)
            {
                return WrongCallback("Action<TView, FileDropEvent>", "file drop");
            }

            typedCallback(typedTarget, fileDrop);
            return ValueTask.CompletedTask;
        }
    }

    private static class FileDropAsyncBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.FileDrop || dispatch.FileDrop is not { } fileDrop)
            {
                return WrongDispatchKind("file drop");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "file drop");
            }
            if (callback is not Func<TView, FileDropEvent, ValueTask> typedCallback)
            {
                return WrongCallback("Func<TView, FileDropEvent, ValueTask>", "file drop");
            }

            return typedCallback(typedTarget, fileDrop);
        }
    }

    private static class FileDropTaskBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.FileDrop || dispatch.FileDrop is not { } fileDrop)
            {
                return WrongDispatchKind("file drop");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "file drop");
            }
            if (callback is not Func<TView, FileDropEvent, Task> typedCallback)
            {
                return WrongCallback("Func<TView, FileDropEvent, Task>", "file drop");
            }

            return new ValueTask(typedCallback(typedTarget, fileDrop));
        }
    }

    private static class NativeExtensionBinder<TView, TEvent>
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent>
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (
                dispatch.Kind != EventDispatchKind.NativeExtension
                || dispatch.NativeExtension is not { } nativeExtensionEvent
            )
            {
                return WrongDispatchKind("native extension");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "native extension");
            }
            if (callback is not Action<TView, TEvent> typedCallback)
            {
                return WrongCallback($"Action<TView, {typeof(TEvent).Name}>", "native extension");
            }

            typedCallback(typedTarget, TEvent.Decode(nativeExtensionEvent));
            return ValueTask.CompletedTask;
        }
    }

    private static class NativeExtensionAsyncBinder<TView, TEvent>
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent>
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (
                dispatch.Kind != EventDispatchKind.NativeExtension
                || dispatch.NativeExtension is not { } nativeExtensionEvent
            )
            {
                return WrongDispatchKind("native extension");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "native extension");
            }
            if (callback is not Func<TView, TEvent, ValueTask> typedCallback)
            {
                return WrongCallback(
                    $"Func<TView, {typeof(TEvent).Name}, ValueTask>",
                    "native extension"
                );
            }

            return typedCallback(typedTarget, TEvent.Decode(nativeExtensionEvent));
        }
    }

    private static class NativeExtensionTaskBinder<TView, TEvent>
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent>
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static ValueTask Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (
                dispatch.Kind != EventDispatchKind.NativeExtension
                || dispatch.NativeExtension is not { } nativeExtensionEvent
            )
            {
                return WrongDispatchKind("native extension");
            }
            if (target is not TView typedTarget)
            {
                return WrongTarget<TView>(target, "native extension");
            }
            if (callback is not Func<TView, TEvent, Task> typedCallback)
            {
                return WrongCallback(
                    $"Func<TView, {typeof(TEvent).Name}, Task>",
                    "native extension"
                );
            }

            return new ValueTask(typedCallback(typedTarget, TEvent.Decode(nativeExtensionEvent)));
        }
    }

    private static ValueTask WrongDispatchKind(string eventType) =>
        ValueTask.FromException(
            new InvalidOperationException($"The event binder cannot dispatch a {eventType} event.")
        );

    private static ValueTask WrongTarget<TView>(object target, string eventType)
        where TView : ViewBase =>
        ValueTask.FromException(
            new InvalidOperationException(
                $"The {eventType} callback requires target type {typeof(TView).FullName}, "
                    + $"but received {target.GetType().FullName}."
            )
        );

    private static ValueTask WrongCallback(string callbackType, string eventType) =>
        ValueTask.FromException(
            new InvalidOperationException(
                $"The {eventType} callback has an incompatible delegate type; expected {callbackType}."
            )
        );
}
