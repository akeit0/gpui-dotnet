using System.Runtime.CompilerServices;

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
            throw new InvalidOperationException(
                "Event state is confined to the GPUI application thread."
            );
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

    private struct EventEntry
    {
        internal object? Target;
        internal Delegate? Callback;

        // Decode routing for extension bindings; always zero for built-in families,
        // which dispatch through the statically known per-payload invoker.
        internal int ExtensionIndex;

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
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed input callback on this mounted View.</summary>
    internal ulong BindInput<TView>(Action<TView, InputEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal ulong BindInputWrite<TView>(Action<TView, InputWriteResult> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal ulong BindSlider<TView>(Action<TView, SliderEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal ulong BindListActivation<TView>(Action<TView, ListActivationEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal ulong BindListContextMenu<TView>(Action<TView, ListContextMenuEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal ulong BindListTooltip<TView>(Action<TView, ListTooltipEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal ulong BindShortcut<TView>(Action<TView> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal void DispatchShortcutCore(uint eventId)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, "shortcut");
            return;
        }
        Unsafe.As<Action<ViewBase>>(entry.Callback!)((ViewBase)entry.Target!);
    }

    private void DispatchPayloadCore<TEvent>(uint eventId, TEvent payload, string eventType)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, eventType);
            return;
        }
        // Pairing is the caller's contract — entries always store the callback together
        // with the target it was bound for, bind sites reject nulls, tokens are routed by
        // native kind to the matching dispatch core, and entry ids are never reused within
        // their owning identity — so the reinterpreted callback always receives its own
        // view type.
        Unsafe.As<Action<ViewBase, TEvent>>(entry.Callback!)((ViewBase)entry.Target!, payload);
    }

    internal ulong BindListSelection<TView>(Action<TView, ListSelectionEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed Dock area callback on this mounted View.</summary>
    internal ulong BindDock<TView>(Action<TView, DockEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed key-event callback on this mounted View.</summary>
    internal ulong BindKey<TView>(Action<TView, KeyEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed mouse-event callback on this mounted View.</summary>
    internal ulong BindMouse<TView>(Action<TView, MouseEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed modifier-key callback on this mounted View.</summary>
    internal ulong BindModifiers<TView>(Action<TView, ModifiersEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed hover-state callback on this mounted View.</summary>
    internal ulong BindHover<TView>(Action<TView, HoverEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed mouse-move callback on this mounted View.</summary>
    internal ulong BindMouseMove<TView>(Action<TView, MouseMoveEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed scroll-wheel callback on this mounted View.</summary>
    internal ulong BindScrollWheel<TView>(Action<TView, ScrollWheelEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    /// <summary>Registers a typed file-drop callback on this mounted View.</summary>
    internal ulong BindFileDrop<TView>(Action<TView, FileDropEvent> callback)
        where TView : ViewBase => BindDynamicEvent(_owner!, callback);

    internal ulong BindNativeExtensionEvent<TView, TEvent>(Action<TView, TEvent> callback)
        where TView : ViewBase
        where TEvent : INativeExtensionEvent<TEvent> =>
        BindDynamicEvent(_owner!, callback, NativeExtensionInvoker<TEvent>.Index);

    private ulong BindDynamicEvent(ViewBase target, Delegate callback, int extensionIndex = 0)
    {
        return (_currentEventBindingOwner ?? _owner!).Runtime.Events.BindDynamicEventCore(
            target,
            callback,
            extensionIndex
        );
    }

    private ulong BindDynamicEventCore(ViewBase target, Delegate callback, int extensionIndex)
    {
        var attachment = RequireActive(
            "Event callbacks can only be bound while the View is mounted and rendering."
        );
        var entries = attachment.EventEntries ??= [];
        var scope = attachment.EventBindingScope;
        var pass = attachment.EventBindingPass;
        var scopeSlots = attachment.RootEventSlots;
        var demand = scope == ViewEventBindingScope.Demand;
        var artifactHead =
            demand
            && attachment.ArtifactEventSlots is { } artifactSlots
            && artifactSlots.TryGetValue(attachment.EventBindingArtifact, out var head)
                ? head
                : -1;
        var count = scopeSlots?.Count ?? 0;
        var index =
            demand ? artifactHead
            : count == 0 ? -1
            : scopeSlots![0];
        for (var candidate = 0; index != -1; candidate++)
        {
            var current = entries[index];
            if (
                current.ExtensionIndex == extensionIndex
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
            index =
                demand ? current.NextArtifactSlot
                : candidate + 1 < count ? scopeSlots![candidate + 1]
                : -1;
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
            ExtensionIndex = extensionIndex,
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
            throw new InvalidOperationException(
                "Demand event bindings require an artifact identity."
            );
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
                    entry.Id != 0
                    && IsEntryInScope(entry.LastPass, scope)
                    && entry.LastPass != attachment.EventBindingPass
                )
                {
                    ReleaseEventSlot(attachment, index);
                }
                else
                    slots[retained++] = index;
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
        return entry.Id != 0 && entry.Target is ViewBase { Runtime.IsMounted: true };
    }

    private void MissingDynamicEvent(uint eventId, string eventType)
    {
        if (
            IsWellFormedEventId(eventId)
            && (Active is null || (eventId & DynamicEventEntryMask) <= Active.NextEventId)
        )
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
        DispatchPayloadCore(eventId, clickEvent, "click");

    internal void DispatchInputCore(uint eventId, InputEvent inputEvent) =>
        DispatchPayloadCore(eventId, inputEvent, "input");

    internal void DispatchInputWriteCore(uint eventId, InputWriteResult result) =>
        DispatchPayloadCore(eventId, result, "input write");

    internal void DispatchListActivationCore(uint eventId, ListActivationEvent activation) =>
        DispatchPayloadCore(eventId, activation, "list activation");

    internal void DispatchListTooltipCore(uint eventId, ListTooltipEvent request) =>
        DispatchPayloadCore(eventId, request, "list tooltip");

    internal void DispatchListContextMenuCore(uint eventId, ListContextMenuEvent request) =>
        DispatchPayloadCore(eventId, request, "list context menu");

    internal void DispatchListSelectionCore(uint eventId, ListSelectionEvent selection) =>
        DispatchPayloadCore(eventId, selection, "list selection");

    internal void DispatchSliderCore(uint eventId, SliderEvent sliderEvent) =>
        DispatchPayloadCore(eventId, sliderEvent, "slider");

    internal void DispatchDockCore(uint eventId, DockEvent dockEvent) =>
        DispatchPayloadCore(eventId, dockEvent, "dock");

    internal void DispatchKeyCore(uint eventId, KeyEvent keyEvent) =>
        DispatchPayloadCore(eventId, keyEvent, "key");

    internal void DispatchMouseCore(uint eventId, MouseEvent mouseEvent) =>
        DispatchPayloadCore(eventId, mouseEvent, "mouse");

    internal void DispatchModifiersCore(uint eventId, ModifiersEvent modifiersEvent) =>
        DispatchPayloadCore(eventId, modifiersEvent, "modifiers");

    internal void DispatchHoverCore(uint eventId, HoverEvent hoverEvent) =>
        DispatchPayloadCore(eventId, hoverEvent, "hover");

    internal void DispatchMouseMoveCore(uint eventId, MouseMoveEvent mouseMoveEvent) =>
        DispatchPayloadCore(eventId, mouseMoveEvent, "mouse move");

    internal void DispatchScrollWheelCore(uint eventId, ScrollWheelEvent scrollWheelEvent) =>
        DispatchPayloadCore(eventId, scrollWheelEvent, "scroll wheel");

    internal void DispatchFileDropCore(uint eventId, FileDropEvent fileDropEvent) =>
        DispatchPayloadCore(eventId, fileDropEvent, "file drop");

    internal void DispatchNativeExtensionCore(uint eventId, NativeExtensionEvent packet) =>
        DispatchExtensionCore(eventId, packet, "native extension");

    private void DispatchExtensionCore(uint eventId, NativeExtensionEvent packet, string eventType)
    {
        if (!TryGetDynamicEvent(eventId, out var entry))
        {
            MissingDynamicEvent(eventId, eventType);
            return;
        }
        ExtensionInvokerRegistry.GetExtensionInvoker(entry.ExtensionIndex)(
            entry.Target!,
            entry.Callback!,
            packet
        );
    }

    private static class ExtensionInvokerRegistry
    {
        // Extension event types form an open universe, so decode routing stays dynamic.
        // Built-in families dispatch statically and never enter this registry.
        private static readonly object ExtensionLock = new();
        private static Action<object, Delegate, NativeExtensionEvent>[] _invokers = [];
        private static int _count;

        internal static int AddExtension(Action<object, Delegate, NativeExtensionEvent> invoker)
        {
            lock (ExtensionLock)
            {
                if (_count == _invokers.Length)
                {
                    Array.Resize(ref _invokers, _invokers.Length == 0 ? 4 : _invokers.Length * 2);
                }
                _invokers[_count] = invoker;
                return ++_count;
            }
        }

        internal static Action<object, Delegate, NativeExtensionEvent> GetExtensionInvoker(
            int index
        )
        {
            // Registration happens during binding; dispatch only reads. Snapshotting the
            // current table keeps the event path lock-free; bounds and null checks cover a
            // stale snapshot.
            var invokers = Volatile.Read(ref _invokers);
            if ((uint)(index - 1) < (uint)invokers.Length && invokers[index - 1] is { } invoker)
            {
                return invoker;
            }
            throw new InvalidOperationException("The event binder index is invalid.");
        }
    }

    private static class NativeExtensionInvoker<TEvent>
        where TEvent : INativeExtensionEvent<TEvent>
    {
        internal static readonly int Index = ExtensionInvokerRegistry.AddExtension(Invoke);

        internal static void Invoke(object target, Delegate callback, NativeExtensionEvent packet)
        {
            Unsafe.As<Action<ViewBase, TEvent>>(callback)((ViewBase)target, TEvent.Decode(packet));
        }
    }
}
