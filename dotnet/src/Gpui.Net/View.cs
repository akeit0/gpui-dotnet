namespace Gpui;

/// <summary>
/// Managed application unit. C# owns durable application state and Rust owns GPUI objects,
/// native render state, and frame-sensitive work.
/// </summary>
public abstract class View
{
    private readonly object _runtimeGate = new();
    private Action<Action>? _post;
    private Action<View>? _invalidate;
    private uint _runtimeViewHandle;
    private int _mounted;

    protected View() => Dispatcher = new Dispatcher(this);

    /// <summary>Posts managed work to this view's GPUI UI thread.</summary>
    protected internal Dispatcher Dispatcher { get; }

    /// <summary>True between <see cref="OnMounted"/> and <see cref="OnUnmounted"/>.</summary>
    protected bool IsMounted => Volatile.Read(ref _mounted) != 0;

    /// <summary>Called exactly once for each attachment to a running managed view tree.</summary>
    protected virtual void OnMounted(ref ViewContext context) { }

    /// <summary>Called when the view leaves the committed tree or the application stops.</summary>
    protected virtual void OnUnmounted(ref ViewContext context) { }

    /// <summary>Schedules a dirty render. Safe to call from any thread while mounted.</summary>
    protected internal void Invalidate()
    {
        var invalidate =
            Volatile.Read(ref _invalidate)
            ?? throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        invalidate(this);
    }

    /// <summary>
    /// Binds a generated local handler id to this mounted view without allocating or registering
    /// a delegate. Source-generated event accessors call this method.
    /// </summary>
    protected Callback<ClickEvent> BindClick(uint handlerId)
    {
        if (handlerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handlerId), "Handler id 0 is reserved.");
        }

        var viewHandle = Volatile.Read(ref _runtimeViewHandle);
        if (viewHandle == 0 || Volatile.Read(ref _mounted) == 0)
        {
            throw new InvalidOperationException(
                "Generated callbacks can only be materialized while the view is mounted. "
                    + "Use Events.<handler> from Render(), not from a constructor or field initializer."
            );
        }

        return new Callback<ClickEvent>(((ulong)viewHandle << 32) | handlerId);
    }

    /// <summary>
    /// Generated views override this with a switch over compile-time handler ids. The base
    /// implementation is intentionally strict so stale or malformed event tokens fail loudly.
    /// </summary>
    protected virtual ValueTask DispatchClickAsync(uint handlerId, ClickEvent clickEvent) =>
        ValueTask.FromException(
            new InvalidOperationException($"Generated click handler 0x{handlerId:X8} is not defined.")
        );

    protected abstract Element Render(ref RenderContext ui);

    /// <summary>Renders a child view into the current managed snapshot.</summary>
    protected static Element RenderView(View view, ref RenderContext ui)
    {
        ArgumentNullException.ThrowIfNull(view);
        return ui.RenderView(view);
    }

    internal Element RenderCore(ref RenderContext ui) => Render(ref ui);

    internal ValueTask DispatchClickCore(uint handlerId, ClickEvent clickEvent) =>
        DispatchClickAsync(handlerId, clickEvent);

    internal uint RuntimeViewHandle => Volatile.Read(ref _runtimeViewHandle);

    internal void AttachRuntime(uint viewHandle, Action<Action> post, Action<View> invalidate)
    {
        if (viewHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewHandle));
        }
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(invalidate);

        lock (_runtimeGate)
        {
            if (_mounted != 0)
            {
                throw new InvalidOperationException("The view is already mounted.");
            }

            _post = post;
            _invalidate = invalidate;
            _runtimeViewHandle = viewHandle;
            Volatile.Write(ref _mounted, 1);
        }

        try
        {
            var context = new ViewContext(this);
            OnMounted(ref context);
        }
        catch
        {
            lock (_runtimeGate)
            {
                Volatile.Write(ref _mounted, 0);
                _runtimeViewHandle = 0;
                _invalidate = null;
                _post = null;
            }
            throw;
        }
    }

    internal void DetachRuntime()
    {
        if (Volatile.Read(ref _mounted) == 0)
        {
            return;
        }

        try
        {
            var context = new ViewContext(this);
            OnUnmounted(ref context);
        }
        finally
        {
            lock (_runtimeGate)
            {
                Volatile.Write(ref _mounted, 0);
                _runtimeViewHandle = 0;
                _invalidate = null;
                _post = null;
            }
        }
    }

    internal void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var post =
            Volatile.Read(ref _post)
            ?? throw new InvalidOperationException("The view is not mounted in a GPUI application.");
        post(callback);
    }
}

internal unsafe interface IViewRenderer
{
    Element RenderChild(View view, View parent, Interop.RenderArena* destination);
}
