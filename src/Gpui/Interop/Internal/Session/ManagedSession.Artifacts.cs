namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    private readonly record struct DemandArtifact(ReactiveConsumer Consumer, int OwnerSlot);
    private readonly Dictionary<ulong, DemandArtifact> _demandArtifacts = [];
    private ulong _nextArtifactId;

    private ulong CreateDemandArtifact(ulong source, ViewBase owner)
    {
        if (source == 0)
        {
            throw new InvalidOperationException("Demand rendering requires a bound source identity.");
        }
        var id = checked(++_nextArtifactId);
        var slots = GetRenderState(owner).DemandArtifacts ??= [];
        var consumer = new ReactiveConsumer(this, owner, source, id);
        _demandArtifacts.Add(id, new DemandArtifact(consumer, slots.Count));
        try { slots.Add(id); }
        catch
        {
            _demandArtifacts.Remove(id);
            consumer.Dispose();
            throw;
        }
        return id;
    }

    internal void AcceptDemandArtifact(ulong source, ulong artifact)
    {
        ThrowIfUnavailable(retireFailure: false);
        using var execution = Execution.Enter(ExecutionPhase.ArtifactAcceptance);
        RequireAcceptedRender();
        if (!_demandArtifacts.TryGetValue(artifact, out var entry)
            || entry.Consumer.Source != source || entry.Consumer.Accepted)
            throw new InvalidOperationException("Artifact acceptance has no matching publication.");
        entry.Consumer.Commit();
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
            if (entry.Consumer.Source != source)
            {
                throw new InvalidOperationException("Demand artifact belongs to another source.");
            }
            RemoveDemandArtifact(artifact);
        }
        if (status != 0)
        {
            throw new InvalidOperationException($"Native demand snapshot validation failed with status {status}.");
        }
    }

    private void RemoveDemandArtifact(ulong id)
    {
        if (!_demandArtifacts.Remove(id, out var entry)) return;
        var consumer = entry.Consumer;
        var slots = _renderStates[consumer.Owner].DemandArtifacts!;
        System.Diagnostics.Debug.Assert(slots[entry.OwnerSlot] == id);
        var last = slots[^1];
        slots[entry.OwnerSlot] = last;
        slots.RemoveAt(slots.Count - 1);
        if (last != id)
        {
            var moved = _demandArtifacts[last];
            _demandArtifacts[last] = moved with { OwnerSlot = entry.OwnerSlot };
        }
        consumer.Dispose();
        consumer.Owner.Runtime.Events.ReleaseEventArtifact(id);
    }

    private void RetireDemandArtifacts(RetainedViewState? state)
    {
        if (state?.DemandArtifacts is not { } slots) return;
        while (slots.Count != 0) RemoveDemandArtifact(slots[^1]);
    }
}
