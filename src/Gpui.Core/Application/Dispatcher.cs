namespace Gpui;

/// <summary>Schedules managed callbacks on a running view's GPUI UI thread.</summary>
public readonly struct Dispatcher
{
    private readonly Gpui.Interop.Internal.ViewRuntime? _runtime;

    internal Dispatcher(Gpui.Interop.Internal.ViewRuntime runtime) => _runtime = runtime;

    /// <summary>
    /// Enqueues a callback and wakes the native view. Calls made before the application starts or
    /// after its window closes throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public void Post(Action callback) => Runtime.Post(callback);

    /// <summary>Posts explicit state; use a static callback to avoid a capturing delegate.</summary>
    public void Post<TState>(TState state, Action<TState> callback) =>
        Runtime.Post(state, callback);

    private Gpui.Interop.Internal.ViewRuntime Runtime =>
        _runtime ?? throw new InvalidOperationException("The dispatcher is not initialized.");
}
