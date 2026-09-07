using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void RemovedChildElementsCannotWriteToRetiredFragmentStorage()
    {
        Element<DivTag> escaped = default;
        var include = true;
        var declaration = new FragmentDeclaration((ref RenderContext ui) =>
        {
            escaped = ui.Div();
            return escaped;
        });
        using var fixture = new SessionFixture(new DeclarationRoot((ref RenderContext ui) =>
            include
                ? ui.Child("fragment", new ViewSpec<DeclarationChild, FragmentDeclaration>(declaration))
                : ui.Div()));
        fixture.Render();
        include = false;
        fixture.Render();
        Assert.Throws<ObjectDisposedException>(() => escaped.FontFamily("retired"));
    }

    [Fact]
    public void RootBindingRemovalAndReintroductionPreserveArtifactSlots()
    {
        var view = new ChangingRootBindings();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var originalFirst = view.First;
        var originalSecond = view.Second;
        var artifact = fixture.Range(0);
        var rowToken = view.RowToken;
        view.IncludeFirst = false;
        fixture.Render();
        Assert.Equal(originalSecond, view.Second);
        Assert.Equal(0, fixture.Click(originalFirst));
        Assert.Equal(0, view.FirstClicks);

        // A row can reuse the vacated root slot. Later root compaction must not release it.
        var replacementArtifact = fixture.Range(1);
        var replacementRowToken = view.RowToken;
        view.IncludeFirst = true;
        fixture.Render();
        Assert.NotEqual(originalFirst, view.First);
        Assert.Equal(originalSecond, view.Second);
        Assert.Equal(0, fixture.Click(view.First));
        Assert.Equal(1, view.FirstClicks);
        Assert.Equal(0, fixture.Click(view.Second));
        Assert.Equal(1, view.SecondClicks);
        Assert.Equal(0, fixture.Click(rowToken));
        Assert.Equal(0, fixture.Click(replacementRowToken));
        Assert.Equal(1, view.ClickCount);
        Assert.Equal(1, view.SecondClickCount);
        Assert.Equal(0, fixture.Release(1, artifact));
        Assert.Equal(0, fixture.Release(1, replacementArtifact));
        Assert.Equal(0, fixture.Click(view.First));
        Assert.Equal(2, view.FirstClicks);
    }

    private sealed class ChangingRootBindings : ProbeView
    {
        internal bool IncludeFirst = true;
        internal ulong First;
        internal ulong Second;
        internal int FirstClicks;
        internal int SecondClicks;

        protected override Element Render(ref RenderContext ui)
        {
            if (IncludeFirst)
                First = Runtime.Events.BindClick<ChangingRootBindings>(static (view, _) => view.FirstClicks++);
            Second = Runtime.Events.BindClick<ChangingRootBindings>(static (view, _) => view.SecondClicks++);
            return ui.Text("root");
        }
    }

    [Theory]
    [InlineData("default")]
    [InlineData("foreign")]
    [InlineData("stale")]
    [InlineData("index")]
    public void ChildFragmentRejectsInvalidRootBeforeCopying(string kind)
    {
        using var foreignArena = new RenderArenaOwner();
        var foreignUi = foreignArena.BeginRender();
        Element foreign = foreignUi.Text("foreign");
        Element previous = default;
        var invalid = false;
        var declaration = new FragmentDeclaration((ref RenderContext ui) =>
        {
            Element current = ui.Text("current");
            if (!invalid)
            {
                previous = current;
                return current;
            }
            return kind switch
            {
                "default" => default,
                "foreign" => foreign,
                "stale" => previous,
                _ => new Element(current.Owner!, uint.MaxValue, current.Generation),
            };
        });
        using var fixture = new SessionFixture(new DeclarationRoot((ref RenderContext ui) =>
            ui.Child("fragment", new ViewSpec<DeclarationChild, FragmentDeclaration>(declaration))));
        fixture.Render();
        var child = fixture.State(fixture.View).Children!.Values.Single().View;
        invalid = true;
        child.Invalidate();

        var error = Assert.Throws<InvalidOperationException>(fixture.Render);

        Assert.Contains(kind == "index" ? "outside the node arena" : "active render generation", error.Message);
        Assert.Same(error, fixture.Session.Failure);
        Assert.True(child.Runtime.IsUnmounted);
    }

    [Fact]
    public void RootPublicationStillValidatesChildSemantics()
    {
        var declaration = new FragmentDeclaration((ref RenderContext ui) =>
        {
            _ = ui.Text("unattached");
            return ui.Text("root");
        });
        using var fixture = new SessionFixture(new DeclarationRoot((ref RenderContext ui) =>
            ui.Child("fragment", new ViewSpec<DeclarationChild, FragmentDeclaration>(declaration))));

        var error = Assert.Throws<InvalidOperationException>(fixture.Publish);

        Assert.Contains("declared but never attached", error.Message);
        Assert.Same(error, fixture.Session.Failure);
        Assert.Equal(0UL, fixture.Session.PendingRenderRevision);
    }

    [Fact]
    public void DuplicateChildKeyFaultsTheComposition()
    {
        using var fixture = new SessionFixture(new DeclarationRoot((ref RenderContext ui) =>
        {
            var a = ui.Child("same", ChildView.Spec());
            return ui.Div(a, ui.Child("same", ChildView.Spec()));
        }));

        var error = Assert.Throws<InvalidOperationException>(fixture.Publish);

        Assert.Contains("rendered more than once", error.Message);
        Assert.True(fixture.View.Runtime.IsUnmounted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyKeyedSlotsCanReplaceTheirAcceptedType(bool keyed)
    {
        var replace = false;
        using var fixture = new SessionFixture(new DeclarationRoot((ref RenderContext ui) =>
            replace
                ? keyed ? ui.Child("slot", BranchView.Spec()) : ui.Child(BranchView.Spec())
                : keyed ? ui.Child("slot", ChildView.Spec()) : ui.Child(ChildView.Spec())));
        fixture.Render();
        var original = fixture.Child;
        replace = true;

        if (keyed)
        {
            fixture.Render();
            Assert.IsType<BranchView>(fixture.State(fixture.View).Children!.Values.Single().View);
            Assert.Null(fixture.Session.Failure);
        }
        else
        {
            var error = Assert.Throws<InvalidOperationException>(fixture.Publish);
            Assert.Contains("positional slot", error.Message);
        }
        Assert.True(original.Runtime.IsUnmounted);
    }

    [Fact]
    public void FactoryRejectsAnExistingViewWithoutRetiringItsOwner()
    {
        using var fixture = new SessionFixture(new ParentView());
        fixture.Render();
        var existing = fixture.Child;

        var error = Assert.Throws<InvalidOperationException>(() =>
            ViewFactory.Construct<ChildView, ChildView>(existing, static (_, view) => view));

        Assert.Contains("factory must return the View bound to its construction context", error.Message);
        Assert.True(existing.Runtime.IsMounted);
        fixture.Render();
        Assert.Same(existing, fixture.Child);
    }

    [Fact]
    public void FailedParentRenderRetiresNestedCandidatesChildFirst()
    {
        var cleanup = new List<string>();
        var grandchild = new FragmentDeclaration(
            (ref RenderContext ui) => ui.Text("grandchild"),
            () => cleanup.Add("grandchild"));
        var child = new FragmentDeclaration(
            (ref RenderContext ui) => ui.Child("grandchild", new ViewSpec<DeclarationChild, FragmentDeclaration>(grandchild)),
            () => cleanup.Add("child"));
        using var fixture = new SessionFixture(new DeclarationRoot((ref RenderContext ui) =>
        {
            _ = ui.Child("child", new ViewSpec<DeclarationChild, FragmentDeclaration>(child));
            throw new InvalidOperationException("parent render failed");
        }));

        Assert.Throws<InvalidOperationException>(fixture.Publish);

        Assert.Equal(["grandchild", "child"], cleanup);
    }

    [Fact]
    public void ArtifactBindingsDeduplicateWithinRecycledSlotsAndStayIndependent()
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var retired = fixture.Range(0);
        var retiredToken = fixture.View.RowToken;
        fixture.Range(1);
        var unrelatedToken = fixture.View.RowToken;
        Assert.Equal(0, fixture.Release(1, retired));

        fixture.Range(0);
        var replacementToken = fixture.View.RowToken;
        // ProbeView binds the same callback twice in each row. Reuse the released slot
        // once, while leaving the root and unrelated artifact live.
        Assert.Equal(3, fixture.View.Runtime.Events.EntryCount);
        Assert.NotEqual(retiredToken, replacementToken);
        Assert.Equal(0, fixture.Click(retiredToken));
        Assert.Equal(0, fixture.View.ClickCount);
        Assert.Equal(0, fixture.Click(replacementToken));
        Assert.Equal(1, fixture.View.ClickCount);
        Assert.Equal(0, fixture.Click(unrelatedToken));
        Assert.Equal(1, fixture.View.SecondClickCount);
    }

    private delegate Element ElementDeclaration(ref RenderContext ui);
    private readonly record struct FragmentDeclaration(ElementDeclaration Render, Action? Cleanup = null);

    private sealed class DeclarationRoot(ElementDeclaration render) : ProbeView
    {
        protected override Element Render(ref RenderContext ui) => render(ref ui);
    }

    private sealed class DeclarationChild : View<FragmentDeclaration>, IGeneratedViewFactory<DeclarationChild, FragmentDeclaration>
    {
        private DeclarationChild(ViewConstruction construction, FragmentDeclaration props) : base(construction)
        {
            if (props.Cleanup is { } cleanup)
                construction.Own(new TestCleanup(cleanup));
        }

        public static DeclarationChild CreateGpuiView(ViewConstruction construction, FragmentDeclaration props) => new(construction, props);
        protected override Element Render(in FragmentDeclaration props, ref RenderContext ui) => props.Render(ref ui);
    }
}
