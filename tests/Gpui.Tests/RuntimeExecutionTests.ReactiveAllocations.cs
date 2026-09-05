using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    private const int AllocationBatchSize = 128;
    private const int AllocationWarmups = 4;
    private const int AllocationMeasurements = 3;

    [Theory]
    [InlineData("empty-view")]
    [InlineData("view-with-signal")]
    [InlineData("view-with-props")]
    [InlineData("view-with-lifetime")]
    [InlineData("signal-int")]
    public void ViewAndSignalCreationAllocations(string pattern)
    {
        var instances = new object[AllocationBatchSize];
        for (var batch = 0; batch < AllocationWarmups + AllocationMeasurements; batch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < instances.Length; index++)
            {
                instances[index] = pattern switch
                {
                    "view-with-signal" => new AllocationSignalView(),
                    "view-with-props" => new AllocationPropsView(),
                    "view-with-lifetime" => CreateViewWithLifetime(),
                    "signal-int" => new Signal<int>(0),
                    _ => new AllocationEmptyView()
                };
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ReportAllocation(pattern, batch, bytes);
            Assert.All(instances, static instance => Assert.NotNull(instance));
            GC.KeepAlive(instances);
        }
    }

    private static AllocationEmptyView CreateViewWithLifetime()
    {
        var view = new AllocationEmptyView();
        _ = view.Runtime.Lifetime;
        return view;
    }

    [Fact]
    public void SimpleViewFirstAcceptedRenderAllocations()
    {
        for (var batch = 0; batch < AllocationWarmups + AllocationMeasurements; batch++)
        {
            long bytes = 0;
            for (var index = 0; index < AllocationBatchSize; index++)
            {
                // Session construction/reflection and disposal are deliberately excluded.
                using var fixture = new SessionFixture(new AllocationRenderRoot());
                var before = GC.GetAllocatedBytesForCurrentThread();
                fixture.Render();
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.False(fixture.State(fixture.View).Dirty);
            }
            ReportAllocation("first-accepted-text-root", batch, bytes);
        }
    }

    [Theory]
    [InlineData("untracked-read")]
    [InlineData("equal-write")]
    [InlineData("unsubscribed-write")]
    public void UnboundSignalAccessAllocations(string pattern)
    {
        var signal = new Signal<int>(0);
        for (var batch = 0; batch < AllocationWarmups + AllocationMeasurements; batch++)
        {
            var total = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < AllocationBatchSize; index++)
            {
                if (pattern == "untracked-read")
                    total += signal.Value;
                else if (pattern == "equal-write")
                    signal.Set(0);
                else
                    signal.Value++;
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ReportAllocation(pattern, batch, bytes);
            if (batch >= AllocationWarmups)
                Assert.Equal(0, bytes);
            Assert.Equal(pattern == "unsubscribed-write" ? (batch + 1) * AllocationBatchSize : 0, signal.Value);
            Assert.Equal(0, total);
        }
    }

    [Theory]
    [InlineData("first-dependencies", 1)]
    [InlineData("first-dependencies", 8)]
    [InlineData("first-dependencies", 32)]
    [InlineData("stable-dependencies", 1)]
    [InlineData("stable-dependencies", 8)]
    [InlineData("stable-dependencies", 32)]
    [InlineData("repeated-same-signal", 32)]
    [InlineData("conditional-switch", 1)]
    [InlineData("detach-resubscribe-pair", 1)]
    public void SignalDependencyTrackingAllocations(string pattern, int dependencyCount)
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        var signals = Enumerable.Range(0, Math.Max(2, dependencyCount)).Select(static _ => new Signal<int>(0)).ToArray();
        var consumer = new ReactiveConsumer(fixture.Session, fixture.View);
        try
        {
            for (var batch = 0; batch < AllocationWarmups + AllocationMeasurements; batch++)
            {
                // This isolates tracking/acceptance from consumer and render-arena creation.
                var fresh = pattern == "first-dependencies" ? new ReactiveConsumer[AllocationBatchSize] : [];
                for (var index = 0; index < fresh.Length; index++)
                    fresh[index] = new ReactiveConsumer(fixture.Session, fixture.View);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < AllocationBatchSize; index++)
                {
                    var current = fresh.Length != 0 ? fresh[index] : consumer;
                    if (pattern == "detach-resubscribe-pair")
                    {
                        using (current.Begin()) { }
                        current.Commit();
                    }
                    using (current.Begin())
                    {
                        for (var dependency = 0; dependency < dependencyCount; dependency++)
                            _ = signals[pattern == "conditional-switch" ? index % 2
                                : pattern is "repeated-same-signal" or "detach-resubscribe-pair" ? 0 : dependency].Value;
                    }
                    current.Commit();
                }
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                ReportAllocation($"{pattern}-{dependencyCount}", batch, bytes);
                if (batch >= AllocationWarmups && (pattern is "stable-dependencies" or "repeated-same-signal"))
                    Assert.Equal(0, bytes);
                foreach (var item in fresh)
                    item.Dispose();
                Assert.True(fresh.Length != 0 || consumer.Accepted);
            }
        }
        finally { consumer.Dispose(); }
    }

    [Theory]
    [InlineData("shared-signal", 1)]
    [InlineData("shared-signal", 8)]
    [InlineData("shared-signal", 32)]
    [InlineData("many-signals", 8)]
    [InlineData("many-signals", 32)]
    [InlineData("conditional-switch", 8)]
    [InlineData("paused-readers", 8)]
    [InlineData("equal-write", 8)]
    [InlineData("coalesced-writes", 8)]
    public void CrossViewSignalUpdateAllocations(string pattern, int count)
    {
        var manySignals = pattern == "many-signals";
        var signals = Enumerable.Range(0, manySignals ? count : 1).Select(static _ => new Signal<int>(0)).ToArray();
        var root = new AllocationTreeRoot(signals, manySignals ? 1 : count);
        using var fixture = new SessionFixture(root);
        fixture.Render();
        var readers = fixture.State(root).Children!.Values.Select(static entry => (AllocationReaderView)entry.View).ToArray();
        if (pattern == "paused-readers")
        {
            for (var index = 0; index < readers.Length; index += 2)
                readers[index].Following.Value = false;
            fixture.Render();
        }
        var states = readers.Select(fixture.State).ToArray();
        for (var batch = 0; batch < AllocationWarmups + AllocationMeasurements; batch++)
        {
            long writes = 0;
            long renders = 0;
            var counts = readers.Select(static reader => reader.Renders).ToArray();
            var notifications = fixture.Notifications;
            for (var index = 0; index < AllocationBatchSize; index++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                if (pattern == "conditional-switch")
                    root.UseAlternate.Value = !root.UseAlternate.Value;
                else if (pattern == "equal-write")
                    signals[0].Set(signals[0].Value);
                else
                {
                    foreach (var signal in signals)
                        signal.Value++;
                    if (pattern == "coalesced-writes")
                        for (var repeat = 1; repeat < 32; repeat++)
                            signals[0].Value++;
                }
                writes += GC.GetAllocatedBytesForCurrentThread() - before;

                for (var reader = 0; reader < readers.Length; reader++)
                    Assert.Equal(pattern != "equal-write" && readers[reader].Following.Value, states[reader].Dirty);
                before = GC.GetAllocatedBytesForCurrentThread();
                fixture.Render();
                renders += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            ReportAllocation($"{pattern}-{count}/write", batch, writes);
            ReportAllocation($"{pattern}-{count}/render-accept", batch, renders);
            if (batch >= AllocationWarmups)
            {
                Assert.Equal(0, writes);
                if (pattern != "conditional-switch")
                    Assert.Equal(0, renders);
            }
            Assert.Equal(pattern == "equal-write" ? 0 : AllocationBatchSize, fixture.Notifications - notifications);
            for (var index = 0; index < readers.Length; index++)
            {
                var active = readers[index].Following.Value;
                Assert.Equal(counts[index] + (active && pattern != "equal-write" ? AllocationBatchSize : 0), readers[index].Renders);
                var expected = root.UseAlternate.Value ? root.Alternate.Value : signals.Sum(static signal => signal.Value);
                Assert.Equal(active ? expected : -1, readers[index].Observed);
            }
        }
    }

    private static void ReportAllocation(string pattern, int batch, long bytes)
    {
        if (batch >= AllocationWarmups)
            TestContext.Current.TestOutputHelper!.WriteLine($"{pattern}: {bytes / (double)AllocationBatchSize:N1} B/op");
    }

    private sealed class AllocationEmptyView : View
    {
        protected override Element Render(ref RenderContext ui) => ui.Text("view");
    }

    private sealed class AllocationSignalView : View
    {
        private readonly Signal<int> _count = new(0);
        protected override Element Render(ref RenderContext ui)
        {
            _ = _count.Value;
            return ui.Text("view");
        }
    }

    private sealed class AllocationPropsView : View<int>
    {
        protected override Element Render(ref RenderContext ui) => ui.Text("view");
    }

    private sealed class AllocationRenderRoot : ProbeView
    {
        protected override Element Render(ref RenderContext ui) => ui.Text("view");
    }

    private readonly record struct AllocationReaderProps(Signal<int>[] Signals, Signal<int> Alternate, Signal<bool> UseAlternate);

    private sealed class AllocationReaderView : View<AllocationReaderProps>, IGeneratedViewFactory<AllocationReaderView>
    {
        public static AllocationReaderView CreateGpuiView() => new();
        internal readonly Signal<bool> Following = new(true);
        internal int Renders;
        internal int Observed;
        protected override Element Render(ref RenderContext ui)
        {
            Renders++;
            Observed = -1;
            if (Following.Value)
            {
                Observed = 0;
                if (Props.UseAlternate.Value)
                    Observed = Props.Alternate.Value;
                else
                    foreach (var signal in Props.Signals)
                        Observed += signal.Value;
            }
            // Constant text isolates dependency/render machinery from number formatting.
            return ui.Text("reader");
        }
    }

    private sealed class AllocationTreeRoot(Signal<int>[] signals, int readers) : ProbeView
    {
        internal readonly Signal<int> Alternate = new(42);
        internal readonly Signal<bool> UseAlternate = new(false);
        protected override Element Render(ref RenderContext ui)
        {
            Span<Element> children = stackalloc Element[readers];
            for (var index = 0; index < readers; index++)
                children[index] = ui.Child<AllocationReaderView, AllocationReaderProps>(index, new(signals, Alternate, UseAlternate));
            return ui.Div(children);
        }
    }
}
