namespace Gpui.Interop.Internal;

internal enum ViewEventBindingScope
{
    None,
    Render,
    Demand,
}

internal sealed class ViewEventRegistry
{
    private ViewBase? _owner;
    private int _threadId;
    private uint ViewHandle { get; set; }
    private List<EventEntry>? EventEntries { get; set; }
    private Stack<int>? FreeEventSlots { get; set; }
    private Dictionary<uint, int>? EventSlots { get; set; }
    private Dictionary<ulong, int>? ArtifactEventSlots { get; set; }
    private List<int>? RootEventSlots { get; set; }
    private uint NextEventId { get; set; }
    private ulong EventBindingArtifact { get; set; }
    private ViewEventBindingScope EventBindingScope { get; set; }
    private long EventBindingPass { get; set; }
    private long NextEventBindingPass { get; set; }
    private ViewEventRegistry? Active => _owner is null ? null : this;
    internal int EntryCount => EventEntries?.Count ?? 0;

    internal void Activate(ViewBase owner, uint handle)
    {
        _owner = owner;
        ViewHandle = handle;
        _threadId = Environment.CurrentManagedThreadId;
    }

    internal void Reset()
    {
        AssertAccess();
        if (EventEntries is { Capacity: > 256 })
        {
            EventEntries = null;
            FreeEventSlots = null;
            EventSlots = null;
            RootEventSlots = null;
        }
        else
        {
            EventEntries?.Clear();
            FreeEventSlots?.Clear();
            EventSlots?.Clear();
            RootEventSlots?.Clear();
        }
        ArtifactEventSlots = null;
        NextEventId = 0;
        EventBindingArtifact = 0;
        EventBindingScope = ViewEventBindingScope.None;
        EventBindingPass = 0;
        NextEventBindingPass = 0;
        _owner = null;
        ViewHandle = 0;
        _threadId = 0;
    }

    private ViewEventRegistry RequireActive(string? message = null)
    {
        if (_owner is null)
            throw new InvalidOperationException(message ?? "The event registry has retired.");
        AssertAccess();
        return this;
    }

    private void AssertAccess()
    {
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Event state is confined to the GPUI application thread.");
    }

    private const uint DynamicEventBit = 0x8000_0000u;
    private const uint DynamicEventEntryMask = 0x7FFF_FFFFu;

    internal static bool IsWellFormedEventId(uint eventId) =>
        (eventId & DynamicEventBit) != 0 && (eventId & DynamicEventEntryMask) != 0;

    [ThreadStatic]
    private static ViewBase? _currentEventBindingOwner;

    internal static ViewBase? CurrentEventBindingOwner
    {
        get => _currentEventBindingOwner;
        set => _currentEventBindingOwner = value;
    }

    private delegate void EventBinder(
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
        // Artifact-local chain in recyclable storage; -1 ends the chain.
        internal int NextArtifactSlot;
        internal long LastPass;
        internal uint Id;
        internal ulong Artifact;
    }

    /// <summary>
    /// Registers a typed click callback. Equivalent bindings reuse their live token within one
    /// render scope or demand artifact; retired identities never alias recycled storage slots.
    /// </summary>
    internal ulong BindClick<TView>(Action<TView, ClickEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, ClickEventBinder<TView>.Index);

    /// <summary>Registers a typed input callback on this mounted View.</summary>
    internal ulong BindInput<TView>(Action<TView, InputEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, InputBinder<TView>.Index);

    internal ulong BindSlider<TView>(Action<TView, SliderEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, SliderBinder<TView>.Index);

    /// <summary>Registers a typed Dock area callback on this mounted View.</summary>
    internal ulong BindDock<TView>(Action<TView, DockEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, DockBinder<TView>.Index);

    /// <summary>Registers a typed key-event callback on this mounted View.</summary>
    internal ulong BindKey<TView>(Action<TView, KeyEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, KeyBinder<TView>.Index);

    /// <summary>Registers a typed mouse-event callback on this mounted View.</summary>
    internal ulong BindMouse<TView>(Action<TView, MouseEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, MouseBinder<TView>.Index);

    /// <summary>Registers a typed modifier-key callback on this mounted View.</summary>
    internal ulong BindModifiers<TView>(Action<TView, ModifiersEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, ModifiersBinder<TView>.Index);

    /// <summary>Registers a typed hover-state callback on this mounted View.</summary>
    internal ulong BindHover<TView>(Action<TView, HoverEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, HoverBinder<TView>.Index);

    /// <summary>Registers a typed mouse-move callback on this mounted View.</summary>
    internal ulong BindMouseMove<TView>(Action<TView, MouseMoveEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, MouseMoveBinder<TView>.Index);

    /// <summary>Registers a typed scroll-wheel callback on this mounted View.</summary>
    internal ulong BindScrollWheel<TView>(Action<TView, ScrollWheelEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, ScrollWheelBinder<TView>.Index);

    /// <summary>Registers a typed file-drop callback on this mounted View.</summary>
    internal ulong BindFileDrop<TView>(Action<TView, FileDropEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback, FileDropBinder<TView>.Index);

    internal ulong BindNativeExtensionEvent<TView, TEvent>(Action<TView, TEvent> callback)
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent> =>
        BindDynamicEvent(_owner!, callback, NativeExtensionBinder<TView, TEvent>.Index);

    private ulong BindDynamicEvent(ViewBase target, Delegate callback, int binderIndex)
    {
        return (_currentEventBindingOwner ?? _owner!).Runtime.Events.BindDynamicEventCore(
            target,
            callback,
            binderIndex
        );
    }

    private ulong BindDynamicEventCore(ViewBase target, Delegate callback, int binderIndex)
    {
        var attachment = RequireActive(
            "Event callbacks can only be bound while the View is mounted and rendering."
        );
        var entries = attachment.EventEntries ??= [];
        var scope = attachment.EventBindingScope;
        var pass = attachment.EventBindingPass;
        var scopeSlots = attachment.RootEventSlots;
        var demand = scope == ViewEventBindingScope.Demand;
        var artifactHead = demand && attachment.ArtifactEventSlots is { } artifactSlots
            && artifactSlots.TryGetValue(attachment.EventBindingArtifact, out var head) ? head : -1;
        var count = scopeSlots?.Count ?? 0;
        var index = demand ? artifactHead : count == 0 ? -1 : scopeSlots![0];
        for (var candidate = 0; index != -1; candidate++)
        {
            var current = entries[index];
            if (
                current.BinderIndex == binderIndex
                && IsEntryInScope(current.LastPass, scope)
                && current.Artifact == attachment.EventBindingArtifact
                && ReferenceEquals(current.Target, target)
                && Equals(current.Callback, callback)
            )
            {
                current.Target = target;
                current.Callback = callback;
                current.LastPass = pass;
                entries[index] = current;
                return DynamicEventToken(attachment.ViewHandle, current.Id);
            }
            index = demand ? current.NextArtifactSlot
                : candidate + 1 < count ? scopeSlots![candidate + 1] : -1;
        }

        if (attachment.NextEventId == DynamicEventEntryMask)
        {
            throw new InvalidOperationException(
                "The View has exhausted its dynamic event identities."
            );
        }
        var id = ++attachment.NextEventId;
        var freeSlots = attachment.FreeEventSlots;
        var entryIndex = freeSlots is { Count: > 0 } ? freeSlots.Pop() : entries.Count;

        var entry = new EventEntry
        {
            Target = target,
            Callback = callback,
            BinderIndex = binderIndex,
            NextArtifactSlot = artifactHead,
            LastPass = pass,
            Id = id,
            Artifact = attachment.EventBindingArtifact,
        };
        if (entryIndex == entries.Count)
        {
            entries.Add(entry);
        }
        else
        {
            entries[entryIndex] = entry;
        }
        (attachment.EventSlots ??= []).Add(id, entryIndex);
        if (entry.Artifact != 0)
        {
            (attachment.ArtifactEventSlots ??= [])[entry.Artifact] = entryIndex;
        }
        else
            (attachment.RootEventSlots ??= []).Add(entryIndex);
        return DynamicEventToken(attachment.ViewHandle, id);
    }

    internal void BeginEventBindingPass(ViewEventBindingScope scope, ulong artifact = 0)
    {
        var attachment = RequireActive();
        if (attachment.EventBindingScope != ViewEventBindingScope.None)
        {
            throw new InvalidOperationException(
                "Nested View event-binding passes are not supported."
            );
        }

        if ((scope == ViewEventBindingScope.Demand) != (artifact != 0))
        {
            throw new InvalidOperationException("Demand event bindings require an artifact identity.");
        }
        var pass = checked(++attachment.NextEventBindingPass);
        attachment.EventBindingArtifact = artifact;
        attachment.EventBindingScope = scope;
        attachment.EventBindingPass = scope == ViewEventBindingScope.Demand ? -pass : pass;
    }

    internal void CompleteEventBindingPass(ViewEventBindingScope scope, bool completed)
    {
        var attachment = Active;
        if (attachment is null)
        {
            return;
        }
        attachment.AssertAccess();
        if (attachment.EventBindingScope != scope)
        {
            return;
        }

        if (scope == ViewEventBindingScope.Demand)
        {
            if (!completed)
            {
                ReleaseEventArtifact(attachment.EventBindingArtifact);
            }
        }
        else if (completed && attachment.RootEventSlots is { } slots)
        {
            var entries = attachment.EventEntries!;
            var retained = 0;
            for (var candidate = 0; candidate < slots.Count; candidate++)
            {
                var index = slots[candidate];
                var entry = entries[index];
                if (
                    entry.BinderIndex != 0
                    && IsEntryInScope(entry.LastPass, scope)
                    && entry.LastPass != attachment.EventBindingPass
                )
                {
                    ReleaseEventSlot(attachment, index);
                }
                else slots[retained++] = index;
            }
            slots.RemoveRange(retained, slots.Count - retained);
        }

        attachment.EventBindingScope = ViewEventBindingScope.None;
        attachment.EventBindingPass = 0;
        attachment.EventBindingArtifact = 0;
    }

    internal void ReleaseEventArtifact(ulong artifact)
    {
        var attachment = Active;
        if (attachment is null)
        {
            return;
        }
        attachment.AssertAccess();
        if (attachment.ArtifactEventSlots?.Remove(artifact, out var index) == true)
        {
            while (index != -1)
            {
                var next = attachment.EventEntries![index].NextArtifactSlot;
                ReleaseEventSlot(attachment, index);
                index = next;
            }
        }
    }

    private static void ReleaseEventSlot(ViewEventRegistry attachment, int index)
    {
        var entries = attachment.EventEntries!;
        attachment.EventSlots!.Remove(entries[index].Id);
        entries[index] = default;
        (attachment.FreeEventSlots ??= new Stack<int>()).Push(index);
    }

    private static ulong DynamicEventToken(uint viewHandle, uint id) =>
        ((ulong)viewHandle << 32) | DynamicEventBit | id;

    private void DispatchDynamicClick(uint eventId, ClickEvent clickEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "click");
            return;
        }

        var dispatch = new EventDispatch(clickEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicInput(uint eventId, InputEvent inputEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "input");
            return;
        }

        var dispatch = new EventDispatch(inputEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicSlider(uint eventId, SliderEvent sliderEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "slider");
            return;
        }

        var dispatch = new EventDispatch(sliderEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicDock(uint eventId, DockEvent dockEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "dock");
            return;
        }

        var dispatch = new EventDispatch(dockEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicKey(uint eventId, KeyEvent keyEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "key");
            return;
        }

        var dispatch = new EventDispatch(keyEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicMouse(uint eventId, MouseEvent mouseEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "mouse");
            return;
        }

        var dispatch = new EventDispatch(mouseEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicModifiers(uint eventId, ModifiersEvent modifiersEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "modifiers");
            return;
        }

        var dispatch = new EventDispatch(modifiersEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicHover(uint eventId, HoverEvent hoverEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "hover");
            return;
        }

        var dispatch = new EventDispatch(hoverEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicMouseMove(uint eventId, MouseMoveEvent mouseMoveEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "mouse move");
            return;
        }

        var dispatch = new EventDispatch(mouseMoveEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicScrollWheel(uint eventId, ScrollWheelEvent scrollWheelEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "scroll wheel");
            return;
        }

        var dispatch = new EventDispatch(scrollWheelEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
            entry.Target!,
            entry.Callback!,
            in dispatch
        );
    }

    private void DispatchDynamicFileDrop(uint eventId, FileDropEvent fileDropEvent)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "file drop");
            return;
        }

        var dispatch = new EventDispatch(fileDropEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
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

        var attachment = Active;
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

        if (attachment.EventSlots?.TryGetValue(entryId, out var index) != true)
        {
            entry = default;
            return false;
        }

        entry = entries[index];
        return entry.BinderIndex != 0 && entry.Target is ViewBase { Runtime.IsMounted: true };
    }

    private void MissingDynamicEvent(uint eventId, string eventType)
    {
        if (IsWellFormedEventId(eventId)
            && (Active is null || (eventId & DynamicEventEntryMask) <= Active.NextEventId))
        {
            return;
        }
        throw new InvalidOperationException(
            $"Dynamic {eventType} event entry {eventId & DynamicEventEntryMask} has no callback."
        );
    }

    private static bool IsEntryInScope(long pass, ViewEventBindingScope scope) =>
        scope switch
        {
            ViewEventBindingScope.None => pass == 0,
            ViewEventBindingScope.Render => pass > 0,
            ViewEventBindingScope.Demand => pass < 0,
            _ => false,
        };

    internal void DispatchClickCore(uint eventId, ClickEvent clickEvent) =>
        DispatchDynamicClick(eventId, clickEvent);

    internal void DispatchInputCore(uint eventId, InputEvent inputEvent) =>
        DispatchDynamicInput(eventId, inputEvent);

    internal void DispatchSliderCore(uint eventId, SliderEvent sliderEvent) =>
        DispatchDynamicSlider(eventId, sliderEvent);

    internal void DispatchDockCore(uint eventId, DockEvent dockEvent) =>
        DispatchDynamicDock(eventId, dockEvent);

    internal void DispatchKeyCore(uint eventId, KeyEvent keyEvent) =>
        DispatchDynamicKey(eventId, keyEvent);

    internal void DispatchMouseCore(uint eventId, MouseEvent mouseEvent) =>
        DispatchDynamicMouse(eventId, mouseEvent);

    internal void DispatchModifiersCore(uint eventId, ModifiersEvent modifiersEvent) =>
        DispatchDynamicModifiers(eventId, modifiersEvent);

    internal void DispatchHoverCore(uint eventId, HoverEvent hoverEvent) =>
        DispatchDynamicHover(eventId, hoverEvent);

    internal void DispatchMouseMoveCore(uint eventId, MouseMoveEvent mouseMoveEvent) =>
        DispatchDynamicMouseMove(eventId, mouseMoveEvent);

    internal void DispatchScrollWheelCore(uint eventId, ScrollWheelEvent scrollWheelEvent) =>
        DispatchDynamicScrollWheel(eventId, scrollWheelEvent);

    internal void DispatchFileDropCore(uint eventId, FileDropEvent fileDropEvent) =>
        DispatchDynamicFileDrop(eventId, fileDropEvent);

    internal void DispatchNativeExtensionCore(
        uint eventId,
        NativeExtensionEvent nativeExtensionEvent
    )
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "native extension");
            return;
        }

        var dispatch = new EventDispatch(nativeExtensionEvent);
        EventBinderRegistry.Get(entry.BinderIndex)(
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

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Click)
            {
                throw WrongDispatchKind("click");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "click");
            }
            if (callback is not Action<TView, ClickEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, ClickEvent>", "click");
            }

            typedCallback(typedTarget, dispatch.Click);
        }
    }

    private static class InputBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Input || dispatch.Input is not { } input)
            {
                throw WrongDispatchKind("input");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "input");
            }
            if (callback is not Action<TView, InputEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, InputEvent>", "input");
            }

            typedCallback(typedTarget, input);
        }
    }

    private static class SliderBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Slider)
            {
                throw WrongDispatchKind("slider");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "slider");
            }
            if (callback is not Action<TView, SliderEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, SliderEvent>", "slider");
            }

            typedCallback(typedTarget, dispatch.Slider);
        }
    }

    private static class DockBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Dock)
            {
                throw WrongDispatchKind("dock");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "dock");
            }
            if (callback is not Action<TView, DockEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, DockEvent>", "dock");
            }

            typedCallback(typedTarget, dispatch.Dock);
        }
    }

    private static class KeyBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Key || dispatch.Key is not { } key)
            {
                throw WrongDispatchKind("key");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "key");
            }
            if (callback is not Action<TView, KeyEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, KeyEvent>", "key");
            }

            typedCallback(typedTarget, key);
        }
    }

    private static class MouseBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Mouse)
            {
                throw WrongDispatchKind("mouse");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "mouse");
            }
            if (callback is not Action<TView, MouseEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, MouseEvent>", "mouse");
            }

            typedCallback(typedTarget, dispatch.Mouse);
        }
    }

    private static class ModifiersBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Modifiers)
            {
                throw WrongDispatchKind("modifiers");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "modifiers");
            }
            if (callback is not Action<TView, ModifiersEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, ModifiersEvent>", "modifiers");
            }

            typedCallback(typedTarget, dispatch.Modifiers);
        }
    }

    private static class HoverBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.Hover)
            {
                throw WrongDispatchKind("hover");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "hover");
            }
            if (callback is not Action<TView, HoverEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, HoverEvent>", "hover");
            }

            typedCallback(typedTarget, dispatch.Hover);
        }
    }

    private static class MouseMoveBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.MouseMove)
            {
                throw WrongDispatchKind("mouse move");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "mouse move");
            }
            if (callback is not Action<TView, MouseMoveEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, MouseMoveEvent>", "mouse move");
            }

            typedCallback(typedTarget, dispatch.MouseMove);
        }
    }

    private static class ScrollWheelBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.ScrollWheel)
            {
                throw WrongDispatchKind("scroll wheel");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "scroll wheel");
            }
            if (callback is not Action<TView, ScrollWheelEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, ScrollWheelEvent>", "scroll wheel");
            }

            typedCallback(typedTarget, dispatch.ScrollWheel);
        }
    }

    private static class FileDropBinder<TView>
        where TView : ViewBase
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (dispatch.Kind != EventDispatchKind.FileDrop || dispatch.FileDrop is not { } fileDrop)
            {
                throw WrongDispatchKind("file drop");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "file drop");
            }
            if (callback is not Action<TView, FileDropEvent> typedCallback)
            {
                throw WrongCallback("Action<TView, FileDropEvent>", "file drop");
            }

            typedCallback(typedTarget, fileDrop);
        }
    }

    private static class NativeExtensionBinder<TView, TEvent>
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent>
    {
        internal static readonly int Index = EventBinderRegistry.Add(Invoke);

        private static void Invoke(object target, Delegate callback, in EventDispatch dispatch)
        {
            if (
                dispatch.Kind != EventDispatchKind.NativeExtension
                || dispatch.NativeExtension is not { } nativeExtensionEvent
            )
            {
                throw WrongDispatchKind("native extension");
            }
            if (target is not TView typedTarget)
            {
                throw WrongTarget<TView>(target, "native extension");
            }
            if (callback is not Action<TView, TEvent> typedCallback)
            {
                throw WrongCallback($"Action<TView, {typeof(TEvent).Name}>", "native extension");
            }

            typedCallback(typedTarget, TEvent.Decode(nativeExtensionEvent));
        }
    }

    private static Exception WrongDispatchKind(string eventType) =>
        new InvalidOperationException($"The event binder cannot dispatch a {eventType} event.");

    private static Exception WrongTarget<TView>(object target, string eventType)
        where TView : ViewBase =>
        new InvalidOperationException(
            $"The {eventType} callback requires target type {typeof(TView).FullName}, "
                + $"but received {target.GetType().FullName}."
        );

    private static Exception WrongCallback(string callbackType, string eventType) =>
        new InvalidOperationException(
            $"The {eventType} callback has an incompatible delegate type; expected {callbackType}."
        );
}
