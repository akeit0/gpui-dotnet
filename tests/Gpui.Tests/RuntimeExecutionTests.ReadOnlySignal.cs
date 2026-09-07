namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Fact]
    public void CovariantReadOnlySignalTracksTheOriginalSignal()
    {
        var signal = new Signal<string>("first");
        IReadOnlySignal<object> reader = signal;
        object? observed = null;
        using var fixture = new SessionFixture(new ProbeView { DuringRender = () => observed = reader.Value });
        Assert.Same(signal, reader);
        fixture.Render();
        Assert.Equal("first", observed);
        signal.Value = "second";
        Assert.True(fixture.State(fixture.View).Dirty);
        fixture.Render();
        Assert.Equal("second", observed);
        Assert.False(signal.Set("second"));
        Assert.False(fixture.State(fixture.View).Dirty);
    }

    [Fact]
    public void ReadOnlySignalPreservesCrossApplicationAccessChecks()
    {
        IReadOnlySignal<int> reader = new Signal<int>(0);
        using var first = new SessionFixture(new ProbeView { DuringRender = () => _ = reader.Value });
        using var second = new SessionFixture(new ProbeView { DuringRender = () => _ = reader.Value });
        first.Render();
        Assert.Throws<InvalidOperationException>(second.Render);
    }
}
