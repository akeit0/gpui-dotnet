using System.Runtime.CompilerServices;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Fact]
    public void DispatcherValuesRejectDefaultAndUnownedUse()
    {
        Assert.Throws<InvalidOperationException>(() => default(Dispatcher).Post(static () => { }));
        Assert.Throws<InvalidOperationException>(() => default(Dispatcher).Post(1, static _ => { }));
        var dispatcher = new ProbeView().Dispatcher;
        Assert.Throws<InvalidOperationException>(() => dispatcher.Post(1, static _ => { }));
    }

    [Fact]
    public void CopiedDispatcherPostsExplicitStateFromAWorkerToTheOwningThread()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var dispatcher = fixture.View.Dispatcher;
        var copy = dispatcher;
        var values = new List<int>();
        var state = (values, thread: Environment.CurrentManagedThreadId);
        RunWorker(() => copy.Post(state, static state =>
        {
            Assert.Equal(state.thread, Environment.CurrentManagedThreadId);
            state.values.Add(42);
        }));
        Assert.Empty(values);
        fixture.Render();
        Assert.Equal([42], values);
        Assert.Throws<ArgumentNullException>(() => copy.Post(0, (Action<int>)null!));
    }

    [Fact]
    public void ExplicitStatePostRechecksTheOriginalViewAfterRetirement()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var dispatcher = fixture.Child.Dispatcher;
        var values = new List<int>();
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Publish();
        dispatcher.Post(values, static values => values.Add(1));
        Assert.Equal(0, fixture.Complete());
        fixture.Render();
        Assert.Empty(values);
        Assert.Throws<InvalidOperationException>(() => dispatcher.Post(values, static values => values.Add(2)));
    }

    [Theory]
    [InlineData(false, 32)]
    [InlineData(true, 32)]
    [InlineData(false, 65)]
    [InlineData(true, 65)]
    public void ClearedDependencyStorageDoesNotRetainRemovedOrRejectedSignals(bool reject, int count)
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        var consumer = new ReactiveConsumer(fixture.Session, fixture.View);
        var references = RemoveAllocationDependency(consumer, reject, count);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, static reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(consumer);
        consumer.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RemoveAllocationDependency(ReactiveConsumer consumer, bool reject, int count)
    {
        var signals = Enumerable.Range(0, count).Select(static _ => new Signal<object>(new object())).ToArray();
        var references = signals.SelectMany(static signal => new[] { new WeakReference(signal), new WeakReference(signal.Value) }).ToArray();
        using (consumer.Begin())
            foreach (var signal in signals)
                _ = signal.Value;
        if (reject)
            consumer.Abort();
        else
        {
            consumer.Commit();
            using (consumer.Begin()) { }
            consumer.Commit();
        }
        return references;
    }

    [Fact]
    public void ReusedDependencyPreservesAcceptanceAndRevisionGapSemantics()
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        var consumer = new ReactiveConsumer(fixture.Session, fixture.View);
        var oldSignal = new Signal<int>(0);
        var provisional = new Signal<int>(0);
        var replacement = new Signal<int>(0);
        using (consumer.Begin())
            _ = oldSignal.Value;
        consumer.Commit();
        using (consumer.Begin())
            _ = provisional.Value;
        consumer.Abort();
        provisional.Value++;
        Assert.Equal(0, fixture.Notifications);
        oldSignal.Value++;
        Assert.Equal(1, fixture.Notifications);
        fixture.Render();

        using (consumer.Begin())
            _ = replacement.Value;
        replacement.Value++;
        Assert.Equal(1, fixture.Notifications);
        consumer.Commit();
        Assert.Equal(2, fixture.Notifications);
        fixture.Render();
        oldSignal.Value++;
        Assert.Equal(2, fixture.Notifications);
        consumer.Dispose();
        replacement.Value++;
        Assert.Equal(2, fixture.Notifications);
    }

    [Theory]
    [InlineData("static")]
    [InlineData("explicit-state")]
    [InlineData("local-capture")]
    public void DispatcherAllocationPatterns(string pattern)
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        var dispatcher = fixture.View.Dispatcher;
        var counter = new DispatchCounter();
        for (var batch = 0; batch < AllocationWarmups + AllocationMeasurements; batch++)
        {
            counter.Value = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < AllocationBatchSize; index++)
            {
                if (pattern == "static")
                    dispatcher.Post(static () => { });
                else if (pattern == "explicit-state")
                    dispatcher.Post(counter, static state => state.Value++);
                else
                    PostCapturedState(dispatcher, counter);
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, counter.Value);
            fixture.Render();
            Assert.Equal(pattern == "static" ? 0 : AllocationBatchSize, counter.Value);
            ReportAllocation($"dispatch-{pattern}", batch, bytes);
            if (batch >= AllocationWarmups && pattern == "explicit-state")
                Assert.InRange(bytes, 1, 48L * AllocationBatchSize);
        }
    }

    private static void PostCapturedState(Dispatcher dispatcher, DispatchCounter counter) =>
        dispatcher.Post(() => counter.Value++);

    private sealed class DispatchCounter { internal int Value; }
}
