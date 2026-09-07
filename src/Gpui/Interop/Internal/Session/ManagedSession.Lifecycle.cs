using System.Runtime.ExceptionServices;
using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    internal void RetireFailedViews()
    {
        BuildUnmountOrder();
        foreach (var view in _unmountCandidates)
        {
            try { Unmount(view); }
            catch (Exception) { /* The session retains the original failure; all owners still retire. */ }
        }
        _unmountCandidates.Clear();
        _acceptedViews.Clear();
        _snapshotStack.Clear();
        _unmountVisited.Clear();
        _rootView = null;
    }

    internal void Stop()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }
        // A session that never entered a native callback owns no UI state and may be
        // discarded on the window-opening thread if native registration fails.
        using var execution = Volatile.Read(ref _renderingStarted) == 0
            ? default(ApplicationExecution.Scope)
            : Execution.Enter(ExecutionPhase.Cleanup);
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        DiscardIngress();
        _rootDeclaration = null;
        _rootView = null;
        BuildUnmountOrder();
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

        foreach (var state in _renderStates.Values)
        {
            state.Consumer?.Dispose();
            state.Fragment?.Dispose();
        }
        _renderStates.Clear();

        _attachedViews.Clear();
        _demandArtifacts.Clear();
        _invalidArtifacts?.Clear();
        _viewsByHandle.Clear();
        _renderingViews.Clear();
        _snapshotStack.Clear();
        _unmountCandidates.Clear();
        _unmountStack.Clear();
        _unmountVisited.Clear();
        _rootOutputArena?.Dispose();
        _acceptedViews.Clear();
        _pendingRenderRevision = 0;
        _rootOutputArena = null;
        _demandOutputArena?.Dispose();
        _demandOutputArena = null;
    }

    private void AttachRoot()
    {
        if (_rootView is null)
        {
            var declaration = _rootDeclaration ?? throw new InvalidOperationException("No root declaration.");
            _rootDeclaration = null;
            _rootView = declaration.Create(_window!);
        }
        var rootState = GetRenderState(RootView);
        if (rootState.Parent is not null)
        {
            throw new InvalidOperationException(
                "The root View cannot be owned by another View."
            );
        }
        Attach(RootView);
    }

    private void Attach(ViewBase view)
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
            view.Runtime.PrepareRuntime(
                handle,
                Post,
                Invalidate,
                DispatchResourceCommand,
                DispatchUtf8InputValue,
                DispatchNativeExtensionCommand,
                ThrowIfUnavailable
            );
        }
        catch
        {
            _viewsByHandle.Remove(handle);
            _attachedViews.Remove(view);
            if (_renderStates.Remove(view, out var failedState))
            {
                failedState.Fragment?.Dispose();
            }
            throw;
        }
    }

    private void Unmount(ViewBase view)
    {
        _renderStates.TryGetValue(view, out var retiring);
        RetireDemandArtifacts(retiring);
        retiring?.Consumer?.Dispose();
        var handle = view.Runtime.RuntimeViewHandle;
        Exception? lifecycleFailure = null;
        try
        {
            view.Runtime.UnmountRuntime();
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

            if (_renderStates.Remove(view, out var state))
            {
                state.Parent = null;
                state.Fragment?.Dispose();
            }
        }

        if (lifecycleFailure is not null)
        {
            ExceptionDispatchInfo.Capture(lifecycleFailure).Throw();
        }
    }

    private RetainedViewState GetRenderState(ViewBase view)
    {
        Execution.AssertAccess();
        if (!_renderStates.TryGetValue(view, out var state))
        {
            state = new RetainedViewState();
            _renderStates.Add(view, state);
        }
        return state;
    }

    private void MarkDirty(ViewBase view)
    {
        Execution.AssertAccess();
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        ViewBase? current = view;
        while (current is not null)
        {
            if (!_renderStates.TryGetValue(current, out var state) || state.Dirty)
            {
                return;
            }
            // An already-dirty ancestor has already propagated to the root.
            state.Dirty = true;
            current = state.Parent;
        }
    }

    private void BuildUnmountOrder()
    {
        _unmountCandidates.Clear();
        _unmountStack.Clear();
        _unmountVisited.Clear();

        foreach (var view in _attachedViews)
        {
            _unmountStack.Push((view, false));
            CollectUnmountCandidates();
        }
    }

    private void CollectUnmountCandidates()
    {
        while (_unmountStack.TryPop(out var entry))
        {
            if (entry.Expanded)
            {
                _unmountCandidates.Add(entry.View);
                continue;
            }

            if (!_unmountVisited.Add(entry.View))
            {
                continue;
            }

            _unmountStack.Push((entry.View, true));
            if (!_renderStates.TryGetValue(entry.View, out var state))
            {
                continue;
            }

            PushCleanupChildren(state.Children);
            if (state.HasStagedComposition)
            {
                PushCleanupChildren(state.StagedChildren);
            }
            PushCleanupChildren(state.Candidates);
        }
    }

    private void PushCleanupChildren(Dictionary<ChildSlot, ChildEntry>? children)
    {
        if (children is null)
        {
            return;
        }

        foreach (var child in children.Values)
        {
            _unmountStack.Push((child.View, false));
        }
    }

    private static Dictionary<ChildSlot, ChildEntry> GetWorkingChildren(RetainedViewState state) =>
        state.WorkingChildren ??= [];

    private static Dictionary<ChildSlot, ChildEntry> GetCandidates(RetainedViewState state) =>
        state.Candidates ??= [];
}
