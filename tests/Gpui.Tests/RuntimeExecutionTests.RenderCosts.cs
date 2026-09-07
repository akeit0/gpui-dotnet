using System.Diagnostics;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(false, 128)]
    [InlineData(false, 1024)]
    [InlineData(true, 128)]
    [InlineData(true, 512)]
    public void SemanticValidationCost(bool dock, int count)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender(fixture.Session, fixture.View);
        Element root;
        if (dock)
        {
            var panels = new Element[count];
            for (var index = 0; index < panels.Length; index++)
                panels[index] = ui.DockPanel($"panel-{index}", "Panel", ui.Text("content"));
            root = ui.DockArea("dock", ui.DockTabs(0, panels));
        }
        else
        {
            root = ui.Text("leaf");
            for (var index = 0; index < count; index++)
                root = ui.Dynamic(false, root);
        }
        MeasureRenderCost($"validate-{(dock ? "dock" : "wrappers")}-{count}", 16, () => arena.Validate(root));
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(8, false)]
    [InlineData(64, false)]
    [InlineData(8, true)]
    [InlineData(64, true)]
    public void DeepRetainedTreeCost(int depth, bool dirtyLeaf)
    {
        var input = new DeepRenderInput(depth, new Signal<int>(0));
        using var fixture = new SessionFixture(new DeclarationRoot((ref RenderContext ui) =>
            ui.Child("tree", new ViewSpec<DeepRenderView, DeepRenderInput>(input))));
        fixture.Render();
        MeasureRenderCost($"tree-depth-{depth}-dirty-{dirtyLeaf}", 32, () =>
        {
            if (dirtyLeaf) input.Value.Value++;
            fixture.Render();
        });
        Assert.Null(fixture.Session.Failure);
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(512)]
    public void RootRenderWithCachedRowsCost(int batches)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var artifacts = new ulong[batches];
        for (var index = 0; index < batches; index++)
            artifacts[index] = fixture.Range((uint)index * 48, count: 48);
        MeasureRenderCost($"root-with-{batches}-cached-batches", 128, fixture.Render);
        var token = fixture.View.ClickToken;
        for (var index = 0; index < artifacts.Length; index++)
            Assert.Equal(0, fixture.Release(1, artifacts[index]));
        Assert.Equal(0, fixture.Click(token));
        Assert.Equal(1, fixture.View.ClickCount);
    }

    private static void MeasureRenderCost(string label, int iterations, Action operation)
    {
        var samples = new double[5];
        var allocations = new long[5];
        for (var batch = 0; batch < 9; batch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var iteration = 0; iteration < iterations; iteration++) operation();
            var elapsed = Stopwatch.GetTimestamp() - start;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (batch >= 4)
            {
                samples[batch - 4] = elapsed * (1e9 / Stopwatch.Frequency) / iterations;
                allocations[batch - 4] = allocated / iterations;
            }
        }
        Array.Sort(samples);
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{label}: median={samples[2]:F1} ns/op; allocated={string.Join(",", allocations)} B/op");
    }

    private readonly record struct DeepRenderInput(int Depth, Signal<int> Value);

    private sealed class DeepRenderView(ViewConstruction construction)
        : View<DeepRenderInput>(construction), IGeneratedViewFactory<DeepRenderView, DeepRenderInput>
    {
        public static DeepRenderView CreateGpuiView(ViewConstruction construction, DeepRenderInput props) => new(construction);
        protected override Element Render(in DeepRenderInput props, ref RenderContext ui)
        {
            if (props.Depth != 0)
                return ui.VStack(ui.Child("next", new ViewSpec<DeepRenderView, DeepRenderInput>(props with { Depth = props.Depth - 1 })));
            _ = props.Value.Value;
            return ui.Text("leaf");
        }
    }
}
