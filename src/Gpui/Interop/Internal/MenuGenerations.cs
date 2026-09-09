namespace Gpui.Interop.Internal;

// Pending generations retain callbacks until native installs their replacement. Native filters
// stale platform actions before acknowledging that older generations can be released.
internal sealed class MenuGenerations
{
    private readonly object _gate = new();
    private readonly SortedDictionary<ulong, Dictionary<ulong, Action>> _generations = [];
    private ulong _nextGeneration;

    internal ulong Stage(Dictionary<ulong, Action> actions)
    {
        lock (_gate)
        {
            if (_generations.Count >= 64)
                throw new InvalidOperationException(
                    "Too many application menu updates are pending."
                );
            var generation = checked(++_nextGeneration);
            _generations.Add(generation, actions);
            return generation;
        }
    }

    internal void Reject(ulong generation)
    {
        lock (_gate)
            _generations.Remove(generation);
    }

    internal void Applied(ulong generation)
    {
        lock (_gate)
        {
            if (!_generations.ContainsKey(generation))
                throw new InvalidOperationException("Unknown application menu generation.");
            foreach (var old in _generations.Keys.TakeWhile(key => key < generation).ToArray())
                _generations.Remove(old);
        }
    }

    internal Action? Find(ulong actionId)
    {
        lock (_gate)
        {
            foreach (var actions in _generations.Values)
                if (actions.TryGetValue(actionId, out var callback))
                    return callback;
            return null;
        }
    }

    internal void Clear()
    {
        lock (_gate)
            _generations.Clear();
    }
}
