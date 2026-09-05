using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gpui.Interop;
using Gpui.Interop.Internal;
using Gpui.Interop.Internal.Session;

namespace Gpui.Tests;

public sealed unsafe class RuntimeExecutionTests
{
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
    public void NativeRejectionDoesNotCleanTheRenderedChild()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        child.Invalidate();
        fixture.Publish();
        Assert.Equal(-103, fixture.Complete(status: -40));
        Assert.True(fixture.State(child).Dirty);
    }

    [Fact]
    public void RootFailureAfterRenderingAChildDoesNotCleanThatChild()
    {
        var parent = new ParentView();
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var child = fixture.Child;
        child.Invalidate();
        parent.AfterChildren = () => throw new InvalidOperationException("parent render failed");
        Assert.Throws<InvalidOperationException>(fixture.Publish);
        Assert.Equal(2, child.RenderCount);
        Assert.True(fixture.State(child).Dirty);
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
            Assert.False(child.IsMountedCore);
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
        Assert.True(child.IsUnmountedCore);
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
        Assert.True(child.IsUnmountedCore);
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
        fixture.View.DuringRender = () => child.Post(() => ran = true);
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
        var attachment = typeof(ViewBase).GetField("_uiAttachment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.View)!;
        var entries = (System.Collections.ICollection)attachment.GetType().GetProperty("EventEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attachment)!;
        Assert.Equal(2, entries.Count);
    }

    private sealed class SessionFixture : IDisposable
    {
        private static long _nextId = 1_000_000;
        private static readonly ConcurrentDictionary<ulong, int> NotificationCounts = new();
        private readonly GpuiDotnetApiV3* _api;
        private readonly ulong _id;
        internal ProbeView View { get; }
        internal ManagedSession Session { get; }
        internal int Notifications => NotificationCounts[_id];
        internal ChildView Child => (ChildView)State(View).Children!.Values.Single().View;
        internal ChildView CandidateChild => (ChildView)State(View).StagedChildren!.Values.Single().View;

        internal SessionFixture(ProbeView view, GpuiApplication? application = null)
        {
            View = view;
            _id = checked((ulong)Interlocked.Increment(ref _nextId));
            NotificationCounts[_id] = 0;
            _api = (GpuiDotnetApiV3*)NativeMemory.AllocZeroed((nuint)sizeof(GpuiDotnetApiV3));
            _api->notify_view = &Notify;
            var constructor = typeof(NativeRuntime).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var runtime = (NativeRuntime)constructor.Invoke([Pointer.Box(_api, typeof(GpuiDotnetApiV3*)), null]);
            Session = new ManagedSession(runtime, application ?? new GpuiApplication(), _id, view);
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

        internal int Complete(ulong? revision = null, int status = 0)
        {
            delegate* unmanaged[Cdecl]<ulong, ulong, int, int> callback = &NativeCallbacks.RenderCompleted;
            return callback(_id, revision ?? Session.PendingRenderRevision, status);
        }

        internal ulong Range(uint start, ulong source = 1)
        {
            RenderArena arena = default;
            uint root = 0;
            ulong artifact = 0;
            delegate* unmanaged[Cdecl]<ulong, ulong, ulong, uint, uint, RenderArena*, uint*, ulong*, int> callback = &NativeCallbacks.ListRenderRange;
            Assert.Equal(0, callback(_id, ((ulong)View.RuntimeViewHandle << 32) | 1, source, start, 1, &arena, &root, &artifact));
            return artifact;
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
            NativeMemory.Free(_api);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int Notify(ulong id)
        {
            NotificationCounts.AddOrUpdate(id, 1, static (_, count) => count + 1);
            return 0;
        }
    }

    private class ProbeView : View
    {
        internal int RenderCount;
        internal int MountCount;
        internal int ClickCount;
        internal int SecondClickCount;
        internal ulong RowToken;
        internal object? RowCapture;
        internal int UnmountCount;
        internal ulong ClickToken;
        internal Action? DuringRender;
        internal Action? DuringMount;
        internal Action? OnClick;
        internal bool ThrowDuringUnmount;
        internal CancellationToken CapturedLifetime => Lifetime;

        protected override void OnMounted(ref ViewContext context)
        {
            MountCount++;
            DuringMount?.Invoke();
        }

        protected override Element Render(ref RenderContext ui)
        {
            RenderCount++;
            ClickToken = BindClick<ProbeView>(static (view, _) =>
            {
                view.ClickCount++;
                view.OnClick?.Invoke();
            });
            DuringRender?.Invoke();
            return ui.Text("probe");
        }

        protected override void OnUnmounted()
        {
            UnmountCount++;
            if (ThrowDuringUnmount)
                throw new InvalidOperationException("cleanup fault");
        }

        protected override Element RenderListItem(uint rendererId, int index, ref RenderContext ui)
        {
            Action<ProbeView, ClickEvent> callback = index == 0
                ? static (view, _) => view.ClickCount++
                : static (view, _) => view.SecondClickCount++;
            if (RowCapture is { } captured)
            {
                callback = (view, _) => { GC.KeepAlive(captured); view.ClickCount++; };
            }
            RowToken = BindClick(callback);
            return ui.Button("row", "row").OnClick(this, callback);
        }
    }

    private sealed class ChildView : ProbeView, IGeneratedViewFactory<ChildView>
    {
        public static ChildView CreateGpuiView() => new();
    }

    private sealed class ParentView : ProbeView
    {
        internal bool ShowChild = true;
        internal Action? AfterChildren;

        protected override Element Render(ref RenderContext ui)
        {
            var root = base.Render(ref ui);
            var result = ShowChild ? ui.Div(root, ui.Child<ChildView>("child")) : root;
            AfterChildren?.Invoke();
            return result;
        }
    }

    private sealed class BranchView : ProbeView, IGeneratedViewFactory<BranchView>
    {
        public static BranchView CreateGpuiView() => new();
        protected override Element Render(ref RenderContext ui) =>
            ui.Div(base.Render(ref ui), ui.Child<ChildView>("first"), ui.Child<ChildView>("second"));
    }

    private sealed class TreeView : ProbeView
    {
        protected override Element Render(ref RenderContext ui) =>
            ui.Div(base.Render(ref ui), ui.Child<BranchView>("branch"), ui.Child<ChildView>("unaffected"));
    }

    private sealed record LabelProps(string Text);

    private sealed class PropsChildView : View<LabelProps>, IGeneratedViewFactory<PropsChildView>
    {
        public static PropsChildView CreateGpuiView() => new();
        internal LabelProps CurrentProps => Props;
        internal int RenderCount;
        protected override Element Render(ref RenderContext ui)
        {
            RenderCount++;
            return ui.Text(Props.Text);
        }
    }

    private sealed class PropsParentView : ProbeView
    {
        internal string Label = "accepted";
        protected override Element Render(ref RenderContext ui) =>
            ui.Child<PropsChildView, LabelProps>("props-child", new LabelProps(Label));
    }
}
