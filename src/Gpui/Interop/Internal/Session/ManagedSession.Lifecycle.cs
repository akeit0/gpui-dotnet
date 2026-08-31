using System.Runtime.ExceptionServices;
using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        while (_posted.TryDequeue(out _)) { }
        BuildDetachOrder(includeCommittedTree: true);
        foreach (var view in _detachCandidates)
        {
            try
            {
                Detach(view);
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
            }
        }

        lock (_renderStateGate)
        {
            foreach (var state in _renderStates.Values)
            {
                state.Fragment?.Dispose();
            }
            _renderStates.Clear();
        }

        _attachedViews.Clear();
        _viewsByHandle.Clear();
        _renderingViews.Clear();
        _snapshotStack.Clear();
        _snapshotVisited.Clear();
        _detachCandidates.Clear();
        _detachStack.Clear();
        _detachVisited.Clear();
    }

    private void AttachRoot()
    {
        lock (_renderStateGate)
        {
            var rootState = GetRenderState(RootView);
            if (rootState.Parent is not null)
            {
                throw new InvalidOperationException(
                    "The root View cannot be owned by another View."
                );
            }
        }
        Attach(RootView);
    }

    private void Attach(View view)
    {
        if (!_attachedViews.Add(view))
        {
            return;
        }

        if (_nextViewHandle == uint.MaxValue)
        {
            _attachedViews.Remove(view);
            throw new InvalidOperationException("The session view-handle space was exhausted.");
        }

        var handle = ++_nextViewHandle;
        _viewsByHandle.Add(handle, view);
        try
        {
            _ = GetRenderState(view);
            view.AttachRuntime(
                handle,
                Post,
                Invalidate,
                DispatchResourceCommand,
                DispatchUtf8InputValue
            );
        }
        catch
        {
            _viewsByHandle.Remove(handle);
            _attachedViews.Remove(view);
            lock (_renderStateGate)
            {
                if (_renderStates.Remove(view, out var failedState))
                {
                    failedState.Fragment?.Dispose();
                }
            }
            throw;
        }
    }

    private void Detach(View view)
    {
        var handle = view.RuntimeViewHandle;
        Exception? lifecycleFailure = null;
        try
        {
            view.DetachRuntime();
        }
        catch (Exception exception)
        {
            lifecycleFailure = exception;
        }
        finally
        {
            if (handle != 0)
            {
                _viewsByHandle.Remove(handle);
            }
            _attachedViews.Remove(view);

            lock (_renderStateGate)
            {
                if (_renderStates.Remove(view, out var state))
                {
                    state.Parent = null;
                    state.Fragment?.Dispose();
                }
            }
        }

        if (lifecycleFailure is not null)
        {
            ExceptionDispatchInfo.Capture(lifecycleFailure).Throw();
        }
    }

    private RetainedViewState GetRenderState(View view)
    {
        lock (_renderStateGate)
        {
            if (!_renderStates.TryGetValue(view, out var state))
            {
                state = new RetainedViewState();
                _renderStates.Add(view, state);
            }
            return state;
        }
    }

    private void MarkDirty(View view)
    {
        lock (_renderStateGate)
        {
            if (Volatile.Read(ref _stopped) != 0 || view.RuntimeViewHandle == 0)
            {
                return;
            }

            View? current = view;
            while (current is not null)
            {
                if (!_renderStates.TryGetValue(current, out var state))
                {
                    return;
                }
                state.RequiredVersion++;
                current = state.Parent;
            }
        }
    }

    private void BuildDetachOrder(bool includeCommittedTree)
    {
        _detachCandidates.Clear();
        _detachStack.Clear();
        _detachVisited.Clear();

        lock (_renderStateGate)
        {
            foreach (var view in _attachedViews)
            {
                if (!includeCommittedTree && _snapshotVisited.Contains(view))
                {
                    continue;
                }

                _detachStack.Push((view, false));
                while (_detachStack.TryPop(out var entry))
                {
                    if (entry.Expanded)
                    {
                        _detachCandidates.Add(entry.View);
                        continue;
                    }

                    if (!_detachVisited.Add(entry.View))
                    {
                        continue;
                    }

                    _detachStack.Push((entry.View, true));
                    if (!_renderStates.TryGetValue(entry.View, out var state))
                    {
                        continue;
                    }

                    PushCleanupChildren(state.Children, includeCommittedTree);
                    if (state.HasStagedComposition)
                    {
                        PushCleanupChildren(state.StagedChildren, includeCommittedTree);
                    }
                    PushCleanupChildren(state.Candidates, includeCommittedTree);
                }
            }
        }
    }

    private void PushCleanupChildren(
        Dictionary<ChildSlot, ChildEntry>? children,
        bool includeCommittedTree
    )
    {
        if (children is null)
        {
            return;
        }

        foreach (var child in children.Values)
        {
            if (!includeCommittedTree && _snapshotVisited.Contains(child.View))
            {
                continue;
            }
            _detachStack.Push((child.View, false));
        }
    }

    private static Dictionary<ChildSlot, ChildEntry> GetWorkingChildren(RetainedViewState state) =>
        state.WorkingChildren ??= [];

    private static Dictionary<ChildSlot, ChildEntry> GetCandidates(RetainedViewState state) =>
        state.Candidates ??= [];

    private static HashSet<View> GetWorkingViews(RetainedViewState state) =>
        state.WorkingViews ??= new HashSet<View>(ViewIdentity);
}
