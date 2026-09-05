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
    public void InvalidateQueuesOneRequestWithoutTouchingRetainedStateOnTheWorker()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = fixture.Child;
        var state = fixture.State(child);
        var version = state.RequiredVersion;

        RunWorker(() => Parallel.For(0, 1000, _ => child.Invalidate()));

        Assert.Equal(version, state.RequiredVersion);
        Assert.Equal(1, fixture.Notifications);
        fixture.Render();
        Assert.Equal(version + 1, state.RequiredVersion);
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
        var version = state.RequiredVersion;
        RunWorker(() =>
        {
            for (var i = 0; i < 100; i++)
                fixture.Session.InvalidateAllViews();
        });
        Assert.Equal(version, state.RequiredVersion);
        Assert.Equal(1, fixture.Notifications);
        fixture.Render();
        Assert.Equal(version + 1, state.RequiredVersion);
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
            RenderArena arena = default;
            Session.RenderRootOutput(&arena);
        }

        internal int Click()
        {
            delegate* unmanaged[Cdecl]<ulong, ulong, ulong, NativeClickEvent*, int> callback = &NativeCallbacks.Click;
            NativeClickEvent click = default;
            return callback(_id, View.ClickToken, 0, &click);
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
        internal int ClickCount;
        internal int UnmountCount;
        internal ulong ClickToken;
        internal Action? DuringRender;
        internal Action? OnClick;
        internal bool ThrowDuringUnmount;

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
    }

    private sealed class ChildView : ProbeView, IGeneratedViewFactory<ChildView>
    {
        public static ChildView CreateGpuiView() => new();
    }

    private sealed class ParentView : ProbeView
    {
        internal bool ShowChild = true;

        protected override Element Render(ref RenderContext ui)
        {
            var root = base.Render(ref ui);
            return ShowChild ? ui.Div(root, ui.Child<ChildView>("child")) : root;
        }
    }
}
