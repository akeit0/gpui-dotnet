using System.Diagnostics;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Fact]
    public void SignalChangeBeforeAcceptanceDoesNotHideDirtyReusedDescendant()
    {
        using var fixture = new SessionFixture(new TreeView());
        fixture.Render();
        var branch = fixture.State(fixture.View).Children!.Values.Select(entry => entry.View).OfType<BranchView>().Single();
        var leaf = fixture.State(branch).Children!.Values.Select(entry => entry.View).OfType<ChildView>().First();
        var signal = new Signal<int>(0);
        leaf.DuringRender = () => _ = signal.Value;
        leaf.Invalidate();
        fixture.Render();
        var before = leaf.RenderCount;
        branch.Invalidate();
        fixture.Publish();
        signal.Value = 1;
        Assert.Equal(0, fixture.Complete());
        fixture.Render();
        Assert.Equal(before + 1, leaf.RenderCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MountedRenderRejectsEffectsOnTheCallingThread(bool post)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        fixture.View.DuringRender = () =>
        {
            if (post) fixture.View.Runtime.Post(static () => { });
            else fixture.View.Invalidate();
        };
        var failure = Assert.Throws<InvalidOperationException>(() => fixture.Render());
        Assert.Contains("during rendering", failure.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DispatcherAdmissionCost(bool validate, bool capture)
    {
        using var fixture = new SessionFixture(new AllocationRenderRoot());
        fixture.Render();
        Action cached = static () => { };
        const int count = 512;
        var samples = new double[7];
        var allocations = new long[7];
        for (var batch = 0; batch < 12; batch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var index = 0; index < count; index++)
            {
                Action callback = cached;
                if (capture)
                {
                    var state = index;
                    callback = () => GC.KeepAlive(state);
                }
                if (validate) InspectCallbackBaseline(callback);
                fixture.View.Runtime.Post(callback);
            }
            var elapsed = Stopwatch.GetTimestamp() - start;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            fixture.Render();
            if (batch >= 5)
            {
                samples[batch - 5] = elapsed * (1e9 / Stopwatch.Frequency) / count;
                allocations[batch - 5] = allocated / count;
            }
        }
        Array.Sort(samples);
        TestContext.Current.TestOutputHelper!.WriteLine($"dispatcher-admission validate={validate} capture={capture}: median={samples[3]:F1} ns/post; allocated={allocations[3]} B/post");
    }

    // Test-only baseline for the removed runtime inspection. Never used by library admission.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Reflection.MethodInfo, object> InspectionCache = new();
    private static readonly object SynchronousMethod = new();

    private static void InspectCallbackBaseline(Delegate callback)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(callback))
        {
            var contract = InspectionCache.GetValue(handler.Method, static method =>
                method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false)
                    ? throw new InvalidOperationException("Async callback") : SynchronousMethod);
            GC.KeepAlive(contract);
        }
    }
}
