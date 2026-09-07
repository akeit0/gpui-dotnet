using Gpui;

namespace Gpui.Tests;

public sealed class ViewPropsTests
{
    [Fact]
    public void CommittedPropsNeverExposeStagedRenderInputs()
    {
        var view = new ProbeView();

        Assert.Throws<InvalidOperationException>(() => view.ReadProps());

        Assert.True(view.StageProps(1));
        Assert.Throws<InvalidOperationException>(() => view.ReadProps());
        view.ValidateRenderInputs();
        view.CommitStagedProps();
        Assert.Equal(1, view.ReadProps());

        Assert.True(view.StageProps(2));
        Assert.Equal(1, view.ReadProps());
        view.ValidateRenderInputs();
        view.RollBackStagedProps();
        Assert.Equal(1, view.ReadProps());

        Assert.False(view.StageProps(2));
        view.RollBackStagedProps();
        Assert.True(view.StageProps(1));
    }

    private sealed class ProbeView : View<int>
    {
        public ProbeView() : this(TestViews.Construction()) { }
        public ProbeView(ViewConstruction construction) : base(construction) { }

        internal int ReadProps() => CommittedProps;

        protected override Element Render(in int props, ref RenderContext ui) => ui.Text($"{props}");
    }
}
