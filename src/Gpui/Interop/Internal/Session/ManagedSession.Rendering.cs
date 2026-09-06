using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    internal Element RenderRoot(RenderArena* arena)
    {
        ThrowIfUnavailable();
        using var execution = Execution.Enter(ExecutionPhase.Ingress);
        RequireAcceptedRender();
        try
        {
            BeginRendering();
            AttachRoot();
            BeginComposition(RootView);
            var completed = false;
            try
            {
                using var reads = GetRenderState(RootView).Consumer!.Begin();
                var ui = new RenderContext(arena, this, RootView, _application.Theme);
                var element = RootView.Runtime.RenderCore(ref ui);
                ThrowIfUnavailable();
                ManagedValidator.Validate(arena, element);
                CompleteComposition(RootView);
                completed = true;
                _pendingRenderRevision = checked(++_nextRenderRevision);
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
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            EndRendering();
        }
    }

    internal Element RenderListRange(
        ulong rendererToken,
        ulong source,
        uint start,
        uint count,
        RenderArena* arena,
        out ulong artifact
    )
    {
        ThrowIfUnavailable();
        using var execution = Execution.Enter(ExecutionPhase.DemandRender);
        RequireAcceptedRender();
        var viewHandle = unchecked((uint)(rendererToken >> 32));
        var rendererId = unchecked((uint)rendererToken);
        if (viewHandle == 0 || rendererId == 0)
        {
            throw new InvalidOperationException("List renderer token 0 is reserved.");
        }
        if (!_viewsByHandle.TryGetValue(viewHandle, out var owner) || !owner.Runtime.IsMounted)
        {
            throw new InvalidOperationException(
                $"List renderer owner View 0x{viewHandle:X8} is no longer mounted."
            );
        }
        if (count is 0 or > 512 || (ulong)start + count > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        artifact = CreateDemandArtifact(source, owner);
        Volatile.Write(ref _renderingStarted, 1);
        if (Interlocked.CompareExchange(ref _renderingManaged, 1, 0) != 0)
        {
            throw new InvalidOperationException("Nested managed list rendering is not supported.");
        }
        Volatile.Write(ref _notifyAfterRender, 0);
        var previousEventBindingOwner = ViewEventRegistry.CurrentEventBindingOwner;
        var completed = false;
        try
        {
            owner.Runtime.Events.BeginEventBindingPass(ViewEventBindingScope.ListRange, artifact);
            ViewEventRegistry.CurrentEventBindingOwner = owner;
            using var reads = _demandArtifacts[artifact].Begin();
            var ui = new RenderContext(arena, theme: _application.Theme);
            var batchRoot = ui.Div();
            for (uint offset = 0; offset < count; offset++)
            {
                var index = checked((int)(start + offset));
                var row = owner.RenderListItemCore(rendererId, index, ref ui);
                ArenaWriter.AddChild(batchRoot, row);
            }

            ManagedValidator.Validate(arena, batchRoot);
            ThrowIfUnavailable();
            completed = true;
            return batchRoot;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            try
            {
                owner.Runtime.Events.CompleteEventBindingPass(ViewEventBindingScope.ListRange, completed);
                if (!completed)
                {
                    if (_demandArtifacts.Remove(artifact, out var failed))
                        failed.Dispose();
                }
            }
            finally
            {
                ViewEventRegistry.CurrentEventBindingOwner = previousEventBindingOwner;
                EndRendering();
            }
        }
    }

    private Element RenderResolvedChild(ViewBase view, RenderArena* destination)
    {
        ThrowIfUnavailable();
        var state = GetRenderState(view);

        if (state.Fragment is null || state.Dirty)
        {
            state.Fragment ??= new RenderArenaOwner(64, 256, 128, 4096);
            BeginComposition(view);
            try
            {
                using var reads = state.Consumer!.Begin();
                var ui = state.Fragment.BeginRender(this, view, _application.Theme);
                var element = view.Runtime.RenderCore(ref ui);
                ThrowIfUnavailable();
                state.Fragment.Validate(element);
                view.ValidateRenderInputs();
                state.Root = element.Node;
                CompleteComposition(view);
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
        state.Consumer ??= new ReactiveConsumer(this, view);
        state.Dirty = true;
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
        _acceptedViews.Clear();

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
                // Publication stages output; only native acceptance makes it reusable.
                state.Dirty = false;
                state.Consumer!.Commit();
                if (current.Ownership.Effects is { } effects)
                    foreach (var effect in effects) effect.Commit();
            }

            current.CommitStagedProps();
            _acceptedViews.Add(current);

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

        for (var index = _acceptedViews.Count - 1; index >= 0; index--)
            if (_acceptedViews[index].Ownership.Effects is { } effects)
                foreach (var effect in effects) effect.StopChanged();

        foreach (var view in _acceptedViews)
        {
            ThrowIfUnavailable();
            view.Runtime.MountRuntime();
        }
        foreach (var view in _acceptedViews)
        {
            ThrowIfUnavailable();
            if (view.Ownership.Effects is { } effects)
                foreach (var effect in effects) effect.Start();
        }
        _acceptedViews.Clear();
    }

    private void MarkViewFragmentDirty(ViewBase view)
    {
        Execution.AssertAccess();
        if (_renderStates.TryGetValue(view, out var state))
        {
            // Changed props are discovered while the parent is already composing.
            state.Dirty = true;
        }
    }

    private void RollBackStagedProps()
    {
        Execution.AssertAccess();
        foreach (var view in _renderStates.Keys)
        {
            _renderStates[view].Consumer?.Abort();
            view.RollBackStagedProps();
        }
    }
}
