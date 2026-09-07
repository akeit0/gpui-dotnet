using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession : IViewRenderer
{
    Element IViewRenderer.RenderChild<TView>(
        ViewBase parent,
        ChildSlot requestedSlot,
        RenderArenaOwner destination
    )
    {
        var child = ResolveFrameworkChild<TView>(parent, requestedSlot);
        return RenderResolvedChild(child, destination);
    }

    Element IViewRenderer.RenderChild<TView, TProps>(
        ViewBase parent,
        ChildSlot requestedSlot,
        in TProps props,
        RenderArenaOwner destination
    )
    {
        var child = ResolveFrameworkChild<TView, TProps>(parent, requestedSlot, in props);
        return RenderResolvedChild(child, destination);
    }

    private TView ResolveFrameworkChild<TView>(ViewBase parent, ChildSlot requestedSlot)
        where TView : View, IGeneratedViewFactory<TView>
    {
        EnsureParentIsRendering(parent);
        var parentState = GetRenderState(parent);
        var slot = NormalizeSlot(parentState, requestedSlot);
        EnsureSlotUnused(parentState, slot);

        if (TryGetFrameworkChild<TView>(parentState, slot, out var existing))
        {
            RegisterWorkingChild(parentState, slot, existing);
            return (TView)existing.View;
        }

        var child = ViewFactory.Create<TView>(parent.Ownership.Window);
        AttachResolvedCandidate(child, parent, parentState, slot);
        return child;
    }

    private TView ResolveFrameworkChild<TView, TProps>(
        ViewBase parent,
        ChildSlot requestedSlot,
        in TProps props
    )
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps>
    {
        EnsureParentIsRendering(parent);
        var parentState = GetRenderState(parent);
        var slot = NormalizeSlot(parentState, requestedSlot);
        EnsureSlotUnused(parentState, slot);

        if (TryGetFrameworkChild<TView>(parentState, slot, out var existing))
        {
            var child = (TView)existing.View;
            if (child.StageProps(in props))
            {
                MarkViewFragmentDirty(child);
            }
            RegisterWorkingChild(parentState, slot, existing);
            return child;
        }

        var created = ViewFactory.Create<TView, TProps>(props, parent.Ownership.Window);
        AttachResolvedCandidate(created, parent, parentState, slot);
        return created;
    }

    private void AttachResolvedCandidate(
        ViewBase child,
        ViewBase parent,
        RetainedViewState parentState,
        ChildSlot slot
    )
    {
        try
        {
            Attach(child);
            GetRenderState(child).Parent = parent;
        }
        catch
        {
            child.Runtime.UnmountRuntime();
            throw;
        }

        var entry = new ChildEntry(child);
        GetCandidates(parentState)[slot] = entry;
        RegisterWorkingChild(parentState, slot, entry);
    }

    private static bool TryGetFrameworkChild<TView>(
        RetainedViewState parentState,
        ChildSlot slot,
        out ChildEntry entry
    )
        where TView : ViewBase
    {
        if (slot.IsPositional
            && parentState.Children is not null
            && parentState.Children.TryGetValue(slot, out var accepted)
            && accepted.View.GetType() != typeof(TView))
        {
            throw new InvalidOperationException(
                $"Accepted positional slot '{slot}' cannot change View type. Use an explicit key."
            );
        }

        if (
            parentState.Children is not null
            && parentState.Children.TryGetValue(slot, out entry)
            && entry.View.GetType() == typeof(TView)
        )
        {
            return true;
        }

        entry = default;
        return false;
    }

    private static ChildSlot NormalizeSlot(RetainedViewState state, ChildSlot requestedSlot)
    {
        if (!requestedSlot.IsAuto)
        {
            return requestedSlot;
        }

        if (state.WorkingNextPosition == uint.MaxValue)
        {
            throw new InvalidOperationException(
                "The managed child positional slot space was exhausted."
            );
        }

        return ChildSlot.Positional(state.WorkingNextPosition++);
    }

    private static void EnsureSlotUnused(RetainedViewState parentState, ChildSlot slot)
    {
        if (parentState.WorkingChildren?.ContainsKey(slot) == true)
        {
            throw new InvalidOperationException(
                $"Managed child slot '{slot}' was rendered more than once in a single Render()."
            );
        }
    }

    private static void RegisterWorkingChild(
        RetainedViewState parentState,
        ChildSlot slot,
        ChildEntry entry
    ) =>
        GetWorkingChildren(parentState).Add(slot, entry);

    private void EnsureParentIsRendering(ViewBase parent)
    {
        if (!_renderingViews.Contains(parent))
        {
            throw new InvalidOperationException(
                "A child view can only be rendered while its parent is rendering."
            );
        }
    }
}
