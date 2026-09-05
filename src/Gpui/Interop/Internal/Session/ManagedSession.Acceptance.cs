namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    internal void CompleteRender(ulong revision, int status)
    {
        ThrowIfUnavailable();
        using var execution = Execution.Enter(ExecutionPhase.Acceptance);
        try
        {
            if (revision == 0 || revision != _pendingRenderRevision)
            {
                throw new InvalidOperationException("Native render acknowledgement has no matching publication.");
            }
            if (status != 0)
            {
                throw new InvalidOperationException($"Native snapshot validation failed with status {status}.");
            }

            // External entry remains excluded while every reachable View commits its props
            // and composition, followed by retirement and parent-first mounting.
            CommitSnapshotTree();
            ThrowIfUnavailable();
            _pendingRenderRevision = 0;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }
}
