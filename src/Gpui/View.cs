namespace Gpui;

/// <summary>
/// Implemented by the source generator for every managed view. The runtime uses the static factory
/// to create framework-owned children without reflection or Activator.CreateInstance, preserving
/// NativeAOT/trimming friendliness.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IGeneratedViewFactory<TSelf>
    where TSelf : ViewBase
{
    static abstract TSelf CreateGpuiView();
}

/// <summary>
/// Common runtime base for <see cref="View"/> and <see cref="View{TProps}"/>. Applications inherit
/// one of those two concrete API shapes rather than inheriting this class directly.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public abstract partial class ViewBase
{
    /// <summary>
    /// Binds a generated list-item renderer id to this mounted view. The token is consumed by
    /// native virtualization and is never a managed delegate.
    /// </summary>
    protected ListItemRenderer BindListRenderer(uint rendererId)
    {
        if (rendererId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rendererId), "Renderer id 0 is reserved.");
        }

        var attachment = RequireUiAttachment(
            "Generated list renderers can only be materialized while the view is mounted. "
                + "Use Rows.<renderer> from Render(), not from a constructor or field initializer."
        );

        return new ListItemRenderer(((ulong)attachment.ViewHandle << 32) | rendererId);
    }

    /// <summary>
    /// Generated views override this for [GpuiListItem] methods. List item rendering is synchronous
    /// and element-only; retained child View composition is intentionally not available inside a
    /// virtualized row batch.
    /// </summary>
    protected virtual Element RenderListItem(uint rendererId, int index, ref RenderContext ui) =>
        throw new InvalidOperationException(
            $"Generated list renderer 0x{rendererId:X8} is not defined on {GetType().Name}."
        );

    /// <summary>
    /// Renders the view tree into native IR. Rendering must be synchronous, repeatable, and free of
    /// externally visible side effects: the framework may invoke it more than once per frame when
    /// the render arena needs to grow before the final render is accepted, and list batch renderers
    /// are invoked lazily whenever the viewport requires an uncached range.
    /// </summary>
    protected abstract Element Render(ref RenderContext ui);

    internal Element RenderCore(ref RenderContext ui)
    {
        BeginEventBindingPass(ViewEventBindingScope.Render);
        var previousEventBindingOwner = _currentEventBindingOwner;
        _currentEventBindingOwner = this;
        var completed = false;
        try
        {
            var element = Render(ref ui);
            completed = true;
            return element;
        }
        finally
        {
            try
            {
                CompleteEventBindingPass(ViewEventBindingScope.Render, completed);
            }
            finally
            {
                _currentEventBindingOwner = previousEventBindingOwner;
            }
        }
    }

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
public abstract class View : ViewBase { }

/// <summary>
/// View with parent-supplied render inputs. Props are updated before mount/render and are compared
/// using EqualityComparer&lt;TProps&gt;.Default; changed props invalidate only this child's retained
/// fragment, not the whole application tree. Props must implement IEquatable&lt;TProps&gt;; records
/// and record structs provide this automatically.
/// </summary>
public abstract class View<TProps> : ViewBase
    where TProps : IEquatable<TProps>
{
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
    // RequiredVersion/RenderedVersion records whether a failed render actually accepted it.
    private TProps _latestProps = default!;
    private PropsState _propsState;

    /// <summary>
    /// The props for the active render, or the most recently committed props outside rendering.
    /// A candidate that never commits retains its latest declaration through unmount cleanup.
    /// </summary>
    protected ref readonly TProps Props
    {
        get
        {
            if (HasPropsState(PropsState.Staged))
            {
                return ref _latestProps;
            }
            if (HasPropsState(PropsState.Committed))
            {
                return ref _committedProps;
            }
            if (HasPropsState(PropsState.Latest))
            {
                return ref _latestProps;
            }
            throw new InvalidOperationException(
                $"{GetType().Name} requires props. Render it with ui.Child<TView, TProps>(...)."
            );
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

internal unsafe interface IViewRenderer
{
    Element RenderChild<TView>(ViewBase parent, ChildSlot slot, Interop.RenderArena* destination)
        where TView : View, IGeneratedViewFactory<TView>;

    Element RenderChild<TView, TProps>(
        ViewBase parent,
        ChildSlot slot,
        in TProps props,
        Interop.RenderArena* destination
    )
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView>;
}
