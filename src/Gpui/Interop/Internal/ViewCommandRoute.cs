namespace Gpui.Interop.Internal;

// Any-thread ingress is isolated from the resettable GPUI-thread state below. A caller may
// briefly retain this route while unmount waits in Deactivate, so routes are never pooled.
internal sealed class ViewCommandRoute
{
    private readonly object _gate = new();
    private bool _active;

    internal ViewCommandRoute(
        uint viewHandle,
        Action<IIngressWork> post,
        Action<ViewBase> invalidate,
        Action<uint, ResourceCommand> resourceCommand,
        Utf8InputValueDispatcher utf8InputValue,
        NativeExtensionCommandDispatcher nativeExtensionCommand,
        Action ensureAvailable
    )
    {
        ViewHandle = viewHandle;
        Post = post;
        Invalidate = invalidate;
        ResourceCommand = resourceCommand;
        Utf8InputValue = utf8InputValue;
        NativeExtensionCommand = nativeExtensionCommand;
        EnsureAvailable = ensureAvailable;
    }

    internal uint ViewHandle { get; }
    internal Action<IIngressWork> Post { get; }
    internal Action<ViewBase> Invalidate { get; }
    internal Action<uint, ResourceCommand> ResourceCommand { get; }
    internal Utf8InputValueDispatcher Utf8InputValue { get; }
    internal NativeExtensionCommandDispatcher NativeExtensionCommand { get; }
    internal Action EnsureAvailable { get; }

    internal bool IsActive => Volatile.Read(ref _active);

    internal bool TryPost(Action callback) => TryPost(new RoutedCallback(this, callback));

    internal bool TryPost<TState>(TState state, Action<TState> callback) =>
        TryPost(new RoutedCallback<TState>(this, state, callback));

    internal bool TryPost(IIngressWork work)
    {
        lock (_gate)
        {
            if (!_active)
            {
                return false;
            }
            Post(work);
            return true;
        }
    }

    private sealed class RoutedCallback(ViewCommandRoute route, Action callback) : IIngressWork
    {
        public void Invoke()
        {
            if (route.IsActive)
                callback();
        }
    }

    private sealed class RoutedCallback<TState>(ViewCommandRoute route, TState state, Action<TState> callback) : IIngressWork
    {
        public void Invoke()
        {
            if (route.IsActive)
                callback(state);
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

    internal bool TryNativeExtensionCommand(
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
        lock (_gate)
        {
            if (!_active)
            {
                return false;
            }
            NativeExtensionCommand(
                ViewHandle,
                schemaVersion,
                schemaHash,
                extensionId,
                componentKind,
                utf8Key,
                command,
                flags,
                expectedRevision,
                payload
            );
            return true;
        }
    }

    internal void Deactivate()
    {
        lock (_gate)
        {
            Volatile.Write(ref _active, false);
        }
    }

    internal void Activate()
    {
        lock (_gate)
        {
            Volatile.Write(ref _active, true);
        }
    }
}
