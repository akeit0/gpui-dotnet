using Gpui.Interop.Internal;

namespace Gpui;

/// <summary>
/// Implemented by the source generator for every managed view. The runtime uses the static factory
/// to create framework-owned roots and children without reflection or Activator.CreateInstance, preserving
/// NativeAOT/trimming friendliness.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IGeneratedViewFactory<TSelf>
    where TSelf : ViewBase
{
    static abstract TSelf CreateGpuiView(ViewConstruction construction);
}

/// <summary>
/// Common authoring base for <see cref="View"/> and <see cref="View{TProps}"/>. Applications inherit
/// one of those two concrete API shapes rather than inheriting this class directly.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public abstract class ViewBase
{
    private protected ViewBase(ViewConstruction construction)
    {
        Ownership = construction.Bind(this);
        Runtime = new ViewRuntime(this);
    }

    internal ViewOwnership Ownership { get; }

    internal ViewRuntime Runtime { get; }

    /// <summary>Posts callbacks to this View's UI thread while it remains mounted.</summary>
    protected internal Dispatcher Dispatcher => Runtime.Dispatcher;

    /// <summary>True while the framework owns this View in a mounted tree.</summary>
    protected bool IsMounted => Runtime.IsMounted;

    /// <summary>True after terminal retirement; a CLR reference does not retain UI ownership.</summary>
    protected bool IsUnmounted => Runtime.IsUnmounted;

    /// <summary>Stable, lazily created token cancelled before owned cleanup runs.</summary>
    protected CancellationToken Lifetime => Runtime.Lifetime;

    /// <summary>Requests a dirty render. Safe from any thread while mounted.</summary>
    protected internal void Invalidate() => Runtime.Invalidate();

    /// <summary>Binds a generated element-only row renderer to this View's native handle.</summary>
    protected ListItemRenderer BindListRenderer(uint rendererId) =>
        Runtime.BindListRenderer(rendererId);

    /// <summary>Builds render IR without observable state changes or external effects.</summary>
    internal abstract Element RenderCore(ref RenderContext ui);

    /// <summary>Generated dispatch for element-only virtual rows; called on demand.</summary>
    protected virtual Element RenderListItem(uint rendererId, int index, ref RenderContext ui) =>
        throw new InvalidOperationException(
            $"Generated list renderer 0x{rendererId:X8} is not defined on {GetType().Name}."
        );

    internal Element RenderListItemCore(uint rendererId, int index, ref RenderContext ui) =>
        RenderListItem(rendererId, index, ref ui);

    internal virtual void ValidateRenderInputs() { }

    internal virtual void CommitStagedProps() { }

    internal virtual void RollBackStagedProps() { }

    internal virtual void ReleaseRetainedState() { }
}

/// <summary>
/// Managed application unit without parent-supplied props. C# owns durable application state and
/// Rust owns GPUI objects, native render state, and frame-sensitive work.
/// </summary>
public abstract class View : ViewBase
{
    protected View(ViewConstruction construction)
        : base(construction) { }

    protected abstract Element Render(ref RenderContext ui);

    internal override Element RenderCore(ref RenderContext ui) => Render(ref ui);
}

/// <summary>
/// View with parent-supplied render inputs. Props are updated before mount/render and are compared
/// using EqualityComparer&lt;TProps&gt;.Default; changed props invalidate only this child's retained
/// fragment, not the whole application tree. Props must implement IEquatable&lt;TProps&gt;; records
/// and record structs provide this automatically.
/// </summary>
public abstract class View<TProps> : ViewBase
    where TProps : IEquatable<TProps>
{
    protected View(ViewConstruction construction)
        : base(construction) { }

    protected abstract Element Render(in TProps props, ref RenderContext ui);

    internal override Element RenderCore(ref RenderContext ui) => Render(in _latestProps, ref ui);

    [Flags]
    private enum PropsState : byte
    {
        None = 0,
        Latest = 1 << 0,
        Staged = 1 << 1,
        Committed = 1 << 2,
    }

    private TProps _committedProps = default!;

    // The latest declaration is also the retained-fragment comparison baseline. Fragment
    // dirty state clears only when native accepts the snapshot containing that declaration.
    private TProps _latestProps = default!;
    private PropsState _propsState;

    /// <summary>
    /// Most recently accepted props, independent of rendering phase. Unaccepted Views have none.
    /// </summary>
    protected ref readonly TProps CommittedProps
    {
        get
        {
            if (!HasPropsState(PropsState.Committed))
                throw new InvalidOperationException("This View has no accepted props.");
            return ref _committedProps;
        }
    }

    internal bool StageProps(in TProps props)
    {
        var changed =
            !HasPropsState(PropsState.Latest)
            || !EqualityComparer<TProps>.Default.Equals(_latestProps, props);
        _latestProps = props;
        _propsState |= PropsState.Latest | PropsState.Staged;
        return changed;
    }

    internal override void ValidateRenderInputs()
    {
        if (!HasPropsState(PropsState.Staged))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} was rendered without staging its required props."
            );
        }
    }

    internal override void CommitStagedProps()
    {
        if (!HasPropsState(PropsState.Staged))
        {
            return;
        }
        _committedProps = _latestProps;
        _propsState = (_propsState | PropsState.Committed) & ~PropsState.Staged;
    }

    internal override void RollBackStagedProps()
    {
        _propsState &= ~PropsState.Staged;
    }

    internal override void ReleaseRetainedState()
    {
        _committedProps = default!;
        _latestProps = default!;
        _propsState = PropsState.None;
    }

    private bool HasPropsState(PropsState state) => (_propsState & state) != 0;
}

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IGeneratedViewFactory<TSelf, TProps>
    where TProps : IEquatable<TProps>
    where TSelf : View<TProps>
{
    static abstract TSelf CreateGpuiView(ViewConstruction construction, TProps initialProps);
}

internal unsafe interface IViewRenderer
{
    Element RenderChild<TView>(ViewBase parent, ChildSlot slot, RenderArenaOwner destination)
        where TView : View, IGeneratedViewFactory<TView>;

    Element RenderChild<TView, TProps>(
        ViewBase parent,
        ChildSlot slot,
        in TProps props,
        RenderArenaOwner destination
    )
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps>;
}
