using System.Diagnostics;

namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Fact]
    public void CleanBranchAcceptsEqualPropsWithoutRedeclaringItsDescendants()
    {
        var props = new LabelProps("same");
        using var fixture = new SessionFixture(
            new DeclarationRoot(
                (ref RenderContext ui) =>
                    ui.Child("branch", new ViewSpec<AcceptancePropsBranch, LabelProps>(props))
            )
        );
        fixture.Render();
        var branch = Assert.IsType<AcceptancePropsBranch>(
            fixture.State(fixture.View).Children!.Values.Single().View
        );
        var leaf = Assert.IsType<PropsChildView>(
            fixture.State(branch).Children!.Values.Single().View
        );
        var initial = props;
        props = new LabelProps("same");
        fixture.Publish();
        Assert.Same(initial, branch.CurrentProps);
        Assert.Equal(0, fixture.Complete());
        Assert.Same(props, branch.CurrentProps);
        Assert.Same(initial, leaf.CurrentProps);
        Assert.Equal(1, branch.RenderCount);
        Assert.Equal(1, leaf.RenderCount);

        leaf.Invalidate();
        fixture.Render();
        Assert.Same(props, leaf.CurrentProps);
        Assert.Equal(2, branch.RenderCount);
        Assert.Equal(2, leaf.RenderCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingBranchRetiresItsWholeSubtreeBeforeActivation(bool cleanupFails)
    {
        var replace = false;
        using var fixture = new SessionFixture(
            new DeclarationRoot(
                (ref RenderContext ui) =>
                    ui.Div(
                        replace
                            ? ui.Child("replace", ChildView.Spec())
                            : ui.Child("replace", BranchView.Spec()),
                        ui.Child("keep", BranchView.Spec())
                    )
            )
        );
        fixture.Render();
        var branches = fixture
            .State(fixture.View)
            .Children!.Values.Select(entry => (BranchView)entry.View)
            .ToArray();
        var removed = branches[0];
        var retained = branches[1];
        var leaves = fixture
            .State(removed)
            .Children!.Values.Select(entry => (ChildView)entry.View)
            .ToArray();
        var retainedLeaves = fixture
            .State(retained)
            .Children!.Values.Select(entry => (ChildView)entry.View)
            .ToArray();
        var order = new List<string>();
        using var branchCancellation = removed.CapturedLifetime.Register(() => order.Add("branch"));
        using var firstCancellation = leaves[0].CapturedLifetime.Register(() => order.Add("leaf"));
        using var secondCancellation = leaves[1].CapturedLifetime.Register(() => order.Add("leaf"));
        leaves[0].ThrowDuringUnmount = cleanupFails;
        leaves[0].Invalidate();
        replace = true;
        fixture.Publish();
        var replacement = fixture
            .State(fixture.View)
            .StagedChildren!.Values.Select(entry => entry.View)
            .OfType<ChildView>()
            .Single();
        replacement.DuringMount = () =>
        {
            Assert.Equal(new[] { "leaf", "leaf", "branch" }, order);
            Assert.True(retained.Runtime.IsMounted);
            Assert.All(retainedLeaves, leaf => Assert.True(leaf.Runtime.IsMounted));
        };
        Assert.Equal(cleanupFails ? -103 : 0, fixture.Complete());
        Assert.Equal(new[] { "leaf", "leaf", "branch" }, order);
        Assert.Equal(1, removed.UnmountCount);
        Assert.All(leaves, leaf => Assert.Equal(1, leaf.UnmountCount));
        Assert.Equal(cleanupFails ? 0 : 1, replacement.MountCount);
        Assert.Equal(cleanupFails, replacement.Runtime.IsUnmounted);
        Assert.Equal(cleanupFails ? 1 : 0, retained.UnmountCount);
        Assert.All(
            retainedLeaves,
            leaf =>
            {
                Assert.Equal(1, leaf.RenderCount);
                Assert.Equal(cleanupFails ? 1 : 0, leaf.UnmountCount);
            }
        );
        if (!cleanupFails)
        {
            fixture.Render();
            Assert.Same(
                retained,
                fixture
                    .State(fixture.View)
                    .Children!.Values.Select(entry => entry.View)
                    .OfType<BranchView>()
                    .Single()
            );
            Assert.Equal(1, retained.RenderCount);
        }
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(1, false)]
    [InlineData(16, false)]
    [InlineData(64, false)]
    [InlineData(16, true)]
    [InlineData(64, true)]
    public void RetainedAcceptanceCost(int branches, bool dirtyLeaf)
    {
        using var fixture = new SessionFixture(
            new DeclarationRoot(
                (ref RenderContext ui) =>
                {
                    var root = ui.Div();
                    for (var index = 0; index < branches; index++)
                        root.Child(ui.Child(new ViewSpec<AcceptanceBranch, int>(64)));
                    return root;
                }
            )
        );
        fixture.Render();
        var branch = fixture.State(fixture.View).Children!.Values.First().View;
        var leaf = (ChildView)fixture.State(branch).Children!.Values.First().View;
        var renders = leaf.RenderCount;
        var publication = new double[5];
        var acceptance = new double[5];
        var allocations = new long[5];
        const int iterations = 16;
        for (var batch = 0; batch < 9; batch++)
        {
            long publishTicks = 0,
                acceptTicks = 0,
                allocated = 0;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                if (dirtyLeaf)
                    leaf.Invalidate();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                fixture.Publish();
                var published = Stopwatch.GetTimestamp();
                fixture.Session.CompleteRender(fixture.Session.PendingRenderRevision, 0);
                acceptTicks += Stopwatch.GetTimestamp() - published;
                publishTicks += published - start;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            if (batch >= 4)
            {
                publication[batch - 4] = publishTicks * (1e6 / Stopwatch.Frequency) / iterations;
                acceptance[batch - 4] = acceptTicks * (1e6 / Stopwatch.Frequency) / iterations;
                allocations[batch - 4] = allocated / iterations;
            }
        }
        Array.Sort(publication);
        Array.Sort(acceptance);
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"accept-{branches}-branches-64-leaves-dirty-{dirtyLeaf}: publish={publication[2]:F2} us; accept={acceptance[2]:F2} us; allocated={string.Join(",", allocations)} B/cycle"
        );
        Assert.Equal(renders + (dirtyLeaf ? 9 * iterations : 0), leaf.RenderCount);
        Assert.Null(fixture.Session.Failure);
    }

    private sealed class AcceptanceBranch(ViewConstruction construction)
        : View<int>(construction),
            IGeneratedViewFactory<AcceptanceBranch, int>
    {
        public static AcceptanceBranch CreateGpuiView(ViewConstruction construction, int props) =>
            new(construction);

        protected override Element Render(in int props, ref RenderContext ui)
        {
            var root = ui.Div();
            for (var index = 0; index < props; index++)
                root.Child(ui.Child(ChildView.Spec()));
            return root;
        }
    }

    private sealed class AcceptancePropsBranch(ViewConstruction construction)
        : View<LabelProps>(construction),
            IGeneratedViewFactory<AcceptancePropsBranch, LabelProps>
    {
        public static AcceptancePropsBranch CreateGpuiView(
            ViewConstruction construction,
            LabelProps props
        ) => new(construction);

        internal LabelProps CurrentProps => CommittedProps;
        internal int RenderCount;

        protected override Element Render(in LabelProps props, ref RenderContext ui)
        {
            RenderCount++;
            return ui.Child("leaf", PropsChildView.Spec(props));
        }
    }
}
