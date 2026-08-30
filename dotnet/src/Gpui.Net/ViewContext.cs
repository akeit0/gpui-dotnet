namespace Gpui;

/// <summary>
/// Lifecycle/control-plane context. Unlike <see cref="RenderContext"/>, this context is for
/// mounted resources and commands; it is never used to build render IR.
/// </summary>
public readonly ref struct ViewContext
{
    private readonly View _view;

    internal ViewContext(View view) => _view = view;

    public Dispatcher Dispatcher => _view.Dispatcher;

    public void Invalidate() => _view.Invalidate();

    /// <summary>
    /// Creates an optional imperative controller for a ui.Scroll() resource owned by this View.
    /// The matching Scroll element may appear later; commands are queued natively until it exists.
    /// </summary>
    public ScrollController CreateScrollController(string key)
    {
        ValidateResourceKey(key);
        return new ScrollController(_view, key);
    }

    /// <summary>
    /// Creates an optional imperative controller for a ui.List() resource owned by this View.
    /// Use it for scroll-to-item and structural splice/reset notifications.
    /// </summary>
    public ListController CreateListController(string key)
    {
        ValidateResourceKey(key);
        return new ListController(_view, key);
    }

    private static void ValidateResourceKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            throw new ArgumentException("A native resource key cannot be empty.", nameof(key));
        }
    }
}
