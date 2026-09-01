namespace Gpui;

/// <summary>Schedules managed callbacks on a running view's GPUI UI thread.</summary>
public sealed class Dispatcher
{
    private readonly ViewBase _view;

    internal Dispatcher(ViewBase view) => _view = view;

    /// <summary>
    /// Enqueues a callback and wakes the native view. Calls made before the application starts or
    /// after its window closes throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public void Post(Action callback) => _view.Post(callback);
}
