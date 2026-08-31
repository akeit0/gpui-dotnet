using System.Runtime.ExceptionServices;

namespace Gpui;

internal enum ViewEventBindingScope
{
    None,
    Render,
    ListRange,
}

internal delegate void Utf8InputValueDispatcher(
    View owner,
    ReadOnlySpan<byte> utf8Key,
    ReadOnlySpan<byte> utf8Value
);

/// <summary>
/// Implemented by the source generator for every <see cref="GpuiViewAttribute"/> view. The
/// runtime uses the static factory to create framework-owned children without reflection or
/// Activator.CreateInstance, preserving NativeAOT/trimming friendliness.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IGeneratedViewFactory<TSelf>
    where TSelf : View
{
    static abstract TSelf CreateGpuiView();
}

/// <summary>
/// Managed application unit. C# owns durable application state and Rust owns GPUI objects,
/// native render state, and frame-sensitive work.
/// </summary>
public abstract class View
{
    private const uint DynamicEventBit = 0x8000_0000u;
    private const uint DynamicEventEntryMask = 0x7FFF_FFFFu;

    [ThreadStatic]
    private static View? _currentEventBindingOwner;

    internal static View? CurrentEventBindingOwner
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
    }

    private readonly struct EventDispatch
    {
        internal EventDispatch(ClickEvent click)
        {
            Kind = EventDispatchKind.Click;
            Click = click;
            Input = null;
            Slider = default;
        }

        internal EventDispatch(InputEvent input)
        {
            Kind = EventDispatchKind.Input;
            Click = default;
            Input = input;
            Slider = default;
        }

        internal EventDispatch(SliderEvent slider)
        {
            Kind = EventDispatchKind.Slider;
            Click = default;
            Input = null;
            Slider = slider;
        }

        internal EventDispatchKind Kind { get; }
        internal ClickEvent Click { get; }
        internal InputEvent? Input { get; }
        internal SliderEvent Slider { get; }
    }

    private struct EventEntry
    {
        internal object? Target;
        internal Delegate? Callback;
        internal int BinderIndex;
        internal int LastPass;
    }

    private readonly object _runtimeGate = new();
    private readonly List<EventEntry> _eventEntries = [];
    private readonly Stack<int> _freeEventIds = new();
    private Action<Action>? _post;
    private Action<View>? _invalidate;
    private Action<View, ResourceCommand>? _resourceCommand;
    private Utf8InputValueDispatcher? _utf8InputValue;
    private CancellationTokenSource? _mountLifetime;
    private uint _runtimeViewHandle;
    private ViewEventBindingScope _eventBindingScope;
    private int _eventBindingPass;
    private int _nextEventBindingPass;
    private int _mounted;
    private ulong _nextResourceKeyId;

    protected View() => Dispatcher = new Dispatcher(this);

    /// <summary>Posts managed work to this view's GPUI UI thread.</summary>
    protected internal Dispatcher Dispatcher { get; }

    /// <summary>
    /// Allocates the next auto resource-key id for this view. Ids are monotonic per view
    /// instance: ref-bound controllers retain the id assigned by their first render, so later
    /// renders and re-renders reuse it instead of allocating.
    /// </summary>
    internal ulong NextResourceKeyId() => ++_nextResourceKeyId;

    /// <summary>True between <see cref="OnMounted"/> and <see cref="OnUnmounted"/>.</summary>
    protected bool IsMounted => Volatile.Read(ref _mounted) != 0;

    /// <summary>
    /// Cancellation token for the current mount. It is cancelled before <see cref="OnUnmounted"/>
    /// runs. Async event handlers should pass it to cancellable operations that must not outlive
    /// this View's current tree membership.
    /// </summary>
    protected CancellationToken Lifetime
    {
        get
        {
            lock (_runtimeGate)
            {
                return _mountLifetime?.Token ?? CancellationToken.None;
            }
        }
    }

    /// <summary>Called exactly once for each attachment to a running managed view tree.</summary>
    protected virtual void OnMounted(ref ViewContext context) { }

    /// <summary>Called when the view leaves the committed tree or the application stops.</summary>
    protected virtual void OnUnmounted(ref ViewContext context) { }

    /// <summary>Schedules a dirty render. Safe to call from any thread while mounted.</summary>
    protected internal void Invalidate()
    {
        var invalidate =
            Volatile.Read(ref _invalidate)
            ?? throw new InvalidOperationException(
                "The view is not mounted in a GPUI application."
            );
        invalidate(this);
    }

    /// <summary>
    /// Registers a typed click callback on this mounted View. Equivalent bindings reuse their
    /// recyclable per-View entry, so the native token remains compact and stable across renders.
    /// </summary>
    internal ulong BindClick<TView>(Action<TView, ClickEvent> callback)
        where TView : View => BindDynamicEvent(this, callback, ClickEventBinder<TView>.Index);

    internal ulong BindClick<TView>(Func<TView, ClickEvent, ValueTask> callback)
        where TView : View => BindDynamicEvent(this, callback, ClickEventAsyncBinder<TView>.Index);

    internal ulong BindClick<TView>(Func<TView, ClickEvent, Task> callback)
        where TView : View => BindDynamicEvent(this, callback, ClickEventTaskBinder<TView>.Index);

    /// <summary>Registers a typed input callback on this mounted View.</summary>
    internal ulong BindInput<TView>(Action<TView, InputEvent> callback)
        where TView : View => BindDynamicEvent(this, callback, InputBinder<TView>.Index);

    internal ulong BindInput<TView>(Func<TView, InputEvent, ValueTask> callback)
        where TView : View => BindDynamicEvent(this, callback, InputAsyncBinder<TView>.Index);

    internal ulong BindInput<TView>(Func<TView, InputEvent, Task> callback)
        where TView : View => BindDynamicEvent(this, callback, InputTaskBinder<TView>.Index);

    internal ulong BindSlider<TView>(Action<TView, SliderEvent> callback)
        where TView : View => BindDynamicEvent(this, callback, SliderBinder<TView>.Index);

    internal ulong BindSlider<TView>(Func<TView, SliderEvent, ValueTask> callback)
        where TView : View => BindDynamicEvent(this, callback, SliderAsyncBinder<TView>.Index);

    internal ulong BindSlider<TView>(Func<TView, SliderEvent, Task> callback)
        where TView : View => BindDynamicEvent(this, callback, SliderTaskBinder<TView>.Index);

    private ulong BindDynamicEvent(View target, Delegate callback, int binderIndex)
    {
        return (_currentEventBindingOwner ?? this).BindDynamicEventCore(
            target,
            callback,
            binderIndex
        );
    }

    private ulong BindDynamicEventCore(View target, Delegate callback, int binderIndex)
    {
        var viewHandle = Volatile.Read(ref _runtimeViewHandle);
        if (viewHandle == 0 || Volatile.Read(ref _mounted) == 0)
        {
            throw new InvalidOperationException(
                "Event callbacks can only be bound while the View is mounted and rendering."
            );
        }

        lock (_runtimeGate)
        {
            var scope = _eventBindingScope;
            var pass = _eventBindingPass;
            for (var index = 0; index < _eventEntries.Count; index++)
            {
                var current = _eventEntries[index];
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
                    _eventEntries[index] = current;
                    return DynamicEventToken(viewHandle, index);
                }
            }

            var entryIndex = _freeEventIds.Count != 0 ? _freeEventIds.Pop() : _eventEntries.Count;
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
            if (entryIndex == _eventEntries.Count)
            {
                _eventEntries.Add(entry);
            }
            else
            {
                _eventEntries[entryIndex] = entry;
            }
            return DynamicEventToken(viewHandle, entryIndex);
        }
    }

    internal void BeginEventBindingPass(ViewEventBindingScope scope)
    {
        lock (_runtimeGate)
        {
            if (_eventBindingScope != ViewEventBindingScope.None)
            {
                throw new InvalidOperationException(
                    "Nested View event-binding passes are not supported."
                );
            }

            var pass = ++_nextEventBindingPass;
            _eventBindingScope = scope;
            _eventBindingPass = scope == ViewEventBindingScope.ListRange ? -pass : pass;
        }
    }

    internal void CompleteEventBindingPass(ViewEventBindingScope scope, bool completed)
    {
        lock (_runtimeGate)
        {
            if (_eventBindingScope != scope)
            {
                return;
            }

            if (completed)
            {
                for (var index = 0; index < _eventEntries.Count; index++)
                {
                    var entry = _eventEntries[index];
                    if (
                        entry.BinderIndex != 0
                        && IsEntryInScope(entry.LastPass, scope)
                        && entry.LastPass != _eventBindingPass
                    )
                    {
                        _eventEntries[index] = default;
                        _freeEventIds.Push(index);
                    }
                }
            }

            _eventBindingScope = ViewEventBindingScope.None;
            _eventBindingPass = 0;
        }
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

        lock (_runtimeGate)
        {
            var index = checked((int)entryId - 1);
            if (index >= _eventEntries.Count)
            {
                entry = default;
                return false;
            }

            entry = _eventEntries[index];
            return entry.BinderIndex != 0;
        }
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

    /// <summary>
    /// Binds a generated list-item renderer id to this mounted view. The token is consumed by
    /// native virtualization and is never a managed delegate.
    /// </summary>
    protected ListItemRenderer BindListRenderer(uint rendererId)
    {
        if (rendererId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rendererId), "Renderer id 0 is reserved.");
        }

        var viewHandle = Volatile.Read(ref _runtimeViewHandle);
        if (viewHandle == 0 || Volatile.Read(ref _mounted) == 0)
        {
            throw new InvalidOperationException(
                "Generated list renderers can only be materialized while the view is mounted. "
                    + "Use Rows.<renderer> from Render(), not from a constructor or field initializer."
            );
        }

        return new ListItemRenderer(((ulong)viewHandle << 32) | rendererId);
    }

    /// <summary>
    /// Generated views override this for [GpuiListItem] methods. List item rendering is synchronous
    /// and element-only; retained child View composition is intentionally not available inside a
    /// virtualized row batch.
    /// </summary>
    protected virtual Element RenderListItem(uint rendererId, int index, ref RenderContext ui) =>
        throw new InvalidOperationException(
            $"Generated list renderer 0x{rendererId:X8} is not defined on {GetType().Name}."
        );

    /// <summary>
    /// Renders the view tree into native IR. Rendering must be synchronous, repeatable, and free of
    /// externally visible side effects: the framework may invoke it more than once per frame when
    /// the render arena needs to grow before the final render is accepted, and list batch renderers
    /// are invoked lazily whenever the viewport requires an uncached range.
    /// </summary>
    protected abstract Element Render(ref RenderContext ui);

    internal Element RenderCore(ref RenderContext ui)
    {
        BeginEventBindingPass(ViewEventBindingScope.Render);
        var previousEventBindingOwner = _currentEventBindingOwner;
        _currentEventBindingOwner = this;
        var completed = false;
        try
        {
            var element = Render(ref ui);
            completed = true;
            return element;
        }
        finally
        {
            try
            {
                CompleteEventBindingPass(ViewEventBindingScope.Render, completed);
            }
            finally
            {
                _currentEventBindingOwner = previousEventBindingOwner;
            }
        }
    }

    internal Element RenderListItemCore(uint rendererId, int index, ref RenderContext ui) =>
        RenderListItem(rendererId, index, ref ui);

    internal ValueTask DispatchClickCore(uint eventId, ClickEvent clickEvent) =>
        DispatchDynamicClickAsync(eventId, clickEvent);

    internal ValueTask DispatchInputCore(uint eventId, InputEvent inputEvent) =>
        DispatchDynamicInputAsync(eventId, inputEvent);

    internal ValueTask DispatchSliderCore(uint eventId, SliderEvent sliderEvent) =>
        DispatchDynamicSliderAsync(eventId, sliderEvent);

    internal uint RuntimeViewHandle => Volatile.Read(ref _runtimeViewHandle);
    internal bool IsMountedCore => Volatile.Read(ref _mounted) != 0;

    internal virtual bool RequiresProps => false;

    internal void AttachRuntime(
        uint viewHandle,
        Action<Action> post,
        Action<View> invalidate,
        Action<View, ResourceCommand> resourceCommand,
        Utf8InputValueDispatcher utf8InputValue
    )
    {
        if (viewHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewHandle));
        }
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(invalidate);
        ArgumentNullException.ThrowIfNull(resourceCommand);
        ArgumentNullException.ThrowIfNull(utf8InputValue);

        var lifetime = new CancellationTokenSource();
        lock (_runtimeGate)
        {
            if (_mounted != 0)
            {
                lifetime.Dispose();
                throw new InvalidOperationException("The view is already mounted.");
            }

            _post = post;
            _invalidate = invalidate;
            _resourceCommand = resourceCommand;
            _utf8InputValue = utf8InputValue;
            _mountLifetime = lifetime;
            _runtimeViewHandle = viewHandle;
            Volatile.Write(ref _mounted, 1);
        }

        try
        {
            var context = new ViewContext(this);
            OnMounted(ref context);
        }
        catch (Exception mountFailure)
        {
            Exception? cancellationFailure = null;
            try
            {
                lifetime.Cancel();
            }
            catch (Exception exception)
            {
                cancellationFailure = exception;
            }
            finally
            {
                lock (_runtimeGate)
                {
                    Volatile.Write(ref _mounted, 0);
                    _runtimeViewHandle = 0;
                    _mountLifetime = null;
                    _invalidate = null;
                    _resourceCommand = null;
                    _utf8InputValue = null;
                    _post = null;
                }
                lifetime.Dispose();
            }

            if (cancellationFailure is not null)
            {
                throw new AggregateException(mountFailure, cancellationFailure);
            }
            throw;
        }
    }

    internal void DetachRuntime()
    {
        if (Volatile.Read(ref _mounted) == 0)
        {
            return;
        }

        var lifetime = Volatile.Read(ref _mountLifetime);
        Exception? cancellationFailure = null;
        Exception? lifecycleFailure = null;
        try
        {
            try
            {
                lifetime?.Cancel();
            }
            catch (Exception exception)
            {
                cancellationFailure = exception;
            }

            try
            {
                var context = new ViewContext(this);
                OnUnmounted(ref context);
            }
            catch (Exception exception)
            {
                lifecycleFailure = exception;
            }
        }
        finally
        {
            lock (_runtimeGate)
            {
                Volatile.Write(ref _mounted, 0);
                _runtimeViewHandle = 0;
                _mountLifetime = null;
                _invalidate = null;
                _resourceCommand = null;
                _utf8InputValue = null;
                _post = null;
            }
            lifetime?.Dispose();
        }

        if (cancellationFailure is not null && lifecycleFailure is not null)
        {
            throw new AggregateException(cancellationFailure, lifecycleFailure);
        }
        if (cancellationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cancellationFailure).Throw();
        }
        if (lifecycleFailure is not null)
        {
            ExceptionDispatchInfo.Capture(lifecycleFailure).Throw();
        }
    }

    internal void DispatchResourceCommand(ResourceCommand command)
    {
        var dispatch =
            Volatile.Read(ref _resourceCommand)
            ?? throw new InvalidOperationException(
                "The view is not mounted in a GPUI application."
            );
        dispatch(this, command);
    }

    internal void DispatchUtf8InputValue(ReadOnlySpan<byte> utf8Key, ReadOnlySpan<byte> utf8Value)
    {
        var dispatch =
            Volatile.Read(ref _utf8InputValue)
            ?? throw new InvalidOperationException(
                "The view is not mounted in a GPUI application."
            );
        dispatch(this, utf8Key, utf8Value);
    }

    internal void InvalidateFromController()
    {
        var invalidate =
            Volatile.Read(ref _invalidate)
            ?? throw new InvalidOperationException(
                "The view is not mounted in a GPUI application."
            );
        invalidate(this);
    }

    internal void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var post =
            Volatile.Read(ref _post)
            ?? throw new InvalidOperationException(
                "The view is not mounted in a GPUI application."
            );
        post(callback);
    }

    private static class EventBinderRegistry
    {
        private const int SegmentShift = 8;
        private const int SegmentSize = 1 << SegmentShift;
        private const int SegmentMask = SegmentSize - 1;
        private const int MaxSegments = 256;
        private static readonly EventBinder[]?[] _segments = new EventBinder[]?[MaxSegments];
        private static int _count;

        internal static int Add(EventBinder binder)
        {
            var index = Interlocked.Increment(ref _count);
            var segmentIndex = index >> SegmentShift;
            if ((uint)segmentIndex >= MaxSegments)
            {
                throw new InvalidOperationException("The event binder registry is exhausted.");
            }

            var segment = Volatile.Read(ref _segments[segmentIndex]);
            if (segment is null)
            {
                var created = new EventBinder[SegmentSize];
                segment =
                    Interlocked.CompareExchange(ref _segments[segmentIndex], created, null)
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

            var segment = Volatile.Read(ref _segments[segmentIndex]);
            var binder = segment is null ? null : Volatile.Read(ref segment[index & SegmentMask]);
            return binder
                ?? throw new InvalidOperationException("The event binder is not registered.");
        }
    }

    private static class ClickEventBinder<TView>
        where TView : View
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
        where TView : View
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
        where TView : View
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
        where TView : View
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
        where TView : View
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
        where TView : View
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
        where TView : View
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
        where TView : View
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
        where TView : View
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

    private static ValueTask WrongDispatchKind(string eventType) =>
        ValueTask.FromException(
            new InvalidOperationException($"The event binder cannot dispatch a {eventType} event.")
        );

    private static ValueTask WrongTarget<TView>(object target, string eventType)
        where TView : View =>
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

/// <summary>
/// View with parent-supplied render inputs. Props are updated before mount/render and are compared
/// using EqualityComparer&lt;TProps&gt;.Default; changed props invalidate only this child's retained
/// fragment, not the whole application tree.
/// </summary>
public abstract class View<TProps> : View
{
    private TProps _props = default!;
    private bool _hasProps;

    /// <summary>The most recently committed-or-staged props supplied by the parent.</summary>
    protected ref readonly TProps Props
    {
        get
        {
            if (!_hasProps)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} requires props. Render it with ui.Child<TView, TProps>(...)."
                );
            }
            return ref _props;
        }
    }

    internal override bool RequiresProps => true;

    internal bool ApplyProps(in TProps props)
    {
        var changed = !_hasProps || !EqualityComparer<TProps>.Default.Equals(_props, props);
        _props = props;
        _hasProps = true;
        return changed;
    }
}

internal unsafe interface IViewRenderer
{
    Element RenderChild<TView>(View parent, ChildSlot slot, Interop.RenderArena* destination)
        where TView : View, IGeneratedViewFactory<TView>;

    Element RenderChild<TView, TProps>(
        View parent,
        ChildSlot slot,
        in TProps props,
        Interop.RenderArena* destination
    )
        where TView : View<TProps>, IGeneratedViewFactory<TView>;

    Element RenderChild(View view, View parent, ChildSlot slot, Interop.RenderArena* destination);

    Element RenderChild<TProps>(
        View<TProps> view,
        View parent,
        ChildSlot slot,
        in TProps props,
        Interop.RenderArena* destination
    );
}
