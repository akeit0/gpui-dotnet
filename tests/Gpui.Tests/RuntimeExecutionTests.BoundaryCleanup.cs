using System.Runtime.CompilerServices;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void RemovedChildIsCollectibleBeforeAnotherRootAcceptance()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var child = RemoveChildForCollection(fixture);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(child.IsAlive);
        GC.KeepAlive(fixture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RemoveChildForCollection(SessionFixture fixture)
    {
        var reference = new WeakReference(fixture.Child);
        ((ParentView)fixture.View).ShowChild = false;
        fixture.Render();
        return reference;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneSignalFailureFlushesRowChangesMadeDuringRetirement(bool failDuringFlush)
    {
        var application = new GpuiApplication();
        var trigger = new Signal<int>(0);
        var cleanupSignal = new Signal<int>(0);
        using var healthyRows = new SessionFixture(
            new ProbeView
            {
                DuringRow = _ =>
                {
                    _ = cleanupSignal.Value;
                },
            },
            application
        );
        using var failing = new SessionFixture(
            new ProbeView
            {
                DuringRender = failDuringFlush
                    ? null
                    : () =>
                    {
                        _ = trigger.Value;
                    },
                DuringRow = _ =>
                {
                    _ = trigger.Value;
                },
            },
            application
        );
        healthyRows.Render();
        var artifact = healthyRows.Range(0);
        failing.Render();
        if (failDuringFlush)
        {
            failing.Range(0);
            failing.ArtifactStatus = -33;
        }
        else
            failing.NotifyStatus = -32;
        using var registration = failing.View.CapturedLifetime.Register(() => cleanupSignal.Set(1));

        // Standalone writes must finish cleanup-generated work without a second callback scope.
        var error = Assert.Throws<InvalidOperationException>(() => trigger.Set(1));

        Assert.Same(failing.Session.Failure, error);
        Assert.True(failing.View.Runtime.IsUnmounted);
        Assert.Equal(1, cleanupSignal.Value);
        Assert.Null(healthyRows.Session.Failure);
        Assert.False(healthyRows.State(healthyRows.View).Dirty);
        Assert.Equal(artifact, Assert.Single(Assert.Single(healthyRows.ArtifactBatches)).artifact);
        Assert.Null(ApplicationExecution.Current);
    }

    [Fact]
    public void CleanupGeneratedFlushFailureRetiresItsOwnerAndDrainsFurtherRows()
    {
        var application = new GpuiApplication();
        var trigger = new Signal<int>(0);
        var intermediate = new Signal<int>(0);
        var final = new Signal<int>(0);
        using var first = new SessionFixture(
            new ProbeView
            {
                DuringRow = _ =>
                {
                    _ = trigger.Value;
                },
            },
            application
        );
        using var second = new SessionFixture(
            new ProbeView
            {
                DuringRow = _ =>
                {
                    _ = intermediate.Value;
                },
            },
            application
        );
        using var healthy = new SessionFixture(
            new ProbeView
            {
                DuringRow = _ =>
                {
                    _ = final.Value;
                },
            },
            application
        );
        first.Render();
        first.Range(0);
        second.Render();
        second.Range(0);
        healthy.Render();
        var artifact = healthy.Range(0);
        first.ArtifactStatus = -32;
        second.ArtifactStatus = -33;
        using var firstCleanup = first.View.CapturedLifetime.Register(() => intermediate.Set(1));
        using var secondCleanup = second.View.CapturedLifetime.Register(() => final.Set(1));

        var error = Assert.Throws<InvalidOperationException>(() => trigger.Set(1));

        Assert.Same(first.Session.Failure, error);
        Assert.Contains("-33", second.Session.Failure!.Message);
        Assert.True(first.View.Runtime.IsUnmounted);
        Assert.True(second.View.Runtime.IsUnmounted);
        Assert.Equal(artifact, Assert.Single(Assert.Single(healthy.ArtifactBatches)).artifact);
        Assert.Null(healthy.Session.Failure);
        Assert.False(healthy.State(healthy.View).Dirty);
        Assert.Null(ApplicationExecution.Current);
        Assert.False(trigger.Set(1));
    }
}
