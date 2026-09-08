using Gpui.Interop.Internal;

namespace Gpui.Tests;

[CollectionDefinition("Runtime execution", DisableParallelization = true)]
public sealed class RuntimeExecutionCollection { }

// Allocation measurements must not overlap other collections that explicitly force GC.
[Collection("Runtime execution")]
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
    [InlineData("readonly-signal-int")]
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
                    "readonly-signal-int" => (IReadOnlySignal<int>)new Signal<int>(0),
                    _ => new AllocationEmptyView(),
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
            Assert.Equal(
                pattern == "unsubscribed-write" ? (batch + 1) * AllocationBatchSize : 0,
                signal.Value
            );
            Assert.Equal(0, total);
        }
    }

    [Theory]
    [InlineData("first-dependencies", 1)]
    [InlineData("first-dependencies", 8)]
    [InlineData("first-dependencies", 32)]
    [InlineData("first-dependencies", 65)]
    [InlineData("readonly-first-dependencies", 1)]
    [InlineData("readonly-first-dependencies", 8)]
    [InlineData("stable-dependencies", 1)]
    [InlineData("stable-dependencies", 8)]
    [InlineData("stable-dependencies", 32)]
    [InlineData("stable-dependencies", 128)]
    [InlineData("readonly-stable-dependencies", 1)]
    [InlineData("readonly-stable-dependencies", 8)]
    [InlineData("repeated-same-signal", 32)]
    [InlineData("conditional-switch", 1)]
    [InlineData("conditional-switch", 8)]
    [InlineData("conditional-switch", 32)]
    [InlineData("conditional-switch", 128)]
    [InlineData("detach-resubscribe-pair", 1)]
    public void SignalDependencyTrackingAllocations(string pattern, int dependencyCount)
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        var signals = Enumerable
            .Range(
                0,
                Math.Max(2, pattern == "conditional-switch" ? 2 * dependencyCount : dependencyCount)
            )
            .Select(static _ => new Signal<int>(0))
            .ToArray();
        var consumer = new ReactiveConsumer(fixture.Session, fixture.View);
        IReadOnlySignal<int>[] readOnly = signals;
        var firstDependencies = pattern is "first-dependencies" or "readonly-first-dependencies";
        var throughInterface = pattern.StartsWith("readonly-", StringComparison.Ordinal);
        try
        {
            for (var batch = 0; batch < AllocationWarmups + AllocationMeasurements; batch++)
            {
                // This isolates tracking/acceptance from consumer and render-arena creation.
                var fresh = firstDependencies ? new ReactiveConsumer[AllocationBatchSize] : [];
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
                        {
                            var slot =
                                pattern == "conditional-switch"
                                    ? (index % 2) * dependencyCount + dependency
                                : pattern is "repeated-same-signal" or "detach-resubscribe-pair" ? 0
                                : dependency;
                            _ = throughInterface ? readOnly[slot].Value : signals[slot].Value;
                        }
                    }
                    current.Commit();
                }
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                ReportAllocation($"{pattern}-{dependencyCount}", batch, bytes);
                if (
                    batch >= AllocationWarmups
                    && !firstDependencies
                    && !(pattern == "conditional-switch" && dependencyCount > 8)
                )
                    Assert.Equal(0, bytes);
                foreach (var item in fresh)
                    item.Dispose();
                Assert.True(fresh.Length != 0 || consumer.Accepted);
            }
        }
        finally
        {
            consumer.Dispose();
        }
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
        var signals = Enumerable
            .Range(0, manySignals ? count : 1)
            .Select(static _ => new Signal<int>(0))
            .ToArray();
        var root = new AllocationTreeRoot(signals, manySignals ? 1 : count);
        using var fixture = new SessionFixture(root);
        fixture.Render();
        var readers = fixture
            .State(root)
            .Children!.Values.Select(static entry => (AllocationReaderView)entry.View)
            .ToArray();
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
                    Assert.Equal(
                        pattern != "equal-write" && readers[reader].Following.Value,
                        states[reader].Dirty
                    );
                before = GC.GetAllocatedBytesForCurrentThread();
                fixture.Render();
                renders += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            ReportAllocation($"{pattern}-{count}/write", batch, writes);
            ReportAllocation($"{pattern}-{count}/render-accept", batch, renders);
            if (batch >= AllocationWarmups)
            {
                Assert.Equal(0, writes);
                Assert.Equal(0, renders);
            }
            Assert.Equal(
                pattern == "equal-write" ? 0 : AllocationBatchSize,
                fixture.Notifications - notifications
            );
            for (var index = 0; index < readers.Length; index++)
            {
                var active = readers[index].Following.Value;
                Assert.Equal(
                    counts[index] + (active && pattern != "equal-write" ? AllocationBatchSize : 0),
                    readers[index].Renders
                );
                var expected = root.UseAlternate.Value
                    ? root.Alternate.Value
                    : signals.Sum(static signal => signal.Value);
                Assert.Equal(active ? expected : -1, readers[index].Observed);
            }
        }
    }

    private static void ReportAllocation(string pattern, int batch, long bytes)
    {
        if (batch >= AllocationWarmups)
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"{pattern}: {bytes / (double)AllocationBatchSize:N1} B/op"
            );
    }

    private sealed class AllocationEmptyView : View
    {
        public AllocationEmptyView()
            : this(TestViews.Construction()) { }

        public AllocationEmptyView(ViewConstruction construction)
            : base(construction) { }

        protected override Element Render(ref RenderContext ui) => ui.Text("view");
    }

    private sealed class AllocationSignalView : View
    {
        public AllocationSignalView()
            : this(TestViews.Construction()) { }

        public AllocationSignalView(ViewConstruction construction)
            : base(construction) { }

        private readonly Signal<int> _count = new(0);

        protected override Element Render(ref RenderContext ui)
        {
            _ = _count.Value;
            return ui.Text("view");
        }
    }

    private sealed class AllocationPropsView : View<int>
    {
        public AllocationPropsView()
            : this(TestViews.Construction()) { }

        public AllocationPropsView(ViewConstruction construction)
            : base(construction) { }

        protected override Element Render(in int props, ref RenderContext ui) => ui.Text("view");
    }

    private sealed class AllocationRenderRoot : ProbeView
    {
        protected override Element Render(ref RenderContext ui) => ui.Text("view");
    }

    private readonly record struct AllocationReaderProps(
        Signal<int>[] Signals,
        Signal<int> Alternate,
        Signal<bool> UseAlternate
    );

    private sealed class AllocationReaderView
        : View<AllocationReaderProps>,
            IGeneratedViewFactory<AllocationReaderView, AllocationReaderProps>
    {
        public static ViewSpec<AllocationReaderView, AllocationReaderProps> Spec(
            AllocationReaderProps props
        ) => new(props);

        public AllocationReaderView()
            : this(TestViews.Construction()) { }

        public AllocationReaderView(ViewConstruction construction)
            : base(construction) { }

        public static AllocationReaderView CreateGpuiView(
            ViewConstruction construction,
            AllocationReaderProps initialProps
        ) => new(construction);

        internal readonly Signal<bool> Following = new(true);
        internal int Renders;
        internal int Observed;

        protected override Element Render(in AllocationReaderProps props, ref RenderContext ui)
        {
            Renders++;
            Observed = -1;
            if (Following.Value)
            {
                Observed = 0;
                if (props.UseAlternate.Value)
                    Observed = props.Alternate.Value;
                else
                    foreach (var signal in props.Signals)
                        Observed += signal.Value;
            }
            // Constant text isolates dependency/render machinery from number formatting.
            return ui.Text("reader");
        }
    }

    private sealed class AllocationTreeRoot(Signal<int>[] signals, int readers) : ProbeView
    {
        [System.Runtime.CompilerServices.InlineArray(32)]
        private struct ChildBuffer
        {
            private Element _element;
        }

        internal readonly Signal<int> Alternate = new(42);
        internal readonly Signal<bool> UseAlternate = new(false);

        protected override Element Render(ref RenderContext ui)
        {
            ChildBuffer buffer = default;
            Span<Element> children = ((Span<Element>)buffer)[..readers];
            for (var index = 0; index < readers; index++)
                children[index] = ui.Child(
                    index,
                    AllocationReaderView.Spec(new(signals, Alternate, UseAlternate))
                );
            return ui.Div(children);
        }
    }
}
