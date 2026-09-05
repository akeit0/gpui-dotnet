namespace Gpui;

/// <summary>Schedules managed callbacks on a running view's GPUI UI thread.</summary>
public sealed class Dispatcher
{
    private readonly Gpui.Interop.Internal.ViewRuntime _runtime;

    internal Dispatcher(Gpui.Interop.Internal.ViewRuntime runtime) => _runtime = runtime;

    /// <summary>
    /// Enqueues a callback and wakes the native view. Calls made before the application starts or
    /// after its window closes throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public void Post(Action callback) => _runtime.Post(callback);
}
