namespace Gpui;

/// <summary>Checked geometric growth without resetting lengths or re-running user code.</summary>
internal static class ArenaCapacity
{
    internal static int GrowTo(int current, int required)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfNegative(required);
        if (required <= current)
        {
            return current;
        }

        // Widen before doubling: growing beyond 2^30 must not wrap an Int32.
        var doubled = Math.Min((long)int.MaxValue, Math.Max(1L, (long)current * 2));
        return checked((int)Math.Max(doubled, required));
    }
}
