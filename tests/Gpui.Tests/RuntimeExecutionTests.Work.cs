using System.Runtime.CompilerServices;

namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Fact]
    public void WorkScopeRemovesOutOfOrderOperationsBeforeReentrantStarts()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var scope = fixture.View.Runtime.GetWorkScope();
        var results = new List<int>();
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        var third = new TaskCompletionSource<int>();
        foreach (var source in new[] { first, second, third })
            scope.Start((scope, results), source.Task, static (task, _) => task,
                static (state, value) =>
                {
                    state.results.Add(value);
                    if (value == 2)
                        state.scope.Start(state.results, 4, static (value, _) => Task.FromResult(value),
                            static (results, value) => results.Add(value));
                });
        second.SetResult(2);
        fixture.Render();
        first.SetResult(1);
        fixture.Render();
        third.SetResult(3);
        fixture.Render();
        Assert.Equal([2, 4, 1, 3], results);
    }

    [Fact]
    public void PendingOwnedWorkHasABoundedWarmAllocationCost()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var scope = fixture.View.Runtime.GetWorkScope();
        const int count = 128;
        static long StartBatch(WorkScope scope, ProbeView view, TaskCompletionSource<int>[] sources)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var source in sources)
                scope.Start(view, source.Task, static (task, _) => task,
                    static (owner, value) => owner.ClickCount += value);
            foreach (var source in sources)
                source.SetResult(1);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        static TaskCompletionSource<int>[] Sources(int count) =>
            Enumerable.Range(0, count).Select(static _ => new TaskCompletionSource<int>()).ToArray();
        _ = StartBatch(scope, fixture.View, Sources(count));
        fixture.Render();
        var sources = Sources(count);
        var allocated = StartBatch(scope, fixture.View, sources);
        fixture.Render();
        TestContext.Current.TestOutputHelper!.WriteLine($"Pending work: {allocated / (double)count:N1} bytes/operation");
        Assert.InRange(allocated, 1, count * 224L);
        Assert.Equal(2 * count, fixture.View.ClickCount);
    }

    [Fact]
    public void OwnedWorkPreservesTheProducersForegroundAwaitContext()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var source = new TaskCompletionSource<int>();
        var foreground = Environment.CurrentManagedThreadId;
        WorkScope.PendingWork? operation = null;
        fixture.View.OnClick = () => operation = fixture.View.Runtime.GetWorkScope().StartCore(0,
            (source.Task, Thread: foreground),
            static async (request, _) =>
            {
                Assert.Equal(request.Thread, Environment.CurrentManagedThreadId);
                Assert.NotNull(SynchronizationContext.Current);
                var context = SynchronizationContext.Current;
                var value = await request.Task;
                Assert.Equal(request.Thread, Environment.CurrentManagedThreadId);
                Assert.Same(context, SynchronizationContext.Current);
                return value;
            },
            (_, value) => fixture.View.ClickCount += value);
        Assert.Equal(0, fixture.Click());
        Assert.NotNull(operation);
        Assert.False(operation.IsFinished);
        // The application controls the completion source. Its await continuation enters UI ingress.
        source.SetResult(12);
        fixture.RenderFromNative();
        FinishObservation(operation);
        fixture.RenderFromNative();
        Assert.Equal(13, fixture.View.ClickCount);
    }

    [Fact]
    public void CompletedOwnedWorkHasABoundedWarmAllocationCost()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        const int count = 128;
        var completed = Task.FromResult(42);
        static long StartBatch(ProbeView view, Task<int> completed, int count)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < count; index++)
                _ = view.Runtime.GetWorkScope().StartCore(view, completed, static (task, _) => task,
                    static (owner, value) => owner.ClickCount += value);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        _ = StartBatch(fixture.View, completed, count);
        fixture.Render();
        var allocated = StartBatch(fixture.View, completed, count);
        fixture.Render();
        TestContext.Current.TestOutputHelper!.WriteLine($"Completed work: {allocated / (double)count:N1} bytes/operation");
        // One operation record plus amortized ingress segment storage. Producer Tasks,
        // initial registry capacity, and rendering are outside this measurement.
        Assert.InRange(allocated, 1, count * 160L);
        Assert.Equal(2 * count * 42, fixture.View.ClickCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedWorkDeliversExplicitCompletionStateOnlyThroughForegroundIngress(bool fail)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var pending = new PendingProducer();
        var failure = new InvalidOperationException("producer failed");
        var state = (View: fixture.View, Thread: Environment.CurrentManagedThreadId, Failure: failure);
        var operation = fixture.View.Runtime.GetWorkScope().StartCore(state, pending, StartPendingProducer,
            static (state, result) =>
            {
                Assert.Equal(state.Thread, Environment.CurrentManagedThreadId);
                state.View.ClickCount += result;
            },
            static (state, error) =>
            {
                Assert.Equal(state.Thread, Environment.CurrentManagedThreadId);
                Assert.Same(state.Failure, error);
                state.View.SecondClickCount++;
            });
        FinishObservation(pending.Started.Task);
        if (fail)
            pending.Result.SetException(failure);
        else
            pending.Result.SetResult(12);
        FinishObservation(operation);
        Assert.Equal(0, fixture.View.ClickCount);
        Assert.Equal(0, fixture.View.SecondClickCount);
        fixture.Render();
        Assert.Equal(fail ? 0 : 12, fixture.View.ClickCount);
        Assert.Equal(fail ? 1 : 0, fixture.View.SecondClickCount);
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void OwnedWorkInvokesProducerOnCallingThreadWithoutChangingContext()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var ambient = new AsyncLocal<string?> { Value = "UI state" };
        var foreground = Environment.CurrentManagedThreadId;
        var context = SynchronizationContext.Current;
        var completed = false;
        var operation = fixture.View.Runtime.GetWorkScope().StartCore(0,
            ambient,
            static (state, _) => Task.FromResult((Environment.CurrentManagedThreadId, SynchronizationContext.Current, state.Value)),
            (_, result) =>
            {
                Assert.Equal(foreground, result.Item1);
                Assert.Same(context, result.Item2);
                Assert.Equal("UI state", result.Value);
                Assert.Equal(foreground, Environment.CurrentManagedThreadId);
                completed = true;
            }
        );
        FinishObservation(operation);
        Assert.False(completed);
        Assert.Equal("UI state", ambient.Value);
        fixture.Render();
        Assert.True(completed);
    }

    [Fact]
    public void OwnedWorkCanStartFromMountAndCompleteWithASignalWrite()
    {
        var signal = new Signal<int>(0);
        var view = new ProbeView { DuringRender = () => _ = signal.Value };
        using var fixture = new SessionFixture(view);
        WorkScope.PendingWork? operation = null;
        view.DuringMount = () => operation = view.Runtime.GetWorkScope().StartCore(0,
            41, static (value, _) => Task.FromResult(value + 1), (_, value) => signal.Value = value
        );
        fixture.Render();
        FinishObservation(operation!);
        Assert.Equal(0, signal.Value);
        fixture.Render();
        Assert.Equal(42, signal.Value);
        Assert.Equal(1, view.MountCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedWorkDropsLateSuccessAndFailureAfterChildRetirement(bool fail)
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        var pending = new PendingProducer();
        var completed = false;
        var failed = false;
        var operation = child.Runtime.GetWorkScope().StartCore(0,pending, StartPendingProducer,
            (_, _) => completed = true, (_, _) => failed = true);
        FinishObservation(pending.Started.Task);
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Render();
        Assert.True(pending.Lifetime.IsCancellationRequested);
        if (fail)
            pending.Result.SetException(new InvalidOperationException("late failure"));
        else
            pending.Result.SetResult(12);
        FinishObservation(operation);
        fixture.Render();
        Assert.False(completed);
        Assert.False(failed);
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void OwnedWorkRechecksRetirementAfterResultWasQueued()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var pending = new PendingProducer();
        var completed = false;
        var operation = fixture.Child.Runtime.GetWorkScope().StartCore(0,pending, StartPendingProducer, (_, _) => completed = true);
        FinishObservation(pending.Started.Task);
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Publish();
        pending.Result.SetResult(12);
        FinishObservation(operation);
        Assert.Equal(0, fixture.Complete());
        fixture.Render();
        Assert.False(completed);
        Assert.Null(fixture.Session.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedWorkRoutesLiveProducerFailureThroughIngress(bool handled)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var failure = new InvalidOperationException("producer failed");
        Exception? observed = null;
        Action<int, Exception>? onFailure = handled ? (_, error) => observed = error : null;
        var operation = fixture.View.Runtime.GetWorkScope().StartCore<int, Exception, int>(0,
            failure, static (error, _) => throw error,
            (_, _) => Assert.Fail("Failure must not invoke success"), onFailure
        );
        FinishObservation(operation);
        Assert.Null(observed);
        Assert.Null(fixture.Session.Failure);
        if (handled)
        {
            fixture.Render();
            Assert.Same(failure, observed);
            Assert.Null(fixture.Session.Failure);
        }
        else
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Render));
            Assert.Same(failure, fixture.Session.Failure);
        }
    }

    [Fact]
    public void OwnedWorkObservesPostingFailureAndDoesNotInvokeCompletion()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        fixture.NotifyStatus = -32;
        var completed = false;
        var operation = fixture.View.Runtime.GetWorkScope().StartCore(0,
            1, static (value, _) => Task.FromResult(value), (_, _) => completed = true);
        FinishObservation(operation);
        Assert.NotNull(fixture.Session.Failure);
        Assert.False(completed);
        Assert.Throws<InvalidOperationException>(fixture.Render);
    }

    [Fact]
    public void OwnedWorkCompletionFailureFaultsIngressAfterRemovingTheOperation()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var failure = new InvalidOperationException("completion failed");
        var operation = fixture.View.Runtime.GetWorkScope().StartCore(0,
            1, static (value, _) => Task.FromResult(value), (_, _) => throw failure);
        FinishObservation(operation);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Render));
        Assert.Same(failure, fixture.Session.Failure);
    }

    [Fact]
    public void OwnedWorkRejectsAFaultedSessionBeforeScheduling()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var failure = new InvalidOperationException("terminal session");
        fixture.Session.RecordFailure(failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
        {
            _ = fixture.View.Runtime.GetWorkScope().StartCore(0,1, static (value, _) => Task.FromResult(value), (_, _) => { });
        }));
        Assert.Equal(0, fixture.Notifications);
    }

    [Fact]
    public void OwnedWorkRejectsRenderingAndUnownedViews()
    {
        var unowned = new ProbeView();
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = unowned.Runtime.GetWorkScope().StartCore(0,0, static (value, _) => Task.FromResult(value), (_, _) => { });
        });
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        fixture.View.DuringRender = () => fixture.View.Runtime.GetWorkScope().StartCore(0,
            0, static (value, _) => Task.FromResult(value), (_, _) => { });
        var error = Assert.Throws<InvalidOperationException>(fixture.Render);
        Assert.Contains("during rendering", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncooperativeOwnedWorkDoesNotRetainRetiredViewSessionOrCallbackCaptures(bool explicitState)
    {
        var pending = new PendingProducer();
        var (operation, scope, references) = RetirePendingOwnedWork(pending, explicitState);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        pending.Result.SetResult(1);
        FinishObservation(operation);
        Assert.Throws<InvalidOperationException>(() => scope.Start(
            0, 0, static (value, _) => Task.FromResult(value), static (_, _) => { }));
        GC.KeepAlive(scope);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WorkScope.PendingWork, ProbeView, WeakReference) RetireCapturedCompletion(PendingProducer pending, bool explicitState)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var captured = new object();
        var operation = explicitState
            ? fixture.View.Runtime.GetWorkScope().StartCore(captured, pending, StartPendingProducer, static (state, _) => GC.KeepAlive(state))
            : fixture.View.Runtime.GetWorkScope().StartCore(0,pending, StartPendingProducer, (_, _) => GC.KeepAlive(captured));
        FinishObservation(pending.Started.Task);
        return (operation, fixture.View, new WeakReference(captured));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedRetiredViewReleasesPendingCompletionCaptures(bool explicitState)
    {
        var pending = new PendingProducer();
        var (operation, retired, captured) = RetireCapturedCompletion(pending, explicitState);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(captured.IsAlive);
        pending.Result.SetResult(1);
        FinishObservation(operation);
        GC.KeepAlive(retired);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WorkScope.PendingWork, WorkScope, WeakReference[]) RetirePendingOwnedWork(PendingProducer pending, bool explicitState)
    {
        var application = new GpuiApplication();
        using var fixture = new SessionFixture(new ProbeView(), application);
        fixture.Render();
        var captured = new object();
        var operation = explicitState
            ? fixture.View.Runtime.GetWorkScope().StartCore((captured, fixture), pending, StartPendingProducer,
                static (state, _) => { GC.KeepAlive(state.captured); state.fixture.View.ClickCount++; })
            : fixture.View.Runtime.GetWorkScope().StartCore(0,pending, StartPendingProducer,
                (_, _) => { GC.KeepAlive(captured); fixture.View.ClickCount++; });
        FinishObservation(pending.Started.Task);
        return (operation, fixture.View.Runtime.GetWorkScope(),
            [new(fixture.View), new(fixture.Session), new(application), new(captured)]);
    }

    private static Task<int> StartPendingProducer(PendingProducer pending, CancellationToken lifetime)
    {
        pending.Lifetime = lifetime;
        pending.Started.SetResult();
        return pending.Result.Task;
    }

    private static void FinishObservation(Task operation) =>
        operation.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

    private static void FinishObservation(WorkScope.PendingWork observation) =>
        Assert.True(SpinWait.SpinUntil(() => observation.IsFinished, TimeSpan.FromSeconds(5)));

    private sealed class PendingProducer
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<int> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Lifetime;
    }
}
