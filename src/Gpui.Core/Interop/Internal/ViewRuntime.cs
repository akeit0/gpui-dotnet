using System.Runtime.ExceptionServices;

namespace Gpui.Interop.Internal;

internal delegate void Utf8InputValueDispatcher(
    uint ownerView,
    ReadOnlySpan<byte> utf8Key,
    ReadOnlySpan<byte> utf8Value,
    ResourceCommandKind command,
    ulong expectedRevision,
    ulong policies
);

internal delegate void NativeExtensionCommandDispatcher(
    uint ownerView,
    uint schemaVersion,
    ulong schemaHash,
    ReadOnlySpan<byte> extensionId,
    ReadOnlySpan<byte> componentKind,
    ReadOnlySpan<byte> utf8Key,
    ushort command,
    ushort flags,
    ulong expectedRevision,
    ReadOnlySpan<byte> payload
);

internal sealed class ViewRuntime
{
    private static readonly CancellationToken CancelledLifetime = new(canceled: true);

    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _lifetimeSource;
    private CancellationToken _lifetime;
    private ViewCommandRoute? _commandRoute;
    private MountedViewAttachment? _uiAttachment;
    private int _lifecycle;

    // One-shot identity state: queued invalidation never reads a pooled UI attachment.
    private int _invalidationPending;

    internal bool TryQueueInvalidation() =>
        Interlocked.CompareExchange(ref _invalidationPending, 1, 0) == 0;

    internal void ConsumeInvalidation() => Volatile.Write(ref _invalidationPending, 0);

    private const int LifecycleCreated = 0;
    private const int LifecyclePrepared = 1;
    private const int LifecycleMounted = 3;
    private const int LifecycleUnmounting = 4;
    private const int LifecycleUnmounted = 5;

    private readonly ViewBase _owner;

    internal ViewRuntime(ViewBase owner) => _owner = owner;

    internal ViewEventRegistry Events => RequireUiAttachment().Events;

    /// <summary>Posts managed work to this view's GPUI UI thread.</summary>
    internal Dispatcher Dispatcher => new(this);

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
    internal bool IsMounted
    {
        get
        {
            var lifecycle = Volatile.Read(ref _lifecycle);
            return lifecycle == LifecycleMounted;
        }
    }

    /// <summary>
    /// True after this View permanently leaves framework ownership. An unmounted View instance
    /// cannot be mounted or used again, even when application code still holds a reference.
    /// </summary>
    internal bool IsUnmounted => Volatile.Read(ref _lifecycle) >= LifecycleUnmounting;

    /// <summary>
    /// Cancellation token for this View instance's complete one-shot lifetime. It is allocated
    /// lazily, remains stable once requested, and is cancelled before application cleanup.
    /// </summary>
    internal CancellationToken Lifetime
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

    /// <summary>Schedules a dirty render. Safe to call from any thread while mounted.</summary>
    internal void Invalidate()
    {
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryInvalidate(_owner))
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

    internal void PrepareRuntime(
        uint viewHandle,
        Action<IIngressWork> post,
        Action<ViewBase> invalidate,
        Action<uint, ResourceCommand> resourceCommand,
        Utf8InputValueDispatcher utf8InputValue,
        NativeExtensionCommandDispatcher nativeExtensionCommand,
        Action ensureAvailable
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
        ArgumentNullException.ThrowIfNull(nativeExtensionCommand);
        ArgumentNullException.ThrowIfNull(ensureAvailable);

        lock (_lifecycleGate)
        {
            if (_lifecycle >= LifecycleUnmounting)
            {
                throw new ObjectDisposedException(
                    _owner.GetType().FullName,
                    "An unmounted View instance cannot be mounted again."
                );
            }
            if (_lifecycle != LifecycleCreated)
            {
                throw new InvalidOperationException("The view is already mounted.");
            }

            Volatile.Write(
                ref _commandRoute,
                new ViewCommandRoute(
                    viewHandle,
                    post,
                    invalidate,
                    resourceCommand,
                    utf8InputValue,
                    nativeExtensionCommand,
                    ensureAvailable
                )
            );
            _uiAttachment = MountedViewAttachment.Rent(_owner, viewHandle);
            Volatile.Write(ref _lifecycle, LifecyclePrepared);
        }
    }

    internal WorkScope GetWorkScope()
    {
        RequireActiveRoute();
        return GetConstructionWorkScope();
    }

    internal ViewCommandRoute RequireActiveRoute()
    {
        var route = Volatile.Read(ref _commandRoute);
        if (!IsMounted || route is null || !route.IsActive)
            throw new InvalidOperationException("The View has not been accepted or has retired.");
        route.EnsureAvailable();
        return route;
    }

    internal bool TryPostOwned(IIngressWork work) =>
        Volatile.Read(ref _commandRoute)?.TryPost(work) == true;

    private WorkScope? _constructionWork;

    internal WorkScope GetConstructionWorkScope() => _constructionWork ??= new WorkScope(this);

    internal void MountRuntime()
    {
        RequireUiAttachment();
        lock (_lifecycleGate)
        {
            if (_lifecycle == LifecycleMounted)
                return;
            if (_lifecycle != LifecyclePrepared)
                throw new InvalidOperationException("Only a prepared View can activate.");
            _commandRoute!.Activate();
            Volatile.Write(ref _lifecycle, LifecycleMounted);
        }
    }

    internal void UnmountRuntime()
    {
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
        _constructionWork?.Revoke();
        _owner.Ownership.RevokeEffects();
        Exception? workFailure = null;
        try
        {
            _constructionWork?.Retire();
        }
        catch (Exception exception)
        {
            workFailure = exception;
        }
        _constructionWork = null;
        if (uiAttachment is not null)
        {
            MountedViewAttachment.Return(uiAttachment);
        }

        Exception? cancellationFailure = null;
        Exception? lifecycleFailure = workFailure;
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

            try
            {
                _owner.Ownership.Retire();
            }
            catch (Exception exception)
            {
                lifecycleFailure = lifecycleFailure is null
                    ? exception
                    : new AggregateException(lifecycleFailure, exception);
            }
        }
        finally
        {
            try
            {
                _owner.ReleaseRetainedState();
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

    internal void DispatchUtf8InputValue(
        ReadOnlySpan<byte> utf8Key,
        ReadOnlySpan<byte> utf8Value,
        ResourceCommandKind command = ResourceCommandKind.InputSetValue,
        ulong expectedRevision = 0,
        ulong policies = 0
    )
    {
        var route = Volatile.Read(ref _commandRoute);
        if (
            route is null
            || !route.TryUtf8InputValue(utf8Key, utf8Value, command, expectedRevision, policies)
        )
        {
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        }
    }

    internal void DispatchNativeExtensionCommand(
        uint schemaVersion,
        ulong schemaHash,
        ReadOnlySpan<byte> extensionId,
        ReadOnlySpan<byte> componentKind,
        ReadOnlySpan<byte> utf8Key,
        ushort command,
        ushort flags,
        ulong expectedRevision,
        ReadOnlySpan<byte> payload
    )
    {
        var route = Volatile.Read(ref _commandRoute);
        if (
            route is null
            || !route.TryNativeExtensionCommand(
                schemaVersion,
                schemaHash,
                extensionId,
                componentKind,
                utf8Key,
                command,
                flags,
                expectedRevision,
                payload
            )
        )
        {
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        }
    }

    internal void InvalidateFromController()
    {
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryInvalidate(_owner))
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

    internal void Post<TState>(TState state, Action<TState> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var route = Volatile.Read(ref _commandRoute);
        if (route is null || !route.TryPost(state, callback))
            throw new InvalidOperationException("The view is not mounted in a GPUI application.");
    }

    private MountedViewAttachment RequireUiAttachment(
        string message = "The view is not mounted in a GPUI application."
    )
    {
        var attachment = _uiAttachment ?? throw new InvalidOperationException(message);
        attachment.AssertAccess();
        return attachment;
    }

    internal ListItemRenderer BindListRenderer(uint rendererId)
    {
        if (rendererId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rendererId), "Renderer id 0 is reserved.");
        }

        var attachment = RequireUiAttachment(
            "Generated list renderers can only be materialized while the view is mounted. "
                + "Use Items.<renderer> from Render(), not from a constructor or field initializer."
        );

        return new ListItemRenderer(((ulong)attachment.ViewHandle << 32) | rendererId);
    }

    internal Element RenderCore(ref RenderContext ui)
    {
        Events.BeginEventBindingPass(ViewEventBindingScope.Render);
        var previousEventBindingOwner = ViewEventRegistry.CurrentEventBindingOwner;
        ViewEventRegistry.CurrentEventBindingOwner = _owner;
        var completed = false;
        try
        {
            _owner.Ownership.Pass = checked(_owner.Ownership.Pass + 1);
            var element = _owner.RenderCore(ref ui);
            completed = true;
            return element;
        }
        finally
        {
            try
            {
                Events.CompleteEventBindingPass(ViewEventBindingScope.Render, completed);
            }
            finally
            {
                ViewEventRegistry.CurrentEventBindingOwner = previousEventBindingOwner;
            }
        }
    }
}
