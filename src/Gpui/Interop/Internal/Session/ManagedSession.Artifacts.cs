namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    private readonly Dictionary<ulong, ReactiveConsumer> _demandArtifacts = [];
    private ulong _nextArtifactId;

    private ulong CreateDemandArtifact(ulong source, ViewBase owner)
    {
        if (source == 0)
        {
            throw new InvalidOperationException("Demand rendering requires a bound source identity.");
        }
        var id = checked(++_nextArtifactId);
        _demandArtifacts.Add(id, new ReactiveConsumer(this, owner, source, id));
        return id;
    }

    internal void AcceptDemandArtifact(ulong source, ulong artifact)
    {
        ThrowIfUnavailable();
        using var execution = Execution.Enter(ExecutionPhase.ArtifactAcceptance);
        RequireAcceptedRender();
        if (!_demandArtifacts.TryGetValue(artifact, out var consumer)
            || consumer.Source != source || consumer.Accepted)
            throw new InvalidOperationException("Artifact acceptance has no matching publication.");
        consumer.Commit();
    }

    internal void ReleaseDemandArtifact(ulong source, ulong artifact, int status)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }
        using var execution = Execution.Enter(ExecutionPhase.ArtifactRelease);
        // Resource reconciliation may release artifacts before root acknowledgement. This
        // callback only releases framework state and never enters application code.
        if (source == 0 || artifact == 0 || artifact > _nextArtifactId)
        {
            throw new InvalidOperationException("Invalid demand artifact release identity.");
        }
        if (_demandArtifacts.TryGetValue(artifact, out var entry))
        {
            if (entry.Source != source)
            {
                throw new InvalidOperationException("Demand artifact belongs to another source.");
            }
            _demandArtifacts.Remove(artifact);
            entry.Dispose();
            entry.Owner.Runtime.Events.ReleaseEventArtifact(artifact);
        }
        if (status != 0)
        {
            throw new InvalidOperationException($"Native demand snapshot validation failed with status {status}.");
        }
    }

    private void RetireDemandArtifacts(ViewBase owner)
    {
        foreach (var (id, artifact) in _demandArtifacts)
        {
            if (ReferenceEquals(artifact.Owner, owner))
            {
                owner.Runtime.Events.ReleaseEventArtifact(id);
                artifact.Dispose();
                _demandArtifacts.Remove(id);
            }
        }
    }
}
