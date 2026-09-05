using System.Diagnostics;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependencyStoragePromotionPreservesAcceptedAndProvisionalEdges(bool reject)
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        var signals = Enumerable.Range(0, 65).Select(static _ => new Signal<int>(0)).ToArray();
        var consumer = new ReactiveConsumer(fixture.Session, fixture.View);
        try
        {
            using (consumer.Begin())
                for (var index = 0; index < 64; index++)
                    _ = signals[index].Value;
            consumer.Commit();
            using (consumer.Begin())
            {
                _ = signals[0].Value;
                _ = signals[64].Value;
            }
            signals[64].Value++;
            Assert.Equal(0, fixture.Notifications);
            if (reject)
            {
                consumer.Abort();
                signals[63].Value++;
            }
            else
                consumer.Commit();
            Assert.Equal(1, fixture.Notifications);
            fixture.Render();

            // Reset invalidation with the accepted set, after conversion and removal.
            using (consumer.Begin())
            {
                _ = signals[0].Value;
                if (reject)
                    for (var index = 1; index < 64; index++)
                        _ = signals[index].Value;
                else
                    _ = signals[64].Value;
            }
            consumer.Commit();
            signals[reject ? 64 : 63].Value++;
            Assert.Equal(1, fixture.Notifications);
            signals[0].Value++;
            Assert.Equal(2, fixture.Notifications);
        }
        finally { consumer.Dispose(); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(256)]
    public void DependencyLookupCost(int count)
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        var signals = Enumerable.Range(0, count).Select(static index => new Signal<int>(index)).ToArray();
        var consumer = new ReactiveConsumer(fixture.Session, fixture.View);
        const int passes = 4096;
        var samples = new double[5];
        try
        {
            for (var batch = 0; batch < 8; batch++)
            {
                long sum = 0;
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var pass = 0; pass < passes; pass++)
                {
                    using (consumer.Begin())
                    {
                        // Alternating order avoids measuring only one traversal order.
                        for (var index = 0; index < count; index++)
                            sum += signals[(pass & 1) == 0 ? index : count - index - 1].Value;
                    }
                    consumer.Commit();
                }
                var elapsed = Stopwatch.GetTimestamp() - start;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal((long)passes * count * (count - 1) / 2, sum);
                if (batch >= 3)
                {
                    Assert.Equal(0, allocated);
                    samples[batch - 3] = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / passes;
                }
            }
            Array.Sort(samples);
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"dependency-lookup-{count}: median={samples[2]:N1} ns/pass, min={samples[0]:N1}, max={samples[^1]:N1}");
        }
        finally { consumer.Dispose(); }
    }
}
