using System.Runtime.ExceptionServices;
using Gpui.Interop.Internal;

namespace Gpui;

public abstract partial class ViewBase
{
    private Dictionary<ulong, object>? _pendingWork;
    private ulong _nextWorkId;

    /// <summary>
    /// Starts a static producer on the thread pool with an explicit request snapshot and this
    /// View's lifetime token. Completion runs through application-thread ingress only while this
    /// View remains mounted. Calls are independent; no latest-request policy is implied.
    /// </summary>
    /// <remarks>
    /// Requests must not contain Views, bound Signals, or controllers. Success/failure callbacks
    /// are owned by the View and released on retirement even if production ignores cancellation.
    /// An unhandled producer failure faults the live session. Lifetime cancellation is ignored.
    /// </remarks>
    protected void StartWork<TRequest, TResult>(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> produce,
        Action<TResult> complete,
        Action<Exception>? failed = null
    ) => _ = StartWorkCore(request, produce, complete, failed);

    // The returned task observes production and posting, not application of the result.
    // Keep it internal so application code cannot confuse worker completion with UI completion.
    internal Task StartWorkCore<TRequest, TResult>(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> produce,
        Action<TResult> complete,
        Action<Exception>? failed = null
    )
    {
        ArgumentNullException.ThrowIfNull(produce);
        ArgumentNullException.ThrowIfNull(complete);
        RequireUiAttachment();
        if (!IsMountedCore)
            throw new InvalidOperationException("Asynchronous work requires a mounted View.");
        if (ApplicationExecution.Current?.Phase is ExecutionPhase.Render or ExecutionPhase.DemandRender)
            throw new InvalidOperationException("Asynchronous work cannot start during rendering.");

        var route = _commandRoute!;
        route.EnsureAvailable();
        var id = checked(++_nextWorkId);
        var work = _pendingWork ??= [];
        work.Add(id, new WorkCompletion<TResult>(complete, failed));
        try
        {
            return QueueWork(
                new WeakReference<ViewBase>(this), new WeakReference<ViewCommandRoute>(route),
                id, request, produce, Lifetime
            );
        }
        catch
        {
            work.Remove(id);
            throw;
        }
    }

    private static Task QueueWork<TRequest, TResult>(
        WeakReference<ViewBase> owner,
        WeakReference<ViewCommandRoute> route,
        ulong id,
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> produce,
        CancellationToken lifetime
    )
    {
        // A producer must not inherit AsyncLocal state or a UI synchronization context that
        // could retain its application. Suppression is scoped only to scheduling the worker.
        using var flow = ExecutionContext.IsFlowSuppressed() ? default : ExecutionContext.SuppressFlow();
        return Task.Run(() => ProduceWork(owner, route, id, request, produce, lifetime));
    }

    private static async Task ProduceWork<TRequest, TResult>(
        WeakReference<ViewBase> owner,
        WeakReference<ViewCommandRoute> route,
        ulong id,
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> produce,
        CancellationToken lifetime
    )
    {
        TResult result = default!;
        Exception? failure = null;
        try
        {
            lifetime.ThrowIfCancellationRequested();
            result = await produce(request, lifetime).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (lifetime.IsCancellationRequested || !route.TryGetTarget(out var target))
            return;
        try
        {
            target.TryPost(() =>
            {
                if (owner.TryGetTarget(out var view))
                    view.CompleteWork(id, result, failure);
            });
        }
        catch (Exception)
        {
            // Native wake-up failure is recorded by the session's Post path. The worker must
            // observe it; retrying cannot repair a terminal session and could duplicate delivery.
        }
    }

    private void CompleteWork<TResult>(ulong id, TResult result, Exception? failure)
    {
        RequireUiAttachment();
        if (_pendingWork is null || !_pendingWork.Remove(id, out var entry))
            return;
        var completion = (WorkCompletion<TResult>)entry;
        if (failure is null)
            completion.Complete(result);
        else if (completion.Failed is { } failed)
            failed(failure);
        else
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed record WorkCompletion<TResult>(Action<TResult> Complete, Action<Exception>? Failed);
}
