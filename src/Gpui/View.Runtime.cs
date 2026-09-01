using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Gpui;

internal delegate void Utf8InputValueDispatcher(
    uint ownerView,
    ReadOnlySpan<byte> utf8Key,
    ReadOnlySpan<byte> utf8Value
);

public abstract partial class ViewBase
{
    private static readonly CancellationToken CancelledLifetime = new(canceled: true);
    private static readonly ConcurrentBag<MountedViewAttachment> UiAttachmentPool = [];
    private static int _uiAttachmentPoolCount;

    private const int MaxPooledUiAttachments = 256;
    private const int MaxRetainedEventEntryCapacity = 256;

    // Any-thread ingress is isolated from the resettable GPUI-thread state below. A caller may
    // briefly retain this route while unmount waits in Deactivate, so routes are never pooled.
    private sealed class ViewCommandRoute
    {
        private readonly object _gate = new();
        private bool _active = true;

        internal ViewCommandRoute(
            uint viewHandle,
            Action<Action> post,
            Action<ViewBase> invalidate,
            Action<uint, ResourceCommand> resourceCommand,
            Utf8InputValueDispatcher utf8InputValue
        )
        {
            ViewHandle = viewHandle;
            Post = post;
            Invalidate = invalidate;
            ResourceCommand = resourceCommand;
            Utf8InputValue = utf8InputValue;
        }

        internal uint ViewHandle { get; }
        internal Action<Action> Post { get; }
        internal Action<ViewBase> Invalidate { get; }
        internal Action<uint, ResourceCommand> ResourceCommand { get; }
        internal Utf8InputValueDispatcher Utf8InputValue { get; }

        internal bool TryPost(Action callback)
        {
            lock (_gate)
            {
                if (!_active)
                {
                    return false;
                }
                Post(callback);
                return true;
            }
        }

        internal bool TryInvalidate(ViewBase owner)
        {
            lock (_gate)
            {
                if (!_active)
                {
                    return false;
                }
                Invalidate(owner);
                return true;
            }
        }

        internal bool TryResourceCommand(ResourceCommand command)
        {
            lock (_gate)
            {
                if (!_active)
                {
                    return false;
                }
                ResourceCommand(ViewHandle, command);
                return true;
            }
        }

        internal bool TryUtf8InputValue(ReadOnlySpan<byte> utf8Key, ReadOnlySpan<byte> utf8Value)
        {
            lock (_gate)
            {
                if (!_active)
                {
                    return false;
                }
                Utf8InputValue(ViewHandle, utf8Key, utf8Value);
                return true;
            }
        }

        internal void Deactivate()
        {
            lock (_gate)
            {
                _active = false;
            }
        }
    }

    // Only GPUI foreground callbacks access this object. That confinement makes complete reset
    // and immediate reuse safe after the command route has been deactivated.
    private sealed class MountedViewAttachment
    {
        internal uint ViewHandle { get; private set; }
        internal int ManagedThreadId { get; private set; }
        internal List<EventEntry>? EventEntries { get; set; }
        internal Stack<int>? FreeEventIds { get; set; }
        internal ViewEventBindingScope EventBindingScope { get; set; }
        internal int EventBindingPass { get; set; }
        internal int NextEventBindingPass { get; set; }
        internal ulong NextResourceKeyId { get; set; }

        internal void Activate(uint viewHandle)
        {
            ViewHandle = viewHandle;
            ManagedThreadId = Environment.CurrentManagedThreadId;
        }

        internal void Reset()
        {
            AssertAccess();
            ViewHandle = 0;
            if (EventEntries is { Capacity: > MaxRetainedEventEntryCapacity })
            {
                EventEntries = null;
                FreeEventIds = null;
            }
            else
            {
                EventEntries?.Clear();
                FreeEventIds?.Clear();
            }
            EventBindingScope = ViewEventBindingScope.None;
            EventBindingPass = 0;
            NextEventBindingPass = 0;
            NextResourceKeyId = 0;
            ManagedThreadId = 0;
        }

        internal void AssertAccess()
        {
            if (ManagedThreadId != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException(
                    "Managed View render and event state is confined to the GPUI application thread."
                );
            }
        }
    }

    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _lifetimeSource;
    private CancellationToken _lifetime;
    private ViewCommandRoute? _commandRoute;
    private MountedViewAttachment? _uiAttachment;
    private int _lifecycle;

    private const int LifecycleCreated = 0;
    private const int LifecycleMounting = 1;
    private const int LifecycleMounted = 2;
    private const int LifecycleUnmounting = 3;
    private const int LifecycleUnmounted = 4;

    private protected ViewBase()
    {
        Dispatcher = new Dispatcher(this);
    }

    /// <summary>Posts managed work to this view's GPUI UI thread.</summary>
    protected internal Dispatcher Dispatcher { get; }

    /// <summary>
    /// Allocates the next auto resource-key id for this view. Ids are monotonic per view
    /// instance: ref-bound controllers retain the id assigned by their first render, so later
    /// renders and re-renders reuse it instead of allocating.
    /// </summary>
    internal ulong NextResourceKeyId()
    {
        var attachment = RequireUiAttachment();
        return ++attachment.NextResourceKeyId;
    }

    /// <summary>True while this View is owned by a running managed View tree.</summary>
    protected bool IsMounted
    {
        get
        {
            var lifecycle = Volatile.Read(ref _lifecycle);
            return lifecycle is LifecycleMounting or LifecycleMounted;
        }
    }

    /// <summary>
    /// True after this View permanently leaves framework ownership. An unmounted View instance
    /// cannot be mounted or used again, even when application code still holds a reference.
    /// </summary>
    protected bool IsUnmounted => Volatile.Read(ref _lifecycle) >= LifecycleUnmounting;

    /// <summary>
    /// Cancellation token for this View instance's complete one-shot lifetime. It is allocated
    /// lazily, remains stable once requested, and is cancelled before <see cref="OnUnmounted"/>
    /// runs.
    /// </summary>
    protected CancellationToken Lifetime
    {
        get
        {
            lock (_lifecycleGate)
            {
                if (_lifetime.CanBeCanceled)
                {
                    return _lifetime;
                }
                if (_lifecycle >= LifecycleUnmounting)
                {
                    return _lifetime = CancelledLifetime;
                }

                _lifetimeSource = new CancellationTokenSource();
                return _lifetime = _lifetimeSource.Token;
            }
        }
    }

    /// <summary>Called at most once when this View enters a running managed View tree.</summary>
    protected virtual void OnMounted(ref ViewContext context) { }

    /// <summary>
    /// Called exactly once if mounting began, after this View permanently leaves framework
    /// ownership. <see cref="Lifetime"/> is cancelled and runtime commands are unavailable.
    /// </summary>
    protected virtual void OnUnmounted() { }

    /// <summary>Schedules a dirty render. Safe to call from any thread while mounted.</summary>
    protected internal void Invalidate()
    {
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryInvalidate(this))
        {
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        }
    }

    internal uint RuntimeViewHandle
    {
        get
        {
            var attachment = Volatile.Read(ref _uiAttachment);
            if (attachment is null)
            {
                return 0;
            }
            attachment.AssertAccess();
            return attachment.ViewHandle;
        }
    }

    internal bool IsMountedCore
    {
        get
        {
            var lifecycle = Volatile.Read(ref _lifecycle);
            return lifecycle is LifecycleMounting or LifecycleMounted;
        }
    }
    internal bool IsUnmountedCore => Volatile.Read(ref _lifecycle) >= LifecycleUnmounting;

    internal void AttachRuntime(
        uint viewHandle,
        Action<Action> post,
        Action<ViewBase> invalidate,
        Action<uint, ResourceCommand> resourceCommand,
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

        lock (_lifecycleGate)
        {
            if (_lifecycle >= LifecycleUnmounting)
            {
                throw new ObjectDisposedException(
                    GetType().FullName,
                    "An unmounted View instance cannot be mounted again."
                );
            }
            if (_lifecycle != LifecycleCreated)
            {
                throw new InvalidOperationException("The view is already mounted.");
            }

            Volatile.Write(
                ref _commandRoute,
                new ViewCommandRoute(viewHandle, post, invalidate, resourceCommand, utf8InputValue)
            );
            _uiAttachment = RentUiAttachment(viewHandle);
            Volatile.Write(ref _lifecycle, LifecycleMounting);
        }

        try
        {
            var context = new ViewContext(this);
            OnMounted(ref context);
            lock (_lifecycleGate)
            {
                if (
                    _lifecycle != LifecycleMounting
                    || _commandRoute is null
                    || _uiAttachment is null
                )
                {
                    throw new ObjectDisposedException(
                        GetType().FullName,
                        "The View left framework ownership while it was mounting."
                    );
                }
                Volatile.Write(ref _lifecycle, LifecycleMounted);
            }
        }
        catch (Exception mountFailure)
        {
            try
            {
                UnmountRuntime();
            }
            catch (Exception unmountFailure)
            {
                throw new AggregateException(mountFailure, unmountFailure);
            }
            throw;
        }
    }

    internal void UnmountRuntime()
    {
        bool invokeLifecycle;
        CancellationTokenSource? lifetimeSource;
        ViewCommandRoute? commandRoute;
        MountedViewAttachment? uiAttachment;
        lock (_lifecycleGate)
        {
            if (_lifecycle >= LifecycleUnmounting)
            {
                return;
            }

            uiAttachment = _uiAttachment;
            uiAttachment?.AssertAccess();
            invokeLifecycle = _lifecycle is LifecycleMounting or LifecycleMounted;
            Volatile.Write(ref _lifecycle, LifecycleUnmounting);
            commandRoute = Interlocked.Exchange(ref _commandRoute, null);
            Volatile.Write(ref _uiAttachment, null);
            if (!_lifetime.CanBeCanceled)
            {
                _lifetime = CancelledLifetime;
            }
            lifetimeSource = _lifetimeSource;
        }

        commandRoute?.Deactivate();
        if (uiAttachment is not null)
        {
            ReturnUiAttachment(uiAttachment);
        }

        Exception? cancellationFailure = null;
        Exception? lifecycleFailure = null;
        try
        {
            try
            {
                lifetimeSource?.Cancel();
            }
            catch (Exception exception)
            {
                cancellationFailure = exception;
            }

            if (invokeLifecycle)
            {
                try
                {
                    OnUnmounted();
                }
                catch (Exception exception)
                {
                    lifecycleFailure = exception;
                }
            }
        }
        finally
        {
            try
            {
                ReleaseRetainedState();
                lifetimeSource?.Dispose();
            }
            finally
            {
                Volatile.Write(ref _lifecycle, LifecycleUnmounted);
            }
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
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryResourceCommand(command))
        {
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        }
    }

    internal void DispatchUtf8InputValue(ReadOnlySpan<byte> utf8Key, ReadOnlySpan<byte> utf8Value)
    {
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryUtf8InputValue(utf8Key, utf8Value))
        {
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        }
    }

    internal void InvalidateFromController()
    {
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryInvalidate(this))
        {
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        }
    }

    internal void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryPost(callback))
        {
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        }
    }

    private static MountedViewAttachment RentUiAttachment(uint viewHandle)
    {
        if (UiAttachmentPool.TryTake(out var attachment))
        {
            Interlocked.Decrement(ref _uiAttachmentPoolCount);
        }
        else
        {
            attachment = new MountedViewAttachment();
        }

        attachment.Activate(viewHandle);
        return attachment;
    }

    private static void ReturnUiAttachment(MountedViewAttachment attachment)
    {
        attachment.Reset();
        if (Interlocked.Increment(ref _uiAttachmentPoolCount) <= MaxPooledUiAttachments)
        {
            UiAttachmentPool.Add(attachment);
            return;
        }

        Interlocked.Decrement(ref _uiAttachmentPoolCount);
    }

    private MountedViewAttachment RequireUiAttachment(
        string message = "The view is not mounted in a GPUI application."
    )
    {
        var attachment = _uiAttachment ?? throw new InvalidOperationException(message);
        attachment.AssertAccess();
        return attachment;
    }
}
