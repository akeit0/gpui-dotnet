using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    internal Element RenderRoot(RenderArena* arena)
    {
        try
        {
            BeginRendering();
            AttachRoot();
            BeginComposition(RootView);
            var completed = false;
            try
            {
                var ui = new RenderContext(arena, this, RootView, _application.Theme);
                var element = RootView.RenderCore(ref ui);
                ManagedValidator.Validate(arena, element);
                CompleteComposition(RootView);
                completed = true;
                CommitSnapshotTree();
                return element;
            }
            catch
            {
                if (!completed)
                {
                    AbortComposition(RootView);
                }
                RollBackStagedProps();
                throw;
            }
        }
        finally
        {
            EndRendering();
        }
    }

    internal Element RenderListRange(
        ulong rendererToken,
        uint start,
        uint count,
        RenderArena* arena
    )
    {
        var viewHandle = unchecked((uint)(rendererToken >> 32));
        var rendererId = unchecked((uint)rendererToken);
        if (viewHandle == 0 || rendererId == 0)
        {
            throw new InvalidOperationException("List renderer token 0 is reserved.");
        }
        if (!_viewsByHandle.TryGetValue(viewHandle, out var owner) || !owner.IsMountedCore)
        {
            throw new InvalidOperationException(
                $"List renderer owner View 0x{viewHandle:X8} is no longer mounted."
            );
        }
        if (count is 0 or > 512 || (ulong)start + count > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Volatile.Write(ref _renderingStarted, 1);
        if (Interlocked.CompareExchange(ref _renderingManaged, 1, 0) != 0)
        {
            throw new InvalidOperationException("Nested managed list rendering is not supported.");
        }
        Volatile.Write(ref _notifyAfterRender, 0);
        owner.BeginEventBindingPass(ViewEventBindingScope.ListRange);
        var previousEventBindingOwner = ViewBase.CurrentEventBindingOwner;
        ViewBase.CurrentEventBindingOwner = owner;
        var completed = false;
        try
        {
            var ui = new RenderContext(arena, theme: _application.Theme);
            var batchRoot = ui.Div();
            for (uint offset = 0; offset < count; offset++)
            {
                var index = checked((int)(start + offset));
                var row = owner.RenderListItemCore(rendererId, index, ref ui);
                ArenaWriter.AddChild(batchRoot, row);
            }

            ManagedValidator.Validate(arena, batchRoot);
            completed = true;
            return batchRoot;
        }
        finally
        {
            try
            {
                owner.CompleteEventBindingPass(ViewEventBindingScope.ListRange, completed);
            }
            finally
            {
                ViewBase.CurrentEventBindingOwner = previousEventBindingOwner;
                EndRendering();
            }
        }
    }

    private Element RenderResolvedChild(ViewBase view, RenderArena* destination)
    {
        var state = GetRenderState(view);
        long requiredVersion;
        lock (_renderStateGate)
        {
            requiredVersion = state.RequiredVersion;
        }

        if (state.Fragment is null || state.RenderedVersion != requiredVersion)
        {
            state.Fragment ??= new RenderArenaOwner(64, 256, 128, 4096);
            BeginComposition(view);
            try
            {
                var ui = state.Fragment.BeginRender(this, view, _application.Theme);
                var element = view.RenderCore(ref ui);
                state.Fragment.Validate(element);
                view.ValidateRenderInputs();
                state.Root = element.Node;
                CompleteComposition(view);
                lock (_renderStateGate)
                {
                    state.RenderedVersion = requiredVersion;
                }
            }
            catch
            {
                AbortComposition(view);
                throw;
            }
        }

        return ArenaWriter.AppendFragment(destination, state.Fragment.NativeArena, state.Root);
    }

    private void BeginComposition(ViewBase view)
    {
        if (!_renderingViews.Add(view))
        {
            throw new InvalidOperationException(
                "Managed views cannot recursively render themselves."
            );
        }

        var state = GetRenderState(view);
        state.WorkingChildren?.Clear();
        state.WorkingViews?.Clear();
        state.WorkingNextPosition = 0;
    }

    private void CompleteComposition(ViewBase view)
    {
        if (!_renderingViews.Remove(view))
        {
            throw new InvalidOperationException("Managed view composition was not active.");
        }

        var state = GetRenderState(view);
        (state.StagedChildren, state.WorkingChildren) = (
            state.WorkingChildren,
            state.StagedChildren
        );
        state.WorkingChildren?.Clear();
        state.WorkingViews?.Clear();
        state.HasStagedComposition = true;
    }

    private void AbortComposition(ViewBase view)
    {
        if (!_renderingViews.Remove(view))
        {
            return;
        }

        var state = GetRenderState(view);
        state.WorkingChildren?.Clear();
        state.WorkingViews?.Clear();
        state.WorkingNextPosition = 0;
    }

    private void CommitSnapshotTree()
    {
        _snapshotStack.Clear();
        _snapshotVisited.Clear();

        lock (_renderStateGate)
        {
            _snapshotStack.Push(RootView);
            while (_snapshotStack.TryPop(out var current))
            {
                if (!_snapshotVisited.Add(current))
                {
                    continue;
                }
                if (!_renderStates.TryGetValue(current, out var state))
                {
                    throw new InvalidOperationException(
                        "The committed managed view tree references a missing render state."
                    );
                }

                if (state.HasStagedComposition)
                {
                    (state.Children, state.StagedChildren) = (state.StagedChildren, state.Children);
                    state.StagedChildren?.Clear();
                    state.HasStagedComposition = false;
                    state.Candidates?.Clear();
                }

                current.CommitStagedProps();

                if (state.Children is null)
                {
                    continue;
                }

                foreach (var entry in state.Children.Values)
                {
                    if (!_renderStates.TryGetValue(entry.View, out var childState))
                    {
                        throw new InvalidOperationException(
                            "A committed child View is missing its retained render state."
                        );
                    }
                    if (
                        childState.Parent is not null
                        && !ReferenceEquals(childState.Parent, current)
                    )
                    {
                        throw new InvalidOperationException(
                            "A managed child View cannot be committed under multiple parents."
                        );
                    }
                    childState.Parent = current;
                    _snapshotStack.Push(entry.View);
                }
            }
        }

        BuildUnmountOrder(includeCommittedTree: false);
        foreach (var view in _unmountCandidates)
        {
            try
            {
                Unmount(view);
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
            }
        }
    }

    private void MarkViewFragmentDirty(ViewBase view)
    {
        lock (_renderStateGate)
        {
            if (_renderStates.TryGetValue(view, out var state))
            {
                state.RequiredVersion++;
            }
        }
    }

    private void RollBackStagedProps()
    {
        ViewBase[] views;
        lock (_renderStateGate)
        {
            views = _renderStates.Keys.ToArray();
        }

        foreach (var view in views)
        {
            view.RollBackStagedProps();
        }
    }
}
