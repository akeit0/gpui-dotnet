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
            NativeExtension = null;
        }

        internal EventDispatch(InputEvent input)
        {
            Kind = EventDispatchKind.Input;
            Click = default;
            Input = input;
            Slider = default;
            NativeExtension = null;
        }

        internal EventDispatch(SliderEvent slider)
        {
            Kind = EventDispatchKind.Slider;
            Click = default;
            Input = null;
            Slider = slider;
            NativeExtension = null;
        }

        internal EventDispatch(NativeExtensionEvent nativeExtension)
        {
            Kind = EventDispatchKind.NativeExtension;
            Click = default;
            Input = null;
            Slider = default;
            NativeExtension = nativeExtension;
        }

        internal EventDispatchKind Kind { get; }
        internal ClickEvent Click { get; }
        internal InputEvent? Input { get; }
        internal SliderEvent Slider { get; }
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
