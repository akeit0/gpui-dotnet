namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData("text", 1)]
    [InlineData("text", 512)]
    [InlineData("shared-click", 1)]
    [InlineData("shared-click", 512)]
    public void WarmRowBatchesHaveBoundedAllocationIndependentOfRowCount(string pattern, int count)
    {
        using var fixture = new SessionFixture(new AllocationRowView(pattern));
        fixture.Render();
        var status = 0;
        for (var batch = 0; batch < 5; batch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 64; index++)
            {
                status |= fixture.NativeRange(0, out var artifact, count: (uint)count);
                status |= fixture.Accept(1, artifact);
                status |= fixture.Release(1, artifact);
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (batch != 0)
                Assert.InRange(bytes, 0, 128 * 64);
        }
        Assert.Equal(0, status);
    }

    [Fact]
    public void InterleavedArtifactReleasePreservesEveryBindingInReusedSlots()
    {
        var view = new DistinctRowView();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var first = fixture.Range(0, count: 32);
        var firstTokens = view.Tokens.ToArray();
        var second = fixture.Range(0, source: 2, count: 32);
        var secondTokens = view.Tokens.ToArray();
        Assert.Equal(65, view.Runtime.Events.EntryCount);
        Assert.Equal(32, firstTokens.Distinct().Count());
        Assert.Equal(0, fixture.Release(1, first));
        var third = fixture.Range(0, source: 3, count: 16);
        var thirdTokens = view.Tokens[..16];
        Assert.Equal(65, view.Runtime.Events.EntryCount);
        foreach (var token in firstTokens)
            Assert.Equal(0, fixture.Click(token));
        Assert.All(view.Clicks, count => Assert.Equal(0, count));
        foreach (var token in secondTokens)
            Assert.Equal(0, fixture.Click(token));
        Assert.All(view.Clicks, count => Assert.Equal(1, count));
        Assert.Equal(0, fixture.Release(2, second));
        foreach (var token in thirdTokens)
            Assert.Equal(0, fixture.Click(token));
        for (var index = 0; index < 32; index++)
            Assert.Equal(index < 16 ? 2 : 1, view.Clicks[index]);
        Assert.Equal(0, fixture.Release(3, third));
        foreach (var token in secondTokens.Concat(thirdTokens))
            Assert.Equal(0, fixture.Click(token));
        for (var index = 0; index < 32; index++)
            Assert.Equal(index < 16 ? 2 : 1, view.Clicks[index]);
        Assert.Equal(0, fixture.Click());
        Assert.Equal(1, view.ClickCount);
        Assert.Null(fixture.Session.Failure);
    }

    private sealed class DistinctRowView : ProbeView
    {
        internal readonly ulong[] Tokens = new ulong[32];
        internal readonly int[] Clicks = new int[32];
        private readonly Action<DistinctRowView, ClickEvent>[] _callbacks = Enumerable
            .Range(0, 32)
            .Select(Capture)
            .ToArray();

        private static Action<DistinctRowView, ClickEvent> Capture(int index) =>
            (view, _) => view.Clicks[index]++;

        protected override Element RenderListItem(uint rendererId, int index, ref RenderContext ui)
        {
            Tokens[index] = Runtime.Events.BindClick(_callbacks[index]);
            return ui.Button("row", "row").OnClick(this, _callbacks[index]);
        }
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData("text", 1)]
    [InlineData("text", 48)]
    [InlineData("text", 512)]
    [InlineData("shared-click", 1)]
    [InlineData("shared-click", 48)]
    [InlineData("shared-click", 512)]
    [InlineData("captured-click", 48)]
    [InlineData("signal", 48)]
    public void RowBatchAllocationPatterns(string pattern, int count)
    {
        using var fixture = new SessionFixture(new AllocationRowView(pattern));
        fixture.Render();
        var status = 0;
        MeasureRenderCost(
            $"row-{pattern}-{count}",
            32,
            () =>
            {
                status |= fixture.NativeRange(0, out var artifact, count: (uint)count);
                status |= fixture.Accept(1, artifact);
                status |= fixture.Release(1, artifact);
            }
        );
        Assert.Equal(0, status);
    }

    private sealed class AllocationRowView(string pattern) : ProbeView
    {
        private readonly Signal<int> _value = new(0);

        protected override Element RenderListItem(uint rendererId, int index, ref RenderContext ui)
        {
            if (pattern == "text")
                return ui.Text("row");
            if (pattern == "signal")
                _ = _value.Value;
            var callback = pattern == "captured-click" ? Capture(index) : SharedClick;
            return ui.Button("row", "row").OnClick(this, callback, (ulong)index);
        }

        private static void SharedClick(AllocationRowView view, ClickEvent click) =>
            view.ClickCount++;

        private static Action<AllocationRowView, ClickEvent> Capture(int index) =>
            (view, _) => view.ClickCount += index;
    }
}
