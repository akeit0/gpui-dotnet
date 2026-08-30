namespace Gpui;

/// <summary>
/// Lifecycle/control-plane context. Unlike <see cref="RenderContext"/>, this context is for
/// mounted resources, dispatch, and future native entities; it is never used to build render IR.
/// </summary>
public readonly ref struct ViewContext
{
    private readonly View _view;

    internal ViewContext(View view) => _view = view;

    public Dispatcher Dispatcher => _view.Dispatcher;

    public void Invalidate() => _view.Invalidate();
}
