using Gpui;

namespace Gpui.Tests;

public sealed class ViewLifetimeTests
{
    [Fact]
    public void LifetimeIsStableAndUnmountIsTerminal()
    {
        var view = new LifecycleView();
        var lifetime = view.ViewLifetime;

        Assert.True(lifetime.CanBeCanceled);
        Assert.False(lifetime.IsCancellationRequested);
        Assert.Equal(0u, view.RuntimeViewHandle);

        Attach(view);

        Assert.True(view.Mounted);
        Assert.False(view.Unmounted);
        Assert.Equal(1u, view.RuntimeViewHandle);
        Assert.Equal(lifetime, view.ViewLifetime);
        Assert.Equal(1, view.MountCount);

        view.AllocateEventStorage();

        view.UnmountRuntime();

        Assert.False(view.Mounted);
        Assert.True(view.Unmounted);
        Assert.Equal(0u, view.RuntimeViewHandle);
        Assert.True(lifetime.IsCancellationRequested);
        Assert.True(view.LifetimeWasCancelledDuringUnmount);
        Assert.False(view.RuntimeWasAvailableDuringUnmount);
        Assert.True(view.InvalidateWasRejectedDuringUnmount);
        Assert.Equal(1, view.UnmountCount);

        view.UnmountRuntime();
        Assert.Equal(1, view.UnmountCount);
        Assert.Throws<ObjectDisposedException>(() => Attach(view, 2));
    }

    [Fact]
    public void FailedMountStillRunsTerminalCleanupExactlyOnce()
    {
        var view = new LifecycleView { ThrowDuringMount = true };

        Assert.Throws<InvalidOperationException>(() => Attach(view));

        Assert.Equal(1, view.MountCount);
        Assert.Equal(1, view.UnmountCount);
        Assert.True(view.Unmounted);
        Assert.Equal(0u, view.RuntimeViewHandle);
        Assert.True(view.ViewLifetime.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => Attach(view, 2));
    }

    [Fact]
    public void NeverMountedViewCanBeRetiredWithoutLifecycleCallbacks()
    {
        var view = new LifecycleView();

        view.UnmountRuntime();
        var lifetime = view.ViewLifetime;

        Assert.Equal(0, view.MountCount);
        Assert.Equal(0, view.UnmountCount);
        Assert.True(view.Unmounted);
        Assert.True(lifetime.IsCancellationRequested);
        Assert.Equal(lifetime, view.ViewLifetime);
        Assert.Throws<ObjectDisposedException>(() => Attach(view));
    }

    [Fact]
    public void UnmountFailureStillLeavesViewTerminal()
    {
        var view = new LifecycleView { ThrowDuringUnmount = true };
        Attach(view);

        Assert.Throws<InvalidOperationException>(view.UnmountRuntime);

        Assert.True(view.Unmounted);
        Assert.True(view.ViewLifetime.IsCancellationRequested);
        Assert.Equal(1, view.UnmountCount);
        view.UnmountRuntime();
        Assert.Throws<ObjectDisposedException>(() => Attach(view, 2));
    }

    [Fact]
    public void PropsRemainAvailableForUnmountCleanupAndAreThenReleased()
    {
        var view = new PropsLifecycleView();
        view.Stage(new PropsPayload("account"));
        Attach(view);
        view.Commit();

        view.UnmountRuntime();

        Assert.Equal("account", view.UnmountedValue);
        Assert.Throws<InvalidOperationException>(view.ReadValue);
    }

    [Fact]
    public void UncommittedCandidatePropsRemainAvailableForUnmountCleanup()
    {
        var view = new PropsLifecycleView();
        view.Stage(new PropsPayload("candidate"));
        Attach(view);
        view.RollBack();

        view.UnmountRuntime();

        Assert.Equal("candidate", view.UnmountedValue);
        Assert.Throws<InvalidOperationException>(view.ReadValue);
    }

    [Fact]
    public void CrossThreadCommandsUseTheStableRouteButUiStateRemainsConfined()
    {
        var view = new LifecycleView();
        var invalidations = 0;
        var posted = 0;
        uint commandOwner = 0;
        Attach(
            view,
            17,
            post: callback =>
            {
                Interlocked.Increment(ref posted);
                callback();
            },
            invalidate: _ => Interlocked.Increment(ref invalidations),
            resourceCommand: (owner, _) => Volatile.Write(ref commandOwner, owner)
        );

        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                view.RequestInvalidate();
                view.RequestPost(static () => { });
                view.RequestResourceCommand();
                _ = view.RuntimeViewHandle;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        worker.Start();
        worker.Join();

        Assert.Equal(1, Volatile.Read(ref invalidations));
        Assert.Equal(1, Volatile.Read(ref posted));
        Assert.Equal(17u, Volatile.Read(ref commandOwner));
        Assert.IsType<InvalidOperationException>(failure);

        view.UnmountRuntime();
    }

    [Fact]
    public async Task ReusedUiAttachmentDoesNotRetainEventEntries()
    {
        var first = new LifecycleView();
        Attach(first, 1);
        var staleEventId = first.AllocateEventStorage();
        Assert.Equal(1ul, first.AllocateResourceKey());
        Assert.Equal(2ul, first.AllocateResourceKey());
        first.UnmountRuntime();

        var second = new LifecycleView();
        Attach(second, 2);
        try
        {
            Assert.Equal(1ul, second.AllocateResourceKey());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                second.DispatchClickCore(staleEventId, default).AsTask()
            );
        }
        finally
        {
            second.UnmountRuntime();
        }
    }

    private static void Attach(
        ViewBase view,
        uint handle = 1,
        Action<Action>? post = null,
        Action<ViewBase>? invalidate = null,
        Action<uint, ResourceCommand>? resourceCommand = null
    ) =>
        view.AttachRuntime(
            handle,
            post ?? (static callback => callback()),
            invalidate ?? (static _ => { }),
            resourceCommand ?? (static (_, _) => { }),
            static (_, _, _) => { }
        );

    private sealed class LifecycleView : View
    {
        internal bool ThrowDuringMount { get; init; }
        internal bool ThrowDuringUnmount { get; init; }
        internal int MountCount { get; private set; }
        internal int UnmountCount { get; private set; }
        internal bool LifetimeWasCancelledDuringUnmount { get; private set; }
        internal bool RuntimeWasAvailableDuringUnmount { get; private set; }
        internal bool InvalidateWasRejectedDuringUnmount { get; private set; }
        internal bool Mounted => IsMounted;
        internal bool Unmounted => IsUnmounted;
        internal CancellationToken ViewLifetime => Lifetime;

        internal uint AllocateEventStorage() =>
            (uint)BindClick<LifecycleView>(static (_, _) => { });

        internal ulong AllocateResourceKey() => NextResourceKeyId();

        internal void RequestInvalidate() => Invalidate();

        internal void RequestPost(Action callback) => Dispatcher.Post(callback);

        internal void RequestResourceCommand() =>
            DispatchResourceCommand(
                new ResourceCommand(
                    ResourceKind.Scroll,
                    ResourceCommandKind.ScrollToTop,
                    "content",
                    0,
                    0
                )
            );

        protected override void OnMounted(ref ViewContext context)
        {
            MountCount++;
            if (ThrowDuringMount)
            {
                throw new InvalidOperationException("mount failed");
            }
        }

        protected override void OnUnmounted()
        {
            UnmountCount++;
            LifetimeWasCancelledDuringUnmount = Lifetime.IsCancellationRequested;
            RuntimeWasAvailableDuringUnmount = IsMounted;
            try
            {
                Invalidate();
            }
            catch (InvalidOperationException)
            {
                InvalidateWasRejectedDuringUnmount = true;
            }
            if (ThrowDuringUnmount)
            {
                throw new InvalidOperationException("unmount failed");
            }
        }

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }

    private sealed record PropsPayload(string Value);

    private sealed class PropsLifecycleView : View<PropsPayload>
    {
        internal string? UnmountedValue { get; private set; }

        internal void Stage(PropsPayload props) => StageProps(in props);

        internal void Commit()
        {
            ValidateRenderInputs();
            CommitStagedProps();
        }

        internal void RollBack() => RollBackStagedProps();

        internal string ReadValue() => Props.Value;

        protected override void OnUnmounted() => UnmountedValue = Props.Value;

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }
}
