using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gpui.Interop;
using Gpui.Interop.Internal;
using Gpui.Interop.Internal.Session;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void SynchronousEventCancellationFaultsTheSession()
    {
        var failure = new OperationCanceledException("synchronous event failed");
        using var fixture = new SessionFixture(new ProbeView { OnClick = () => throw failure });
        fixture.Render();
        Assert.Equal(-111, fixture.Click());
        Assert.Same(failure, fixture.Session.Failure);
    }

    [Fact]
    public void RowSignalsInvalidateOnlyTheirAcceptedArtifactsAndBatchAtCallbackExit()
    {
        var first = new Signal<int>(0);
        var second = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView
        {
            DuringRow = index => _ = index == 0 ? first.Value : second.Value
        });
        fixture.Render();
        var a = fixture.Range(0);
        var b = fixture.Range(1);
        fixture.View.OnClick = () => { first.Value++; second.Value++; first.Value++; };
        Assert.Equal(0, fixture.Click());
        Assert.Single(fixture.ArtifactBatches);
        Assert.Equal(new[] { a, b }, fixture.ArtifactBatches[0].Select(key => key.artifact).Order().ToArray());
        Assert.Equal(0, fixture.Notifications);
        Assert.False(fixture.State(fixture.View).Dirty);
        Assert.Equal(1, fixture.View.RenderCount);
        Assert.Equal(0, fixture.Release(1, a));
        Assert.Equal(0, fixture.Release(1, b));
        first.Value++;
        second.Value++;
        Assert.Single(fixture.ArtifactBatches);
    }

    [Fact]
    public void AnUnacceptedRowObservationDoesNotSubscribeAndClosesTheRevisionGap()
    {
        var signal = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value });
        fixture.Render();
        var artifact = fixture.Range(0, accept: false);
        signal.Value++;
        Assert.Empty(fixture.ArtifactBatches);
        Assert.Equal(0, fixture.Accept(1, artifact));
        Assert.Equal(artifact, Assert.Single(Assert.Single(fixture.ArtifactBatches)).artifact);
        Assert.Equal(-109, fixture.Accept(1, artifact));
    }

    [Fact]
    public void SourcesSharingARendererHaveIndependentSignalSubscriptions()
    {
        var signal = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value });
        fixture.Render();
        var a = fixture.Range(0, source: 10);
        var b = fixture.Range(0, source: 20);
        Assert.Equal(0, fixture.Release(10, a));
        signal.Value++;
        var key = Assert.Single(Assert.Single(fixture.ArtifactBatches));
        Assert.Equal(20UL, key.source);
        Assert.Equal(b, key.artifact);
    }

    [Fact]
    public void RejectedDemandObservationNeverSubscribes()
    {
        var signal = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value });
        fixture.Render();
        var artifact = fixture.Range(0, accept: false);
        Assert.Equal(-109, fixture.Release(1, artifact, -40));
        signal.Value++;
        Assert.Empty(fixture.ArtifactBatches);
    }

    [Fact]
    public void SharedSignalInvalidatesTwoWindowsInOneApplication()
    {
        var signal = new Signal<int>(0);
        var application = new GpuiApplication();
        using var first = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value }, application);
        using var second = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value }, application);
        first.Render();
        second.Render();
        signal.Value++;
        Assert.True(first.State(first.View).Dirty);
        Assert.True(second.State(second.View).Dirty);
        Assert.Equal(1, first.Notifications);
        Assert.Equal(1, second.Notifications);
    }

    [Fact]
    public void SignalNotificationFailureStillInvalidatesLaterSubscribers()
    {
        var signal = new Signal<int>(0);
        var application = new GpuiApplication();
        var observed = -1;
        using var healthy = new SessionFixture(new ProbeView { DuringRender = () => observed = signal.Value }, application);
        using var secondFailure = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value }, application);
        using var firstFailure = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value }, application);
        // New subscriptions precede old ones: both failures occur before the healthy subscriber.
        healthy.Render();
        secondFailure.Render();
        firstFailure.Render();
        firstFailure.NotifyStatus = -32;
        secondFailure.NotifyStatus = -33;

        var error = Assert.Throws<InvalidOperationException>(() => signal.Set(1));
        Assert.Equal(1, signal.Value);
        Assert.Same(firstFailure.Session.Failure, error);
        Assert.Contains("NotifyView", error.StackTrace);
        Assert.NotNull(secondFailure.Session.Failure);
        Assert.Null(healthy.Session.Failure);
        Assert.Equal(1, healthy.Notifications);
        Assert.True(healthy.State(healthy.View).Dirty);
        Assert.False(signal.Set(1));
        healthy.Render();
        Assert.Equal(1, observed);
        Assert.True(signal.Set(2));
        healthy.Render();
        Assert.Equal(2, observed);
        Assert.Equal(1, firstFailure.Notifications);
        Assert.Equal(1, secondFailure.Notifications);
    }

    [Fact]
    public void SignalNotificationFailureStillFlushesLaterRowSubscribersAndPreservesFirstError()
    {
        var signal = new Signal<int>(0);
        var application = new GpuiApplication();
        using var healthyRows = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value }, application);
        using var failedRows = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value }, application);
        using var failedView = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value }, application);
        healthyRows.Render();
        var healthyArtifact = healthyRows.Range(0);
        failedRows.Render();
        failedRows.Range(0);
        failedView.Render();
        failedView.NotifyStatus = -32;
        failedRows.ArtifactStatus = -33;

        var error = Assert.Throws<InvalidOperationException>(() => signal.Set(1));
        Assert.Same(failedView.Session.Failure, error);
        Assert.NotNull(failedRows.Session.Failure);
        Assert.Null(healthyRows.Session.Failure);
        Assert.Equal(healthyArtifact, Assert.Single(Assert.Single(healthyRows.ArtifactBatches)).artifact);
        Assert.Single(failedRows.ArtifactBatches);
        Assert.False(healthyRows.State(healthyRows.View).Dirty);
        Assert.Null(ApplicationExecution.Current);
        Assert.False(signal.Set(1));
        // Flush failure must also clear application scratch state and permit another delivery.
        Assert.Equal(0, healthyRows.Release(1, healthyArtifact));
        var replacement = healthyRows.Range(0);
        Assert.True(signal.Set(2));
        Assert.Equal(replacement, Assert.Single(healthyRows.ArtifactBatches[1]).artifact);
        Assert.Single(failedRows.ArtifactBatches);
    }

    [Fact]
    public void SignalRowFlushFailureStillDeliversOtherWindows()
    {
        var signal = new Signal<int>(0);
        var application = new GpuiApplication();
        using var healthy = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value }, application);
        using var failing = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value }, application);
        healthy.Render();
        var artifact = healthy.Range(0);
        failing.Render();
        failing.Range(0);
        failing.ArtifactStatus = -33;

        var error = Assert.Throws<InvalidOperationException>(() => signal.Set(1));
        Assert.Same(failing.Session.Failure, error);
        Assert.Equal(artifact, Assert.Single(Assert.Single(healthy.ArtifactBatches)).artifact);
        Assert.Null(healthy.Session.Failure);
        Assert.Equal(1, signal.Value);
        Assert.Null(ApplicationExecution.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignalFailureInAnEventStillFlushesHealthyRows(bool failRowFlush)
    {
        var signal = new Signal<int>(0);
        var application = new GpuiApplication();
        using var healthyRows = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value }, application);
        using var otherRows = new SessionFixture(new ProbeView { DuringRow = _ => _ = signal.Value }, application);
        using var failedView = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value }, application);
        using var writer = new SessionFixture(new ProbeView { OnClick = () => signal.Set(1) }, application);
        healthyRows.Render();
        var artifact = healthyRows.Range(0);
        otherRows.Render();
        otherRows.Range(0);
        failedView.Render();
        writer.Render();
        failedView.NotifyStatus = -32;
        otherRows.ArtifactStatus = failRowFlush ? -33 : 0;

        Assert.Equal(-111, writer.Click());
        Assert.Same(failedView.Session.Failure, writer.Session.Failure);
        Assert.Equal(failRowFlush, otherRows.Session.Failure is not null);
        Assert.Equal(artifact, Assert.Single(Assert.Single(healthyRows.ArtifactBatches)).artifact);
        Assert.Single(otherRows.ArtifactBatches);
        Assert.Null(healthyRows.Session.Failure);
        Assert.Equal(1, signal.Value);
        Assert.Null(ApplicationExecution.Current);
    }

    [Fact]
    public void ASignalReadOutsideRenderingDoesNotSubscribe()
    {
        var signal = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView { OnClick = () => _ = signal.Value });
        fixture.Render();
        Assert.Equal(0, fixture.Click());
        signal.Value++;
        Assert.False(fixture.State(fixture.View).Dirty);
        Assert.Equal(0, fixture.Notifications);
    }

    [Fact]
    public void ASignalWriteFromMountInvalidatesTheAlreadyAcceptedConsumer()
    {
        var signal = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView
        {
            DuringRender = () => _ = signal.Value,
            DuringMount = () => signal.Value++
        });
        fixture.Render();
        Assert.True(fixture.State(fixture.View).Dirty);
        fixture.Render();
        Assert.False(fixture.State(fixture.View).Dirty);
    }

    [Fact]
    public void SignalDoesNotRetainARetiredViewSessionOrApplication()
    {
        var signal = new Signal<int>(0);
        var references = RetireSignalConsumer(signal);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        signal.Value++;
        GC.KeepAlive(signal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RetireSignalConsumer(Signal<int> signal)
    {
        var application = new GpuiApplication();
        using var fixture = new SessionFixture(new ProbeView
        {
            DuringRender = () => _ = signal.Value,
            DuringRow = index => _ = signal.Value
        }, application);
        fixture.Render();
        fixture.Range(0);
        return [new(fixture.View), new(fixture.Session), new(application)];
    }

    [Fact]
    public void WarmSignalReadsAndCoalescedWritesAllocateNothing()
    {
        var signal = new Signal<int>(0);
        var allocated = -1L;
        using var fixture = new SessionFixture(new ProbeView
        {
            DuringRender = () =>
            {
                _ = signal.Value;
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 10_000; i++)
                    _ = signal.Value;
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
        });
        fixture.Render();
        fixture.Render();
        Assert.Equal(0, allocated);
        for (var batch = 0; batch < 7; batch++)
        {
            var bytes = MeasureCoalescedSignalWrites(signal);
            if (batch >= 4)
                Assert.Equal(0, bytes);
        }
        Assert.Equal(1, fixture.Notifications);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureCoalescedSignalWrites(Signal<int> signal)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            signal.Value++;
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void SignalChangeInvalidatesOnlyTheChildThatReadIt()
    {
        var value = new Signal<int>(0);
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        child.DuringRender = () => _ = value.Value;
        child.Invalidate();
        fixture.Render();
        Assert.True(value.Set(1));
        fixture.Render();
        Assert.Equal(3, child.RenderCount);
        Assert.False(value.Set(1));
        fixture.Render();
        Assert.Equal(3, child.RenderCount);
    }

    [Fact]
    public void PausedSharedSignalReaderReusesItsFragmentWhileItsSiblingRenders()
    {
        var parent = new SignalSiblingParentView();
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var readers = fixture.State(parent).Children!.Values
            .Select(entry => (SharedSignalReaderView)entry.View).ToArray();
        var live = readers.Single(reader => !reader.CanPause);
        var pausable = readers.Single(reader => reader.CanPause);
        Assert.Equal(1, live.RenderCount);
        Assert.Equal(1, pausable.RenderCount);

        parent.OnClick = () => parent.Count.Value++;
        Assert.Equal(0, fixture.Click());
        fixture.Render();
        Assert.Equal(2, live.RenderCount);
        Assert.Equal(2, pausable.RenderCount);

        Assert.Equal(0, fixture.Click(pausable.ToggleToken));
        fixture.Render();
        Assert.Equal(2, live.RenderCount);
        Assert.Equal(3, pausable.RenderCount);
        Assert.Null(pausable.ObservedValue);

        Assert.Equal(0, fixture.Click());
        Assert.True(fixture.State(live).Dirty);
        Assert.False(fixture.State(pausable).Dirty);
        fixture.Render();
        Assert.Equal(3, live.RenderCount);
        Assert.Equal(2, live.ObservedValue);
        Assert.Equal(3, pausable.RenderCount);

        Assert.Equal(0, fixture.Click(pausable.ToggleToken));
        fixture.Render();
        Assert.Equal(3, live.RenderCount);
        Assert.Equal(4, pausable.RenderCount);
        Assert.Equal(2, pausable.ObservedValue);
    }

    [Fact]
    public void ConditionalSignalDependenciesChangeOnlyAtAcceptance()
    {
        var first = new Signal<int>(0);
        var second = new Signal<int>(0);
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        child.DuringRender = () => _ = first.Value;
        child.Invalidate();
        fixture.Render();
        child.DuringRender = () => _ = second.Value;
        child.Invalidate();
        fixture.Publish();
        Assert.Equal(0, fixture.Complete());
        first.Value++;
        Assert.False(fixture.State(child).Dirty);
        second.Value++;
        Assert.True(fixture.State(child).Dirty);
    }

    [Fact]
    public void SignalChangeBetweenObservationAndAcceptanceIsNotLost()
    {
        var signal = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value });
        fixture.Publish();
        signal.Value = 1;
        Assert.Equal(0, fixture.Complete());
        Assert.True(fixture.State(fixture.View).Dirty);
    }

    [Fact]
    public void BoundSignalRejectsReadsAndWritesOnAWorker()
    {
        var signal = new Signal<int>(0);
        IReadOnlySignal<int> readOnly = signal;
        using var fixture = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value });
        fixture.Render();
        RunWorker(() =>
        {
            Assert.Throws<InvalidOperationException>(() => signal.Value);
            Assert.Throws<InvalidOperationException>(() => readOnly.Value);
            Assert.Throws<InvalidOperationException>(() => signal.Set(1));
        });
    }

    [Fact]
    public void SignalCannotBindToAnotherApplicationOnTheSameThread()
    {
        var signal = new Signal<int>(0);
        using var first = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value });
        using var second = new SessionFixture(new ProbeView { DuringRender = () => _ = signal.Value });
        first.Render();
        Assert.Throws<InvalidOperationException>(second.Render);
    }

    [Fact]
    public void EvenAnEqualUnboundSignalWriteDuringRenderIsRejected()
    {
        var signal = new Signal<int>(0);
        using var fixture = new SessionFixture(new ProbeView { DuringRender = () => signal.Set(0) });
        Assert.Throws<InvalidOperationException>(fixture.Render);
    }

    [Fact]
    public void PublishedChildRemainsDirtyUntilNativeAcknowledgement()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Publish();
        var child = fixture.CandidateChild;
        Assert.True(fixture.State(child).Dirty);
        Assert.Equal(0, fixture.Complete());
        Assert.False(fixture.State(child).Dirty);
        fixture.Render();
        Assert.Equal(1, child.RenderCount);
    }

    [Fact]
    public void NativeRejectionImmediatelyRetiresTheRenderedChild()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        child.Invalidate();
        fixture.Publish();
        Assert.Equal(-103, fixture.Complete(status: -40));
        Assert.True(child.Runtime.IsUnmounted);
    }

    [Fact]
    public void RootFailureAfterRenderingAChildImmediatelyRetiresThatChild()
    {
        var parent = new ParentView();
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var child = fixture.Child;
        child.Invalidate();
        parent.AfterChildren = () => throw new InvalidOperationException("parent render failed");
        Assert.Throws<InvalidOperationException>(fixture.Publish);
        Assert.Equal(2, child.RenderCount);
        Assert.True(child.Runtime.IsUnmounted);
    }

    [Fact]
    public void MountingObservesTheWholeTreeWithAcceptedCleanState()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Publish();
        var child = fixture.CandidateChild;
        fixture.View.DuringMount = () =>
        {
            Assert.False(fixture.State(fixture.View).Dirty);
            Assert.False(fixture.State(child).Dirty);
        };
        Assert.Equal(0, fixture.Complete());
    }

    [Fact]
    public void InvalidationWhileAwaitingAcceptanceSurvivesAcknowledgement()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        child.Invalidate();
        fixture.Publish();
        RunWorker(child.Invalidate);
        Assert.Equal(0, fixture.Complete());
        fixture.Render();
        Assert.Equal(3, child.RenderCount);
        fixture.Render();
        Assert.Equal(3, child.RenderCount);
    }

    [Fact]
    public void RootInvalidationReusesCleanDescendants()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        fixture.View.Invalidate();
        fixture.Render();
        Assert.Equal(2, fixture.View.RenderCount);
        Assert.Equal(1, fixture.Child.RenderCount);
    }

    [Fact]
    public void SiblingInvalidationsPropagateThroughAnAlreadyDirtyBranch()
    {
        using var fixture = new SessionFixture(new TreeView());
        fixture.Render();
        var children = fixture.State(fixture.View).Children!.Values.Select(entry => entry.View).ToArray();
        var branch = Assert.IsType<BranchView>(children.Single(view => view is BranchView));
        var unaffected = Assert.IsType<ChildView>(children.Single(view => view is ChildView));
        var leaves = fixture.State(branch).Children!.Values.Select(entry => (ChildView)entry.View).ToArray();

        // Dirty the common ancestor first, then both leaves. Each leaf must still render.
        branch.Invalidate();
        foreach (var leaf in leaves)
            leaf.Invalidate();
        fixture.Render();
        Assert.Equal(2, branch.RenderCount);
        Assert.All(leaves, leaf => Assert.Equal(2, leaf.RenderCount));
        Assert.Equal(1, unaffected.RenderCount);
        fixture.Render();
        Assert.Equal(2, branch.RenderCount);
    }

    [Fact]
    public void ChangedPropsRemainDirtyUntilAcceptedAndThenReuseTheFragment()
    {
        var parent = new PropsParentView();
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var child = Assert.IsType<PropsChildView>(fixture.State(parent).Children!.Values.Single().View);
        parent.Label = "changed";
        fixture.Publish();
        Assert.True(fixture.State(child).Dirty);
        Assert.Equal(0, fixture.Complete());
        Assert.False(fixture.State(child).Dirty);
        fixture.Render();
        Assert.Equal(2, child.RenderCount);
        Assert.Equal(new LabelProps("changed"), child.CurrentProps);
    }

    [Fact]
    public void RenderRootOutputPreparesViewsWithoutRunningMountHooks()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Publish();
        Assert.Equal(0, fixture.View.MountCount);
    }

    [Fact]
    public void FailedRootRenderNeverMountsOrUnmountsThePreparedView()
    {
        using var fixture = new SessionFixture(new ProbeView
        {
            DuringRender = () => throw new InvalidOperationException("invalid declaration")
        });
        Assert.Throws<InvalidOperationException>(fixture.Render);
        fixture.Session.Stop();
        Assert.Equal(0, fixture.View.MountCount);
        Assert.Equal(0, fixture.View.UnmountCount);
    }

    [Fact]
    public void AcceptanceCommitsTheTreeBeforeParentFirstMounting()
    {
        using var fixture = new SessionFixture(new ParentView());
        var order = new List<string>();
        fixture.Publish();
        var child = fixture.CandidateChild;
        fixture.View.DuringMount = () =>
        {
            Assert.Same(child, fixture.Child);
            Assert.Null(fixture.State(fixture.View).StagedChildren?.SingleOrDefault().Value);
            Assert.True(child.Runtime.IsMounted);
            order.Add("parent");
        };
        child.DuringMount = () => order.Add("child");
        Assert.Equal(0, fixture.Complete());
        Assert.Equal(new[] { "parent", "child" }, order);
        fixture.Render();
        Assert.Equal(1, child.MountCount);
        Assert.Equal(1, fixture.View.MountCount);
    }

    [Fact]
    public void AcceptanceCommitsChildPropsBeforeParentMounting()
    {
        using var fixture = new SessionFixture(new PropsParentView());
        fixture.Publish();
        fixture.View.DuringMount = () =>
        {
            var child = Assert.IsType<PropsChildView>(fixture.State(fixture.View).Children!.Values.Single().View);
            Assert.Equal(new LabelProps("accepted"), child.CurrentProps);
        };
        Assert.Equal(0, fixture.Complete());
    }

    [Fact]
    public void NativeRejectionRetiresPreparedCandidatesWithoutLifecycleCallbacks()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Publish();
        var child = fixture.CandidateChild;
        var lifetime = child.CapturedLifetime;
        Assert.Equal(-103, fixture.Complete(status: -40));
        fixture.Session.Stop();
        Assert.Equal(0, fixture.View.MountCount);
        Assert.Equal(0, fixture.View.UnmountCount);
        Assert.Equal(0, child.MountCount);
        Assert.Equal(0, child.UnmountCount);
        Assert.True(lifetime.IsCancellationRequested);
        Assert.True(child.Runtime.IsUnmounted);
        Assert.Contains("-40", fixture.Session.Failure!.Message);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(2UL)]
    public void UnmatchedAcknowledgementFaultsWithoutMounting(ulong revision)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Publish();
        Assert.Equal(-103, fixture.Complete(revision));
        Assert.Equal(0, fixture.View.MountCount);
        Assert.NotNull(fixture.Session.Failure);
    }

    [Fact]
    public void DuplicateAcknowledgementCannotMountOrCommitAgain()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Publish();
        var revision = fixture.Session.PendingRenderRevision;
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Equal(-103, fixture.Complete(revision));
        Assert.Equal(1, fixture.View.MountCount);
    }

    [Fact]
    public void NativePublicationReturnsARevisionAndBlocksEventsUntilAcknowledged()
    {
        using var fixture = new SessionFixture(new ProbeView());
        Assert.Equal(0, fixture.NativePublish(out var revision));
        Assert.NotEqual(0UL, revision);
        Assert.Equal(fixture.Session.PendingRenderRevision, revision);
        Assert.Equal(-111, fixture.Click());
        Assert.Equal(0, fixture.View.ClickCount);
        Assert.Equal(-103, fixture.Complete(revision));
        Assert.Equal(0, fixture.View.MountCount);
    }

    [Fact]
    public void PublicationCannotOverwriteAnUnacceptedArena()
    {
        using var fixture = new SessionFixture(new ProbeView());
        Assert.Equal(0, fixture.NativePublish(out _));
        Assert.Equal(-101, fixture.NativePublish(out var revision));
        Assert.Equal(0UL, revision);
        Assert.Equal(1, fixture.View.RenderCount);
    }

    [Fact]
    public void MountInvalidationSchedulesTheNextRender()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.View.DuringMount = fixture.View.Invalidate;
        fixture.Publish();
        Assert.Throws<InvalidOperationException>(fixture.View.Invalidate);
        Assert.Equal(0, fixture.Complete());
        Assert.Equal(1, fixture.Notifications);
        Assert.Equal(1, fixture.View.RenderCount);
        fixture.Render();
        Assert.Equal(2, fixture.View.RenderCount);
    }

    [Fact]
    public void MountFailureCleansUpTheParentAndNeverMountsItsChildren()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Publish();
        var child = fixture.CandidateChild;
        fixture.View.DuringMount = () => throw new InvalidOperationException("mount failure");
        Assert.Equal(-103, fixture.Complete());
        fixture.Session.Stop();
        Assert.Equal(1, fixture.View.UnmountCount);
        Assert.Equal(0, child.MountCount);
        Assert.Equal(0, child.UnmountCount);
        Assert.True(child.Runtime.IsUnmounted);
    }

    [Fact]
    public void PublicationDoesNotRetireThePreviouslyAcceptedChild()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Publish();
        Assert.Same(child, fixture.Child);
        Assert.Equal(0, child.UnmountCount);
        Assert.Equal(0, fixture.Complete());
        Assert.Equal(1, child.UnmountCount);
    }

    [Fact]
    public void InvalidateQueuesOneRequestWithoutTouchingRetainedStateOnTheWorker()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        var state = fixture.State(child);
        Assert.False(state.Dirty);

        RunWorker(() => Parallel.For(0, 1000, _ => child.Invalidate()));

        Assert.False(state.Dirty);
        Assert.Equal(1, fixture.Notifications);
        fixture.Publish();
        Assert.True(state.Dirty);
        Assert.Equal(0, fixture.Complete());
        Assert.False(state.Dirty);
        Assert.Equal(2, child.RenderCount);
    }

    [Fact]
    public void InvalidationArrivingDuringRenderIsConsumedByTheNextRender()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        child.DuringRender = () => RunWorker(child.Invalidate);
        child.Invalidate();
        fixture.Render();
        child.DuringRender = null;
        fixture.Render();
        Assert.Equal(3, child.RenderCount);
    }

    [Fact]
    public void AmbientInvalidationIsQueuedAndCoalesced()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        var state = fixture.State(child);
        Assert.False(state.Dirty);
        RunWorker(() =>
        {
            for (var i = 0; i < 100; i++)
                fixture.Session.InvalidateAllViews();
        });
        Assert.False(state.Dirty);
        Assert.Equal(1, fixture.Notifications);
        fixture.Publish();
        Assert.True(state.Dirty);
        Assert.Equal(0, fixture.Complete());
        Assert.False(state.Dirty);
        Assert.Equal(2, child.RenderCount);
    }

    [Fact]
    public void PostedChildInvalidationIsVisibleInTheSameRender()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        fixture.Session.Post(fixture.Child.Invalidate);
        fixture.Render();
        Assert.Equal(2, fixture.Child.RenderCount);
    }

    [Fact]
    public void SelfPostingWorkDoesNotPreventRendering()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var calls = 0;
        Action? callback = null;
        callback = () =>
        {
            calls++;
            fixture.Session.Post(callback!);
        };
        fixture.Session.Post(callback);
        fixture.Render();
        Assert.InRange(calls, 1, 4096);
        Assert.Equal(2, fixture.View.RenderCount);
    }

    [Fact]
    public void FaultInOneWindowDoesNotStopAnotherWindow()
    {
        var application = new GpuiApplication();
        using var first = new SessionFixture(new ProbeView(), application);
        using var second = new SessionFixture(new ProbeView(), application);
        first.Render();
        first.View.OnClick = () => throw new InvalidOperationException("first window");
        Assert.Equal(-111, first.Click());
        second.Render();
        Assert.Equal(0, second.Click());
        Assert.Null(second.Session.Failure);
    }

    [Fact]
    public void ViewPostIsDiscardedAfterItsOwnerRetires()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        var ran = false;
        // Queue during root rendering, after ingress has drained but before slot retirement.
        fixture.View.DuringRender = () => RunWorker(() => child.Runtime.Post(() => ran = true));
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Render();
        fixture.View.DuringRender = null;
        fixture.Render();
        Assert.False(ran);
        Assert.Equal(1, child.UnmountCount);
    }

    [Fact]
    public void MetadataInvalidationCanBeWokenByTheFallbackNotification()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        fixture.Session.PrepareManagedCodeUpdate();
        Assert.Equal(0, fixture.Notifications);
        fixture.Session.InvalidateAllViews();
        Assert.Equal(1, fixture.Notifications);
        fixture.Render();
        Assert.Equal(2, fixture.Child.RenderCount);
    }

    [Fact]
    public void AllWindowsShareTheActualApplicationCallbackThread()
    {
        var application = new GpuiApplication();
        using var first = new SessionFixture(new ProbeView(), application);
        using var second = new SessionFixture(new ProbeView(), application);
        first.Render();
        Exception? exception = null;
        RunWorker(() => exception = Record.Exception(second.Render));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, second.View.RenderCount);
    }

    [Fact]
    public void RenderFailureIsTerminalEvenAfterMetadataUpdate()
    {
        var failure = new InvalidOperationException("render fault");
        using var fixture = new SessionFixture(new ProbeView { DuringRender = () => throw failure });
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Render));
        fixture.View.DuringRender = null;
        var posted = false;
        fixture.Session.Post(() => posted = true);
        fixture.Session.PrepareManagedCodeUpdate();

        Assert.Same(failure, fixture.Session.Failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Render));
        Assert.False(posted);
        Assert.Equal(1, fixture.View.RenderCount);
    }

    [Fact]
    public void NativeEventFailureStopsLaterEventsAndKeepsTheFirstFailure()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var failure = new InvalidOperationException("event fault");
        fixture.View.OnClick = () => throw failure;
        Assert.Equal(-111, fixture.Click());
        fixture.View.OnClick = () => { };
        Assert.NotEqual(0, fixture.Click());
        Assert.Equal(1, fixture.View.ClickCount);
        Assert.Same(failure, fixture.Session.Failure);
    }

    [Fact]
    public void NativeEventCannotReenterUserCodeDuringRendering()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        fixture.View.DuringRender = () => Assert.Equal(-111, fixture.Click());
        Assert.Throws<InvalidOperationException>(fixture.Render);
        Assert.Equal(0, fixture.View.ClickCount);
        Assert.NotNull(fixture.Session.Failure);
    }

    [Fact]
    public void PostedFailureDiscardsRemainingWorkAndCleanupVisitsEveryView()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        var failure = new InvalidOperationException("posted fault");
        fixture.Session.Post(() => throw failure);
        var ran = false;
        fixture.Session.Post(() => ran = true);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Render));
        child.ThrowDuringUnmount = true;
        fixture.Session.Stop();
        Assert.False(ran);
        Assert.Equal(1, child.UnmountCount);
        Assert.Equal(1, fixture.View.UnmountCount);
        Assert.Same(failure, fixture.Session.Failure);
    }

    private static void RunWorker(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(action));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void RenderingRangeBDoesNotRetireCachedRangeA()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        fixture.Range(0);
        var first = fixture.View.RowToken;
        fixture.Range(1);
        var second = fixture.View.RowToken;
        Assert.Equal(0, fixture.Click(first));
        Assert.Equal(1, fixture.View.ClickCount);
        Assert.Equal(0, fixture.Click(second));
        Assert.Equal(1, fixture.View.SecondClickCount);
    }

    [Fact]
    public void EventForARetiredOwnerIsIgnoredWithoutFaultingTheSession()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var token = fixture.Child.ClickToken;
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Render();
        Assert.Equal(0, fixture.Click(token));
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void TwoSourcesSharingARendererKeepIndependentArtifacts()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var first = fixture.Range(0, source: 11);
        var firstToken = fixture.View.RowToken;
        var second = fixture.Range(0, source: 12);
        var secondToken = fixture.View.RowToken;
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(0, fixture.Release(11, first));
        Assert.Equal(0, fixture.Release(11, first));
        Assert.Equal(0, fixture.Click(firstToken));
        Assert.Equal(0, fixture.View.ClickCount);
        Assert.Equal(0, fixture.Click(secondToken));
        Assert.Equal(1, fixture.View.ClickCount);
        Assert.Equal(0, fixture.Release(12, second));
    }

    [Fact]
    public void ReleasingOneRangeLeavesTheOtherRangeAndRootBindingLive()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var first = fixture.Range(0);
        var firstToken = fixture.View.RowToken;
        fixture.Range(1);
        var secondToken = fixture.View.RowToken;
        fixture.Render();
        Assert.Equal(0, fixture.Release(1, first));
        Assert.Equal(0, fixture.Click(firstToken));
        Assert.Equal(0, fixture.View.ClickCount);
        Assert.Equal(0, fixture.Click(secondToken));
        Assert.Equal(1, fixture.View.SecondClickCount);
        Assert.Equal(0, fixture.Click());
        Assert.Equal(1, fixture.View.ClickCount);
    }

    [Fact]
    public void ArtifactReleaseIsAllowedDuringNativeRootReconciliation()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var artifact = fixture.Range(0);
        fixture.Publish();
        Assert.Equal(0, fixture.Release(1, artifact));
        Assert.Equal(0, fixture.Complete());
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void NativeDemandDecodeFailureReleasesAndFaultsTheSession()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var artifact = fixture.Range(0);
        Assert.Equal(-109, fixture.Release(1, artifact, -63));
        Assert.Contains("-63", fixture.Session.Failure!.Message);
        Assert.Equal(0, fixture.Release(1, artifact));
    }

    [Fact]
    public void AnArtifactCannotBeReleasedByAnotherSource()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var artifact = fixture.Range(0, 11);
        Assert.Equal(-109, fixture.Release(12, artifact));
        Assert.Contains("another source", fixture.Session.Failure!.Message);
        Assert.Equal(0, fixture.Release(11, artifact));
    }

    [Fact]
    public void ReleasedArtifactDoesNotRetainItsCapturedObject()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var (reference, artifact) = BindCapturedRow(fixture);
        GC.Collect();
        Assert.True(reference.IsAlive);
        Assert.Equal(0, fixture.Release(1, artifact));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, ulong) BindCapturedRow(SessionFixture fixture)
    {
        var captured = new object();
        fixture.View.RowCapture = captured;
        var artifact = fixture.Range(0);
        fixture.View.RowCapture = null;
        return (new WeakReference(captured), artifact);
    }

    [Fact]
    public void RepeatedRangeEvictionReusesStorageWithoutReusingTokens()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var tokens = new HashSet<ulong>();
        for (var index = 0; index < 100; index++)
        {
            var artifact = fixture.Range(0);
            Assert.True(tokens.Add(fixture.View.RowToken));
            Assert.Equal(0, fixture.Release(1, artifact));
        }
        foreach (var token in tokens)
        {
            Assert.Equal(0, fixture.Click(token));
        }
        Assert.Equal(0, fixture.View.ClickCount);
        Assert.Equal(2, fixture.View.Runtime.Events.EntryCount);
    }

    private sealed class SessionFixture : IDisposable
    {
        private static long _nextId = 1_000_000;
        private static readonly ConcurrentDictionary<ulong, int> NotificationCounts = new();
        private static readonly ConcurrentDictionary<ulong, List<NativeArtifactKey[]>> ArtifactCalls = new();
        private static readonly ConcurrentDictionary<ulong, int> NotifyStatuses = new();
        private static readonly ConcurrentDictionary<ulong, int> ArtifactStatuses = new();
        private static readonly ConcurrentDictionary<ulong, int> ResourceStatuses = new();
        private readonly GpuiDotnetApiV3* _api;
        private readonly ulong _id;
        internal ProbeView View { get; }
        internal ManagedSession Session { get; }
        internal int Notifications => NotificationCounts[_id];
        internal List<NativeArtifactKey[]> ArtifactBatches => ArtifactCalls[_id];
        internal int NotifyStatus { set => NotifyStatuses[_id] = value; }
        internal int ArtifactStatus { set => ArtifactStatuses[_id] = value; }
        internal int ResourceStatus { set => ResourceStatuses[_id] = value; }
        internal ChildView Child => (ChildView)State(View).Children!.Values.Single().View;
        internal ChildView CandidateChild => (ChildView)State(View).StagedChildren!.Values.Single().View;

        internal SessionFixture(ProbeView? view, GpuiApplication? application = null,
            RootViewDeclaration? declaration = null, GpuiWindow? window = null)
        {
            View = view!;
            _id = checked((ulong)Interlocked.Increment(ref _nextId));
            NotificationCounts[_id] = 0;
            ArtifactCalls[_id] = [];
            _api = (GpuiDotnetApiV3*)NativeMemory.AllocZeroed((nuint)sizeof(GpuiDotnetApiV3));
            _api->notify_view = &Notify;
            _api->invalidate_artifacts = &InvalidateArtifacts;
            _api->dispatch_command = &DispatchResource;
            var constructor = typeof(NativeRuntime).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var runtime = (NativeRuntime)constructor.Invoke([Pointer.Box(_api, typeof(GpuiDotnetApiV3*)), null]);
            Session = declaration is null
                ? new ManagedSession(runtime, application ?? new GpuiApplication(), _id, view!)
                : new ManagedSession(runtime, application!, _id, declaration, window!);
            Assert.True(NativeRegistry.Sessions.TryAdd(_id, Session));
        }

        internal void Render()
        {
            Publish();
            Session.CompleteRender(Session.PendingRenderRevision, 0);
        }

        internal void Publish()
        {
            RenderArena arena = default;
            Session.RenderRootOutput(&arena);
        }

        internal int NativePublish(out ulong revision)
        {
            delegate* unmanaged[Cdecl]<ulong, RenderArena*, uint*, ulong*, int> callback = &NativeCallbacks.Render;
            RenderArena arena = default;
            uint root = 0;
            ulong value = 0;
            var status = callback(_id, &arena, &root, &value);
            revision = value;
            return status;
        }

        internal void RenderFromNative()
        {
            Assert.Equal(0, NativePublish(out var revision));
            Assert.Equal(0, Complete(revision));
        }

        internal int Complete(ulong? revision = null, int status = 0)
        {
            delegate* unmanaged[Cdecl]<ulong, ulong, int, int> callback = &NativeCallbacks.RenderCompleted;
            return callback(_id, revision ?? Session.PendingRenderRevision, status);
        }

        internal ulong Range(uint start, ulong source = 1, bool accept = true, uint count = 1, ProbeView? owner = null)
        {
            Assert.Equal(0, NativeRange(start, out var artifact, source, count, owner));
            if (accept)
                Assert.Equal(0, Accept(source, artifact));
            return artifact;
        }

        internal int NativeRange(uint start, out ulong artifact, ulong source = 1, uint count = 1, ProbeView? owner = null)
        {
            RenderArena arena = default;
            uint root = 0;
            ulong value = 0;
            delegate* unmanaged[Cdecl]<ulong, ulong, ulong, uint, uint, RenderArena*, uint*, ulong*, int> callback = &NativeCallbacks.ListRenderRange;
            var status = callback(_id, ((ulong)(owner ?? View).Runtime.RuntimeViewHandle << 32) | 1, source, start, count, &arena, &root, &value);
            artifact = value;
            return status;
        }

        internal int Accept(ulong source, ulong artifact)
        {
            delegate* unmanaged[Cdecl]<ulong, ulong, ulong, int> callback = &NativeCallbacks.AcceptArtifact;
            return callback(_id, source, artifact);
        }

        internal int Release(ulong source, ulong artifact, int status = 0)
        {
            delegate* unmanaged[Cdecl]<ulong, ulong, ulong, int, int> callback = &NativeCallbacks.ReleaseArtifact;
            return callback(_id, source, artifact, status);
        }

        internal int Click(ulong? token = null)
        {
            delegate* unmanaged[Cdecl]<ulong, ulong, ulong, NativeClickEvent*, int> callback = &NativeCallbacks.Click;
            NativeClickEvent click = default;
            return callback(_id, token ?? View.ClickToken, 0, &click);
        }

        internal RetainedViewState State(ViewBase view)
        {
            var field = typeof(ManagedSession).GetField("_renderStates", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return ((Dictionary<ViewBase, RetainedViewState>)field.GetValue(Session)!)[view];
        }

        public void Dispose()
        {
            Session.Stop();
            NativeRegistry.Sessions.TryRemove(_id, out _);
            NotificationCounts.TryRemove(_id, out _);
            ArtifactCalls.TryRemove(_id, out _);
            NotifyStatuses.TryRemove(_id, out _);
            ArtifactStatuses.TryRemove(_id, out _);
            ResourceStatuses.TryRemove(_id, out _);
            NativeMemory.Free(_api);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int InvalidateArtifacts(ulong id, NativeArtifactKey* keys, int count)
        {
            ArtifactCalls[id].Add(new ReadOnlySpan<NativeArtifactKey>(keys, count).ToArray());
            return ArtifactStatuses.GetValueOrDefault(id);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int DispatchResource(ulong id, NativeResourceCommand* command) =>
            ResourceStatuses.GetValueOrDefault(id);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int Notify(ulong id)
        {
            NotificationCounts.AddOrUpdate(id, 1, static (_, count) => count + 1);
            return NotifyStatuses.GetValueOrDefault(id);
        }
    }

    private class ProbeView : View
    {
        public ProbeView() : this(TestViews.Construction()) { }
        private readonly Effect<NoProps> _activation;
        public ProbeView(ViewConstruction construction) : base(construction)
        {
            _activation = construction.Effect<NoProps>(Activate);
        }

        internal int RenderCount;
        internal int MountCount;
        internal int ClickCount;
        internal int SecondClickCount;
        internal ulong RowToken;
        internal object? RowCapture;
        internal bool RowsWithoutEvents;
        internal int UnmountCount;
        internal ulong ClickToken;
        internal Action? DuringRender;
        internal Action<int>? DuringRow;
        internal Action? DuringMount;
        internal Action? OnClick;
        internal bool ThrowDuringUnmount;
        internal CancellationToken CapturedLifetime => Lifetime;

        private void Activate(EffectScope scope, NoProps input)
        {
            scope.Own(new TestCleanup(Retire));
            MountCount++;
            DuringMount?.Invoke();
        }

        protected override Element Render(ref RenderContext ui)
        {
            ui.Effect(_activation, default);
            RenderCount++;
            ClickToken = Runtime.Events.BindClick<ProbeView>(static (view, _) =>
            {
                view.ClickCount++;
                view.OnClick?.Invoke();
            });
            DuringRender?.Invoke();
            return ui.Text("probe");
        }

        private void Retire()
        {
            UnmountCount++;
            if (ThrowDuringUnmount)
                throw new InvalidOperationException("cleanup fault");
        }

        protected override Element RenderListItem(uint rendererId, int index, ref RenderContext ui)
        {
            DuringRow?.Invoke(index);
            if (RowsWithoutEvents) return ui.Text("row");
            Action<ProbeView, ClickEvent> callback = index == 0
                ? static (view, _) => view.ClickCount++
                : static (view, _) => view.SecondClickCount++;
            if (RowCapture is { } captured)
            {
                callback = (view, _) => { GC.KeepAlive(captured); view.ClickCount++; };
            }
            RowToken = Runtime.Events.BindClick(callback);
            return ui.Button("row", "row").OnClick(this, callback);
        }
    }

    private sealed class ChildView : ProbeView, IGeneratedViewFactory<ChildView>
    {
        public static ViewSpec<ChildView> Spec() => default;

        public ChildView() : this(TestViews.Construction()) { }
        public ChildView(ViewConstruction construction) : base(construction) { }

        public static ChildView CreateGpuiView(ViewConstruction construction) => new(construction);
    }

    private sealed class ParentView : ProbeView
    {
        internal bool ShowChild = true;
        internal Action? AfterChildren;

        protected override Element Render(ref RenderContext ui)
        {
            var root = base.Render(ref ui);
            var result = ShowChild ? ui.Div(root, ui.Child("child", ChildView.Spec())) : root;
            AfterChildren?.Invoke();
            return result;
        }
    }

    private sealed class BranchView : ProbeView, IGeneratedViewFactory<BranchView>
    {
        public static ViewSpec<BranchView> Spec() => default;

        public BranchView() : this(TestViews.Construction()) { }
        public BranchView(ViewConstruction construction) : base(construction) { }

        public static BranchView CreateGpuiView(ViewConstruction construction) => new(construction);
        protected override Element Render(ref RenderContext ui) =>
            ui.Div(base.Render(ref ui), ui.Child("first", ChildView.Spec()), ui.Child("second", ChildView.Spec()));
    }

    private sealed class TreeView : ProbeView
    {
        protected override Element Render(ref RenderContext ui) =>
            ui.Div(base.Render(ref ui), ui.Child("branch", BranchView.Spec()), ui.Child("unaffected", ChildView.Spec()));
    }

    private readonly record struct SharedSignalReaderProps(IReadOnlySignal<int> Count, bool CanPause);

    private sealed class SharedSignalReaderView : View<SharedSignalReaderProps>, IGeneratedViewFactory<SharedSignalReaderView, SharedSignalReaderProps>
    {
        public static ViewSpec<SharedSignalReaderView, SharedSignalReaderProps> Spec(SharedSignalReaderProps props) => new(props);

        public SharedSignalReaderView() : this(TestViews.Construction()) { }
        public SharedSignalReaderView(ViewConstruction construction) : base(construction) { }

        public static SharedSignalReaderView CreateGpuiView(ViewConstruction construction, SharedSignalReaderProps initialProps) => new(construction);
        private readonly Signal<bool> _following = new(true);
        internal bool CanPause => CommittedProps.CanPause;
        internal int RenderCount;
        internal int? ObservedValue;
        internal ulong ToggleToken;

        protected override Element Render(in SharedSignalReaderProps props, ref RenderContext ui)
        {
            RenderCount++;
            ObservedValue = !props.CanPause || _following.Value ? props.Count.Value : null;
            ToggleToken = Runtime.Events.BindClick<SharedSignalReaderView>(static (view, _) =>
            {
                view._following.Value = !view._following.Value;
            });
            return ui.Text(ObservedValue?.ToString() ?? "Paused");
        }
    }

    private sealed class SignalSiblingParentView : ProbeView
    {
        internal readonly Signal<int> Count = new(0);

        protected override Element Render(ref RenderContext ui) =>
            ui.Div(
                base.Render(ref ui),
                ui.Child("live", SharedSignalReaderView.Spec(new(Count, false))),
                ui.Child("pausable", SharedSignalReaderView.Spec(new(Count, true)))
            );
    }

    private sealed record LabelProps(string Text);

    private sealed class PropsChildView : View<LabelProps>, IGeneratedViewFactory<PropsChildView, LabelProps>
    {
        public static ViewSpec<PropsChildView, LabelProps> Spec(LabelProps props) => new(props);

        public PropsChildView() : this(TestViews.Construction()) { }
        public PropsChildView(ViewConstruction construction) : base(construction) { }

        public static PropsChildView CreateGpuiView(ViewConstruction construction, LabelProps initialProps) => new(construction);
        internal LabelProps CurrentProps => CommittedProps;
        internal int RenderCount;
        protected override Element Render(in LabelProps props, ref RenderContext ui)
        {
            RenderCount++;
            return ui.Text(props.Text);
        }
    }

    private sealed class PropsParentView : ProbeView
    {
        internal string Label = "accepted";
        protected override Element Render(ref RenderContext ui) =>
            ui.Child("props-child", PropsChildView.Spec(new LabelProps(Label)));
    }
}
