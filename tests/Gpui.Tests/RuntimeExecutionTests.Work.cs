using System.Runtime.CompilerServices;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void OwnedWorkRunsWithoutCallerContextAndAppliesThroughForegroundIngress()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var ambient = new AsyncLocal<string?> { Value = "UI state" };
        var foreground = Environment.CurrentManagedThreadId;
        var completed = false;
        var worker = fixture.View.StartWorkCore(
            ambient,
            static (state, _) => Task.FromResult((Environment.CurrentManagedThreadId, SynchronizationContext.Current, state.Value)),
            result =>
            {
                Assert.NotEqual(foreground, result.Item1);
                Assert.Null(result.Item2);
                Assert.Null(result.Value);
                Assert.Equal(foreground, Environment.CurrentManagedThreadId);
                completed = true;
            }
        );
        FinishWorker(worker);
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
        Task? worker = null;
        view.DuringMount = () => worker = view.StartWorkCore(
            41, static (value, _) => Task.FromResult(value + 1), value => signal.Value = value
        );
        fixture.Render();
        FinishWorker(worker!);
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
        var worker = child.StartWorkCore(pending, StartPendingProducer,
            _ => completed = true, _ => failed = true);
        FinishWorker(pending.Started.Task);
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Render();
        Assert.True(pending.Lifetime.IsCancellationRequested);
        if (fail)
            pending.Result.SetException(new InvalidOperationException("late failure"));
        else
            pending.Result.SetResult(12);
        FinishWorker(worker);
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
        var worker = fixture.Child.StartWorkCore(pending, StartPendingProducer, _ => completed = true);
        FinishWorker(pending.Started.Task);
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Publish();
        pending.Result.SetResult(12);
        FinishWorker(worker);
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
        Action<Exception>? onFailure = handled ? error => observed = error : null;
        var worker = fixture.View.StartWorkCore<Exception, int>(
            failure, static (error, _) => throw error,
            _ => Assert.Fail("Failure must not invoke success"), onFailure
        );
        FinishWorker(worker);
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
        var worker = fixture.View.StartWorkCore(
            1, static (value, _) => Task.FromResult(value), _ => completed = true);
        FinishWorker(worker);
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
        var worker = fixture.View.StartWorkCore(
            1, static (value, _) => Task.FromResult(value), _ => throw failure);
        FinishWorker(worker);
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
            _ = fixture.View.StartWorkCore(1, static (value, _) => Task.FromResult(value), _ => { });
        }));
        Assert.Equal(0, fixture.Notifications);
    }

    [Fact]
    public void OwnedWorkRejectsRenderingAndUnownedViews()
    {
        var unowned = new ProbeView();
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = unowned.StartWorkCore(0, static (value, _) => Task.FromResult(value), _ => { });
        });
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        fixture.View.DuringRender = () => fixture.View.StartWorkCore(
            0, static (value, _) => Task.FromResult(value), _ => { });
        var error = Assert.Throws<InvalidOperationException>(fixture.Render);
        Assert.Contains("during rendering", error.Message);
    }

    [Fact]
    public void UncooperativeOwnedWorkDoesNotRetainRetiredViewSessionOrCallbackCaptures()
    {
        var pending = new PendingProducer();
        var (worker, references) = RetirePendingOwnedWork(pending);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        pending.Result.SetResult(1);
        FinishWorker(worker);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task, ProbeView, WeakReference) RetireCapturedCompletion(PendingProducer pending)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var captured = new object();
        var worker = fixture.View.StartWorkCore(pending, StartPendingProducer, _ => GC.KeepAlive(captured));
        FinishWorker(pending.Started.Task);
        return (worker, fixture.View, new WeakReference(captured));
    }

    [Fact]
    public void RetainedRetiredViewReleasesPendingCompletionCaptures()
    {
        var pending = new PendingProducer();
        var (worker, retired, captured) = RetireCapturedCompletion(pending);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(captured.IsAlive);
        pending.Result.SetResult(1);
        FinishWorker(worker);
        GC.KeepAlive(retired);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task, WeakReference[]) RetirePendingOwnedWork(PendingProducer pending)
    {
        var application = new GpuiApplication();
        using var fixture = new SessionFixture(new ProbeView(), application);
        fixture.Render();
        var captured = new object();
        var worker = fixture.View.StartWorkCore(pending, StartPendingProducer,
            _ => { GC.KeepAlive(captured); fixture.View.ClickCount++; });
        FinishWorker(pending.Started.Task);
        return (worker, [new(fixture.View), new(fixture.Session), new(application), new(captured)]);
    }

    private static Task<int> StartPendingProducer(PendingProducer pending, CancellationToken lifetime)
    {
        pending.Lifetime = lifetime;
        pending.Started.SetResult();
        return pending.Result.Task;
    }

    private static void FinishWorker(Task worker) =>
        worker.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

    private sealed class PendingProducer
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<int> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Lifetime;
    }
}
