namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    private readonly Dictionary<ulong, DemandArtifact> _demandArtifacts = [];
    private ulong _nextArtifactId;

    private readonly record struct DemandArtifact(ulong Source, ViewBase Owner);

    private ulong CreateDemandArtifact(ulong source, ViewBase owner)
    {
        if (source == 0)
        {
            throw new InvalidOperationException("Demand rendering requires a bound source identity.");
        }
        var id = checked(++_nextArtifactId);
        _demandArtifacts.Add(id, new DemandArtifact(source, owner));
        return id;
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
            entry.Owner.ReleaseEventArtifact(artifact);
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
                owner.ReleaseEventArtifact(id);
                _demandArtifacts.Remove(id);
            }
        }
    }
}
