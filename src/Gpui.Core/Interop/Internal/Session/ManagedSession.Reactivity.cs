using System.Runtime.InteropServices;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    private List<NativeArtifactKey>? _invalidArtifacts;
    internal ApplicationExecution ReactiveExecution => Execution;

    internal void InvalidateReactiveConsumer(ReactiveConsumer consumer)
    {
        Execution.AssertAccess();
        if (!IsAcceptingWork)
            return;
        if (consumer.Artifact == 0)
        {
            if (_pendingRenderRevision != 0 && Execution.Phase != ExecutionPhase.Acceptance)
            {
                // Acceptance may clear an ancestor that reused this consumer's fragment.
                // Replay through ingress after the entire staged tree commits.
                Invalidate(consumer.Owner);
                return;
            }
            MarkDirty(consumer.Owner);
            NotifyRenderPending();
        }
        else
        {
            (_invalidArtifacts ??= []).Add(
                new NativeArtifactKey { source = consumer.Source, artifact = consumer.Artifact }
            );
            Execution.ScheduleArtifacts(this);
        }
    }

    internal void FlushReactiveArtifacts()
    {
        if (_invalidArtifacts is not { Count: > 0 } keys)
            return;
        try
        {
            if (!IsAcceptingWork)
                return;
            fixed (NativeArtifactKey* pointer = CollectionsMarshal.AsSpan(keys))
            {
                var status = _runtime.Api->invalidate_artifacts(_sessionId, pointer, keys.Count);
                GC.KeepAlive(_runtime);
                if (status is not (0 or -30 or -31))
                    throw new InvalidOperationException(
                        $"Native artifact invalidation failed for session {_sessionId}: {NativeStatus.Describe(NativeStatusDomain.ArtifactInvalidation, status)}."
                    );
            }
        }
        finally
        {
            keys.Clear();
        }
    }
}
