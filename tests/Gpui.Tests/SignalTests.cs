namespace Gpui.Tests;

public sealed class SignalTests
{
    [Fact]
    public void SetUsesReplacementSemanticsAndComparesOnce()
    {
        var comparer = new CountingComparer();
        var signal = new Signal<string>("hello", comparer);
        Assert.False(signal.Set("HELLO"));
        Assert.Equal("hello", signal.Value);
        Assert.True(signal.Set("world"));
        Assert.Equal("world", signal.Value);
        Assert.Equal(2, comparer.Calls);
    }

    [Fact]
    public void EqualityComparisonCannotReenterSignalMutation()
    {
        var other = new Signal<int>(0);
        var comparer = new CountingComparer { DuringCompare = () => other.Value++ };
        var signal = new Signal<string>("old", comparer);
        Assert.Throws<InvalidOperationException>(() => signal.Set("new"));
        Assert.Equal("old", signal.Value);
        Assert.Equal(0, other.Value);
        comparer.DuringCompare = null;
        Assert.True(signal.Set("new"));
    }

    private sealed class CountingComparer : IEqualityComparer<string>
    {
        internal int Calls;
        internal Action? DuringCompare;
        public bool Equals(string? x, string? y)
        {
            Calls++;
            DuringCompare?.Invoke();
            return StringComparer.OrdinalIgnoreCase.Equals(x, y);
        }
        public int GetHashCode(string value) => StringComparer.OrdinalIgnoreCase.GetHashCode(value);
    }
}
