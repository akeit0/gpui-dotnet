using System.Runtime.ExceptionServices;

namespace Gpui;

/// <summary>
/// Implemented by the source generator for every <see cref="GpuiViewAttribute"/> view. The
/// runtime uses the static factory to create framework-owned children without reflection or
/// Activator.CreateInstance, preserving NativeAOT/trimming friendliness.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IGeneratedViewFactory<TSelf>
    where TSelf : View
{
    static abstract TSelf CreateGpuiView();
}

/// <summary>
/// Managed application unit. C# owns durable application state and Rust owns GPUI objects,
/// native render state, and frame-sensitive work.
/// </summary>
public abstract class View
{
    private readonly object _runtimeGate = new();
    private Action<Action>? _post;
    private Action<View>? _invalidate;
    private CancellationTokenSource? _mountLifetime;
    private uint _runtimeViewHandle;
    private int _mounted;

    protected View() => Dispatcher = new Dispatcher(this);

    /// <summary>Posts managed work to this view's GPUI UI thread.</summary>
    protected internal Dispatcher Dispatcher { get; }

    /// <summary>True between <see cref="OnMounted"/> and <see cref="OnUnmounted"/>.</summary>
    protected bool IsMounted => Volatile.Read(ref _mounted) != 0;

    /// <summary>
    /// Cancellation token for the current mount. It is cancelled before <see cref="OnUnmounted"/>
    /// runs. Async event handlers should pass it to cancellable operations that must not outlive
    /// this View's current tree membership.
    /// </summary>
    protected CancellationToken Lifetime
    {
        get
        {
            lock (_runtimeGate)
            {
                return _mountLifetime?.Token ?? CancellationToken.None;
            }
        }
    }

    /// <summary>Called exactly once for each attachment to a running managed view tree.</summary>
    protected virtual void OnMounted(ref ViewContext context) { }

    /// <summary>Called when the view leaves the committed tree or the application stops.</summary>
    protected virtual void OnUnmounted(ref ViewContext context) { }

    /// <summary>Schedules a dirty render. Safe to call from any thread while mounted.</summary>
    protected internal void Invalidate()
    {
        var invalidate =
            Volatile.Read(ref _invalidate)
            ?? throw new InvalidOperationException(
                "The view is not mounted in a GPUI application."
            );
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
            new InvalidOperationException(
                $"Generated click handler 0x{handlerId:X8} is not defined."
            )
        );

    protected abstract Element Render(ref RenderContext ui);

    internal Element RenderCore(ref RenderContext ui) => Render(ref ui);

    internal ValueTask DispatchClickCore(uint handlerId, ClickEvent clickEvent) =>
        DispatchClickAsync(handlerId, clickEvent);

    internal uint RuntimeViewHandle => Volatile.Read(ref _runtimeViewHandle);

    internal virtual bool RequiresProps => false;

    internal void AttachRuntime(uint viewHandle, Action<Action> post, Action<View> invalidate)
    {
        if (viewHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewHandle));
        }
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(invalidate);

        var lifetime = new CancellationTokenSource();
        lock (_runtimeGate)
        {
            if (_mounted != 0)
            {
                lifetime.Dispose();
                throw new InvalidOperationException("The view is already mounted.");
            }

            _post = post;
            _invalidate = invalidate;
            _mountLifetime = lifetime;
            _runtimeViewHandle = viewHandle;
            Volatile.Write(ref _mounted, 1);
        }

        try
        {
            var context = new ViewContext(this);
            OnMounted(ref context);
        }
        catch (Exception mountFailure)
        {
            Exception? cancellationFailure = null;
            try
            {
                lifetime.Cancel();
            }
            catch (Exception exception)
            {
                cancellationFailure = exception;
            }
            finally
            {
                lock (_runtimeGate)
                {
                    Volatile.Write(ref _mounted, 0);
                    _runtimeViewHandle = 0;
                    _mountLifetime = null;
                    _invalidate = null;
                    _post = null;
                }
                lifetime.Dispose();
            }

            if (cancellationFailure is not null)
            {
                throw new AggregateException(mountFailure, cancellationFailure);
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

        var lifetime = Volatile.Read(ref _mountLifetime);
        Exception? cancellationFailure = null;
        Exception? lifecycleFailure = null;
        try
        {
            try
            {
                lifetime?.Cancel();
            }
            catch (Exception exception)
            {
                cancellationFailure = exception;
            }

            try
            {
                var context = new ViewContext(this);
                OnUnmounted(ref context);
            }
            catch (Exception exception)
            {
                lifecycleFailure = exception;
            }
        }
        finally
        {
            lock (_runtimeGate)
            {
                Volatile.Write(ref _mounted, 0);
                _runtimeViewHandle = 0;
                _mountLifetime = null;
                _invalidate = null;
                _post = null;
            }
            lifetime?.Dispose();
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

    internal void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var post =
            Volatile.Read(ref _post)
            ?? throw new InvalidOperationException(
                "The view is not mounted in a GPUI application."
            );
        post(callback);
    }
}

/// <summary>
/// View with parent-supplied render inputs. Props are updated before mount/render and are compared
/// using EqualityComparer&lt;TProps&gt;.Default; changed props invalidate only this child's retained
/// fragment, not the whole application tree.
/// </summary>
public abstract class View<TProps> : View
{
    private TProps _props = default!;
    private bool _hasProps;

    /// <summary>The most recently committed-or-staged props supplied by the parent.</summary>
    protected ref readonly TProps Props
    {
        get
        {
            if (!_hasProps)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} requires props. Render it with ui.Child<TView, TProps>(...)."
                );
            }
            return ref _props;
        }
    }

    internal override bool RequiresProps => true;

    internal bool ApplyProps(in TProps props)
    {
        var changed = !_hasProps || !EqualityComparer<TProps>.Default.Equals(_props, props);
        _props = props;
        _hasProps = true;
        return changed;
    }
}

internal unsafe interface IViewRenderer
{
    Element RenderChild<TView>(View parent, ChildSlot slot, Interop.RenderArena* destination)
        where TView : View, IGeneratedViewFactory<TView>;

    Element RenderChild<TView, TProps>(
        View parent,
        ChildSlot slot,
        in TProps props,
        Interop.RenderArena* destination
    )
        where TView : View<TProps>, IGeneratedViewFactory<TView>;

    Element RenderChild(View view, View parent, ChildSlot slot, Interop.RenderArena* destination);

    Element RenderChild<TProps>(
        View<TProps> view,
        View parent,
        ChildSlot slot,
        in TProps props,
        Interop.RenderArena* destination
    );
}
