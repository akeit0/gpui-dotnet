using Gpui.Interop;
using Gpui.Interop.Internal.Session;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void NonRangeDemandArtifactUsesSharedAcceptanceDependenciesAndEventLifetime()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var signal = new Signal<string>("card");
        var token = ((ulong)fixture.View.Runtime.RuntimeViewHandle << 32) | 1;
        RenderArena output = default;
        var root = fixture.Session.RenderDemandOutput(
            token,
            17,
            new CardRequest(signal),
            &output,
            out var artifact
        );
        Assert.Equal((ushort)ComponentId.Button, output.nodes[root].component);
        Assert.Equal(1, output.child_length); // The Button's text, with no range wrapper.
        var click = fixture.View.RowToken;
        signal.Value = "changed before acceptance";
        Assert.Empty(fixture.ArtifactBatches);
        Assert.Equal(0, fixture.Accept(17, artifact));
        var invalidation = Assert.Single(Assert.Single(fixture.ArtifactBatches));
        Assert.Equal(17UL, invalidation.source);
        Assert.Equal(artifact, invalidation.artifact);

        fixture.Range(0, source: 23);
        fixture.Render();
        Assert.Equal(0, fixture.Click(click));
        Assert.Equal(1, fixture.View.SecondClickCount);
        Assert.Equal(0, fixture.Release(17, artifact));
        Assert.Equal(0, fixture.Click(click));
        Assert.Equal(1, fixture.View.SecondClickCount);
        var batches = fixture.ArtifactBatches.Count;
        signal.Value = "released";
        Assert.Equal(batches, fixture.ArtifactBatches.Count);
    }

    [Fact]
    public void NonRangeDemandRenderingKeepsRenderPurityAndRetiresFailedPublication()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var signal = new Signal<string>("card");
        var token = ((ulong)fixture.View.Runtime.RuntimeViewHandle << 32) | 1;
        Assert.Throws<InvalidOperationException>(() =>
        {
            RenderArena output = default;
            fixture.Session.RenderDemandOutput(
                token,
                17,
                new CardRequest(signal, true),
                &output,
                out _
            );
        });
        Assert.Equal("card", signal.Value);
        Assert.True(fixture.View.Runtime.IsUnmounted);
    }

    private readonly struct CardRequest(Signal<string> text, bool mutate = false)
        : IDemandRenderRequest
    {
        public void Validate() { }

        public Element Render(ViewBase owner, uint rendererId, ref RenderContext ui)
        {
            var view = (ProbeView)owner;
            if (mutate)
                text.Value = "forbidden";
            view.RowToken = view.Runtime.Events.BindClick<ProbeView>(
                static (view, _) => view.SecondClickCount++
            );
            return ui.Button("card", text.Value)
                .OnClick(view, static (view, _) => view.SecondClickCount++);
        }
    }
}
