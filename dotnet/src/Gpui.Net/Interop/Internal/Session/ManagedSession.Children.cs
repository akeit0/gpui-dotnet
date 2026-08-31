using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession : IViewRenderer
{
    Element IViewRenderer.RenderChild<TView>(
        View parent,
        ChildSlot requestedSlot,
        RenderArena* destination
    )
    {
        var child = ResolveFrameworkChild<TView>(parent, requestedSlot);
        return RenderResolvedChild(child, destination);
    }

    Element IViewRenderer.RenderChild<TView, TProps>(
        View parent,
        ChildSlot requestedSlot,
        in TProps props,
        RenderArena* destination
    )
    {
        var child = ResolveFrameworkChild<TView, TProps>(parent, requestedSlot, in props);
        return RenderResolvedChild(child, destination);
    }

    Element IViewRenderer.RenderChild(
        View view,
        View parent,
        ChildSlot requestedSlot,
        RenderArena* destination
    )
    {
        ResolveExplicitChild(view, parent, requestedSlot);
        return RenderResolvedChild(view, destination);
    }

    Element IViewRenderer.RenderChild<TProps>(
        View<TProps> view,
        View parent,
        ChildSlot requestedSlot,
        in TProps props,
        RenderArena* destination
    )
    {
        ResolveExplicitChild(view, parent, requestedSlot, in props);
        return RenderResolvedChild(view, destination);
    }

    private TView ResolveFrameworkChild<TView>(View parent, ChildSlot requestedSlot)
        where TView : View, IGeneratedViewFactory<TView>
    {
        EnsureParentIsRendering(parent);
        var parentState = GetRenderState(parent);
        var slot = NormalizeSlot(parentState, requestedSlot);
        EnsureSlotUnused(parentState, slot);

        if (TryGetFrameworkChild<TView>(parentState, slot, out var existing))
        {
            if (existing.View.RequiresProps)
            {
                throw new InvalidOperationException(
                    $"{typeof(TView).Name} derives from View<TProps>; render it with the props overload."
                );
            }
            RegisterWorkingChild(parentState, slot, existing);
            return (TView)existing.View;
        }

        var child = CreateFrameworkView<TView>();
        if (child.RequiresProps)
        {
            throw new InvalidOperationException(
                $"{typeof(TView).Name} derives from View<TProps>; render it with the props overload."
            );
        }

        AttachResolvedCandidate(child, parent, parentState, slot, ChildOwnership.Framework);
        return child;
    }

    private TView ResolveFrameworkChild<TView, TProps>(
        View parent,
        ChildSlot requestedSlot,
        in TProps props
    )
        where TView : View<TProps>, IGeneratedViewFactory<TView>
    {
        EnsureParentIsRendering(parent);
        var parentState = GetRenderState(parent);
        var slot = NormalizeSlot(parentState, requestedSlot);
        EnsureSlotUnused(parentState, slot);

        if (TryGetFrameworkChild<TView>(parentState, slot, out var existing))
        {
            var child = (TView)existing.View;
            if (child.ApplyProps(in props))
            {
                MarkViewFragmentDirty(child);
            }
            RegisterWorkingChild(parentState, slot, existing);
            return child;
        }

        var created = CreateFrameworkView<TView>();
        _ = created.ApplyProps(in props);
        AttachResolvedCandidate(created, parent, parentState, slot, ChildOwnership.Framework);
        return created;
    }

    private void ResolveExplicitChild(View child, View parent, ChildSlot requestedSlot)
    {
        if (child.RequiresProps)
        {
            throw new InvalidOperationException(
                $"{child.GetType().Name} derives from View<TProps>; render it with the props overload."
            );
        }

        EnsureParentIsRendering(parent);
        EnsureNotRecursive(child);
        var parentState = GetRenderState(parent);
        var slot = NormalizeSlot(parentState, requestedSlot);
        EnsureSlotUnused(parentState, slot);
        if (TryGetExplicitChild(parentState, slot, child, out var existing))
        {
            RegisterWorkingChild(parentState, slot, existing);
            return;
        }

        AttachResolvedCandidate(child, parent, parentState, slot, ChildOwnership.Explicit);
    }

    private void ResolveExplicitChild<TProps>(
        View<TProps> child,
        View parent,
        ChildSlot requestedSlot,
        in TProps props
    )
    {
        EnsureParentIsRendering(parent);
        EnsureNotRecursive(child);
        var parentState = GetRenderState(parent);
        var slot = NormalizeSlot(parentState, requestedSlot);
        EnsureSlotUnused(parentState, slot);
        if (TryGetExplicitChild(parentState, slot, child, out var existing))
        {
            if (child.ApplyProps(in props))
            {
                MarkViewFragmentDirty(child);
            }
            RegisterWorkingChild(parentState, slot, existing);
            return;
        }

        _ = child.ApplyProps(in props);
        AttachResolvedCandidate(child, parent, parentState, slot, ChildOwnership.Explicit);
    }

    private static TView CreateFrameworkView<TView>()
        where TView : View, IGeneratedViewFactory<TView>
    {
        var child = TView.CreateGpuiView();
        return child
            ?? throw new InvalidOperationException(
                $"The generated factory for {typeof(TView).FullName} returned null."
            );
    }

    private void AttachResolvedCandidate(
        View child,
        View parent,
        RetainedViewState parentState,
        ChildSlot slot,
        ChildOwnership ownership
    )
    {
        EnsureNotRecursive(child);
        var childState = PrepareOwnership(child, parent);
        try
        {
            Attach(child);
        }
        catch
        {
            RollBackPreparedOwnership(child, childState, parent);
            throw;
        }

        var entry = new ChildEntry(child, ownership);
        GetCandidates(parentState)[slot] = entry;
        RegisterWorkingChild(parentState, slot, entry);
    }

    private void EnsureNotRecursive(View child)
    {
        if (_renderingViews.Contains(child))
        {
            throw new InvalidOperationException(
                "Managed views cannot recursively render themselves."
            );
        }
    }

    private static bool TryGetFrameworkChild<TView>(
        RetainedViewState parentState,
        ChildSlot slot,
        out ChildEntry entry
    )
        where TView : View
    {
        if (
            parentState.Candidates is not null
            && parentState.Candidates.TryGetValue(slot, out entry)
            && entry.Ownership == ChildOwnership.Framework
            && entry.View.GetType() == typeof(TView)
        )
        {
            return true;
        }

        if (
            parentState.Children is not null
            && parentState.Children.TryGetValue(slot, out entry)
            && entry.Ownership == ChildOwnership.Framework
            && entry.View.GetType() == typeof(TView)
        )
        {
            return true;
        }

        entry = default;
        return false;
    }

    private static bool TryGetExplicitChild(
        RetainedViewState parentState,
        ChildSlot slot,
        View requested,
        out ChildEntry entry
    )
    {
        if (
            parentState.Candidates is not null
            && parentState.Candidates.TryGetValue(slot, out entry)
            && entry.Ownership == ChildOwnership.Explicit
            && ReferenceEquals(entry.View, requested)
        )
        {
            return true;
        }

        if (
            parentState.Children is not null
            && parentState.Children.TryGetValue(slot, out entry)
            && entry.Ownership == ChildOwnership.Explicit
            && ReferenceEquals(entry.View, requested)
        )
        {
            return true;
        }

        entry = default;
        return false;
    }

    private RetainedViewState PrepareOwnership(View child, View parent)
    {
        lock (_renderStateGate)
        {
            var childState = GetRenderState(child);
            if (childState.Parent is not null && !ReferenceEquals(childState.Parent, parent))
            {
                throw new InvalidOperationException(
                    $"Managed View '{child.GetType().Name}' is already owned by "
                        + $"'{childState.Parent.GetType().Name}'. A View instance may have only one parent."
                );
            }
            childState.Parent = parent;
            return childState;
        }
    }

    private void RollBackPreparedOwnership(View child, RetainedViewState childState, View parent)
    {
        lock (_renderStateGate)
        {
            if (
                !_attachedViews.Contains(child)
                && ReferenceEquals(childState.Parent, parent)
                && (childState.Children is null || childState.Children.Count == 0)
                && (childState.Candidates is null || childState.Candidates.Count == 0)
                && !childState.HasStagedComposition
            )
            {
                childState.Parent = null;
                if (_renderStates.Remove(child, out var removed))
                {
                    removed.Fragment?.Dispose();
                }
            }
        }
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
    )
    {
        if (!GetWorkingViews(parentState).Add(entry.View))
        {
            throw new InvalidOperationException(
                "The same managed child View instance cannot be rendered in multiple slots of one parent."
            );
        }
        GetWorkingChildren(parentState).Add(slot, entry);
    }

    private void EnsureParentIsRendering(View parent)
    {
        if (!_renderingViews.Contains(parent))
        {
            throw new InvalidOperationException(
                "A child view can only be rendered while its parent is rendering."
            );
        }
    }
}
