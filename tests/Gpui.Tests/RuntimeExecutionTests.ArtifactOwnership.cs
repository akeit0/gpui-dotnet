namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildRetirementReleasesOnlyItsRemainingArtifactsAfterUnorderedEviction(
        bool withoutEvents
    )
    {
        var signal = new Signal<int>(0);
        var parent = new ParentView { DuringItem = _ => _ = signal.Value };
        using var fixture = new SessionFixture(parent);
        fixture.Render();
        var child = fixture.Child;
        child.DuringItem = _ => _ = signal.Value;
        child.ItemsWithoutEvents = withoutEvents;
        var parentArtifact = fixture.Range(0, source: 10);
        var parentToken = parent.ItemToken;
        var childArtifacts = new ulong[32];
        for (var index = 0; index < childArtifacts.Length; index++)
            childArtifacts[index] = fixture.Range(0, source: 20, owner: child);
        // Permuted removal exercises entries moved into holes, including later removal
        // of the moved entry. Refill a slot before retiring accepted and pending leases.
        for (var index = 0; index < 30; index++)
            Assert.Equal(0, fixture.Release(20, childArtifacts[index * 7 % 32]));
        var replacement = fixture.Range(0, source: 20, owner: child);
        var childToken = child.ItemToken;
        var pending = fixture.Range(0, source: 21, accept: false, owner: child);
        var retained = fixture.State(child);
        Assert.Equal(4, retained.DemandArtifacts!.Count);
        parent.ShowChild = false;
        fixture.Render();

        Assert.Equal(1, child.UnmountCount);
        Assert.Empty(retained.DemandArtifacts);
        parent.OnClick = () => signal.Value++;
        Assert.Equal(0, fixture.Click());
        var invalidation = Assert.Single(Assert.Single(fixture.ArtifactBatches));
        Assert.Equal(parentArtifact, invalidation.artifact);
        Assert.Equal(10UL, invalidation.source);
        if (!withoutEvents)
        {
            Assert.Equal(0, fixture.Click(childToken));
            Assert.Equal(0, child.ClickCount);
        }
        Assert.Equal(0, fixture.Click(parentToken));
        Assert.Equal(2, parent.ClickCount);

        foreach (var artifact in childArtifacts)
            Assert.Equal(0, fixture.Release(20, artifact));
        Assert.Equal(0, fixture.Release(20, replacement));
        Assert.Equal(0, fixture.Release(21, pending));
        Assert.Equal(0, fixture.Release(10, parentArtifact));
        Assert.Empty(fixture.State(parent).DemandArtifacts!);
        Assert.Null(fixture.Session.Failure);
    }
}
