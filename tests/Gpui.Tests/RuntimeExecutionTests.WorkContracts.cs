namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData("completed", false)]
    [InlineData("completed", true)]
    [InlineData("pending", false)]
    [InlineData("pending", true)]
    [InlineData("throw", false)]
    [InlineData("throw", true)]
    public void WorkCancellationIsDeferredAndDoesNotFaultTheWindow(string pattern, bool handled)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var scope = fixture.View.Runtime.GetWorkScope();
        var source = new TaskCompletionSource<int>();
        var calls = new List<string>();
        if (pattern == "completed")
            source.SetCanceled(new CancellationToken(true));
        var operation = scope.StartCore(
            calls,
            (pattern, source.Task),
            static (request, _) =>
                request.pattern == "throw" ? throw new OperationCanceledException() : request.Task,
            static (state, _) => state.Add("complete"),
            static (state, _) => state.Add("failed"),
            handled ? static state => state.Add("cancelled") : null
        );
        if (pattern == "pending")
            source.SetCanceled(new CancellationToken(true));
        FinishObservation(operation);
        Assert.Empty(calls);
        fixture.Render();
        Assert.Equal(handled ? ["cancelled"] : Array.Empty<string>(), calls);
        Assert.Null(fixture.Session.Failure);
        Assert.Equal(0, fixture.Click());
    }

    [Fact]
    public void FaultedTaskWithCancellationExceptionIsStillAFailure()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var failure = new OperationCanceledException();
        var scope = fixture.View.Runtime.GetWorkScope();
        var observed = new List<Exception>();
        scope.Start(
            observed,
            Task.FromException<int>(failure),
            static (task, _) => task,
            static (_, _) => Assert.Fail("Unexpected success"),
            static (state, error) => state.Add(error),
            static _ => Assert.Fail("A faulted Task must not become cancellation")
        );
        fixture.Render();
        Assert.Same(failure, Assert.Single(observed));
    }

    [Fact]
    public void FaultedTaskDeliversFirstExceptionWithoutRethrowingItDuringObservation()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");
        var source = new TaskCompletionSource<int>();
        source.SetException([first, second]);
        var observed = new List<Exception>();
        fixture
            .View.Runtime.GetWorkScope()
            .Start(
                observed,
                source.Task,
                static (task, _) => task,
                static (_, _) => Assert.Fail("Unexpected success"),
                static (state, error) => state.Add(error)
            );
        fixture.Render();
        Assert.Same(first, Assert.Single(observed));
        Assert.Null(first.StackTrace);
    }

    [Fact]
    public void ApplicationCanReplaceCancelAndRestartWorkWithoutFaultingTheWindow()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var scope = fixture.View.Runtime.GetWorkScope();
        var state = new ReplaceableLoad();
        var oldSource = new TaskCompletionSource<int>();
        var newSource = new TaskCompletionSource<int>();
        var oldWork = state.Start(scope, oldSource.Task, CancellationToken.None);
        var newWork = state.Start(scope, newSource.Task, CancellationToken.None);
        newSource.SetResult(2);
        FinishObservation(newWork);
        fixture.Render();
        Assert.Equal(2, state.Value);
        Assert.False(state.Busy);
        oldSource.SetResult(1);
        FinishObservation(oldWork);
        fixture.Render();
        Assert.Equal(2, state.Value);

        using var cancellation = new CancellationTokenSource();
        var cancelledSource = new TaskCompletionSource<int>();
        var cancelledWork = state.Start(scope, cancelledSource.Task, cancellation.Token);
        Assert.True(state.Busy);
        cancellation.Cancel();
        FinishObservation(cancelledWork);
        fixture.Render();
        Assert.False(state.Busy);
        Assert.Equal(2, state.Value);
        Assert.Null(fixture.Session.Failure);

        state.Start(scope, Task.FromResult(3), CancellationToken.None);
        fixture.Render();
        Assert.Equal(3, state.Value);
        Assert.False(state.Busy);
    }

    private sealed class ReplaceableLoad
    {
        private int _revision;
        internal int Value;
        internal bool Busy;

        internal WorkScope.PendingWork Start(
            WorkScope scope,
            Task<int> task,
            CancellationToken cancellation
        )
        {
            Busy = true;
            return scope.StartCore(
                (load: this, revision: ++_revision),
                (task, cancellation),
                static async (request, lifetime) =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        lifetime,
                        request.cancellation
                    );
                    return await request.task.WaitAsync(linked.Token).ConfigureAwait(false);
                },
                static (state, result) =>
                {
                    if (state.load._revision != state.revision)
                        return;
                    state.load.Value = result;
                    state.load.Busy = false;
                },
                cancelled: static state =>
                {
                    if (state.load._revision == state.revision)
                        state.load.Busy = false;
                }
            );
        }
    }

    [Fact]
    public void CancellationHandlerFailureFaultsIngress()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var failure = new InvalidOperationException("cancel handler");
        fixture
            .View.Runtime.GetWorkScope()
            .Start(
                failure,
                Task.FromCanceled<int>(new(true)),
                static (task, _) => task,
                static (_, _) => Assert.Fail("Unexpected success"),
                cancelled: static error => throw error
            );
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Render));
        Assert.Same(failure, fixture.Session.Failure);
    }

    [Fact]
    public void RetirementDropsAnAlreadyQueuedCancellation()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var calls = new List<string>();
        var source = new TaskCompletionSource<int>();
        var operation = fixture
            .Child.Runtime.GetWorkScope()
            .StartCore(
                calls,
                source.Task,
                static (task, _) => task,
                static (_, _) => Assert.Fail("Unexpected success"),
                cancelled: static state => state.Add("cancelled")
            );
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Publish();
        source.SetCanceled(new CancellationToken(true));
        FinishObservation(operation);
        Assert.Equal(0, fixture.Complete());
        fixture.Render();
        Assert.Empty(calls);
    }
}
