using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Fact]
    public void RootConstructionIsDeferredAndReceivesInitialPropsOnTheRenderThread()
    {
        var log = new OwnershipLog();
        var application = new GpuiApplication();
        GpuiWindow window = null!;
        var worker = new Thread(() =>
            window = application.OpenWindow(OwnedPropsView.Spec(new(7, log)))
        );
        worker.Start();
        worker.Join();
        Assert.Equal(0, log.Constructions);
        using var fixture = new SessionFixture(
            null,
            application,
            window.TakeRootDeclaration(),
            window
        );
        fixture.Publish();
        var view = Assert.IsType<OwnedPropsView>(fixture.Session.RootView);
        Assert.Equal(7, view.Draft);
        Assert.Equal(Environment.CurrentManagedThreadId, log.ConstructionThread);
        Assert.Equal(14, view.Result);
        Assert.Empty(log.Starts);
        Assert.Equal(0, fixture.Complete());
        Assert.Equal([7], log.Starts);
    }

    [Fact]
    public void CacheHitsKeepSignalDependenciesAndPropsChangesPreserveLocalState()
    {
        var log = new OwnershipLog();
        var parent = new OwnershipParent(new(3, log));
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var child = OwnedChild(fixture);
        Assert.Equal(6, child.Result);
        parent.Invalidate();
        fixture.Render();
        child.Invalidate();
        fixture.Render();
        Assert.Equal(1, log.Calculations);
        log.Signal.Value = 4;
        fixture.Render();
        Assert.Equal(10, child.Result);
        Assert.Equal(2, log.Calculations);
        parent.Input = new(8, log);
        parent.Invalidate();
        fixture.Render();
        Assert.Same(child, OwnedChild(fixture));
        Assert.Equal(3, child.Draft);
        Assert.Equal(20, child.Result);
        Assert.Equal([3, 8], log.Starts);
        Assert.Equal([3], log.Stops);
    }

    [Fact]
    public void ConstructionCannotChangeItsWindowBeforeAcceptance()
    {
        var log = new OwnershipLog { CloseDuringConstruction = true };
        var application = new GpuiApplication();
        var window = application.OpenWindow(OwnedPropsView.Spec(new(1, log)));
        using var fixture = new SessionFixture(
            null,
            application,
            window.TakeRootDeclaration(),
            window
        );
        Assert.Throws<InvalidOperationException>(fixture.Publish);
        Assert.False(window.IsClosed);
        Assert.Equal(["resource"], log.Cleanup);
    }

    [Fact]
    public void ArtifactReleaseFailureDefersUserCleanupUntilNormalIngress()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var artifact = fixture.Range(0);
        Assert.Equal(-109, fixture.Release(1, artifact, -63));
        Assert.Equal(0, fixture.View.UnmountCount);
        Assert.Throws<InvalidOperationException>(fixture.Publish);
        Assert.Equal(1, fixture.View.UnmountCount);
    }

    [Fact]
    public void ConstructionSamplingDoesNotSubscribeTheParentAndWritesRemainForbidden()
    {
        var log = new OwnershipLog();
        var parent = new OwnershipParent(new(1, log));
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var renders = parent.RenderCount;
        log.ConstructionOnly.Value++;
        Assert.Equal(0, fixture.Notifications);
        Assert.Equal(renders, parent.RenderCount);

        var forbidden = new OwnershipLog { WriteDuringConstruction = true };
        using var failing = new SessionFixture(new OwnershipParent(new(1, forbidden)));
        Assert.Throws<InvalidOperationException>(failing.Render);
        Assert.Equal(0, forbidden.ConstructionOnly.Value);
        Assert.Equal(["resource"], forbidden.Cleanup);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConstructorFailureAndRejectedCandidatesReleaseLocalOwnership(
        bool constructorFailure
    )
    {
        var log = new OwnershipLog { ThrowConstruction = constructorFailure };
        using var fixture = new SessionFixture(new OwnershipParent(new(1, log)));
        if (constructorFailure)
            Assert.Throws<InvalidOperationException>(fixture.Publish);
        else
        {
            fixture.Publish();
            Assert.Empty(log.Cleanup);
            Assert.Equal(-103, fixture.Complete(status: -40));
        }
        Assert.Equal(["resource"], log.Cleanup);
        Assert.Empty(log.Starts);
        Assert.True(log.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public void EffectsReplaceAfterAcceptanceAndRevokeOldCallbacksAndWork()
    {
        var log = new OwnershipLog();
        var parent = new OwnershipParent(new(1, log));
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var oldScope = log.Scopes[0];
        var callback = oldScope.Bind(log, static state => state.Deliveries++);
        var completion = new TaskCompletionSource<int>();
        oldScope.Work.Start(
            log,
            completion.Task,
            static (task, _) => task,
            static (state, _) => state.Deliveries++
        );
        parent.Input = new(2, log);
        parent.Invalidate();
        fixture.Publish();
        Assert.Equal([1], log.Starts);
        callback(); // Queued behind the pending acceptance, then revoked by replacement.
        Assert.Equal(0, fixture.Complete());
        Assert.Equal([1, 2], log.Starts);
        Assert.Equal([1], log.Stops);
        Assert.True(oldScope.Lifetime.IsCancellationRequested);
        completion.SetResult(1);
        callback();
        fixture.Render();
        Assert.Equal(0, log.Deliveries);
        parent.Input = new(2, log, Enabled: false);
        parent.Invalidate();
        fixture.Render();
        Assert.Equal([1, 2], log.Stops);
        Assert.False(OwnedChild(fixture).Runtime.IsUnmounted);
    }

    [Fact]
    public void EffectSetupFailureCleansPartialSetupAndConstructionResources()
    {
        var log = new OwnershipLog { ThrowSetup = true };
        using var fixture = new SessionFixture(new OwnershipParent(new(1, log)));
        fixture.Publish();
        Assert.Equal(-103, fixture.Complete());
        Assert.Equal([1], log.Stops);
        Assert.Equal(["effect", "resource"], log.Cleanup);
        Assert.True(log.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public void HotReloadDropsDerivedValuesAndReplacesEffectsWithoutResettingDraft()
    {
        var log = new OwnershipLog();
        using var fixture = new SessionFixture(new OwnershipParent(new(5, log)));
        fixture.Render();
        var child = OwnedChild(fixture);
        fixture.Session.PrepareManagedCodeUpdate();
        fixture.Render();
        Assert.Equal(2, log.Calculations);
        Assert.Equal([5, 5], log.Starts);
        Assert.Equal([5], log.Stops);
        Assert.Equal(5, child.Draft);
        Assert.Same(child, OwnedChild(fixture));
    }

    [Fact]
    public void MemosRejectHiddenSignalReadsAndDoNotCacheFailedCalculations()
    {
        var log = new OwnershipLog();
        using var fixture = new SessionFixture(new OwnershipParent(new(2, log)));
        fixture.Render();
        var child = OwnedChild(fixture);
        Assert.Throws<InvalidOperationException>(() =>
            child.Memo.Get(new(9, 0, log), static input => input.Log.Signal.Value)
        );
        Assert.Equal(18, child.Memo.Get(new(9, 0, log), static input => input.Value * 2));
        fixture.Session.Stop();
        Assert.Throws<ObjectDisposedException>(() => child.Memo.Get(new(9, 0, log), static _ => 1));
    }

    [Fact]
    public void LatestRequestRejectsAnOlderCompletionEvenWhenCancellationIsIgnored()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var work = fixture.View.Runtime.GetWorkScope();
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        var results = new List<int>();
        work.StartLatest(
            results,
            first.Task,
            static (task, _) => task,
            static (state, value) => state.Add(value)
        );
        work.StartLatest(
            results,
            second.Task,
            static (task, _) => task,
            static (state, value) => state.Add(value)
        );
        second.SetResult(2);
        first.SetResult(1);
        fixture.Render();
        Assert.Equal([2], results);
    }

    private static OwnedPropsView OwnedChild(SessionFixture fixture) =>
        (OwnedPropsView)fixture.State(fixture.View).Children!.Values.Single().View;

    [Fact]
    public void CancellationReentrancyCannotStartAnAlreadySupersededProducer()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var work = fixture.View.Runtime.GetWorkScope();
        var log = new List<int>();
        var never = new TaskCompletionSource<int>();
        work.StartLatest(
            log,
            (work, log, never.Task),
            static (input, token) =>
            {
                token.Register(() =>
                    input.work.StartLatest(
                        input.log,
                        3,
                        static (value, _) => Task.FromResult(value),
                        static (state, value) => state.Add(value)
                    )
                );
                return input.Task;
            },
            static (state, value) => state.Add(value)
        );
        work.StartLatest(
            log,
            log,
            static (state, _) =>
            {
                state.Add(-1); // This producer was superseded by the cancellation callback.
                return Task.FromResult(2);
            },
            static (state, value) => state.Add(value)
        );
        fixture.Render();
        Assert.Equal([3], log);
        never.SetResult(1);
        fixture.Render();
        Assert.Equal([3], log);
    }

    [Fact]
    public void RetainedSubscriptionCallbackDoesNotRetainItsTargetAfterEffectRemoval()
    {
        var log = new OwnershipLog();
        var parent = new OwnershipParent(new(1, log));
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var (target, callback) = CapturedEffectCallback(log.Scopes[0]);
        parent.Input = new(1, log, Enabled: false);
        parent.Invalidate();
        fixture.Render();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(target.IsAlive);
        callback();
        GC.KeepAlive(callback);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static (WeakReference, Action) CapturedEffectCallback(EffectScope scope)
    {
        var target = new object();
        return (new WeakReference(target), scope.Bind(target, static value => GC.KeepAlive(value)));
    }

    [Fact]
    public void WarmMemoHitsAllocateNothing()
    {
        var log = new OwnershipLog();
        using var fixture = new SessionFixture(new OwnershipParent(new(1, log)));
        fixture.Render();
        var memo = OwnedChild(fixture).Memo;
        var input = new CalculationInput(1, 0, log);
        Func<CalculationInput, int> compute = static value => value.Value;
        for (var i = 0; i < 1000; i++)
            memo.Get(input, compute);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            memo.Get(input, compute);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, log.Calculations);
    }

    private sealed class OwnershipLog
    {
        internal int Constructions,
            ConstructionThread,
            Calculations,
            Deliveries;
        internal bool ThrowConstruction,
            ThrowSetup,
            WriteDuringConstruction,
            CloseDuringConstruction;
        internal CancellationToken Lifetime;
        internal readonly Signal<int> Signal = new(0);
        internal readonly Signal<int> ConstructionOnly = new(0);
        internal readonly List<int> Starts = [],
            Stops = [];
        internal readonly List<string> Cleanup = [];
        internal readonly List<EffectScope> Scopes = [];
    }

    private readonly record struct OwnershipInput(int Value, OwnershipLog Log, bool Enabled = true);

    private readonly record struct CalculationInput(int Value, int Signal, OwnershipLog Log);

    private sealed class OwnershipParent(OwnershipInput input) : ProbeView
    {
        internal OwnershipInput Input = input;

        protected override Element Render(ref RenderContext ui) =>
            ui.Div(base.Render(ref ui), ui.Child("owned", OwnedPropsView.Spec(Input)));
    }

    private sealed class OwnedPropsView
        : View<OwnershipInput>,
            IGeneratedViewFactory<OwnedPropsView, OwnershipInput>
    {
        internal readonly int Draft;
        internal readonly Memo<CalculationInput, int> Memo;
        internal int Result;
        private readonly Effect<int> _effect;
        private readonly OwnershipLog _log;

        public OwnedPropsView(ViewConstruction construction, OwnershipInput initialProps)
            : base(construction)
        {
            _log = initialProps.Log;
            _log.Constructions++;
            _log.ConstructionThread = Environment.CurrentManagedThreadId;
            _log.Lifetime = Lifetime;
            construction.Own(new TestCleanup(() => _log.Cleanup.Add("resource")));
            if (_log.CloseDuringConstruction)
                construction.Window.Close();
            _ = _log.ConstructionOnly.Value;
            if (_log.WriteDuringConstruction)
                _log.ConstructionOnly.Value++;
            if (_log.ThrowConstruction)
                throw new InvalidOperationException("construction failed");
            Draft = initialProps.Value;
            Memo = construction.Memo<CalculationInput, int>();
            _effect = construction.Effect<int>(Setup);
        }

        public static OwnedPropsView CreateGpuiView(
            ViewConstruction construction,
            OwnershipInput initialProps
        ) => new(construction, initialProps);

        internal static ViewSpec<OwnedPropsView, OwnershipInput> Spec(OwnershipInput props) =>
            new(props);

        private void Setup(EffectScope scope, int input)
        {
            _log.Starts.Add(input);
            _log.Scopes.Add(scope);
            scope.Own(
                new TestCleanup(() =>
                {
                    _log.Stops.Add(input);
                    _log.Cleanup.Add("effect");
                })
            );
            if (_log.ThrowSetup)
                throw new InvalidOperationException("setup failed");
        }

        protected override Element Render(in OwnershipInput props, ref RenderContext ui)
        {
            Result = Memo.Get(
                new(props.Value, props.Log.Signal.Value, props.Log),
                static input =>
                {
                    input.Log.Calculations++;
                    return input.Value * 2 + input.Signal;
                }
            );
            if (props.Enabled)
                ui.Effect(_effect, props.Value);
            return ui.Text("owned");
        }
    }
}
